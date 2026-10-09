' ============================================================================
'  ToolpathView.vb
'  OpenTK GLControl subclass that draws the blank, the glyph outline, the
'  V-carve passes (coloured by depth), rapid moves and a 1" grid.
'  Orthographic camera with pan (left/middle drag), orbit (right drag) and
'  zoom (wheel, about the cursor).
'
'  Lifecycle notes (OpenTK.GLControl 4.0.2): the GL context is created inside
'  OnHandleCreated and destroyed inside OnHandleDestroyed, and MakeCurrent()
'  recreates the handle if it is gone. GL objects are therefore released in
'  OnHandleDestroyed (while the context is still alive) and re-uploaded when a
'  new handle appears. A failed context creation is caught once and the view
'  falls back to a plain label instead of raising on every paint.
' ============================================================================

Imports OpenTK.GLControl
Imports OpenTK.Graphics.OpenGL4
Imports OpenTK.Mathematics
Imports OpenTK.Windowing.Common

Public Class ToolpathView
    Inherits GLControl

    ' ---- GL objects -------------------------------------------------------
    Private _program As Integer
    Private _mvpLoc As Integer
    Private _initialized As Boolean
    Private _initError As String
    Private _contextFailed As Boolean
    Private _errorLabel As Label

    Private Class LineLayer
        Public Vao As Integer
        Public Vbo As Integer
        Public VertexCount As Integer
        Public Data As Single() = New Single() {}   ' last vertex data; re-uploaded after a handle recreation
        Public Dirty As Boolean
    End Class

    Private ReadOnly _grid As New LineLayer()
    Private ReadOnly _blank As New LineLayer()
    Private ReadOnly _outline As New LineLayer()
    Private ReadOnly _cuts As New LineLayer()
    Private ReadOnly _rapids As New LineLayer()

    Private ReadOnly Property AllLayers As LineLayer()
        Get
            Return New LineLayer() {_grid, _blank, _outline, _cuts, _rapids, _selectionLayer}
        End Get
    End Property

    ' ---------------------------------------------------- object editing API

    ''' <summary>The live list of drawings (shared with the form).</summary>
    Public Property Objects As List(Of DesignObject)
        Get
            Return _objects
        End Get
        Set(value As List(Of DesignObject))
            _objects = If(value, New List(Of DesignObject))
            If _selected >= _objects.Count Then _selected = -1
            RebuildSelectionLayer()
            Invalidate()
        End Set
    End Property

    ''' <summary>Flattening tolerance used when placing drawings for the preview.</summary>
    Public Property CurveTolerance As Double
        Get
            Return _curveTolerance
        End Get
        Set(value As Double)
            _curveTolerance = Math.Max(0.0001, value)
        End Set
    End Property

    ''' <summary>Index into Objects, TextSelection (-2) for the text block, or -1 for nothing.</summary>
    Public Property SelectedObject As Integer
        Get
            Return _selected
        End Get
        Set(value As Integer)
            If value >= _objects.Count Then value = -1
            If value < -2 Then value = -1
            If _selected = value Then Return
            _selected = value
            RebuildSelectionLayer()
            Invalidate()
            RaiseEvent SelectionChanged(Me, EventArgs.Empty)
        End Set
    End Property

    Private Function HasText() As Boolean
        Return _toolpath IsNot Nothing AndAlso _toolpath.TextMaxX > _toolpath.TextMinX AndAlso _toolpath.TextMaxY > _toolpath.TextMinY
    End Function

    ''' <summary>Object under a world point: topmost drawing first, then the text block.</summary>
    Private Function HitTest(wx As Double, wy As Double) As Integer
        For i = _objects.Count - 1 To 0 Step -1
            If _objects(i).Visible AndAlso _objects(i).HitTest(wx, wy) Then Return i
        Next
        If HasText() Then
            Dim tp = _toolpath
            If wx >= tp.TextMinX AndAlso wx <= tp.TextMaxX AndAlso wy >= tp.TextMinY AndAlso wy <= tp.TextMaxY Then Return TextSelection
        End If
        Return -1
    End Function

    ''' <summary>Corner handle of the selected drawing under the mouse (0..3), or -1.</summary>
    Private Function HandleHitTest(p As Point) As Integer
        If _selected < 0 OrElse _selected >= _objects.Count Then Return -1
        Dim corners = _objects(_selected).Corners()
        Dim w = ScreenToWorld(p)
        Dim tol As Double = 8 * _unitsPerPixel
        For i = 0 To 3
            If Math.Abs(corners(i).X - w.X) <= tol AndAlso Math.Abs(corners(i).Y - w.Y) <= tol Then Return i
        Next
        Return -1
    End Function

    ''' <summary>Highlight of the selected object: its outline (with live drag preview), box and handles.</summary>
    Private Sub RebuildSelectionLayer()
        Dim buf As New List(Of Single)
        Dim hr As Single = 1.0F, hg As Single = 0.85F, hb As Single = 0.2F
        If _selected >= 0 AndAlso _selected < _objects.Count Then
            Dim o = _objects(_selected)
            Dim preview As DesignObject = o
            If _mode = DragMode.MoveObject OrElse _mode = DragMode.Resize Then
                preview = o.Clone()
                If _mode = DragMode.MoveObject Then
                    preview.X = o.X + _dragDx : preview.Y = o.Y + _dragDy
                Else
                    preview.X = _resizeX : preview.Y = _resizeY : preview.Width = _resizeW : preview.Height = _resizeH
                End If
            End If
            For Each path In preview.PlacedPaths(_curveTolerance)
                For i = 0 To path.Count - 1
                    Dim a = path(i), b = path((i + 1) Mod path.Count)
                    AddLine(buf, a.x, a.y, 0.0005, b.x, b.y, 0.0005, hr, hg, hb)
                Next
            Next
            Dim c = preview.Corners()
            For i = 0 To 3
                Dim a = c(i), b = c((i + 1) Mod 4)
                AddLine(buf, a.X, a.Y, 0.001, b.X, b.Y, 0.001, 0.4F, 0.8F, 1.0F)
            Next
            ' Handles: small squares in screen size.
            Dim h As Double = 5 * _unitsPerPixel
            For i = 0 To 3
                Dim p = c(i)
                AddLine(buf, p.X - h, p.Y - h, 0.002, p.X + h, p.Y - h, 0.002, 1, 1, 1)
                AddLine(buf, p.X + h, p.Y - h, 0.002, p.X + h, p.Y + h, 0.002, 1, 1, 1)
                AddLine(buf, p.X + h, p.Y + h, 0.002, p.X - h, p.Y + h, 0.002, 1, 1, 1)
                AddLine(buf, p.X - h, p.Y + h, 0.002, p.X - h, p.Y - h, 0.002, 1, 1, 1)
            Next
        ElseIf _selected = TextSelection AndAlso HasText() Then
            Dim tp = _toolpath
            Dim dx As Double = If(_mode = DragMode.MoveText, _dragDx, 0), dy As Double = If(_mode = DragMode.MoveText, _dragDy, 0)
            Dim x0 = tp.TextMinX + dx, x1 = tp.TextMaxX + dx, y0 = tp.TextMinY + dy, y1 = tp.TextMaxY + dy
            AddLine(buf, x0, y0, 0.001, x1, y0, 0.001, 0.4F, 0.8F, 1.0F)
            AddLine(buf, x1, y0, 0.001, x1, y1, 0.001, 0.4F, 0.8F, 1.0F)
            AddLine(buf, x1, y1, 0.001, x0, y1, 0.001, 0.4F, 0.8F, 1.0F)
            AddLine(buf, x0, y1, 0.001, x0, y0, 0.001, 0.4F, 0.8F, 1.0F)
            If _mode = DragMode.MoveText Then
                For Each poly In tp.Outline
                    For i = 0 To poly.Count - 1
                        Dim a = poly(i), b = poly((i + 1) Mod poly.Count)
                        If a.X >= tp.TextMinX - 0.001 AndAlso a.X <= tp.TextMaxX + 0.001 AndAlso a.Y >= tp.TextMinY - 0.001 AndAlso a.Y <= tp.TextMaxY + 0.001 Then
                            AddLine(buf, a.X + dx, a.Y + dy, 0.0005, b.X + dx, b.Y + dy, 0.0005, hr, hg, hb)
                        End If
                    Next
                Next
            End If
        End If
        SetPending(_selectionLayer, buf)
    End Sub

    ''' <summary>Resize preview for the dragged corner; keeps the opposite corner fixed.</summary>
    Private Sub ComputeResize(wx As Double, wy As Double)
        Dim o = _objects(_selected)
        Dim rot = o.RotationDeg * Math.PI / 180
        Dim c = Math.Cos(rot), s = Math.Sin(rot)
        Dim cx = _startX + _startW / 2, cy = _startY + _startH / 2
        ' Mouse and the fixed (opposite) corner in the object's local, unrotated frame.
        Dim dx = wx - cx, dy = wy - cy
        Dim lx = dx * c + dy * s, ly = -dx * s + dy * c
        Dim sx = If(_resizeHandle = 1 OrElse _resizeHandle = 2, 1.0, -1.0)   ' dragged corner side
        Dim sy = If(_resizeHandle = 2 OrElse _resizeHandle = 3, 1.0, -1.0)
        Dim fixX = -sx * _startW / 2, fixY = -sy * _startH / 2
        Dim w = Math.Max(0.05, (lx - fixX) * sx)
        Dim h = Math.Max(0.05, (ly - fixY) * sy)
        If o.LockAspect Then
            Dim a = If(_startH > 0, _startW / _startH, 1.0)
            If w / a >= h Then h = w / a Else w = h * a
        End If
        ' New centre in local coordinates, then back to world.
        Dim ncx = fixX + sx * w / 2, ncy = fixY + sy * h / 2
        Dim wcx = cx + ncx * c - ncy * s, wcy = cy + ncx * s + ncy * c
        _resizeW = w : _resizeH = h
        _resizeX = wcx - w / 2 : _resizeY = wcy - h / 2
    End Sub

    ''' <summary>Moves the selection by a keyboard nudge (inches).</summary>
    Public Sub NudgeSelection(dx As Double, dy As Double)
        If _selected >= 0 AndAlso _selected < _objects.Count Then
            _objects(_selected).X = Math.Round(_objects(_selected).X + dx, 4)
            _objects(_selected).Y = Math.Round(_objects(_selected).Y + dy, 4)
            RebuildSelectionLayer()
            Invalidate()
            RaiseEvent ObjectEdited(Me, New ObjectEditedEventArgs With {.Index = _selected})
        ElseIf _selected = TextSelection Then
            RaiseEvent TextMoved(Me, New TextMovedEventArgs With {.Dx = dx, .Dy = dy})
        End If
    End Sub

    Protected Overrides Function IsInputKey(keyData As Keys) As Boolean
        Select Case keyData And Keys.KeyCode
            Case Keys.Left, Keys.Right, Keys.Up, Keys.Down
                Return True
        End Select
        Return MyBase.IsInputKey(keyData)
    End Function

    Protected Overrides Sub OnKeyDown(e As KeyEventArgs)
        MyBase.OnKeyDown(e)
        If _selected = -1 Then Return
        Dim stepIn As Double = If(e.Shift, 0.1, 0.01)
        Select Case e.KeyCode
            Case Keys.Left : NudgeSelection(-stepIn, 0) : e.Handled = True
            Case Keys.Right : NudgeSelection(stepIn, 0) : e.Handled = True
            Case Keys.Up : NudgeSelection(0, stepIn) : e.Handled = True
            Case Keys.Down : NudgeSelection(0, -stepIn) : e.Handled = True
        End Select
    End Sub

    ' ---- scene ------------------------------------------------------------
    Private _toolpath As Toolpath
    Private _sceneMin As New Vector3(-1, -1, 0)
    Private _sceneMax As New Vector3(1, 1, 0)

    ' ---- camera -----------------------------------------------------------
    Private _target As New Vector3(0, 0, 0)
    Private _unitsPerPixel As Double = 0.01   ' zoom
    Private _yaw As Double = 0                ' radians about Z
    Private _pitch As Double = 0              ' radians about X; 0 = straight down

    ' ---- interaction ------------------------------------------------------
    Private _dragButton As MouseButtons = MouseButtons.None
    Private _lastMouse As Point

    ' ---- design objects (select / drag / resize) ---------------------------
    Private Enum DragMode
        None
        Pan
        Orbit
        MoveObject
        MoveText
        Resize
    End Enum

    ''' <summary>Selection value meaning "the text block".</summary>
    Public Const TextSelection As Integer = -2

    Private _objects As List(Of DesignObject) = New List(Of DesignObject)
    Private _selected As Integer = -1
    Private _curveTolerance As Double = 0.0005
    Private _mode As DragMode = DragMode.None
    Private _dragStart As Vector3                ' world point where the drag began
    Private _dragDx As Double, _dragDy As Double ' live move delta (inches)
    Private _resizeHandle As Integer = -1
    Private _resizeW As Double, _resizeH As Double, _resizeX As Double, _resizeY As Double   ' live resize result
    Private _startX, _startY, _startW, _startH As Double
    Private ReadOnly _selectionLayer As New LineLayer()

    ''' <summary>Raised after the user moved or resized an object with the mouse or keyboard (index in Objects).</summary>
    Public Event ObjectEdited As EventHandler(Of ObjectEditedEventArgs)
    ''' <summary>Raised after the user dragged the text block (delta in inches).</summary>
    Public Event TextMoved As EventHandler(Of TextMovedEventArgs)
    ''' <summary>Raised when the selection changes (SelectedObject).</summary>
    Public Event SelectionChanged As EventHandler

    Public Class ObjectEditedEventArgs
        Inherits EventArgs
        Public Property Index As Integer
    End Class

    Public Class TextMovedEventArgs
        Inherits EventArgs
        Public Property Dx As Double
        Public Property Dy As Double
    End Class

    Private _showRapids As Boolean = True
    Private _showOutline As Boolean = True
    Private _showGrid As Boolean = True

    ' ---- material-removal simulation ---------------------------------------
    Private ReadOnly _sim As New CarveSimulation()
    Private _simSettings As CarveSettings
    Private _simNeedsSetup As Boolean
    Private _showSimulation As Boolean
    Private _simPlaying As Boolean
    Private _simSpeed As Double = 1.0           ' Double.PositiveInfinity = instant
    Private _simTime As Double                  ' target simulated time (s)
    Private _simLastTick As DateTime
    Private WithEvents _simTimer As New Timer() With {.Interval = 33}

    ''' <summary>Raised on the UI thread whenever the simulation clock or state changes.</summary>
    Public Event SimulationProgress As EventHandler
    ''' <summary>Raised once the OpenGL context and shaders are usable (hardware limits are known).</summary>
    Public Event GLReady As EventHandler

    Public Sub New()
        MyBase.New(New GLControlSettings() With {
            .APIVersion = New Version(3, 3),
            .Profile = ContextProfile.Core,
            .Flags = ContextFlags.ForwardCompatible,
            .NumberOfSamples = 4
        })
        SetStyle(ControlStyles.ResizeRedraw, True)
        TabStop = True
    End Sub

    ' ------------------------------------------------------------ properties

    ''' <summary>Non-empty when OpenGL could not be used; the view then shows this text.</summary>
    Public ReadOnly Property InitError As String
        Get
            Return _initError
        End Get
    End Property

    Public Property ShowRapids As Boolean
        Get
            Return _showRapids
        End Get
        Set(value As Boolean)
            _showRapids = value
            Invalidate()
        End Set
    End Property

    Public Property ShowOutline As Boolean
        Get
            Return _showOutline
        End Get
        Set(value As Boolean)
            _showOutline = value
            Invalidate()
        End Set
    End Property

    Public Property ShowGrid As Boolean
        Get
            Return _showGrid
        End Get
        Set(value As Boolean)
            _showGrid = value
            Invalidate()
        End Set
    End Property

    ''' <summary>Replaces the displayed toolpath and refits the view the first time.</summary>
    Public Sub SetToolpath(tp As Toolpath)
        SetToolpath(tp, Nothing)
    End Sub

    ''' <summary>Replaces the toolpath; the settings drive the simulation (tool, blank, precision).</summary>
    Public Sub SetToolpath(tp As Toolpath, settings As CarveSettings)
        Dim firstTime As Boolean = (_toolpath Is Nothing)
        _toolpath = tp
        If settings IsNot Nothing Then _simSettings = settings
        _simNeedsSetup = True
        ' A new program: start the clock over; keep showing the finished part unless animating.
        If Not _simPlaying Then _simTime = Double.PositiveInfinity
        BuildBuffers()
        RebuildSelectionLayer()
        If firstTime OrElse Not SceneIntersectsView() Then ZoomToFit() Else Invalidate()
        RaiseEvent SimulationProgress(Me, EventArgs.Empty)
    End Sub

    ' ----------------------------------------------------------- simulation

    ''' <summary>Shows the carved board instead of the bare outline.</summary>
    Public Property ShowSimulation As Boolean
        Get
            Return _showSimulation
        End Get
        Set(value As Boolean)
            If _showSimulation = value Then Return
            _showSimulation = value
            BuildBuffers()
            If value Then
                _simTime = Double.PositiveInfinity    ' first look: the finished part
            Else
                _simPlaying = False
                _simTimer.Stop()
            End If
            Invalidate()
            RaiseEvent SimulationProgress(Me, EventArgs.Empty)
        End Set
    End Property

    Public ReadOnly Property SimulationTotalSeconds As Double
        Get
            Return If(_sim.IsReady, _sim.TotalSeconds, TotalSecondsOf(_toolpath))
        End Get
    End Property

    Public ReadOnly Property SimulationSeconds As Double
        Get
            Return Math.Min(_simTime, SimulationTotalSeconds)
        End Get
    End Property

    Public ReadOnly Property SimulationPlaying As Boolean
        Get
            Return _simPlaying
        End Get
    End Property

    ''' <summary>Playback speed multiplier; PositiveInfinity jumps to the end.</summary>
    Public Property SimulationSpeed As Double
        Get
            Return _simSpeed
        End Get
        Set(value As Double)
            _simSpeed = If(value <= 0, 1.0, value)
            If Double.IsPositiveInfinity(_simSpeed) AndAlso _simPlaying Then SimulationSeek(Double.PositiveInfinity)
        End Set
    End Property

    ''' <summary>Text for the simulation status (cells, memory) once the heightmap exists.</summary>
    Public ReadOnly Property SimulationInfo As String
        Get
            If _sim.SetupError IsNot Nothing Then Return "Simulation unavailable: " & _sim.SetupError
            If Not _sim.IsReady Then Return ""
            Return String.Format(Globalization.CultureInfo.InvariantCulture, "{0} x {1} cells at {2:0.0000}"" ({3:0} MB)",
                                 _sim.TextureWidth, _sim.TextureHeight, _sim.CellSize, _sim.TextureWidth * CDbl(_sim.TextureHeight) * 4 / 1048576.0)
        End Get
    End Property

    Private Shared Function TotalSecondsOf(tp As Toolpath) As Double
        If tp Is Nothing Then Return 0
        Dim t As Double = 0
        For Each mv In tp.Moves
            t += Math.Max(0.0, mv.Seconds)
        Next
        Return t
    End Function

    Public Sub SimulationPlay()
        If _toolpath Is Nothing OrElse _toolpath.IsEmpty Then Return
        If Not _showSimulation Then ShowSimulation = True
        If _simTime >= SimulationTotalSeconds Then _simTime = 0     ' replay from the start
        If Double.IsPositiveInfinity(_simSpeed) Then
            SimulationSeek(Double.PositiveInfinity)
            Return
        End If
        _simPlaying = True
        _simLastTick = DateTime.UtcNow
        _simTimer.Start()
        RaiseEvent SimulationProgress(Me, EventArgs.Empty)
    End Sub

    Public Sub SimulationPause()
        _simPlaying = False
        _simTimer.Stop()
        RaiseEvent SimulationProgress(Me, EventArgs.Empty)
    End Sub

    ''' <summary>Jumps the clock (seconds; PositiveInfinity = finished part). Backwards replays from zero.</summary>
    Public Sub SimulationSeek(seconds As Double)
        _simTime = Math.Max(0.0, seconds)
        If _simTime >= SimulationTotalSeconds Then
            _simPlaying = False
            _simTimer.Stop()
        End If
        Invalidate()
        RaiseEvent SimulationProgress(Me, EventArgs.Empty)
    End Sub

    Private Sub _simTimer_Tick(sender As Object, e As EventArgs) Handles _simTimer.Tick
        If Not _simPlaying Then
            _simTimer.Stop()
            Return
        End If
        Dim now = DateTime.UtcNow
        Dim dt = (now - _simLastTick).TotalSeconds
        _simLastTick = now
        _simTime += dt * _simSpeed
        If _simTime >= SimulationTotalSeconds Then
            _simTime = SimulationTotalSeconds
            _simPlaying = False
            _simTimer.Stop()
        End If
        Invalidate()
        RaiseEvent SimulationProgress(Me, EventArgs.Empty)
    End Sub

    ''' <summary>Runs the pending stamps for the current clock. Context must be current.</summary>
    Private Sub UpdateSimulation()
        If _simNeedsSetup Then
            _simNeedsSetup = False
            If _toolpath Is Nothing OrElse _simSettings Is Nothing OrElse _toolpath.IsEmpty Then
                _sim.Release()
            Else
                _sim.Setup(_toolpath, _simSettings, CarveSettings.HardwareMaxTextureSize)
            End If
        End If
        If Not _sim.IsReady Then Return
        _sim.AdvanceTo(Math.Min(_simTime, _sim.TotalSeconds))
    End Sub

    Public Sub SetTopView()
        _yaw = 0
        _pitch = 0
        Invalidate()
    End Sub

    Public Sub SetIsoView()
        _yaw = -Math.PI / 4
        _pitch = Math.PI / 3.2
        Invalidate()
    End Sub

    Public Sub ZoomToFit()
        Dim size As Vector3 = _sceneMax - _sceneMin
        _target = (_sceneMin + _sceneMax) / 2.0F
        Dim w As Double = Math.Max(size.X, 0.5)
        Dim h As Double = Math.Max(size.Y, 0.5)
        Dim pw As Integer = Math.Max(ClientSize.Width, 1)
        Dim ph As Integer = Math.Max(ClientSize.Height, 1)
        _unitsPerPixel = Math.Max(w / pw, h / ph) * 1.15
        Invalidate()
    End Sub

    ' --------------------------------------------------------------- buffers

    Private Sub BuildBuffers()
        Dim grid As New List(Of Single)
        Dim blank As New List(Of Single)
        Dim outline As New List(Of Single)
        Dim cuts As New List(Of Single)
        Dim rapids As New List(Of Single)

        Dim minX As Double = -1, minY As Double = -1, maxX As Double = 1, maxY As Double = 1, minZ As Double = 0
        Dim tp = _toolpath
        Dim haveBlank As Boolean = tp IsNot Nothing AndAlso tp.BlankMaxX > tp.BlankMinX AndAlso tp.BlankMaxY > tp.BlankMinY
        If haveBlank Then
            minX = tp.BlankMinX : maxX = tp.BlankMaxX : minY = tp.BlankMinY : maxY = tp.BlankMaxY
        End If
        If tp IsNot Nothing AndAlso tp.Outline.Count > 0 Then
            If haveBlank Then
                minX = Math.Min(minX, tp.MinX) : maxX = Math.Max(maxX, tp.MaxX)
                minY = Math.Min(minY, tp.MinY) : maxY = Math.Max(maxY, tp.MaxY)
            Else
                minX = tp.MinX : maxX = tp.MaxX : minY = tp.MinY : maxY = tp.MaxY
            End If
            minZ = tp.MinZ
        End If
        _sceneMin = New Vector3(CSng(minX), CSng(minY), CSng(minZ))
        _sceneMax = New Vector3(CSng(maxX), CSng(maxY), 0)

        ' Blank (stock) outline at Z = 0.
        If haveBlank Then
            Dim bx0 = tp.BlankMinX, by0 = tp.BlankMinY, bx1 = tp.BlankMaxX, by1 = tp.BlankMaxY
            AddLine(blank, bx0, by0, 0, bx1, by0, 0, 0.9F, 0.78F, 0.5F)
            AddLine(blank, bx1, by0, 0, bx1, by1, 0, 0.9F, 0.78F, 0.5F)
            AddLine(blank, bx1, by1, 0, bx0, by1, 0, 0.9F, 0.78F, 0.5F)
            AddLine(blank, bx0, by1, 0, bx0, by0, 0, 0.9F, 0.78F, 0.5F)
        End If

        ' Grid: 1" cells, extended past the scene, plus bright axes through the origin.
        ' With the simulation shown the grid sits under the board so it does not show through the surface.
        Dim gz As Double = If(_showSimulation AndAlso _simSettings IsNot Nothing, -Math.Max(0.05, Math.Min(_simSettings.StockThickness, 2.0)) - 0.001, 0.0)
        Dim g0x As Integer = CInt(Math.Floor(minX)) - 2
        Dim g1x As Integer = CInt(Math.Ceiling(maxX)) + 2
        Dim g0y As Integer = CInt(Math.Floor(minY)) - 2
        Dim g1y As Integer = CInt(Math.Ceiling(maxY)) + 2
        If g1x - g0x > 400 Then g1x = g0x + 400
        If g1y - g0y > 400 Then g1y = g0y + 400
        For x = g0x To g1x
            Dim c As Single = If(x = 0, 0.45F, 0.2F)
            AddLine(grid, x, g0y, gz, x, g1y, gz, If(x = 0, 0.3F, c), If(x = 0, 0.8F, c), If(x = 0, 0.3F, c))
        Next
        For y = g0y To g1y
            Dim c As Single = If(y = 0, 0.45F, 0.2F)
            AddLine(grid, g0x, y, gz, g1x, y, gz, If(y = 0, 0.9F, c), If(y = 0, 0.3F, c), If(y = 0, 0.3F, c))
        Next

        If tp IsNot Nothing Then
            For Each poly In tp.Outline
                For i = 0 To poly.Count - 1
                    Dim a = poly(i)
                    Dim b = poly((i + 1) Mod poly.Count)
                    AddLine(outline, a.X, a.Y, 0, b.X, b.Y, 0, 0.75F, 0.75F, 0.78F)
                Next
            Next

            Dim depthRange As Double = Math.Max(-tp.MinZ, 0.0001)
            Dim prev As Pt3 = New Pt3(0, 0, 0)
            Dim havePrev As Boolean = False
            For Each mv In tp.Moves
                If havePrev Then
                    If mv.Kind = MoveKind.Rapid Then
                        AddLine(rapids, prev.X, prev.Y, prev.Z, mv.Target.X, mv.Target.Y, mv.Target.Z, 0.85F, 0.25F, 0.2F)
                    Else
                        Dim t As Double = Math.Min(1.0, Math.Max(0.0, -mv.Target.Z / depthRange))
                        ' Shallow = cyan, deep = yellow/orange.
                        Dim r As Single = CSng(0.25 + 0.75 * t)
                        Dim gg As Single = CSng(0.85 - 0.2 * t)
                        Dim bb As Single = CSng(1.0 - 0.9 * t)
                        AddLine(cuts, prev.X, prev.Y, prev.Z, mv.Target.X, mv.Target.Y, mv.Target.Z, r, gg, bb)
                    End If
                End If
                prev = mv.Target
                havePrev = True
            Next
        End If

        SetPending(_grid, grid)
        SetPending(_blank, blank)
        SetPending(_outline, outline)
        SetPending(_cuts, cuts)
        SetPending(_rapids, rapids)
        Invalidate()
    End Sub

    Private Shared Sub AddLine(buf As List(Of Single), x0 As Double, y0 As Double, z0 As Double,
                               x1 As Double, y1 As Double, z1 As Double, r As Single, g As Single, b As Single)
        buf.Add(CSng(x0)) : buf.Add(CSng(y0)) : buf.Add(CSng(z0)) : buf.Add(r) : buf.Add(g) : buf.Add(b)
        buf.Add(CSng(x1)) : buf.Add(CSng(y1)) : buf.Add(CSng(z1)) : buf.Add(r) : buf.Add(g) : buf.Add(b)
    End Sub

    Private Shared Sub SetPending(layer As LineLayer, data As List(Of Single))
        layer.Data = data.ToArray()
        layer.Dirty = True
    End Sub

    Private Sub UploadIfDirty(layer As LineLayer)
        If Not layer.Dirty Then Return
        layer.Dirty = False
        If layer.Vao = 0 Then
            layer.Vao = GL.GenVertexArray()
            layer.Vbo = GL.GenBuffer()
            GL.BindVertexArray(layer.Vao)
            GL.BindBuffer(BufferTarget.ArrayBuffer, layer.Vbo)
            Dim stride As Integer = 6 * 4
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, False, stride, 0)
            GL.EnableVertexAttribArray(0)
            GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, False, stride, 3 * 4)
            GL.EnableVertexAttribArray(1)
        Else
            GL.BindVertexArray(layer.Vao)
            GL.BindBuffer(BufferTarget.ArrayBuffer, layer.Vbo)
        End If
        Dim data As Single() = If(layer.Data, New Single() {})
        layer.VertexCount = data.Length \ 6
        GL.BufferData(BufferTarget.ArrayBuffer, data.Length * 4, data, BufferUsageHint.StaticDraw)
        GL.BindVertexArray(0)
    End Sub

    ' ------------------------------------------------------------- GL setup

    Protected Overrides Sub OnHandleCreated(e As EventArgs)
        Try
            MyBase.OnHandleCreated(e)
        Catch ex As Exception
            ' GLFW could not create a 3.3 context (software GL, RDP, VM...). Never retry:
            ' every later base call would recreate the handle and throw again.
            _contextFailed = True
            _initError = ex.Message
            ShowErrorLabel()
        End Try
    End Sub

    Protected Overrides Sub OnHandleDestroyed(e As EventArgs)
        ' Release GL objects while the context still exists, then forget them so a
        ' recreated handle uploads everything again.
        If _initialized AndAlso Not _contextFailed Then
            Try
                MakeCurrent()
                _sim.Release()
                _simNeedsSetup = True
                For Each layer In AllLayers
                    If layer.Vbo <> 0 Then GL.DeleteBuffer(layer.Vbo)
                    If layer.Vao <> 0 Then GL.DeleteVertexArray(layer.Vao)
                Next
                If _program <> 0 Then GL.DeleteProgram(_program)
            Catch
                ' Context already gone: the driver frees the objects with it.
            End Try
        End If
        For Each layer In AllLayers
            layer.Vao = 0
            layer.Vbo = 0
            layer.VertexCount = 0
            layer.Dirty = True
        Next
        _program = 0
        _initialized = False
        MyBase.OnHandleDestroyed(e)
    End Sub

    Protected Overrides Sub OnParentChanged(e As EventArgs)
        If _contextFailed Then
            ' Base would touch the native window; keep the fallback label visible instead.
            ShowErrorLabel()
            Return
        End If
        MyBase.OnParentChanged(e)
    End Sub

    ''' <summary>Shows the failure text in a sibling label that covers the (clipped) GL surface.</summary>
    Private Sub ShowErrorLabel()
        If _errorLabel IsNot Nothing OrElse Parent Is Nothing Then Return
        _errorLabel = New Label() With {
            .Dock = DockStyle.Fill,
            .BackColor = Color.FromArgb(30, 30, 34),
            .ForeColor = Color.Gainsboro,
            .TextAlign = ContentAlignment.MiddleCenter,
            .Padding = New Padding(16),
            .Text = "OpenGL view unavailable:" & Environment.NewLine & If(_initError, "unknown error") &
                    Environment.NewLine & Environment.NewLine & "Toolpath generation and G-code export still work."
        }
        Parent.Controls.Add(_errorLabel)
        _errorLabel.BringToFront()
    End Sub

    Private Sub EnsureInitialized()
        If _initialized OrElse _initError IsNot Nothing Then Return
        Try
            Dim vs As String =
                "#version 330 core" & vbLf &
                "layout(location = 0) in vec3 aPos;" & vbLf &
                "layout(location = 1) in vec3 aColor;" & vbLf &
                "uniform mat4 uMvp;" & vbLf &
                "out vec3 vColor;" & vbLf &
                "void main() { gl_Position = uMvp * vec4(aPos, 1.0); vColor = aColor; }" & vbLf
            Dim fs As String =
                "#version 330 core" & vbLf &
                "in vec3 vColor;" & vbLf &
                "out vec4 FragColor;" & vbLf &
                "void main() { FragColor = vec4(vColor, 1.0); }" & vbLf

            Dim v As Integer = CompileShader(ShaderType.VertexShader, vs)
            Dim f As Integer = CompileShader(ShaderType.FragmentShader, fs)
            _program = GL.CreateProgram()
            GL.AttachShader(_program, v)
            GL.AttachShader(_program, f)
            GL.LinkProgram(_program)
            Dim ok As Integer
            GL.GetProgram(_program, GetProgramParameterName.LinkStatus, ok)
            If ok = 0 Then Throw New InvalidOperationException("Shader link failed: " & GL.GetProgramInfoLog(_program))
            GL.DetachShader(_program, v)
            GL.DetachShader(_program, f)
            GL.DeleteShader(v)
            GL.DeleteShader(f)
            _mvpLoc = GL.GetUniformLocation(_program, "uMvp")

            GL.Enable(EnableCap.DepthTest)
            GL.DepthFunc(DepthFunction.Lequal)
            GL.Enable(EnableCap.Multisample)
            GL.Enable(EnableCap.LineSmooth)
            GL.ClearColor(0.12F, 0.12F, 0.13F, 1.0F)

            ' Hardware limits for the simulation heightmap: texture edge and render viewport.
            Dim maxTex As Integer = GL.GetInteger(GetPName.MaxTextureSize)
            Dim vp(1) As Integer
            GL.GetInteger(GetPName.MaxViewportDims, vp)
            Dim limit As Integer = maxTex
            If vp(0) > 0 Then limit = Math.Min(limit, vp(0))
            If vp(1) > 0 Then limit = Math.Min(limit, vp(1))
            CarveSettings.HardwareMaxTextureSize = Math.Max(256, limit)
            _initialized = True
            RaiseEvent GLReady(Me, EventArgs.Empty)
        Catch ex As Exception
            _initError = ex.Message
            ShowErrorLabel()
        End Try
    End Sub

    Private Shared Function CompileShader(kind As ShaderType, src As String) As Integer
        Dim id As Integer = GL.CreateShader(kind)
        GL.ShaderSource(id, src)
        GL.CompileShader(id)
        Dim ok As Integer
        GL.GetShader(id, ShaderParameter.CompileStatus, ok)
        If ok = 0 Then
            Dim log As String = GL.GetShaderInfoLog(id)
            GL.DeleteShader(id)
            Throw New InvalidOperationException(kind.ToString() & " compile failed: " & log)
        End If
        Return id
    End Function

    ' --------------------------------------------------------------- camera

    Private Function ViewMatrix() As Matrix4
        ' Row-vector convention (OpenTK): applied left to right.
        Return Matrix4.CreateTranslation(-_target) *
               Matrix4.CreateRotationZ(CSng(_yaw)) *
               Matrix4.CreateRotationX(CSng(-_pitch)) *
               Matrix4.CreateTranslation(0, 0, -50.0F)
    End Function

    Private Function ProjectionMatrix() As Matrix4
        Dim w As Single = CSng(Math.Max(ClientSize.Width, 1) * _unitsPerPixel)
        Dim h As Single = CSng(Math.Max(ClientSize.Height, 1) * _unitsPerPixel)
        ' Wide depth range so tilted, panned or very large jobs are never clipped.
        Return Matrix4.CreateOrthographic(w, h, -1000.0F, 1000.0F)
    End Function

    ''' <summary>Vertical screen distances shrink by cos(pitch) when the view is tilted.</summary>
    Private Function PitchScale() As Double
        Return Math.Max(0.05, Math.Cos(_pitch))
    End Function

    ''' <summary>Screen pixel -> world point on the Z=0 plane.</summary>
    Private Function ScreenToWorld(p As Point) As Vector3
        Dim cx As Double = ClientSize.Width / 2.0
        Dim cy As Double = ClientSize.Height / 2.0
        Dim dx As Double = (p.X - cx) * _unitsPerPixel
        Dim dy As Double = (cy - p.Y) * _unitsPerPixel / PitchScale()
        Dim c As Double = Math.Cos(-_yaw), sn As Double = Math.Sin(-_yaw)
        Return New Vector3(CSng(_target.X + dx * c - dy * sn), CSng(_target.Y + dx * sn + dy * c), 0)
    End Function

    Private Function SceneIntersectsView() As Boolean
        Dim halfW As Double = ClientSize.Width * _unitsPerPixel / 2.0
        Dim halfH As Double = ClientSize.Height * _unitsPerPixel / 2.0
        Return _sceneMax.X >= _target.X - halfW AndAlso _sceneMin.X <= _target.X + halfW AndAlso
               _sceneMax.Y >= _target.Y - halfH AndAlso _sceneMin.Y <= _target.Y + halfH
    End Function

    ' --------------------------------------------------------------- paint

    Protected Overrides Sub OnPaint(e As PaintEventArgs)
        If _contextFailed Then
            ShowErrorLabel()
            Return
        End If
        MyBase.OnPaint(e)
        If DesignMode Then Return

        Try
            MakeCurrent()
        Catch ex As Exception
            _contextFailed = True
            _initError = ex.Message
            ShowErrorLabel()
            Return
        End Try

        EnsureInitialized()
        GL.Viewport(0, 0, Math.Max(ClientSize.Width, 1), Math.Max(ClientSize.Height, 1))
        If _initError IsNot Nothing Then
            ' Context works but shaders do not: keep the surface deterministic; the label explains.
            GL.ClearColor(0.12F, 0.12F, 0.13F, 1.0F)
            GL.Clear(ClearBufferMask.ColorBufferBit Or ClearBufferMask.DepthBufferBit)
            SwapBuffers()
            Return
        End If

        ' Material removal happens in its own framebuffer before the view is drawn.
        If _showSimulation Then
            UpdateSimulation()
            GL.Viewport(0, 0, Math.Max(ClientSize.Width, 1), Math.Max(ClientSize.Height, 1))
        End If

        GL.Clear(ClearBufferMask.ColorBufferBit Or ClearBufferMask.DepthBufferBit)
        For Each layer In AllLayers
            UploadIfDirty(layer)
        Next

        Dim mvp As Matrix4 = ViewMatrix() * ProjectionMatrix()
        If _showSimulation AndAlso _sim.IsReady Then
            _sim.DrawBoard(mvp)
            If _simTime < _sim.TotalSeconds Then _sim.DrawTool(mvp, _sim.ToolPositionAt(_simTime))
        End If

        GL.UseProgram(_program)
        GL.UniformMatrix4(_mvpLoc, False, mvp)

        ' Reference layers never occlude the toolpath: draw them without depth writes.
        GL.DepthMask(False)
        If _showGrid Then DrawLayer(_grid)
        DrawLayer(_blank)
        GL.DepthMask(True)
        If _showOutline AndAlso Not _showSimulation Then DrawLayer(_outline)
        If Not _showSimulation Then DrawLayer(_cuts)
        If _showRapids Then DrawLayer(_rapids)
        ' Selection highlight on top, never hidden by the surface.
        GL.Disable(EnableCap.DepthTest)
        DrawLayer(_selectionLayer)
        GL.Enable(EnableCap.DepthTest)

        GL.BindVertexArray(0)
        GL.UseProgram(0)
        SwapBuffers()
    End Sub

    Private Sub DrawLayer(layer As LineLayer)
        If layer.Vao = 0 OrElse layer.VertexCount = 0 Then Return
        GL.BindVertexArray(layer.Vao)
        GL.DrawArrays(PrimitiveType.Lines, 0, layer.VertexCount)
    End Sub

    Protected Overrides Sub OnResize(e As EventArgs)
        If _contextFailed Then Return
        MyBase.OnResize(e)
        Invalidate()
    End Sub

    ' --------------------------------------------------------------- mouse

    Protected Overrides Sub OnMouseDown(e As MouseEventArgs)
        MyBase.OnMouseDown(e)
        Focus()
        _dragButton = e.Button
        _lastMouse = e.Location
        _mode = DragMode.None
        If e.Button = MouseButtons.Right Then
            _mode = DragMode.Orbit
        ElseIf e.Button = MouseButtons.Middle Then
            _mode = DragMode.Pan
        ElseIf e.Button = MouseButtons.Left Then
            Dim w = ScreenToWorld(e.Location)
            _dragStart = w
            _dragDx = 0 : _dragDy = 0
            Dim handle = HandleHitTest(e.Location)
            If handle >= 0 Then
                Dim o = _objects(_selected)
                _resizeHandle = handle
                _startX = o.X : _startY = o.Y : _startW = o.Width : _startH = o.Height
                _resizeX = o.X : _resizeY = o.Y : _resizeW = o.Width : _resizeH = o.Height
                _mode = DragMode.Resize
            Else
                Dim hit = HitTest(w.X, w.Y)
                SelectedObject = hit
                If hit >= 0 Then
                    _mode = DragMode.MoveObject
                ElseIf hit = TextSelection Then
                    _mode = DragMode.MoveText
                Else
                    _mode = DragMode.Pan
                End If
            End If
            If _mode = DragMode.MoveObject OrElse _mode = DragMode.Resize Then Cursor = Cursors.SizeAll
        End If
    End Sub

    Protected Overrides Sub OnMouseUp(e As MouseEventArgs)
        MyBase.OnMouseUp(e)
        Dim mode = _mode
        _mode = DragMode.None
        _dragButton = MouseButtons.None
        Cursor = Cursors.Default
        Select Case mode
            Case DragMode.MoveObject
                If _selected >= 0 AndAlso _selected < _objects.Count AndAlso (_dragDx <> 0 OrElse _dragDy <> 0) Then
                    _objects(_selected).X = Math.Round(_objects(_selected).X + _dragDx, 4)
                    _objects(_selected).Y = Math.Round(_objects(_selected).Y + _dragDy, 4)
                    _dragDx = 0 : _dragDy = 0
                    RebuildSelectionLayer()
                    Invalidate()
                    RaiseEvent ObjectEdited(Me, New ObjectEditedEventArgs With {.Index = _selected})
                End If
            Case DragMode.Resize
                If _selected >= 0 AndAlso _selected < _objects.Count Then
                    Dim o = _objects(_selected)
                    o.X = Math.Round(_resizeX, 4) : o.Y = Math.Round(_resizeY, 4) : o.Width = Math.Round(_resizeW, 4) : o.Height = Math.Round(_resizeH, 4)
                    RebuildSelectionLayer()
                    Invalidate()
                    RaiseEvent ObjectEdited(Me, New ObjectEditedEventArgs With {.Index = _selected})
                End If
            Case DragMode.MoveText
                Dim dx = _dragDx, dy = _dragDy
                _dragDx = 0 : _dragDy = 0
                RebuildSelectionLayer()
                Invalidate()
                If dx <> 0 OrElse dy <> 0 Then RaiseEvent TextMoved(Me, New TextMovedEventArgs With {.Dx = dx, .Dy = dy})
        End Select
    End Sub

    Protected Overrides Sub OnMouseMove(e As MouseEventArgs)
        MyBase.OnMouseMove(e)
        If _mode = DragMode.None Then
            ' Hover feedback over handles / objects.
            If _dragButton = MouseButtons.None Then
                If HandleHitTest(e.Location) >= 0 Then
                    Cursor = Cursors.SizeNWSE
                Else
                    Dim w = ScreenToWorld(e.Location)
                    Cursor = If(HitTest(w.X, w.Y) <> -1, Cursors.Hand, Cursors.Default)
                End If
            End If
            Return
        End If
        Dim dx As Integer = e.X - _lastMouse.X
        Dim dy As Integer = e.Y - _lastMouse.Y
        _lastMouse = e.Location

        Select Case _mode
            Case DragMode.Orbit
                ' Drag right turns the model clockwise; drag up tilts it toward the viewer.
                _yaw += dx * 0.01
                _pitch = Math.Max(0, Math.Min(Math.PI / 2 - 0.05, _pitch - dy * 0.01))
            Case DragMode.Pan
                ' Pan: move the target opposite to the mouse, in screen-aligned world axes.
                Dim wx As Double = -dx * _unitsPerPixel
                Dim wy As Double = dy * _unitsPerPixel / PitchScale()
                Dim c As Double = Math.Cos(-_yaw), sn As Double = Math.Sin(-_yaw)
                _target.X += CSng(wx * c - wy * sn)
                _target.Y += CSng(wx * sn + wy * c)
            Case DragMode.MoveObject, DragMode.MoveText
                Dim w = ScreenToWorld(e.Location)
                _dragDx = w.X - _dragStart.X
                _dragDy = w.Y - _dragStart.Y
                RebuildSelectionLayer()
            Case DragMode.Resize
                Dim w = ScreenToWorld(e.Location)
                ComputeResize(w.X, w.Y)
                RebuildSelectionLayer()
        End Select
        Invalidate()
    End Sub

    Protected Overrides Sub OnMouseWheel(e As MouseEventArgs)
        MyBase.OnMouseWheel(e)
        Dim before As Vector3 = ScreenToWorld(e.Location)
        Dim factor As Double = If(e.Delta > 0, 1 / 1.15, 1.15)
        _unitsPerPixel = Math.Max(0.00001, Math.Min(10.0, _unitsPerPixel * factor))
        Dim after As Vector3 = ScreenToWorld(e.Location)
        ' Keep the world point under the cursor fixed.
        _target.X += before.X - after.X
        _target.Y += before.Y - after.Y
        RebuildSelectionLayer()      ' handle size follows the zoom
        Invalidate()
    End Sub

    Protected Overrides Sub OnMouseDoubleClick(e As MouseEventArgs)
        MyBase.OnMouseDoubleClick(e)
        ZoomToFit()
    End Sub
End Class
