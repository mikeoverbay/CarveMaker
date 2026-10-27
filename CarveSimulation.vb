' ============================================================================
'  CarveSimulation.vb
'  GPU material-removal simulation. The stock blank is a float heightmap
'  texture (one cell = SimCellSize inches, value = depth cut, 0 = untouched).
'  Material is removed by stamping the tool from straight above into that
'  texture with MAX blending, so every texel keeps the deepest point of the
'  tool that ever passed over it. A stamp is one instanced quad over the
'  tool's footprint; its fragment shader evaluates the tool's exact profile
'  (flat, ball, bull nose, V-bit, tapered ball nose, drill, and the shank
'  above the flutes) at the distance from the tip to the texel centre.
'  Stamps are placed every half cell along each cutting move and streamed to
'  the GPU in bounded chunks, so jobs with millions of moves stay fast.
'  A second program draws the carved top surface as a displaced grid with
'  normals derived from neighbouring texels, plus the sides of the board.
'  The revolved tool mesh (ToolModel) only draws the tool above the board.
'
'  All methods must be called with the view's GL context current.
' ============================================================================

Imports OpenTK.Graphics.OpenGL4
Imports OpenTK.Mathematics

Public Class CarveSimulation
    Implements IDisposable

    ' ---- configuration ------------------------------------------------------
    Private _tp As Toolpath
    Private _cell As Double
    Private _blankX0, _blankY0, _blankW, _blankH As Double
    Private _thickness As Double
    ' The board's top: Z 0, or higher by the extra stock above the finished surface. The heightmap
    ' holds how deep each cell is cut below this top, so material above Z 0 is cut like any other.
    Private _topZ As Double
    Private _tool As ToolModel                  ' first tool (kept for callers that ask for "the" tool)
    Private _tools As New List(Of ToolModel)    ' one per distinct tool in the job (drawn above the board)
    Private _toolDefs As New List(Of ToolDefinition)   ' the same tools: shapes of the stamps
    Private _toolVaos As New List(Of Integer), _toolVbos As New List(Of Integer)
    Private _moveTool As Integer()              ' tool index of every move
    Private _thicknessForDraw As Single
    Private _texW, _texH As Integer

    ' ---- GL resources -------------------------------------------------------
    Private _tex As Integer
    Private _fbo As Integer
    Private _stampProg, _surfProg, _meshProg As Integer
    Private _stampVao, _quadVbo, _instVbo As Integer
    Private _surfVao, _surfVbo, _surfEbo As Integer
    Private _surfIndexCount As Integer
    Private _surfGx, _surfGy As Integer            ' display mesh cells
    Private _requestedCell As Double              ' cell size asked for before coarsening
    Private _meshBudget As Double = 4000000.0     ' vertex budget of the drawn surface (settings: Display mesh)
    Private Const RestartIndex As Integer = -1     ' &HFFFFFFFF: separates triangle strips
    Private _wallVao, _wallVbo As Integer
    Private _wallVertexCount As Integer
    Private _ready As Boolean

    ' ---- simulation clock ---------------------------------------------------
    Private _moveStart As Double()          ' cumulative start time of each move (s)
    Private _total As Double
    Private _appliedMove As Integer = -1    ' last move (partially) applied
    Private _appliedFrac As Double = 0      ' fraction of that move applied
    Private _appliedTime As Double = 0

    ' ---- stamp stream -------------------------------------------------------
    Private Const FirstStampCapacity As Integer = 4096
    Private Const MaxStampsPerFlush As Integer = 1048576        ' 12 MB of tip positions per upload
    Private Const FragmentsPerDraw As Double = 268435456.0      ' bounds the work of one draw call
    Private _stamps As Single() = New Single(3 * FirstStampCapacity - 1) {}   ' x, y, z of each pending stamp
    Private _stampCount As Integer
    Private _stampTool As Integer = -1          ' tool index of the pending stamps

    ''' <summary>Non-empty when the simulation could not be set up.</summary>
    Public ReadOnly Property SetupError As String

    ''' <summary>Total program time in seconds.</summary>
    Public ReadOnly Property TotalSeconds As Double
        Get
            Return _total
        End Get
    End Property

    ''' <summary>Simulated time the heightmap currently represents.</summary>
    Public ReadOnly Property AppliedSeconds As Double
        Get
            Return _appliedTime
        End Get
    End Property

    Public ReadOnly Property IsReady As Boolean
        Get
            Return _ready
        End Get
    End Property

    Public ReadOnly Property TextureWidth As Integer
        Get
            Return _texW
        End Get
    End Property

    Public ReadOnly Property TextureHeight As Integer
        Get
            Return _texH
        End Get
    End Property

    Public ReadOnly Property CellSize As Double
        Get
            Return _cell
        End Get
    End Property

    ''' <summary>Cell size the settings asked for (CellSize is larger when the GPU limits forced coarsening).</summary>
    Public ReadOnly Property RequestedCellSize As Double
        Get
            Return _requestedCell
        End Get
    End Property

    ''' <summary>Cells of the drawn surface mesh along X (equals TextureWidth unless the vertex budget was hit).</summary>
    Public ReadOnly Property DisplayCellsX As Integer
        Get
            Return _surfGx
        End Get
    End Property

    Public ReadOnly Property DisplayCellsY As Integer
        Get
            Return _surfGy
        End Get
    End Property

    Public ReadOnly Property Tool As ToolModel
        Get
            Return _tool
        End Get
    End Property

    ' =====================================================================
    '  setup
    ' =====================================================================

    ''' <summary>
    ''' (Re)builds the heightmap for a toolpath and settings. Returns False (with
    ''' Error set) when the GPU cannot provide the texture.
    ''' </summary>
    Public Function Setup(tp As Toolpath, s As CarveSettings, maxTextureSize As Integer) As Boolean
        Release()
        _SetupError = Nothing
        _tp = tp
        _blankX0 = tp.BlankMinX
        _blankY0 = tp.BlankMinY
        _blankW = Math.Max(0.01, tp.BlankMaxX - tp.BlankMinX)
        _blankH = Math.Max(0.01, tp.BlankMaxY - tp.BlankMinY)
        _thickness = Math.Max(0.05, Math.Min(s.StockThickness, 2.0))
        _topZ = Math.Max(0.0, Math.Min(s.ExtraStockZ, 4.0))
        ' One shape and mesh per distinct tool; every move knows which one cuts it.
        _tools.Clear()
        _toolDefs.Clear()
        Dim defs = _toolDefs
        _moveTool = New Integer(Math.Max(0, tp.Moves.Count - 1)) {}
        If tp.Segments.Count = 0 Then
            defs.Add(If(s.CarveTool, ToolDefinition.DefaultVBit()))
        Else
            For Each sg In tp.Segments
                Dim segTool = If(sg.Tool, If(s.CarveTool, ToolDefinition.DefaultVBit()))
                Dim idx = defs.FindIndex(Function(d) d.SameGeometry(segTool))
                If idx < 0 Then
                    defs.Add(segTool)
                    idx = defs.Count - 1
                End If
                For i = sg.FirstMove To Math.Min(tp.Moves.Count, sg.FirstMove + sg.MoveCount) - 1
                    _moveTool(i) = idx
                Next
            Next
        End If
        For Each d In defs
            _tools.Add(New ToolModel(d, 32))
        Next
        _tool = _tools(0)
        ' Cut through below the bottom: the stock thickness under Z 0 plus the extra stock above it.
        _thicknessForDraw = CSng(_thickness + _topZ)

        ' Cell size: requested, coarsened until the texture fits the hardware and a memory cap.
        Dim maxEdge As Integer = Math.Max(256, maxTextureSize)
        Const MaxTexels As Double = 40000000.0     ' 160 MB of R32F
        _cell = s.SimCellSize
        _requestedCell = _cell
        _meshBudget = Math.Max(10000.0, s.SimMeshCells)
        Do
            _texW = CInt(Math.Ceiling(_blankW / _cell))
            _texH = CInt(Math.Ceiling(_blankH / _cell))
            If _texW <= maxEdge AndAlso _texH <= maxEdge AndAlso CDbl(_texW) * _texH <= MaxTexels Then Exit Do
            _cell *= 1.25
        Loop
        _texW = Math.Max(2, _texW)
        _texH = Math.Max(2, _texH)

        ' Clock.
        _moveStart = New Double(tp.Moves.Count) {}
        Dim t As Double = 0
        For i = 0 To tp.Moves.Count - 1
            _moveStart(i) = t
            t += Math.Max(0.0, tp.Moves(i).Seconds)
        Next
        _moveStart(tp.Moves.Count) = t
        _total = t

        Try
            CreatePrograms()
            CreateHeightmap()
            CreateToolBuffers()
            CreateSurfaceGrid()
            CreateWalls()
            ClearHeightmap()
            _ready = True
        Catch ex As Exception
            _SetupError = ex.Message
            Release()
            Return False
        End Try
        Return True
    End Function

    Private Sub CreateHeightmap()
        _tex = GL.GenTexture()
        GL.BindTexture(TextureTarget.Texture2D, _tex)
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.R32f, _texW, _texH, 0, PixelFormat.Red, PixelType.Float, IntPtr.Zero)
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, CInt(TextureMinFilter.Linear))
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, CInt(TextureMagFilter.Linear))
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, CInt(TextureWrapMode.ClampToEdge))
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, CInt(TextureWrapMode.ClampToEdge))
        GL.BindTexture(TextureTarget.Texture2D, 0)

        _fbo = GL.GenFramebuffer()
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, _fbo)
        GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, _tex, 0)
        Dim status = GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer)
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0)
        If status <> FramebufferErrorCode.FramebufferComplete Then
            Throw New InvalidOperationException("Heightmap framebuffer incomplete (" & status.ToString() & ") for " & _texW & "x" & _texH)
        End If
        Dim err = GL.GetError()
        If err <> ErrorCode.NoError Then Throw New InvalidOperationException("Heightmap texture " & _texW & "x" & _texH & " failed: " & err.ToString())
    End Sub

    Private Sub CreateToolBuffers()
        ' Stamps: a unit quad (triangle strip of its corners) instanced once per tip position.
        _stampVao = GL.GenVertexArray()
        _quadVbo = GL.GenBuffer()
        _instVbo = GL.GenBuffer()
        GL.BindVertexArray(_stampVao)
        GL.BindBuffer(BufferTarget.ArrayBuffer, _quadVbo)
        Dim corners As Single() = {-1.0F, -1.0F, 1.0F, -1.0F, -1.0F, 1.0F, 1.0F, 1.0F}
        GL.BufferData(BufferTarget.ArrayBuffer, corners.Length * 4, corners, BufferUsageHint.StaticDraw)
        GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, False, 8, 0)
        GL.EnableVertexAttribArray(0)
        GL.BindBuffer(BufferTarget.ArrayBuffer, _instVbo)
        GL.BufferData(BufferTarget.ArrayBuffer, 12, IntPtr.Zero, BufferUsageHint.StreamDraw)
        GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, False, 12, 0)
        GL.EnableVertexAttribArray(1)
        GL.VertexAttribDivisor(1, 1)
        GL.BindVertexArray(0)

        ' The tools drawn above the board: revolved meshes (position + normal).
        _toolVaos.Clear()
        _toolVbos.Clear()
        For Each tm In _tools
            Dim vao = GL.GenVertexArray()
            Dim vbo = GL.GenBuffer()
            GL.BindVertexArray(vao)
            GL.BindBuffer(BufferTarget.ArrayBuffer, vbo)
            GL.BufferData(BufferTarget.ArrayBuffer, tm.Vertices.Length * 4, tm.Vertices, BufferUsageHint.StaticDraw)
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, False, 24, 0)
            GL.EnableVertexAttribArray(0)
            GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, False, 24, 12)
            GL.EnableVertexAttribArray(1)
            GL.BindVertexArray(0)
            _toolVaos.Add(vao)
            _toolVbos.Add(vbo)
        Next
    End Sub

    Private Sub CreateSurfaceGrid()
        ' Display grid: one vertex per heightmap texel, so the drawn surface shows the chosen
        ' precision; scaled down uniformly only when the texture exceeds the vertex budget.
        Dim f As Double = Math.Min(1.0, Math.Sqrt(_meshBudget / (CDbl(_texW) * _texH)))
        Dim gx As Integer = Math.Max(2, Math.Min(_texW, CInt(Math.Floor(_texW * f))))
        Dim gy As Integer = Math.Max(2, Math.Min(_texH, CInt(Math.Floor(_texH * f))))
        _surfGx = gx : _surfGy = gy
        Dim verts((gx + 1) * (gy + 1) * 2 - 1) As Single
        Dim k As Integer = 0
        For j = 0 To gy
            For i = 0 To gx
                verts(k) = CSng(i / CDbl(gx)) : verts(k + 1) = CSng(j / CDbl(gy))
                k += 2
            Next
        Next
        ' One triangle strip per row (upper vertex, lower vertex, ... = counter-clockwise from +Z),
        ' rows separated by the primitive-restart index: a third of the indices of a triangle list.
        Dim idx(gy * ((gx + 1) * 2 + 1) - 1) As Integer
        k = 0
        For j = 0 To gy - 1
            For i = 0 To gx
                idx(k) = (j + 1) * (gx + 1) + i
                idx(k + 1) = j * (gx + 1) + i
                k += 2
            Next
            idx(k) = RestartIndex
            k += 1
        Next
        _surfIndexCount = idx.Length
        _surfVao = GL.GenVertexArray()
        _surfVbo = GL.GenBuffer()
        _surfEbo = GL.GenBuffer()
        GL.BindVertexArray(_surfVao)
        GL.BindBuffer(BufferTarget.ArrayBuffer, _surfVbo)
        GL.BufferData(BufferTarget.ArrayBuffer, verts.Length * 4, verts, BufferUsageHint.StaticDraw)
        GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, False, 8, 0)
        GL.EnableVertexAttribArray(0)
        GL.BindBuffer(BufferTarget.ElementArrayBuffer, _surfEbo)
        GL.BufferData(BufferTarget.ElementArrayBuffer, idx.Length * 4, idx, BufferUsageHint.StaticDraw)
        GL.BindVertexArray(0)
    End Sub

    Private Sub CreateWalls()
        ' Four side walls and the bottom of the board (position + normal).
        Dim x0 = CSng(_blankX0), y0 = CSng(_blankY0), x1 = CSng(_blankX0 + _blankW), y1 = CSng(_blankY0 + _blankH)
        Dim zt As Single = CSng(_topZ), zb As Single = CSng(-_thickness)
        Dim v As New List(Of Single)
        Dim vert = Sub(x As Single, y As Single, z As Single, nx As Single, ny As Single, nz As Single)
                       v.Add(x) : v.Add(y) : v.Add(z) : v.Add(nx) : v.Add(ny) : v.Add(nz)
                   End Sub
        Dim quad = Sub(ax As Single, ay As Single, az As Single, bx As Single, by As Single, bz As Single,
                       cx As Single, cy As Single, cz As Single, dx As Single, dy As Single, dz As Single,
                       nx As Single, ny As Single, nz As Single)
                       vert(ax, ay, az, nx, ny, nz) : vert(bx, by, bz, nx, ny, nz) : vert(cx, cy, cz, nx, ny, nz)
                       vert(ax, ay, az, nx, ny, nz) : vert(cx, cy, cz, nx, ny, nz) : vert(dx, dy, dz, nx, ny, nz)
                   End Sub
        quad(x0, y0, zb, x1, y0, zb, x1, y0, zt, x0, y0, zt, 0, -1, 0)   ' front (y = y0)
        quad(x1, y1, zb, x0, y1, zb, x0, y1, zt, x1, y1, zt, 0, 1, 0)    ' back
        quad(x0, y1, zb, x0, y0, zb, x0, y0, zt, x0, y1, zt, -1, 0, 0)   ' left
        quad(x1, y0, zb, x1, y1, zb, x1, y1, zt, x1, y0, zt, 1, 0, 0)    ' right
        quad(x0, y1, zb, x1, y1, zb, x1, y0, zb, x0, y0, zb, 0, 0, -1)   ' bottom
        _wallVertexCount = v.Count \ 6
        _wallVao = GL.GenVertexArray()
        _wallVbo = GL.GenBuffer()
        GL.BindVertexArray(_wallVao)
        GL.BindBuffer(BufferTarget.ArrayBuffer, _wallVbo)
        Dim arr = v.ToArray()
        GL.BufferData(BufferTarget.ArrayBuffer, arr.Length * 4, arr, BufferUsageHint.StaticDraw)
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, False, 24, 0)
        GL.EnableVertexAttribArray(0)
        GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, False, 24, 12)
        GL.EnableVertexAttribArray(1)
        GL.BindVertexArray(0)
    End Sub

    Private Sub CreatePrograms()
        ' Stamp: one quad per tip position over the tool's footprint; each texel inside the tool's
        ' radius gets the depth of the tool's lowest point above its centre, output = depth below Z0.
        ' Profile height h(r) above the tip (r = distance from the axis, R = radius):
        '   flat / ball / bull nose: 0 out to the flat radius R - e, then the corner arc e - sqrt(e^2 - (r - (R - e))^2);
        '   V-bit / drill / tapered ball nose: tip ball e - sqrt(e^2 - r^2) out to the join, then the cone;
        '   above the flute length the shank (radius R), so h never exceeds it.
        _stampProg = Link(
            "#version 330 core" & vbLf &
            "layout(location = 0) in vec2 aCorner;" & vbLf &   ' quad corner, -1..1
            "layout(location = 1) in vec3 aTip;" & vbLf &      ' per stamp: tool tip position
            "uniform mat4 uOrtho;" & vbLf &
            "uniform float uReach;" & vbLf &                   ' half the quad: tool radius plus half a cell
            "flat out vec3 vTip;" & vbLf &
            "void main() { vTip = aTip; gl_Position = uOrtho * vec4(aTip.xy + aCorner * uReach, 0.0, 1.0); }" & vbLf,
            "#version 330 core" & vbLf &
            "flat in vec3 vTip;" & vbLf &
            "uniform vec2 uOrigin;" & vbLf &                   ' blank corner: texel (0, 0) starts here
            "uniform vec2 uTexelSize;" & vbLf &                ' inches per texel along X and Y
            "uniform int uAngled;" & vbLf &                    ' 1: tip ball + cone (V-bit, drill, tapered ball nose)
            "uniform float uRadius;" & vbLf &                  ' half the diameter
            "uniform float uEnd;" & vbLf &                     ' ball, corner or tip-ball radius (0 when none)
            "uniform float uFlatR;" & vbLf &                   ' radius of the flat bottom (end mills)
            "uniform float uTanA;" & vbLf &                    ' tangent of the cone's half angle
            "uniform vec2 uJoin;" & vbLf &                     ' (r, h) where the tip ball meets the cone
            "uniform float uFlute;" & vbLf &                   ' flute length: the shank starts there
            "out float oDepth;" & vbLf &
            "float arcRise(float e, float x) { return x * x / max(e + sqrt(max(e * e - x * x, 0.0)), 1e-20); }" & vbLf &   ' e - sqrt(e^2 - x^2) without cancellation
            "void main() {" & vbLf &
            "  vec2 p = uOrigin + gl_FragCoord.xy * uTexelSize;" & vbLf &                   ' texel centre
            "  float r = distance(p, vTip.xy);" & vbLf &
            "  if (r > uRadius) discard;" & vbLf &
            "  float h;" & vbLf &
            "  if (uAngled == 1) h = r <= uJoin.x ? arcRise(uEnd, r) : uJoin.y + (r - uJoin.x) / uTanA;" & vbLf &
            "  else h = r <= uFlatR ? 0.0 : arcRise(uEnd, r - uFlatR);" & vbLf &
            "  oDepth = max(-(vTip.z + min(h, uFlute)), 0.0);" & vbLf &
            "}" & vbLf)

        ' Surface: displaced grid with normals from neighbouring texels.
        _surfProg = Link(
            "#version 330 core" & vbLf &
            "layout(location = 0) in vec2 aUV;" & vbLf &
            "uniform sampler2D uHeight;" & vbLf &
            "uniform vec4 uBlank;" & vbLf &        ' x0, y0, w, h
            "uniform vec2 uTexel;" & vbLf &        ' 1/texW, 1/texH
            "uniform float uCell;" & vbLf &        ' inches per texel
            "uniform float uThickness;" & vbLf &   ' stock thickness: deeper cuts go through
            "uniform float uTop;" & vbLf &         ' Z of the board top (extra stock above Z 0)
            "uniform mat4 uMvp;" & vbLf &
            "out vec3 vNormal;" & vbLf &
            "out float vDepth;" & vbLf &
            "void main() {" & vbLf &
            "  vec2 suv = aUV + 0.5 * uTexel;" & vbLf &                                    ' sample at texel centres: vertex i of a per-texel grid reads texel i exactly
            "  vec2 puv = min(suv, vec2(1.0)) * step(0.0001, aUV);" & vbLf &              ' vertex sits where it samples; first/last column and row stay on the blank edge
            "  float d  = min(texture(uHeight, suv).r, uThickness);" & vbLf &
            "  float dx = texture(uHeight, suv + vec2(uTexel.x, 0.0)).r - texture(uHeight, suv - vec2(uTexel.x, 0.0)).r;" & vbLf &
            "  float dy = texture(uHeight, suv + vec2(0.0, uTexel.y)).r - texture(uHeight, suv - vec2(0.0, uTexel.y)).r;" & vbLf &
            "  vNormal = normalize(vec3(dx / (2.0 * uCell), dy / (2.0 * uCell), 1.0));" & vbLf &
            "  vDepth = d;" & vbLf &
            "  vec3 w = vec3(uBlank.x + puv.x * uBlank.z, uBlank.y + puv.y * uBlank.w, uTop - d);" & vbLf &
            "  gl_Position = uMvp * vec4(w, 1.0);" & vbLf &
            "}" & vbLf,
            "#version 330 core" & vbLf &
            "in vec3 vNormal;" & vbLf &
            "in float vDepth;" & vbLf &
            "uniform float uThickness;" & vbLf &
            "out vec4 FragColor;" & vbLf &
            "void main() {" & vbLf &
            "  if (vDepth > uThickness - 0.0005) discard;" & vbLf &   ' cut through: see past the board
            "  vec3 L = normalize(vec3(0.35, 0.25, 1.0));" & vbLf &
            "  float diff = max(dot(normalize(vNormal), L), 0.0);" & vbLf &
            "  vec3 top = vec3(0.62, 0.46, 0.30);" & vbLf &        ' finished board surface
            "  vec3 cut = vec3(0.92, 0.78, 0.55);" & vbLf &        ' freshly cut wood
            "  vec3 c = mix(top, cut, smoothstep(0.0002, 0.0008, vDepth));" & vbLf &
            "  FragColor = vec4(c * (0.35 + 0.65 * diff), 1.0);" & vbLf &
            "}" & vbLf)

        ' Mesh: lit solid for the tool and the board sides.
        _meshProg = Link(
            "#version 330 core" & vbLf &
            "layout(location = 0) in vec3 aPos;" & vbLf &
            "layout(location = 1) in vec3 aNormal;" & vbLf &
            "uniform vec3 uOffset;" & vbLf &
            "uniform mat4 uMvp;" & vbLf &
            "out vec3 vNormal;" & vbLf &
            "void main() { gl_Position = uMvp * vec4(aPos + uOffset, 1.0); vNormal = aNormal; }" & vbLf,
            "#version 330 core" & vbLf &
            "in vec3 vNormal;" & vbLf &
            "uniform vec3 uColor;" & vbLf &
            "out vec4 FragColor;" & vbLf &
            "void main() {" & vbLf &
            "  vec3 L = normalize(vec3(0.35, 0.25, 1.0));" & vbLf &
            "  float diff = max(dot(normalize(vNormal), L), 0.0);" & vbLf &
            "  FragColor = vec4(uColor * (0.3 + 0.7 * diff), 1.0);" & vbLf &
            "}" & vbLf)
    End Sub

    Private Shared Function Link(vs As String, fs As String) As Integer
        Dim v = Compile(ShaderType.VertexShader, vs)
        Dim f = Compile(ShaderType.FragmentShader, fs)
        Dim p = GL.CreateProgram()
        GL.AttachShader(p, v)
        GL.AttachShader(p, f)
        GL.LinkProgram(p)
        Dim ok As Integer
        GL.GetProgram(p, GetProgramParameterName.LinkStatus, ok)
        GL.DetachShader(p, v)
        GL.DetachShader(p, f)
        GL.DeleteShader(v)
        GL.DeleteShader(f)
        If ok = 0 Then
            Dim log = GL.GetProgramInfoLog(p)
            GL.DeleteProgram(p)
            Throw New InvalidOperationException("Simulation shader link failed: " & log)
        End If
        Return p
    End Function

    Private Shared Function Compile(kind As ShaderType, src As String) As Integer
        Dim id = GL.CreateShader(kind)
        GL.ShaderSource(id, src)
        GL.CompileShader(id)
        Dim ok As Integer
        GL.GetShader(id, ShaderParameter.CompileStatus, ok)
        If ok = 0 Then
            Dim log = GL.GetShaderInfoLog(id)
            GL.DeleteShader(id)
            Throw New InvalidOperationException(kind.ToString() & " compile failed: " & log)
        End If
        Return id
    End Function

    ' =====================================================================
    '  material removal
    ' =====================================================================

    ''' <summary>Restores the untouched blank.</summary>
    Public Sub ClearHeightmap()
        If Not _ready AndAlso _fbo = 0 Then Return
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, _fbo)
        GL.Viewport(0, 0, _texW, _texH)
        GL.ClearColor(0.0F, 0.0F, 0.0F, 0.0F)
        GL.Clear(ClearBufferMask.ColorBufferBit)
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0)
        _appliedMove = -1
        _appliedFrac = 0
        _appliedTime = 0
    End Sub

    ''' <summary>Applies all cutting up to simulated time t (seconds). Going backwards clears and replays.</summary>
    Public Sub AdvanceTo(t As Double)
        If Not _ready Then Return
        t = Math.Max(0.0, Math.Min(t, _total))
        If t < _appliedTime - 0.000001 Then ClearHeightmap()
        If t <= _appliedTime + 0.000001 AndAlso _appliedMove >= 0 Then Return

        Dim moves = _tp.Moves
        Dim startMove As Integer = Math.Max(0, _appliedMove)
        Dim startFrac As Double = If(_appliedMove >= 0, _appliedFrac, 0.0)
        Dim i As Integer = startMove
        While i < moves.Count AndAlso _moveStart(i) < t
            Dim mv = moves(i)
            Dim dur = Math.Max(0.0, mv.Seconds)
            Dim f1 As Double = If(dur <= 0, 1.0, Math.Min(1.0, (t - _moveStart(i)) / dur))
            Dim f0 As Double = If(i = startMove, startFrac, 0.0)
            If f1 > f0 AndAlso mv.Kind <> MoveKind.Rapid Then
                Dim from As Pt3 = If(i = 0, New Pt3(0, 0, 0), moves(i - 1).Target)
                ' A move that stays at or above the board top removes nothing.
                If Math.Min(from.Z, mv.Target.Z) < _topZ Then
                    Dim ti = MoveTool(i)
                    ' The start is already stamped when it was the end of the previous cut with this tool.
                    Dim startDone = f0 > 0 OrElse (i > 0 AndAlso moves(i - 1).Kind <> MoveKind.Rapid AndAlso MoveTool(i - 1) = ti)
                    ' Stamps are measured from the board top (the heightmap is depth below it).
                    AddStamps(ti, New Pt3(from.X, from.Y, from.Z - _topZ), New Pt3(mv.Target.X, mv.Target.Y, mv.Target.Z - _topZ), f0, f1, startDone)
                End If
            End If
            _appliedMove = i
            _appliedFrac = f1
            If f1 < 1.0 Then Exit While
            i += 1
        End While
        _appliedTime = t
        FlushStamps()
    End Sub

    Private Function MoveTool(i As Integer) As Integer
        Return If(_moveTool IsNot Nothing AndAlso i < _moveTool.Length, _moveTool(i), 0)
    End Function

    ''' <summary>
    ''' Queues tool tip positions along a move from fraction f0 to f1, every half cell
    ''' (the start point too unless it is already stamped). Full queues go to the GPU.
    ''' </summary>
    Private Sub AddStamps(toolIndex As Integer, a As Pt3, b As Pt3, f0 As Double, f1 As Double, skipStart As Boolean)
        If toolIndex <> _stampTool Then
            FlushStamps()
            _stampTool = toolIndex
        End If
        Dim len = a.DistanceTo(b)
        Dim stepLen = _cell * 0.5
        Dim n As Integer = Math.Max(1, CInt(Math.Ceiling((f1 - f0) * len / stepLen)))
        For k = If(skipStart, 1, 0) To n
            If _stampCount = MaxStampsPerFlush Then FlushStamps()
            If _stampCount * 3 = _stamps.Length Then Array.Resize(_stamps, _stamps.Length * 2)
            Dim f = f0 + (f1 - f0) * k / n
            Dim j = _stampCount * 3
            _stamps(j) = CSng(a.X + (b.X - a.X) * f)
            _stamps(j + 1) = CSng(a.Y + (b.Y - a.Y) * f)
            _stamps(j + 2) = CSng(a.Z + (b.Z - a.Z) * f)
            _stampCount += 1
        Next
    End Sub

    ''' <summary>Renders the queued stamps into the heightmap.</summary>
    Private Sub FlushStamps()
        If _stampCount > 0 Then Stamp(_stamps, _stampCount, _stampTool)
        _stampCount = 0
    End Sub

    ''' <summary>Renders the tool at the first count stamp positions into the heightmap with MAX blending.</summary>
    Private Sub Stamp(data As Single(), count As Integer, toolIndex As Integer)
        Dim ti = Math.Max(0, Math.Min(_toolDefs.Count - 1, toolIndex))
        Dim texelX = _blankW / _texW, texelY = _blankH / _texH
        Dim radius = SetStampTool(_toolDefs(ti))
        Dim reach = radius + 0.5 * Math.Max(texelX, texelY)
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, _fbo)
        GL.Viewport(0, 0, _texW, _texH)
        GL.Disable(EnableCap.DepthTest)
        GL.Enable(EnableCap.Blend)
        GL.BlendEquation(BlendEquationMode.Max)
        GL.BlendFunc(BlendingFactor.One, BlendingFactor.One)
        Dim ortho As Matrix4 = Matrix4.CreateOrthographicOffCenter(CSng(_blankX0), CSng(_blankX0 + _blankW), CSng(_blankY0), CSng(_blankY0 + _blankH), -1.0F, 1.0F)
        GL.UniformMatrix4(GL.GetUniformLocation(_stampProg, "uOrtho"), False, ortho)
        GL.Uniform1(GL.GetUniformLocation(_stampProg, "uReach"), CSng(reach))
        GL.Uniform2(GL.GetUniformLocation(_stampProg, "uOrigin"), CSng(_blankX0), CSng(_blankY0))
        GL.Uniform2(GL.GetUniformLocation(_stampProg, "uTexelSize"), CSng(texelX), CSng(texelY))
        GL.BindVertexArray(_stampVao)
        GL.BindBuffer(BufferTarget.ArrayBuffer, _instVbo)
        GL.BufferData(BufferTarget.ArrayBuffer, count * 12, data, BufferUsageHint.StreamDraw)
        ' Several draw calls when the stamps are large (a big tool at a fine cell covers many texels each).
        Dim texelsPerStamp = (2 * reach / texelX + 1) * (2 * reach / texelY + 1)
        Dim perDraw As Integer = CInt(Math.Min(count, Math.Max(1024.0, FragmentsPerDraw / texelsPerStamp)))
        Dim first As Integer = 0
        While first < count
            Dim n As Integer = Math.Min(perDraw, count - first)
            GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, False, 12, first * 12)
            GL.DrawArraysInstanced(PrimitiveType.TriangleStrip, 0, 4, n)
            first += n
        End While
        GL.BindVertexArray(0)
        GL.UseProgram(0)
        GL.BlendEquation(BlendEquationMode.FuncAdd)
        GL.Disable(EnableCap.Blend)
        GL.Enable(EnableCap.DepthTest)
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0)
    End Sub

    ''' <summary>
    ''' Makes the stamp program current with the profile of a tool (the same numbers and limits
    ''' as ToolDefinition.FluteProfile). Returns the tool radius.
    ''' </summary>
    Private Function SetStampTool(d As ToolDefinition) As Double
        Dim radius = Math.Max(0.0001, d.Diameter / 2.0)
        Dim endR As Double, flatR As Double = 0, tanA As Double = 1, joinR As Double = 0, joinH As Double = 0
        If d.UsesAngle Then
            Dim a = Math.Max(0.1, Math.Min(179.9, d.AngleDeg)) * Math.PI / 360.0
            tanA = Math.Tan(a)
            endR = d.EffectiveEndRadius
            joinR = endR * Math.Cos(a)
            joinH = endR * (1 - Math.Sin(a))
        Else
            endR = Math.Min(d.EffectiveEndRadius, radius)
            flatR = radius - endR
        End If
        GL.UseProgram(_stampProg)
        GL.Uniform1(GL.GetUniformLocation(_stampProg, "uAngled"), If(d.UsesAngle, 1, 0))
        GL.Uniform1(GL.GetUniformLocation(_stampProg, "uRadius"), CSng(radius))
        GL.Uniform1(GL.GetUniformLocation(_stampProg, "uEnd"), CSng(endR))
        GL.Uniform1(GL.GetUniformLocation(_stampProg, "uFlatR"), CSng(flatR))
        GL.Uniform1(GL.GetUniformLocation(_stampProg, "uTanA"), CSng(tanA))
        GL.Uniform2(GL.GetUniformLocation(_stampProg, "uJoin"), CSng(joinR), CSng(joinH))
        GL.Uniform1(GL.GetUniformLocation(_stampProg, "uFlute"), CSng(Math.Max(0.0001, d.Length)))
        Return radius
    End Function

    ''' <summary>Index of the move running at simulated time t (the last move after the end).</summary>
    Private Function MoveAt(t As Double) As Integer
        Dim lo = 0, hi = _tp.Moves.Count - 1
        While lo < hi
            Dim mid = (lo + hi) \ 2
            If _moveStart(mid + 1) < t Then lo = mid + 1 Else hi = mid
        End While
        Return lo
    End Function

    ''' <summary>Index of the tool cutting at simulated time t.</summary>
    Public Function ToolIndexAt(t As Double) As Integer
        If _tp Is Nothing OrElse _tp.Moves.Count = 0 OrElse _moveTool Is Nothing Then Return 0
        Return MoveTool(MoveAt(Math.Max(0.0, Math.Min(t, _total))))
    End Function

    ''' <summary>Tool tip position at simulated time t (for drawing the tool).</summary>
    Public Function ToolPositionAt(t As Double) As Pt3
        If _tp Is Nothing OrElse _tp.Moves.Count = 0 Then Return New Pt3(0, 0, 0)
        t = Math.Max(0.0, Math.Min(t, _total))
        Dim moves = _tp.Moves
        Dim i = MoveAt(t)
        Dim from As Pt3 = If(i = 0, New Pt3(0, 0, 0), moves(i - 1).Target)
        Dim [to] = moves(i).Target
        Dim dur = Math.Max(0.000001, moves(i).Seconds)
        Dim f = Math.Max(0.0, Math.Min(1.0, (t - _moveStart(i)) / dur))
        Return New Pt3(from.X + ([to].X - from.X) * f, from.Y + ([to].Y - from.Y) * f, from.Z + ([to].Z - from.Z) * f)
    End Function

    ' =====================================================================
    '  drawing
    ' =====================================================================

    ''' <summary>Draws the carved board. Call with the default framebuffer bound and the view's viewport set.</summary>
    Public Sub DrawBoard(mvp As Matrix4)
        If Not _ready Then Return
        ' Push the surface back slightly so toolpath lines drawn afterwards stay visible on it.
        GL.Enable(EnableCap.PolygonOffsetFill)
        GL.PolygonOffset(1.0F, 1.0F)

        GL.UseProgram(_surfProg)
        GL.ActiveTexture(TextureUnit.Texture0)
        GL.BindTexture(TextureTarget.Texture2D, _tex)
        GL.Uniform1(GL.GetUniformLocation(_surfProg, "uHeight"), 0)
        GL.Uniform4(GL.GetUniformLocation(_surfProg, "uBlank"), CSng(_blankX0), CSng(_blankY0), CSng(_blankW), CSng(_blankH))
        GL.Uniform2(GL.GetUniformLocation(_surfProg, "uTexel"), CSng(1.0 / _texW), CSng(1.0 / _texH))
        GL.Uniform1(GL.GetUniformLocation(_surfProg, "uCell"), CSng(_blankW / _texW))
        GL.Uniform1(GL.GetUniformLocation(_surfProg, "uThickness"), _thicknessForDraw)
        GL.Uniform1(GL.GetUniformLocation(_surfProg, "uTop"), CSng(_topZ))
        GL.UniformMatrix4(GL.GetUniformLocation(_surfProg, "uMvp"), False, mvp)
        GL.BindVertexArray(_surfVao)
        GL.Enable(EnableCap.PrimitiveRestart)
        GL.PrimitiveRestartIndex(UInteger.MaxValue)
        GL.DrawElements(PrimitiveType.TriangleStrip, _surfIndexCount, DrawElementsType.UnsignedInt, 0)
        GL.Disable(EnableCap.PrimitiveRestart)
        GL.BindVertexArray(0)
        GL.BindTexture(TextureTarget.Texture2D, 0)

        GL.UseProgram(_meshProg)
        GL.UniformMatrix4(GL.GetUniformLocation(_meshProg, "uMvp"), False, mvp)
        GL.Uniform3(GL.GetUniformLocation(_meshProg, "uOffset"), 0.0F, 0.0F, 0.0F)
        GL.Uniform3(GL.GetUniformLocation(_meshProg, "uColor"), 0.52F, 0.38F, 0.24F)
        GL.BindVertexArray(_wallVao)
        GL.DrawArrays(PrimitiveType.Triangles, 0, _wallVertexCount)
        GL.BindVertexArray(0)
        GL.UseProgram(0)
        GL.Disable(EnableCap.PolygonOffsetFill)
    End Sub

    ''' <summary>Draws the tool model with its tip at the given position.</summary>
    Public Sub DrawTool(mvp As Matrix4, tip As Pt3, Optional toolIndex As Integer = 0)
        If Not _ready Then Return
        Dim ti = Math.Max(0, Math.Min(_tools.Count - 1, toolIndex))
        GL.UseProgram(_meshProg)
        GL.UniformMatrix4(GL.GetUniformLocation(_meshProg, "uMvp"), False, mvp)
        GL.Uniform3(GL.GetUniformLocation(_meshProg, "uOffset"), CSng(tip.X), CSng(tip.Y), CSng(tip.Z))
        GL.Uniform3(GL.GetUniformLocation(_meshProg, "uColor"), 0.75F, 0.78F, 0.82F)
        GL.BindVertexArray(_toolVaos(ti))
        ' The instance attribute is still enabled on this VAO: draw one instance at offset zero.
        GL.BindBuffer(BufferTarget.ArrayBuffer, _instVbo)
        GL.BufferData(BufferTarget.ArrayBuffer, 12, New Single() {0.0F, 0.0F, 0.0F}, BufferUsageHint.StreamDraw)
        GL.DrawArraysInstanced(PrimitiveType.Triangles, 0, _tools(ti).VertexCount, 1)
        GL.BindVertexArray(0)
        GL.UseProgram(0)
    End Sub

    ' =====================================================================
    '  cleanup
    ' =====================================================================

    ''' <summary>Deletes all GL objects (context must be current).</summary>
    Public Sub Release()
        _ready = False
        Try
            If _fbo <> 0 Then GL.DeleteFramebuffer(_fbo)
            If _tex <> 0 Then GL.DeleteTexture(_tex)
            For Each b In _toolVbos
                If b <> 0 Then GL.DeleteBuffer(b)
            Next
            For Each a In _toolVaos
                If a <> 0 Then GL.DeleteVertexArray(a)
            Next
            If _instVbo <> 0 Then GL.DeleteBuffer(_instVbo)
            If _quadVbo <> 0 Then GL.DeleteBuffer(_quadVbo)
            If _stampVao <> 0 Then GL.DeleteVertexArray(_stampVao)
            If _surfVbo <> 0 Then GL.DeleteBuffer(_surfVbo)
            If _surfEbo <> 0 Then GL.DeleteBuffer(_surfEbo)
            If _surfVao <> 0 Then GL.DeleteVertexArray(_surfVao)
            If _wallVbo <> 0 Then GL.DeleteBuffer(_wallVbo)
            If _wallVao <> 0 Then GL.DeleteVertexArray(_wallVao)
            If _stampProg <> 0 Then GL.DeleteProgram(_stampProg)
            If _surfProg <> 0 Then GL.DeleteProgram(_surfProg)
            If _meshProg <> 0 Then GL.DeleteProgram(_meshProg)
        Catch
        End Try
        _fbo = 0 : _tex = 0 : _instVbo = 0 : _quadVbo = 0 : _stampVao = 0
        _stampCount = 0 : _stampTool = -1
        _toolVaos.Clear() : _toolVbos.Clear()
        _surfVbo = 0 : _surfEbo = 0 : _surfVao = 0 : _wallVbo = 0 : _wallVao = 0
        _stampProg = 0 : _surfProg = 0 : _meshProg = 0
    End Sub

    ''' <summary>Forget GL objects without deleting them (the context is already gone).</summary>
    Public Sub Forget()
        _ready = False
        _fbo = 0 : _tex = 0 : _instVbo = 0 : _quadVbo = 0 : _stampVao = 0
        _stampCount = 0 : _stampTool = -1
        _toolVaos.Clear() : _toolVbos.Clear()
        _surfVbo = 0 : _surfEbo = 0 : _surfVao = 0 : _wallVbo = 0 : _wallVao = 0
        _stampProg = 0 : _surfProg = 0 : _meshProg = 0
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        Release()
    End Sub
End Class
