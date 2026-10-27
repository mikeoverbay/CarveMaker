' ============================================================================
'  ModelImport.vb
'  3D models and depth images as reliefs: the model's top surface seen from
'  straight above, as a heightmap the toolpath engines can trust.
'
'  Readers: STL (binary and ASCII), OBJ, PLY (ASCII, binary little and big
'  endian), 3MF (zip; units, components and build transforms), OFF, and
'  grayscale depth images (PNG, JPG, BMP, GIF, TIFF; light = high).
'
'  The model is turned so its carved side faces up (detected: the thin axis
'  of a relief, on the side that is not flat) and rasterized CONSERVATIVELY:
'  each cell holds the highest point of the model over the whole cell, not
'  just at its centre. A linear function's maximum over a convex polygon is
'  at a vertex, and the vertices of (triangle ∩ cell) are triangle corners
'  in the cell, cell corners in the triangle, and triangle edges crossing
'  grid lines, so those three cases give the exact maximum. A tool kept clear
'  of these columns is clear of the model.
' ============================================================================

Imports System.Drawing
Imports System.Globalization
Imports System.IO
Imports System.IO.Compression
Imports System.Text
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Xml.Linq
Imports Clipper2Lib

Public Enum ModelUnits
    Millimeters
    Centimeters
    Meters
    Inches
    Feet
End Enum

''' <summary>Which side of the model faces the cutter (is carved).</summary>
Public Enum ModelUpAxis
    PlusZ
    MinusZ
    PlusY
    MinusY
    PlusX
    MinusX
End Enum

Public Class ModelImportOptions
    ''' <summary>Units of mesh coordinates; Nothing = from the file (3MF) or guessed from the size.</summary>
    Public Property Units As ModelUnits? = Nothing
    ''' <summary>Side of the model that is carved; Nothing = detected.</summary>
    Public Property Up As ModelUpAxis? = Nothing
    ''' <summary>Cells along the longer side of the heightmap.</summary>
    Public Property Resolution As Integer = 4096
    ''' <summary>Depth images: dark is high instead of light.</summary>
    Public Property InvertImage As Boolean = False
    ''' <summary>Depth images: natural relief depth (inches).</summary>
    Public Property ImageDepth As Double = 0.25
End Class

''' <summary>The top surface of an imported model (or a depth image) as a heightmap.</summary>
Public NotInheritable Class ReliefModel
    Private Const Magic As String = "CMRELIEF"
    Private Const FormatVersion As Integer = 1

    Public ReadOnly Property Columns As Integer
    Public ReadOnly Property Rows As Integer
    ''' <summary>
    ''' Height over each cell: 0 = lowest point of the top surface, 1 = highest; the maximum of the
    ''' surface over the whole cell. Single.NaN where no part of the model lies over the cell.
    ''' Index = row * Columns + column; row 0 is the bottom (smallest Y), column 0 the left.
    ''' </summary>
    Public ReadOnly Property Heights As Single()
    ''' <summary>Natural size in inches: the grid's X and Y extent and the height range of the top surface.</summary>
    Public ReadOnly Property NaturalWidth As Double
    Public ReadOnly Property NaturalHeight As Double
    Public ReadOnly Property NaturalDepth As Double
    Public ReadOnly Property Units As ModelUnits
    Public ReadOnly Property Up As ModelUpAxis
    Public ReadOnly Property IsImage As Boolean
    Public ReadOnly Property Format As String
    Public ReadOnly Property Description As String
    Public ReadOnly Property Warnings As New List(Of String)

    Friend Sub New(cols As Integer, rows As Integer, heights As Single(), w As Double, h As Double, d As Double,
                   units As ModelUnits, up As ModelUpAxis, isImage As Boolean, format As String, description As String)
        _Columns = cols : _Rows = rows : _Heights = heights
        _NaturalWidth = w : _NaturalHeight = h : _NaturalDepth = d
        _Units = units : _Up = up : _IsImage = isImage
        _Format = If(format, "") : _Description = If(description, "")
    End Sub

    ''' <summary>The same heightmap read in other units (natural sizes scaled; Heights shared).</summary>
    Public Function WithUnits(units As ModelUnits) As ReliefModel
        If IsImage Then Return Me
        Dim k = ModelImport.UnitInches(units) / ModelImport.UnitInches(Me.Units)
        Dim m As New ReliefModel(Columns, Rows, Heights, NaturalWidth * k, NaturalHeight * k, NaturalDepth * k, units, Up, IsImage, Format, Description)
        m.Warnings.AddRange(Warnings)
        Return m
    End Function

    ' ------------------------------------------------------------ outline

    ''' <summary>
    ''' Outline of the covered cells in natural inches (lower-left of the grid at 0, 0), outers
    ''' counter-clockwise and holes clockwise, simplified to the tolerance.
    ''' </summary>
    Public Function Footprint(tolerance As Double) As PathsD
        ' Work on at most 1024 cells per side: a coarse cell is covered when any of its cells is.
        Dim k = Math.Max(1, CInt(Math.Ceiling(Math.Max(Columns, Rows) / 1024.0)))
        Dim fc = (Columns + k - 1) \ k, fr = (Rows + k - 1) \ k
        Dim cov(fc * fr - 1) As Boolean
        For j = 0 To Rows - 1
            Dim row = j * Columns
            For i = 0 To Columns - 1
                If Not Single.IsNaN(Heights(row + i)) Then cov((j \ k) * fc + i \ k) = True
            Next
        Next
        Dim cw = NaturalWidth / Columns * k, ch = NaturalHeight / Rows * k
        Dim rects As New PathsD()
        For j = 0 To fr - 1
            Dim i = 0
            While i < fc
                If cov(j * fc + i) Then
                    Dim i1 = i
                    While i1 + 1 < fc AndAlso cov(j * fc + i1 + 1)
                        i1 += 1
                    End While
                    Dim x0 = i * cw, x1 = Math.Min(NaturalWidth, (i1 + 1) * cw), y0 = j * ch, y1 = Math.Min(NaturalHeight, (j + 1) * ch)
                    rects.Add(New PathD From {New PointD(x0, y0), New PointD(x1, y0), New PointD(x1, y1), New PointD(x0, y1)})
                    i = i1 + 1
                Else
                    i += 1
                End If
            End While
        Next
        If rects.Count = 0 Then Return rects
        Dim u = Clipper.Union(rects, Nothing, FillRule.NonZero, GlyphOutline.ClipperPrecision)
        Return Clipper.SimplifyPaths(u, Math.Max(0.000001, tolerance))
    End Function

    ' ------------------------------------------------------------ storage

    ''' <summary>
    ''' Compact form for the project file: header, then heights as UInt16 (0 = no model, 1..65535 =
    ''' 0..1 rounded up so they stay at or above the model), each row stored as differences to the
    ''' previous value, deflated.
    ''' </summary>
    Public Function ToBytes() As Byte()
        Using ms As New MemoryStream()
            Using bw As New BinaryWriter(ms, Encoding.UTF8, True)
                bw.Write(Encoding.ASCII.GetBytes(Magic))
                bw.Write(FormatVersion)
                bw.Write(Columns) : bw.Write(Rows)
                bw.Write(NaturalWidth) : bw.Write(NaturalHeight) : bw.Write(NaturalDepth)
                bw.Write(CInt(Units)) : bw.Write(CInt(Up)) : bw.Write(IsImage)
                bw.Write(Format) : bw.Write(Description)
            End Using
            Dim raw(Columns * Rows * 2 - 1) As Byte
            Dim cols = Columns, hs = Heights
            Parallel.For(0, Rows,
                Sub(j)
                    Dim prev As Integer = 0
                    For i = 0 To cols - 1
                        Dim h = hs(j * cols + i)
                        Dim q As Integer = If(Single.IsNaN(h), 0, 1 + CInt(Math.Ceiling(Math.Max(0.0, Math.Min(1.0, CDbl(h))) * 65534.0 - 0.000001)))
                        Dim d = (q - prev) And &HFFFF
                        raw((j * cols + i) * 2) = CByte(d And &HFF)
                        raw((j * cols + i) * 2 + 1) = CByte(d >> 8)
                        prev = q
                    Next
                End Sub)
            Using z As New ZLibStream(ms, CompressionLevel.Optimal, True)
                z.Write(raw, 0, raw.Length)
            End Using
            Return ms.ToArray()
        End Using
    End Function

    Public Shared Function FromBytes(data As Byte()) As ReliefModel
        If data Is Nothing OrElse data.Length < 16 Then Throw New InvalidDataException("The relief data is empty.")
        Using ms As New MemoryStream(data)
            Using br As New BinaryReader(ms, Encoding.UTF8, True)
                If Encoding.ASCII.GetString(br.ReadBytes(Magic.Length)) <> Magic Then Throw New InvalidDataException("This is not " & AppInfo.Name & " relief data.")
                Dim ver = br.ReadInt32()
                If ver <> FormatVersion Then Throw New InvalidDataException("The relief data was made by a newer version (format " & ver & ").")
                Dim cols = br.ReadInt32(), rows = br.ReadInt32()
                If cols <= 0 OrElse rows <= 0 OrElse CLng(cols) * rows > 100000000L Then Throw New InvalidDataException("The relief size is invalid.")
                Dim w = br.ReadDouble(), h = br.ReadDouble(), d = br.ReadDouble()
                Dim units = CType(br.ReadInt32(), ModelUnits), up = CType(br.ReadInt32(), ModelUpAxis)
                Dim img = br.ReadBoolean()
                Dim fmt = br.ReadString(), desc = br.ReadString()
                If Not (w > 0 AndAlso h > 0 AndAlso d >= 0) OrElse Not Double.IsFinite(w + h + d) Then Throw New InvalidDataException("The relief size is invalid.")
                Dim raw(cols * rows * 2 - 1) As Byte
                Using z As New ZLibStream(ms, CompressionMode.Decompress, True)
                    Dim off = 0
                    While off < raw.Length
                        Dim n = z.Read(raw, off, raw.Length - off)
                        If n <= 0 Then Throw New InvalidDataException("The relief data is cut short.")
                        off += n
                    End While
                End Using
                Dim hs(cols * rows - 1) As Single
                Parallel.For(0, rows,
                    Sub(j)
                        Dim prev As Integer = 0
                        For i = 0 To cols - 1
                            Dim dlt = raw((j * cols + i) * 2) Or (CInt(raw((j * cols + i) * 2 + 1)) << 8)
                            Dim q = (prev + dlt) And &HFFFF
                            hs(j * cols + i) = If(q = 0, Single.NaN, CSng((q - 1) / 65534.0))
                            prev = q
                        Next
                    End Sub)
                Return New ReliefModel(cols, rows, hs, w, h, d, units, up, img, fmt, desc)
            End Using
        End Using
    End Function

    ' ------------------------------------------------------------ preview

    ''' <summary>Shaded relief (lit from the upper left, mixed with height), transparent where there is no model.</summary>
    Public Function CreatePreview(maxSize As Integer) As Bitmap
        Dim k = Math.Max(1, CInt(Math.Ceiling(Math.Max(Columns, Rows) / CDbl(Math.Max(16, maxSize)))))
        Dim pw = Math.Max(1, (Columns + k - 1) \ k), ph = Math.Max(1, (Rows + k - 1) \ k)
        ' Highest point of each block (row 0 = bottom).
        Dim hm(pw * ph - 1) As Single
        For j = 0 To ph - 1
            For i = 0 To pw - 1
                Dim m = Single.NaN
                For sj = j * k To Math.Min(Rows, (j + 1) * k) - 1
                    For si = i * k To Math.Min(Columns, (i + 1) * k) - 1
                        Dim v = Heights(sj * Columns + si)
                        If Not Single.IsNaN(v) AndAlso (Single.IsNaN(m) OrElse v > m) Then m = v
                    Next
                Next
                hm(j * pw + i) = m
            Next
        Next
        ' Slopes in height units per block: scale by the relief's real proportions.
        Dim cell = NaturalWidth / Columns * k
        Dim zScale = If(cell > 0, NaturalDepth / cell, 1.0)
        Dim px(pw * ph - 1) As Integer
        For j = 0 To ph - 1
            For i = 0 To pw - 1
                Dim v = hm(j * pw + i)
                Dim argb As Integer = 0
                If Not Single.IsNaN(v) Then
                    Dim l = hm(j * pw + Math.Max(0, i - 1)), r = hm(j * pw + Math.Min(pw - 1, i + 1))
                    Dim dn = hm(Math.Max(0, j - 1) * pw + i), upv = hm(Math.Min(ph - 1, j + 1) * pw + i)
                    If Single.IsNaN(l) Then l = v
                    If Single.IsNaN(r) Then r = v
                    If Single.IsNaN(dn) Then dn = v
                    If Single.IsNaN(upv) Then upv = v
                    Dim gx = (r - l) / 2.0 * zScale, gy = (upv - dn) / 2.0 * zScale
                    ' Light from the upper left, above.
                    Dim nl = Math.Sqrt(gx * gx + gy * gy + 1)
                    Dim lit = (gx * -0.5 + gy * 0.5 + 0.7) / nl / Math.Sqrt(0.5 * 0.5 + 0.5 * 0.5 + 0.7 * 0.7)
                    Dim shade = Math.Max(0.0, Math.Min(1.0, 0.25 + 0.75 * lit))
                    Dim tone = 0.55 * shade + 0.45 * (0.35 + 0.65 * v)
                    Dim rr = CInt(Math.Min(255, 255 * tone * 1.05)), gg = CInt(Math.Min(255, 255 * tone * 0.92)), bb = CInt(Math.Min(255, 255 * tone * 0.75))
                    argb = (255 << 24) Or (rr << 16) Or (gg << 8) Or bb
                End If
                px((ph - 1 - j) * pw + i) = argb     ' bitmap row 0 is the top
            Next
        Next
        Dim bmp As New Bitmap(pw, ph, Imaging.PixelFormat.Format32bppArgb)
        Dim data = bmp.LockBits(New Rectangle(0, 0, pw, ph), Imaging.ImageLockMode.WriteOnly, Imaging.PixelFormat.Format32bppArgb)
        Try
            For y = 0 To ph - 1
                Runtime.InteropServices.Marshal.Copy(px, y * pw, data.Scan0 + y * data.Stride, pw)
            Next
        Finally
            bmp.UnlockBits(data)
        End Try
        Return bmp
    End Function
End Class

''' <summary>Reads model files and depth images into reliefs.</summary>
Public Module ModelImport
    Private ReadOnly Ci As CultureInfo = CultureInfo.InvariantCulture
    Private ReadOnly MeshExtensions As String() = {".stl", ".obj", ".ply", ".3mf", ".off"}
    Private ReadOnly ImageExtensions As String() = {".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff"}

    ''' <summary>OpenFileDialog filter: all supported, then each format.</summary>
    Public ReadOnly Property FileFilter As String
        Get
            Return "3D models and depth images|*.stl;*.obj;*.ply;*.3mf;*.off;*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff" &
                   "|STL (*.stl)|*.stl|Wavefront OBJ (*.obj)|*.obj|PLY (*.ply)|*.ply|3MF (*.3mf)|*.3mf|OFF (*.off)|*.off" &
                   "|Depth images (*.png;*.jpg;*.bmp;*.gif;*.tif)|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff|All files (*.*)|*.*"
        End Get
    End Property

    Public Function IsModelFile(path As String) As Boolean
        Dim ext = IO.Path.GetExtension(If(path, "")).ToLowerInvariant()
        Return MeshExtensions.Contains(ext) OrElse ImageExtensions.Contains(ext)
    End Function

    ''' <summary>Inches per unit.</summary>
    Public Function UnitInches(u As ModelUnits) As Double
        Select Case u
            Case ModelUnits.Millimeters : Return 1.0 / 25.4
            Case ModelUnits.Centimeters : Return 1.0 / 2.54
            Case ModelUnits.Meters : Return 1.0 / 0.0254
            Case ModelUnits.Feet : Return 12.0
            Case Else : Return 1.0
        End Select
    End Function

    ''' <summary>Reads and rasterizes a model or depth image. Throws InvalidDataException with a readable message on bad files.</summary>
    Public Function Load(path As String, options As ModelImportOptions, token As CancellationToken) As ReliefModel
        If options Is Nothing Then options = New ModelImportOptions()
        If Not File.Exists(path) Then Throw New FileNotFoundException("The file was not found.", path)
        Dim ext = IO.Path.GetExtension(path).ToLowerInvariant()
        If ImageExtensions.Contains(ext) Then Return LoadImage(path, options)
        Dim mesh As Mesh
        Select Case ext
            Case ".stl" : mesh = ReadStl(path)
            Case ".obj" : mesh = ReadObj(path)
            Case ".ply" : mesh = ReadPly(path)
            Case ".3mf" : mesh = Read3mf(path)
            Case ".off" : mesh = ReadOff(path)
            Case Else : Throw New InvalidDataException(AppInfo.Name & " cannot read " & ext & " files.")
        End Select
        token.ThrowIfCancellationRequested()
        If mesh.Count = 0 Then Throw New InvalidDataException("The file has no triangles.")
        For Each v In mesh.V
            If Not Single.IsFinite(v) Then Throw New InvalidDataException("The file has coordinates that are not numbers.")
        Next
        Dim units = If(options.Units, If(mesh.Units, GuessUnits(mesh)))
        Dim sideNote As String = Nothing
        Dim up = If(options.Up.HasValue, options.Up.Value, DetectUp(mesh, token, sideNote))
        Orient(mesh, up)
        Dim res = Math.Max(16, Math.Min(16384, options.Resolution))
        Dim m = Rasterize(mesh, res, units, up, mesh.FormatName, mesh.Count.ToString("N0", Ci) & " triangles", token)
        If sideNote IsNot Nothing Then m.Warnings.Add(sideNote)
        If options.Units Is Nothing AndAlso mesh.Units Is Nothing Then
            m.Warnings.Add("The file does not say its units; " & UnitName(units) & " were assumed from its size (change Model units if the size is wrong).")
        End If
        Return m
    End Function

    Private Function UnitName(u As ModelUnits) As String
        Return u.ToString().ToLowerInvariant()
    End Function

    ' ------------------------------------------------------------ mesh

    ''' <summary>Triangles as 9 floats each (x0 y0 z0 x1 y1 z1 x2 y2 z2).</summary>
    Private NotInheritable Class Mesh
        Public V As Single()
        Public Count As Integer
        Public Units As ModelUnits?
        Public FormatName As String

        Public Sub New(v As Single(), count As Integer, format As String, Optional units As ModelUnits? = Nothing)
            Me.V = v : Me.Count = count : FormatName = format : Me.Units = units
        End Sub
    End Class

    ''' <summary>Builds a mesh from vertices and polygon faces (fan-triangulated).</summary>
    Private Function FromFaces(verts As List(Of Single), faces As List(Of Integer()), format As String, Optional units As ModelUnits? = Nothing) As Mesh
        Dim tris As New List(Of Single)(faces.Count * 9)
        Dim nv = verts.Count \ 3
        For Each f In faces
            If f.Length < 3 Then Continue For
            For Each idx In f
                If idx < 0 OrElse idx >= nv Then Throw New InvalidDataException("A face uses a vertex that does not exist (" & (idx + 1) & " of " & nv & ").")
            Next
            For k = 1 To f.Length - 2
                For Each idx In {f(0), f(k), f(k + 1)}
                    tris.Add(verts(idx * 3)) : tris.Add(verts(idx * 3 + 1)) : tris.Add(verts(idx * 3 + 2))
                Next
            Next
        Next
        Return New Mesh(tris.ToArray(), tris.Count \ 9, format, units)
    End Function

    Private Function GuessUnits(mesh As Mesh) As ModelUnits
        Dim mn(2) As Double, mx(2) As Double
        Bounds(mesh, mn, mx)
        Dim largest = Math.Max(mx(0) - mn(0), Math.Max(mx(1) - mn(1), mx(2) - mn(2)))
        Return If(largest > 20, ModelUnits.Millimeters, ModelUnits.Inches)
    End Function

    Private Sub Bounds(mesh As Mesh, mn As Double(), mx As Double())
        For a = 0 To 2
            mn(a) = Double.PositiveInfinity : mx(a) = Double.NegativeInfinity
        Next
        Dim v = mesh.V
        For i = 0 To mesh.Count * 3 - 1
            For a = 0 To 2
                Dim c = v(i * 3 + a)
                If c < mn(a) Then mn(a) = c
                If c > mx(a) Then mx(a) = c
            Next
        Next
    End Sub

    ''' <summary>
    ''' The carved side: a relief is thin along one axis; of its two sides the carved one varies
    ''' more (the back is flat). Anything else is carved from +Z.
    ''' </summary>
    Private Function DetectUp(mesh As Mesh, token As CancellationToken, ByRef note As String) As ModelUpAxis
        note = Nothing
        Dim mn(2) As Double, mx(2) As Double
        Bounds(mesh, mn, mx)
        Dim size = {mx(0) - mn(0), mx(1) - mn(1), mx(2) - mn(2)}
        Dim flat = 0.001 * size.Max()
        Dim order = {0, 1, 2}.OrderBy(Function(a) size(a)).ToArray()
        Dim chosen = ModelUpAxis.PlusZ
        Dim spread As Double
        If size(order(0)) < 0.6 * size(order(1)) Then
            Dim plus = {ModelUpAxis.PlusX, ModelUpAxis.PlusY, ModelUpAxis.PlusZ}(order(0))
            Dim minus = {ModelUpAxis.MinusX, ModelUpAxis.MinusY, ModelUpAxis.MinusZ}(order(0))
            Dim spreadPlus = SideSpread(mesh, plus, token), spreadMinus = SideSpread(mesh, minus, token)
            chosen = If(spreadMinus > spreadPlus * 1.0001, minus, plus)
            spread = Math.Max(spreadPlus, spreadMinus)
        Else
            spread = SideSpread(mesh, ModelUpAxis.PlusZ, token)
        End If
        If spread > flat Then Return chosen
        ' Seen from there the model is flat (the top of a box, a plate's face): carve the side that
        ' shows the most relief instead.
        Dim best = chosen, bestSpread = spread
        For Each side In [Enum].GetValues(Of ModelUpAxis)()
            Dim sp = SideSpread(mesh, side, token)
            If sp > bestSpread * 1.0001 Then best = side : bestSpread = sp
        Next
        If best <> chosen Then
            note = "Seen from " & SideName(chosen) & " the model is flat, so it is carved from " & SideName(best) &
                   ", the side with the most relief (change Carved side to pick another)."
        End If
        Return best
    End Function

    ''' <summary>The side as the Carved side row names it.</summary>
    Private Function SideName(side As ModelUpAxis) As String
        Select Case side
            Case ModelUpAxis.PlusZ : Return "+Z (the top)"
            Case ModelUpAxis.MinusZ : Return "-Z (the bottom)"
            Case ModelUpAxis.PlusY : Return "+Y (the back)"
            Case ModelUpAxis.MinusY : Return "-Y (the front)"
            Case ModelUpAxis.PlusX : Return "+X (the right side)"
            Case Else : Return "-X (the left side)"
        End Select
    End Function

    ''' <summary>Standard deviation of the heights seen from one side (coarse).</summary>
    Private Function SideSpread(mesh As Mesh, up As ModelUpAxis, token As CancellationToken) As Double
        Dim copy As New Mesh(CType(mesh.V.Clone(), Single()), mesh.Count, mesh.FormatName)
        Orient(copy, up)
        Dim m = Rasterize(copy, 256, ModelUnits.Inches, up, "", "", token)
        Dim n = 0, sum = 0.0, sum2 = 0.0
        For Each h In m.Heights
            If Single.IsNaN(h) Then Continue For
            n += 1 : sum += h : sum2 += CDbl(h) * h
        Next
        If n = 0 Then Return 0
        Dim mean = sum / n
        Return Math.Sqrt(Math.Max(0, sum2 / n - mean * mean)) * m.NaturalDepth
    End Function

    ''' <summary>Turns the mesh so the carved side faces +Z (right-handed: never mirrored).</summary>
    Private Sub Orient(mesh As Mesh, up As ModelUpAxis)
        If up = ModelUpAxis.PlusZ Then Return
        Dim v = mesh.V
        For i = 0 To mesh.Count * 3 - 1
            Dim x = v(i * 3), y = v(i * 3 + 1), z = v(i * 3 + 2)
            Select Case up
                Case ModelUpAxis.MinusZ : v(i * 3) = x : v(i * 3 + 1) = -y : v(i * 3 + 2) = -z
                Case ModelUpAxis.PlusY : v(i * 3) = x : v(i * 3 + 1) = -z : v(i * 3 + 2) = y
                Case ModelUpAxis.MinusY : v(i * 3) = x : v(i * 3 + 1) = z : v(i * 3 + 2) = -y
                Case ModelUpAxis.PlusX : v(i * 3) = y : v(i * 3 + 1) = z : v(i * 3 + 2) = x
                Case ModelUpAxis.MinusX : v(i * 3) = -y : v(i * 3 + 1) = z : v(i * 3 + 2) = -x
            End Select
        Next
    End Sub

    ' ------------------------------------------------------------ rasterizing

    ''' <summary>Conservative heightmap of the mesh's top (see the file header), res cells on the longer side.</summary>
    Private Function Rasterize(mesh As Mesh, res As Integer, units As ModelUnits, up As ModelUpAxis, format As String, description As String, token As CancellationToken) As ReliefModel
        Dim mn(2) As Double, mx(2) As Double
        Bounds(mesh, mn, mx)
        Dim w = mx(0) - mn(0), h = mx(1) - mn(1)
        If Not (Math.Max(w, h) > 0) Then Throw New InvalidDataException("The model has no extent seen from above.")
        Dim cell = Math.Max(w, h) / res
        Dim cols = Math.Max(1, CInt(Math.Ceiling(w / cell - 0.000001))), rows = Math.Max(1, CInt(Math.Ceiling(h / cell - 0.000001)))
        Dim grid(cols * rows - 1) As Single
        For i = 0 To grid.Length - 1
            grid(i) = Single.NegativeInfinity
        Next
        Dim v = mesh.V
        Dim x0 = mn(0), y0 = mn(1)
        Dim po As New ParallelOptions With {.CancellationToken = token}
        Dim chunk = 4096
        Parallel.For(0, (mesh.Count + chunk - 1) \ chunk, po,
            Sub(ck)
                For t = ck * chunk To Math.Min(mesh.Count, (ck + 1) * chunk) - 1
                    RasterTriangle(v, t, x0, y0, cell, cols, rows, grid)
                Next
            End Sub)
        token.ThrowIfCancellationRequested()
        Dim zmin = Single.PositiveInfinity, zmax = Single.NegativeInfinity
        For Each z In grid
            If Single.IsNegativeInfinity(z) Then Continue For
            If z < zmin Then zmin = z
            If z > zmax Then zmax = z
        Next
        If Single.IsPositiveInfinity(zmin) Then Throw New InvalidDataException("The model has no surface seen from above.")
        Dim range As Single = zmax - zmin
        ' Differences of a millionth of the model's size are rounding in the file, not relief.
        If Not (range > 0.000001 * Math.Max(w, h)) Then range = 0
        Dim warnings As New List(Of String)
        For i = 0 To grid.Length - 1
            If Single.IsNegativeInfinity(grid(i)) Then
                grid(i) = Single.NaN
            Else
                grid(i) = If(range > 0, (grid(i) - zmin) / range, 1.0F)
            End If
        Next
        If range = 0 Then warnings.Add("The model is flat seen from " & SideName(up) & ": pick another Carved side in the Relief rows.")
        Dim k = UnitInches(units)
        Dim m As New ReliefModel(cols, rows, grid, cols * cell * k, rows * cell * k, Math.Max(0, range) * k, units, up, False, format, description)
        m.Warnings.AddRange(warnings)
        Return m
    End Function

    ''' <summary>Raises every cell the triangle touches to the triangle's highest point over that cell.</summary>
    Private Sub RasterTriangle(v As Single(), t As Integer, x0 As Double, y0 As Double, cell As Double, cols As Integer, rows As Integer, grid As Single())
        Dim b = t * 9
        ' Grid units: one cell = 1.
        Dim ax = (v(b) - x0) / cell, ay = (v(b + 1) - y0) / cell, az As Double = v(b + 2)
        Dim bx = (v(b + 3) - x0) / cell, by = (v(b + 4) - y0) / cell, bz As Double = v(b + 5)
        Dim cx = (v(b + 6) - x0) / cell, cy = (v(b + 7) - y0) / cell, cz As Double = v(b + 8)
        ' (a) corners.
        Raise(grid, cols, rows, ax, ay, az)
        Raise(grid, cols, rows, bx, by, bz)
        Raise(grid, cols, rows, cx, cy, cz)
        ' (c) edges crossing grid lines.
        EdgeCrossings(grid, cols, rows, ax, ay, az, bx, by, bz)
        EdgeCrossings(grid, cols, rows, bx, by, bz, cx, cy, cz)
        EdgeCrossings(grid, cols, rows, cx, cy, cz, ax, ay, az)
        ' (b) grid nodes inside the triangle.
        Dim den = (by - cy) * (ax - cx) + (cx - bx) * (ay - cy)
        If Math.Abs(den) < 1.0E-12 Then Return
        Dim nx0 = CInt(Math.Ceiling(Math.Min(ax, Math.Min(bx, cx)))), nx1 = CInt(Math.Floor(Math.Max(ax, Math.Max(bx, cx))))
        Dim ny0 = CInt(Math.Ceiling(Math.Min(ay, Math.Min(by, cy)))), ny1 = CInt(Math.Floor(Math.Max(ay, Math.Max(by, cy))))
        For ny = Math.Max(0, ny0) To Math.Min(rows, ny1)
            For nx = Math.Max(0, nx0) To Math.Min(cols, nx1)
                Dim l1 = ((by - cy) * (nx - cx) + (cx - bx) * (ny - cy)) / den
                Dim l2 = ((cy - ay) * (nx - cx) + (ax - cx) * (ny - cy)) / den
                Dim l3 = 1 - l1 - l2
                If l1 < -0.000000001 OrElse l2 < -0.000000001 OrElse l3 < -0.000000001 Then Continue For
                Dim z = l1 * az + l2 * bz + l3 * cz
                ' The node is a corner of up to four cells.
                RaiseCell(grid, cols, rows, nx - 1, ny - 1, z)
                RaiseCell(grid, cols, rows, nx, ny - 1, z)
                RaiseCell(grid, cols, rows, nx - 1, ny, z)
                RaiseCell(grid, cols, rows, nx, ny, z)
            Next
        Next
    End Sub

    ''' <summary>A point raises the cell it is in (and its neighbours across a line it sits on).</summary>
    Private Sub Raise(grid As Single(), cols As Integer, rows As Integer, x As Double, y As Double, z As Double)
        Dim i = CInt(Math.Floor(x)), j = CInt(Math.Floor(y))
        RaiseCell(grid, cols, rows, i, j, z)
        If x = i Then RaiseCell(grid, cols, rows, i - 1, j, z)
        If y = j Then RaiseCell(grid, cols, rows, i, j - 1, z)
        If x = i AndAlso y = j Then RaiseCell(grid, cols, rows, i - 1, j - 1, z)
    End Sub

    Private Sub EdgeCrossings(grid As Single(), cols As Integer, rows As Integer, px As Double, py As Double, pz As Double, qx As Double, qy As Double, qz As Double)
        ' Vertical grid lines x = X: the two cells either side of the crossing.
        If Math.Abs(qx - px) > 1.0E-12 Then
            For gx = CInt(Math.Ceiling(Math.Min(px, qx))) To CInt(Math.Floor(Math.Max(px, qx)))
                Dim t = (gx - px) / (qx - px)
                If t < 0 OrElse t > 1 Then Continue For
                Dim y = py + (qy - py) * t, z = pz + (qz - pz) * t
                Dim j = CInt(Math.Floor(y))
                RaiseCell(grid, cols, rows, gx - 1, j, z)
                RaiseCell(grid, cols, rows, gx, j, z)
                If y = j Then
                    RaiseCell(grid, cols, rows, gx - 1, j - 1, z)
                    RaiseCell(grid, cols, rows, gx, j - 1, z)
                End If
            Next
        End If
        ' Horizontal grid lines y = Y.
        If Math.Abs(qy - py) > 1.0E-12 Then
            For gy = CInt(Math.Ceiling(Math.Min(py, qy))) To CInt(Math.Floor(Math.Max(py, qy)))
                Dim t = (gy - py) / (qy - py)
                If t < 0 OrElse t > 1 Then Continue For
                Dim x = px + (qx - px) * t, z = pz + (qz - pz) * t
                Dim i = CInt(Math.Floor(x))
                RaiseCell(grid, cols, rows, i, gy - 1, z)
                RaiseCell(grid, cols, rows, i, gy, z)
                If x = i Then
                    RaiseCell(grid, cols, rows, i - 1, gy - 1, z)
                    RaiseCell(grid, cols, rows, i - 1, gy, z)
                End If
            Next
        End If
    End Sub

    ''' <summary>Atomic max (many threads raise cells at once). Rounded up to stay at or above the model.</summary>
    Private Sub RaiseCell(grid As Single(), cols As Integer, rows As Integer, i As Integer, j As Integer, z As Double)
        ' Points on the far edge of the grid belong to the last cell.
        If i = cols Then i = cols - 1
        If j = rows Then j = rows - 1
        If i < 0 OrElse j < 0 OrElse i >= cols OrElse j >= rows Then Return
        Dim zs = CSng(z)
        If zs < z Then zs = MathF.BitIncrement(zs)
        Dim idx = j * cols + i
        Dim cur = Volatile.Read(grid(idx))
        While zs > cur
            Dim seen = Interlocked.CompareExchange(grid(idx), zs, cur)
            If seen = cur Then Return
            cur = seen
        End While
    End Sub

    ' ------------------------------------------------------------ STL

    Private Function ReadStl(path As String) As Mesh
        Dim bytes = File.ReadAllBytes(path)
        If bytes.Length >= 84 Then
            Dim n = BitConverter.ToUInt32(bytes, 80)
            If 84L + 50L * n = bytes.Length Then Return ReadBinaryStl(bytes, CInt(n))
        End If
        Dim head = Encoding.ASCII.GetString(bytes, 0, Math.Min(bytes.Length, 512)).TrimStart()
        If head.StartsWith("solid", StringComparison.OrdinalIgnoreCase) AndAlso head.IndexOf("facet", StringComparison.OrdinalIgnoreCase) >= 0 Then
            Return ReadAsciiStl(Encoding.ASCII.GetString(bytes))
        End If
        If bytes.Length >= 84 Then Throw New InvalidDataException("The STL file is cut short or damaged (its size does not match its triangle count).")
        Throw New InvalidDataException("This is not an STL file.")
    End Function

    Private Function ReadBinaryStl(bytes As Byte(), n As Integer) As Mesh
        Dim v(n * 9 - 1) As Single
        Parallel.For(0, n,
            Sub(t)
                Dim off = 84 + t * 50 + 12
                For k = 0 To 8
                    v(t * 9 + k) = BitConverter.ToSingle(bytes, off + k * 4)
                Next
            End Sub)
        Return New Mesh(v, n, "STL (binary)")
    End Function

    Private Function ReadAsciiStl(text As String) As Mesh
        Dim verts As New List(Of Single)
        For Each raw In text.Split(ControlChars.Lf)
            Dim line = raw.Trim()
            If line.StartsWith("vertex", StringComparison.OrdinalIgnoreCase) Then
                Dim p = line.Split(New Char() {" "c, ControlChars.Tab}, StringSplitOptions.RemoveEmptyEntries)
                If p.Length < 4 Then Throw New InvalidDataException("An STL vertex line is incomplete: " & line)
                For k = 1 To 3
                    verts.Add(ParseF(p(k)))
                Next
            End If
        Next
        If verts.Count Mod 9 <> 0 Then Throw New InvalidDataException("The STL file has a facet without three vertices.")
        Return New Mesh(verts.ToArray(), verts.Count \ 9, "STL (ASCII)")
    End Function

    Private Function ParseF(s As String) As Single
        Dim d As Double
        If Not Double.TryParse(s, NumberStyles.Float, Ci, d) Then Throw New InvalidDataException("'" & s & "' is not a number.")
        Return CSng(d)
    End Function

    ' ------------------------------------------------------------ OBJ

    Private Function ReadObj(path As String) As Mesh
        Dim verts As New List(Of Single)
        Dim faces As New List(Of Integer())
        For Each raw In File.ReadLines(path)
            Dim line = raw.Trim()
            If line.Length < 2 Then Continue For
            If line.StartsWith("v ") OrElse line.StartsWith("v" & ControlChars.Tab) Then
                Dim p = line.Split(New Char() {" "c, ControlChars.Tab}, StringSplitOptions.RemoveEmptyEntries)
                If p.Length < 4 Then Throw New InvalidDataException("An OBJ vertex is incomplete: " & line)
                verts.Add(ParseF(p(1))) : verts.Add(ParseF(p(2))) : verts.Add(ParseF(p(3)))
            ElseIf line.StartsWith("f ") OrElse line.StartsWith("f" & ControlChars.Tab) Then
                Dim p = line.Split(New Char() {" "c, ControlChars.Tab}, StringSplitOptions.RemoveEmptyEntries)
                Dim f(p.Length - 2) As Integer
                For k = 1 To p.Length - 1
                    Dim first = p(k).Split("/"c)(0)
                    Dim idx As Integer
                    If Not Integer.TryParse(first, NumberStyles.Integer, Ci, idx) OrElse idx = 0 Then Throw New InvalidDataException("An OBJ face index is not valid: " & line)
                    ' Negative indices count back from the vertices read so far.
                    f(k - 1) = If(idx > 0, idx - 1, verts.Count \ 3 + idx)
                Next
                faces.Add(f)
            End If
        Next
        Return FromFaces(verts, faces, "OBJ")
    End Function

    ' ------------------------------------------------------------ OFF

    Private Function ReadOff(path As String) As Mesh
        Dim tokens As New List(Of String)
        For Each raw In File.ReadLines(path)
            Dim line = raw
            Dim hash = line.IndexOf("#"c)
            If hash >= 0 Then line = line.Substring(0, hash)
            tokens.AddRange(line.Split(New Char() {" "c, ControlChars.Tab, ControlChars.Cr}, StringSplitOptions.RemoveEmptyEntries))
        Next
        If tokens.Count = 0 OrElse Not tokens(0).ToUpperInvariant().EndsWith("OFF") Then Throw New InvalidDataException("This is not an OFF file.")
        Dim pos = 1
        Dim nextInt = Function() As Integer
                          If pos >= tokens.Count Then Throw New InvalidDataException("The OFF file is cut short.")
                          Dim v As Integer
                          If Not Integer.TryParse(tokens(pos), NumberStyles.Integer, Ci, v) Then Throw New InvalidDataException("'" & tokens(pos) & "' is not a count.")
                          pos += 1
                          Return v
                      End Function
        Dim nv = nextInt(), nf = nextInt()
        nextInt()     ' edges
        Dim verts As New List(Of Single)(nv * 3)
        For i = 0 To nv * 3 - 1
            If pos >= tokens.Count Then Throw New InvalidDataException("The OFF file is cut short.")
            verts.Add(ParseF(tokens(pos)))
            pos += 1
        Next
        Dim faces As New List(Of Integer())(nf)
        For i = 0 To nf - 1
            Dim n = nextInt()
            Dim f(n - 1) As Integer
            For k = 0 To n - 1
                f(k) = nextInt()
            Next
            faces.Add(f)
        Next
        Return FromFaces(verts, faces, "OFF")
    End Function

    ' ------------------------------------------------------------ PLY

    Private Function ReadPly(path As String) As Mesh
        Dim bytes = File.ReadAllBytes(path)
        ' Header: ASCII lines up to end_header.
        Dim headerEnd = -1
        Dim text = Encoding.ASCII.GetString(bytes, 0, Math.Min(bytes.Length, 65536))
        Dim eh = text.IndexOf("end_header", StringComparison.Ordinal)
        If Not text.StartsWith("ply") OrElse eh < 0 Then Throw New InvalidDataException("This is not a PLY file.")
        headerEnd = text.IndexOf(ControlChars.Lf, eh) + 1
        Dim header = text.Substring(0, headerEnd).Replace(vbCr, "").Split(ControlChars.Lf)
        Dim fmt = ""
        ' Elements in order with their properties: (name, count, props); a prop is (name, type, listCountType).
        Dim elements As New List(Of (Name As String, Count As Integer, Props As List(Of (Name As String, Type As String, ListCount As String))))
        For Each line In header
            Dim p = line.Split(New Char() {" "c}, StringSplitOptions.RemoveEmptyEntries)
            If p.Length = 0 Then Continue For
            Select Case p(0)
                Case "format" : fmt = If(p.Length > 1, p(1), "")
                Case "element" : elements.Add((p(1), Integer.Parse(p(2), Ci), New List(Of (Name As String, Type As String, ListCount As String))))
                Case "property"
                    If elements.Count = 0 Then Throw New InvalidDataException("A PLY property comes before any element.")
                    If p(1) = "list" Then
                        elements(elements.Count - 1).Props.Add((p(4), p(3), p(2)))
                    Else
                        elements(elements.Count - 1).Props.Add((p(2), p(1), Nothing))
                    End If
            End Select
        Next
        Dim verts As New List(Of Single)
        Dim faces As New List(Of Integer())
        If fmt = "ascii" Then
            Dim tokens = Encoding.ASCII.GetString(bytes, headerEnd, bytes.Length - headerEnd).Split(New Char() {" "c, ControlChars.Lf, ControlChars.Cr, ControlChars.Tab}, StringSplitOptions.RemoveEmptyEntries)
            Dim pos = 0
            Dim nextNum = Function() As Double
                              If pos >= tokens.Length Then Throw New InvalidDataException("The PLY file is cut short.")
                              Dim d = Double.Parse(tokens(pos), NumberStyles.Float, Ci)
                              pos += 1
                              Return d
                          End Function
            For Each el In elements
                For n = 0 To el.Count - 1
                    Dim xyz(2) As Single
                    For Each pr In el.Props
                        If pr.ListCount IsNot Nothing Then
                            Dim cnt = CInt(nextNum())
                            Dim f(cnt - 1) As Integer
                            For k = 0 To cnt - 1
                                f(k) = CInt(nextNum())
                            Next
                            If el.Name = "face" AndAlso (pr.Name = "vertex_indices" OrElse pr.Name = "vertex_index") Then faces.Add(f)
                        Else
                            Dim val = nextNum()
                            If el.Name = "vertex" Then
                                If pr.Name = "x" Then xyz(0) = CSng(val)
                                If pr.Name = "y" Then xyz(1) = CSng(val)
                                If pr.Name = "z" Then xyz(2) = CSng(val)
                            End If
                        End If
                    Next
                    If el.Name = "vertex" Then verts.AddRange(xyz)
                Next
            Next
        ElseIf fmt = "binary_little_endian" OrElse fmt = "binary_big_endian" Then
            Dim big = fmt = "binary_big_endian"
            Dim pos = headerEnd
            Dim readNum = Function(type As String) As Double
                              Dim size = TypeSize(type)
                              If pos + size > bytes.Length Then Throw New InvalidDataException("The PLY file is cut short.")
                              Dim b(size - 1) As Byte
                              Array.Copy(bytes, pos, b, 0, size)
                              If big <> Not BitConverter.IsLittleEndian Then Array.Reverse(b)
                              pos += size
                              Select Case type
                                  Case "char", "int8" : Return CSByte(If(b(0) > 127, CInt(b(0)) - 256, CInt(b(0))))
                                  Case "uchar", "uint8" : Return b(0)
                                  Case "short", "int16" : Return BitConverter.ToInt16(b, 0)
                                  Case "ushort", "uint16" : Return BitConverter.ToUInt16(b, 0)
                                  Case "int", "int32" : Return BitConverter.ToInt32(b, 0)
                                  Case "uint", "uint32" : Return BitConverter.ToUInt32(b, 0)
                                  Case "float", "float32" : Return BitConverter.ToSingle(b, 0)
                                  Case Else : Return BitConverter.ToDouble(b, 0)
                              End Select
                          End Function
            For Each el In elements
                For n = 0 To el.Count - 1
                    Dim xyz(2) As Single
                    For Each pr In el.Props
                        If pr.ListCount IsNot Nothing Then
                            Dim cnt = CInt(readNum(pr.ListCount))
                            Dim f(cnt - 1) As Integer
                            For k = 0 To cnt - 1
                                f(k) = CInt(readNum(pr.Type))
                            Next
                            If el.Name = "face" AndAlso (pr.Name = "vertex_indices" OrElse pr.Name = "vertex_index") Then faces.Add(f)
                        Else
                            Dim val = readNum(pr.Type)
                            If el.Name = "vertex" Then
                                If pr.Name = "x" Then xyz(0) = CSng(val)
                                If pr.Name = "y" Then xyz(1) = CSng(val)
                                If pr.Name = "z" Then xyz(2) = CSng(val)
                            End If
                        End If
                    Next
                    If el.Name = "vertex" Then verts.AddRange(xyz)
                Next
            Next
        Else
            Throw New InvalidDataException("Unknown PLY format '" & fmt & "'.")
        End If
        Return FromFaces(verts, faces, "PLY")
    End Function

    Private Function TypeSize(type As String) As Integer
        Select Case type
            Case "char", "uchar", "int8", "uint8" : Return 1
            Case "short", "ushort", "int16", "uint16" : Return 2
            Case "int", "uint", "float", "int32", "uint32", "float32" : Return 4
            Case "double", "float64" : Return 8
        End Select
        Throw New InvalidDataException("Unknown PLY type '" & type & "'.")
    End Function

    ' ------------------------------------------------------------ 3MF

    Private Function Read3mf(path As String) As Mesh
        Using zip = ZipFile.OpenRead(path)
            Dim entry = zip.Entries.FirstOrDefault(Function(e) e.FullName.EndsWith(".model", StringComparison.OrdinalIgnoreCase) AndAlso e.FullName.StartsWith("3D/", StringComparison.OrdinalIgnoreCase))
            If entry Is Nothing Then entry = zip.Entries.FirstOrDefault(Function(e) e.FullName.EndsWith(".model", StringComparison.OrdinalIgnoreCase))
            If entry Is Nothing Then Throw New InvalidDataException("The 3MF file has no 3D model in it.")
            Dim doc As XDocument
            Using st = entry.Open()
                doc = XDocument.Load(st)
            End Using
            Dim root = doc.Root
            Dim ns = root.Name.Namespace
            Dim units As ModelUnits? = ModelUnits.Millimeters
            Select Case If(root.Attribute("unit")?.Value, "millimeter").ToLowerInvariant()
                Case "micron" : units = ModelUnits.Millimeters      ' scaled below
                Case "centimeter" : units = ModelUnits.Centimeters
                Case "inch" : units = ModelUnits.Inches
                Case "foot" : units = ModelUnits.Feet
                Case "meter" : units = ModelUnits.Meters
            End Select
            Dim micron = String.Equals(root.Attribute("unit")?.Value, "micron", StringComparison.OrdinalIgnoreCase)
            Dim objects As New Dictionary(Of String, XElement)
            For Each o In root.Descendants(ns + "object")
                Dim id = o.Attribute("id")?.Value
                If id IsNot Nothing Then objects(id) = o
            Next
            Dim tris As New List(Of Single)
            Dim build = root.Element(ns + "build")
            Dim items = If(build Is Nothing, objects.Values.Select(Function(o) New XElement(ns + "item", New XAttribute("objectid", o.Attribute("id").Value))).ToList(), build.Elements(ns + "item").ToList())
            For Each it In items
                AddObject3mf(objects, ns, it.Attribute("objectid")?.Value, ParseTransform(it.Attribute("transform")?.Value), tris, 0)
            Next
            If micron Then
                For i = 0 To tris.Count - 1
                    tris(i) /= 1000.0F
                Next
            End If
            Return New Mesh(tris.ToArray(), tris.Count \ 9, "3MF", units)
        End Using
    End Function

    Private Sub AddObject3mf(objects As Dictionary(Of String, XElement), ns As XNamespace, id As String, m As Double(), tris As List(Of Single), depth As Integer)
        If depth > 32 Then Throw New InvalidDataException("The 3MF components nest too deep.")
        Dim o As XElement = Nothing
        If id Is Nothing OrElse Not objects.TryGetValue(id, o) Then Throw New InvalidDataException("The 3MF file refers to a missing object (" & id & ").")
        Dim mesh = o.Element(ns + "mesh")
        If mesh IsNot Nothing Then
            Dim vs As New List(Of Double)
            For Each v In mesh.Element(ns + "vertices").Elements(ns + "vertex")
                vs.Add(Double.Parse(v.Attribute("x").Value, Ci)) : vs.Add(Double.Parse(v.Attribute("y").Value, Ci)) : vs.Add(Double.Parse(v.Attribute("z").Value, Ci))
            Next
            Dim nv = vs.Count \ 3
            For Each t In mesh.Element(ns + "triangles").Elements(ns + "triangle")
                For Each a In {"v1", "v2", "v3"}
                    Dim idx = Integer.Parse(t.Attribute(a).Value, Ci)
                    If idx < 0 OrElse idx >= nv Then Throw New InvalidDataException("A 3MF triangle uses a vertex that does not exist.")
                    Dim x = vs(idx * 3), y = vs(idx * 3 + 1), z = vs(idx * 3 + 2)
                    tris.Add(CSng(m(0) * x + m(3) * y + m(6) * z + m(9)))
                    tris.Add(CSng(m(1) * x + m(4) * y + m(7) * z + m(10)))
                    tris.Add(CSng(m(2) * x + m(5) * y + m(8) * z + m(11)))
                Next
            Next
        End If
        Dim comps = o.Element(ns + "components")
        If comps IsNot Nothing Then
            For Each c In comps.Elements(ns + "component")
                AddObject3mf(objects, ns, c.Attribute("objectid")?.Value, Combine(ParseTransform(c.Attribute("transform")?.Value), m), tris, depth + 1)
            Next
        End If
    End Sub

    ''' <summary>3MF transform "m00 m01 m02 m10 m11 m12 m20 m21 m22 m30 m31 m32" (row vectors: p' = p * M).</summary>
    Private Function ParseTransform(s As String) As Double()
        If String.IsNullOrWhiteSpace(s) Then Return {1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0}
        Dim p = s.Split(New Char() {" "c, ControlChars.Tab, ControlChars.Lf, ControlChars.Cr}, StringSplitOptions.RemoveEmptyEntries)
        If p.Length <> 12 Then Throw New InvalidDataException("A 3MF transform does not have 12 numbers.")
        Return p.Select(Function(x) Double.Parse(x, Ci)).ToArray()
    End Function

    ''' <summary>Applies a first, then b (row vectors).</summary>
    Private Function Combine(a As Double(), b As Double()) As Double()
        Dim r(11) As Double
        For row = 0 To 3
            For col = 0 To 2
                Dim v = If(row = 3, b(9 + col), 0.0)
                For k = 0 To 2
                    v += a(row * 3 + k) * b(k * 3 + col)
                Next
                r(row * 3 + col) = v
            Next
        Next
        Return r
    End Function

    ' ------------------------------------------------------------ images

    Private Function LoadImage(path As String, options As ModelImportOptions) As ReliefModel
        Using bmp As New Bitmap(path)
            Dim w = bmp.Width, h = bmp.Height
            Dim heights(w * h - 1) As Single
            Dim data = bmp.LockBits(New Rectangle(0, 0, w, h), Imaging.ImageLockMode.ReadOnly, Imaging.PixelFormat.Format32bppArgb)
            Dim px(w * h - 1) As Integer
            Try
                For y = 0 To h - 1
                    Runtime.InteropServices.Marshal.Copy(data.Scan0 + y * data.Stride, px, y * w, w)
                Next
            Finally
                bmp.UnlockBits(data)
            End Try
            Dim lo = Single.PositiveInfinity, hi = Single.NegativeInfinity
            For y = 0 To h - 1
                For x = 0 To w - 1
                    Dim c = px(y * w + x)
                    Dim a = (c >> 24) And &HFF, r = (c >> 16) And &HFF, g = (c >> 8) And &HFF, b = c And &HFF
                    Dim idx = (h - 1 - y) * w + x          ' row 0 of the heightmap is the bottom
                    If a < 128 Then
                        heights(idx) = Single.NaN
                        Continue For
                    End If
                    Dim lum = CSng((0.299 * r + 0.587 * g + 0.114 * b) / 255.0)
                    If options.InvertImage Then lum = 1.0F - lum
                    heights(idx) = lum
                    If lum < lo Then lo = lum
                    If lum > hi Then hi = lum
                Next
            Next
            If Single.IsPositiveInfinity(lo) Then Throw New InvalidDataException("The image is fully transparent.")
            Dim warnings As New List(Of String)
            For i = 0 To heights.Length - 1
                If Not Single.IsNaN(heights(i)) Then heights(i) = If(hi > lo, (heights(i) - lo) / (hi - lo), 1.0F)
            Next
            If Not (hi > lo) Then warnings.Add("The image is one flat shade: it carves as a flat area.")
            Dim dpiX = If(bmp.HorizontalResolution > 1, bmp.HorizontalResolution, 96.0F)
            Dim dpiY = If(bmp.VerticalResolution > 1, bmp.VerticalResolution, 96.0F)
            Dim m As New ReliefModel(w, h, heights, w / dpiX, h / dpiY, Math.Max(0.001, options.ImageDepth), ModelUnits.Inches, ModelUpAxis.PlusZ, True,
                                     IO.Path.GetExtension(path).TrimStart("."c).ToUpperInvariant() & " image", w.ToString(Ci) & " x " & h.ToString(Ci) & " pixels")
            m.Warnings.AddRange(warnings)
            Return m
        End Using
    End Function
End Module
