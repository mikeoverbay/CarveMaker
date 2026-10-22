' ============================================================================
'  ToolpathView.vb
'  OpenTK GLControl subclass that draws the blank, the glyph outline, the
'  V-carve passes (coloured by depth), rapid moves and a 1" grid.
'  Orthographic camera with pan (left/middle drag), orbit (right drag) and
'  zoom (wheel, about the cursor). Drawings are picked through an offscreen
'  ID buffer, moved, scaled and rotated with handles, and new shapes, areas
'  and text are drawn with the draw tools (ActiveTool).
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
            _pickDirty = True
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
            _pickDirty = True
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

    ''' <summary>What a left drag in the view does: select and edit, or draw a new shape or text.</summary>
    Public Property ActiveTool As DrawTool
        Get
            Return _tool
        End Get
        Set(value As DrawTool)
            If _tool = value Then Return
            CancelDrawing()
            _tool = value
            Cursor = If(_tool = DrawTool.Select, Cursors.Default, ToolCursor())
            RaiseEvent ToolChanged(Me, EventArgs.Empty)
        End Set
    End Property

    ' ---- snapping ----------------------------------------------------------
    Private _snapToGrid As Boolean
    Private _gridSpacing As Double = 0.125
    Private _rotationSnapDeg As Double = 15
    Private _altDuringDrag As Boolean          ' Alt was held in a drag: its release must not open the menu
    Private _gridMinX As Double = -1, _gridMinY As Double = -1, _gridMaxX As Double = 1, _gridMaxY As Double = 1
    Private Const RotateDeadZonePixels As Double = 20
    Private Const WM_SYSKEYUP As Integer = &H105
    Private Const VK_MENU As Integer = &H12

    ''' <summary>Moving, resizing and drawing snap to the grid (hold Alt while dragging to ignore it).</summary>
    Public Property SnapToGrid As Boolean
        Get
            Return _snapToGrid
        End Get
        Set(value As Boolean)
            If _snapToGrid = value Then Return
            _snapToGrid = value
            BuildGridLayer()
            Invalidate()
        End Set
    End Property

    ''' <summary>Snap grid spacing (inches); a finer grid is drawn while snapping is on.</summary>
    Public Property GridSpacing As Double
        Get
            Return _gridSpacing
        End Get
        Set(value As Double)
            If value <= 0 OrElse _gridSpacing = value Then Return
            _gridSpacing = value
            BuildGridLayer()
            Invalidate()
        End Set
    End Property

    ''' <summary>The rotation knob snaps to multiples of this (degrees; 0 = free). Shift rotates freely.</summary>
    Public Property RotationSnapDeg As Double
        Get
            Return _rotationSnapDeg
        End Get
        Set(value As Double)
            _rotationSnapDeg = Math.Max(0.0, value)
        End Set
    End Property

    ''' <summary>Grid snapping applies right now (on, and Alt not held).</summary>
    Private Function GridSnapActive() As Boolean
        Return _snapToGrid AndAlso _gridSpacing > 0 AndAlso (ModifierKeys And Keys.Alt) <> Keys.Alt
    End Function

    ''' <summary>A world point on the nearest grid intersection while snapping, else unchanged.</summary>
    Private Function SnapPoint(x As Double, y As Double) As Pt2
        If Not GridSnapActive() Then Return New Pt2(x, y)
        Return New Pt2(Math.Round(x / _gridSpacing) * _gridSpacing, Math.Round(y / _gridSpacing) * _gridSpacing)
    End Function

    ''' <summary>
    ''' Snaps a move: X and Y separately, each by whichever reference point (box corners and
    ''' centre) is nearest a grid line, so an edge or the centre lines up with the grid.
    ''' </summary>
    Private Sub SnapMove(refs As IList(Of Pt2), ByRef dx As Double, ByRef dy As Double)
        If Not GridSnapActive() OrElse refs Is Nothing OrElse refs.Count = 0 Then Return
        Dim bestX = Double.MaxValue, bestY = Double.MaxValue, fixX = 0.0, fixY = 0.0
        For Each r In refs
            Dim mx = r.X + dx, my = r.Y + dy
            Dim sx = Math.Round(mx / _gridSpacing) * _gridSpacing - mx
            Dim sy = Math.Round(my / _gridSpacing) * _gridSpacing - my
            If Math.Abs(sx) < bestX Then bestX = Math.Abs(sx) : fixX = sx
            If Math.Abs(sy) < bestY Then bestY = Math.Abs(sy) : fixY = sy
        Next
        dx += fixX : dy += fixY
    End Sub

    ''' <summary>Corners and centre of what is being moved (before the move).</summary>
    Private Function MoveReferencePoints() As List(Of Pt2)
        Dim refs As New List(Of Pt2)
        If _mode = DragMode.MoveObject AndAlso _selected >= 0 AndAlso _selected < _objects.Count Then
            Dim o = _objects(_selected)
            refs.AddRange(o.Corners())
            refs.Add(o.LocalToWorld(0, 0))
        ElseIf _mode = DragMode.MoveText AndAlso HasText() Then
            Dim tp = _toolpath
            refs.Add(New Pt2(tp.TextMinX, tp.TextMinY)) : refs.Add(New Pt2(tp.TextMaxX, tp.TextMinY))
            refs.Add(New Pt2(tp.TextMaxX, tp.TextMaxY)) : refs.Add(New Pt2(tp.TextMinX, tp.TextMaxY))
            refs.Add(New Pt2((tp.TextMinX + tp.TextMaxX) / 2, (tp.TextMinY + tp.TextMaxY) / 2))
        End If
        Return refs
    End Function

    Protected Overrides Sub WndProc(ByRef m As Message)
        ' Alt held to drag without snapping: releasing it must not activate the main menu.
        If m.Msg = WM_SYSKEYUP AndAlso m.WParam.ToInt64() = VK_MENU AndAlso _altDuringDrag Then
            _altDuringDrag = False
            Return
        End If
        MyBase.WndProc(m)
    End Sub

    ''' <summary>The kind of drawing a draw tool makes.</summary>
    Public Shared Function KindOf(tool As DrawTool) As DrawingKind
        Select Case tool
            Case DrawTool.Rectangle : Return DrawingKind.Rectangle
            Case DrawTool.Ellipse : Return DrawingKind.Ellipse
            Case DrawTool.Polygon : Return DrawingKind.Polygon
            Case DrawTool.Star : Return DrawingKind.Star
            Case DrawTool.Area : Return DrawingKind.Area
            Case DrawTool.Text : Return DrawingKind.Text
            Case Else : Return DrawingKind.Svg
        End Select
    End Function

    ''' <summary>Rebuilds the pick buffer's shapes on the next pick (after drawings changed outside the view).</summary>
    Public Sub InvalidatePicking()
        _pickDirty = True
    End Sub

    Private Function HasText() As Boolean
        Return _toolpath IsNot Nothing AndAlso _toolpath.TextMaxX > _toolpath.TextMinX AndAlso _toolpath.TextMaxY > _toolpath.TextMinY
    End Function

    ''' <summary>Object at a world point by bounding boxes (used only when the GPU pick is unavailable).</summary>
    Private Function CpuHitTest(wx As Double, wy As Double) As Integer
        For i = _objects.Count - 1 To 0 Step -1
            If _objects(i).Visible AndAlso _objects(i).HitTest(wx, wy) Then Return i
        Next
        If HasText() Then
            Dim tp = _toolpath
            If wx >= tp.TextMinX AndAlso wx <= tp.TextMaxX AndAlso wy >= tp.TextMinY AndAlso wy <= tp.TextMaxY Then Return TextSelection
        End If
        Return -1
    End Function

    ''' <summary>
    ''' What is under a screen point: a drawing's index, TextSelection, or -1. Uses the
    ''' back-buffer pick (exact outlines, holes and overlaps as drawn); falls back to
    ''' bounding boxes when OpenGL is unavailable.
    ''' </summary>
    Public Function PickAt(p As Point) As Integer
        Dim id As Integer
        If TryGpuPick(p, id) Then Return id
        Dim w = ScreenToWorld(p)
        Return CpuHitTest(w.X, w.Y)
    End Function

    ''' <summary>"GPU" while the back-buffer pick works, otherwise why it does not.</summary>
    Public ReadOnly Property PickingInfo As String
        Get
            If _pickFailed Then Return "CPU (bounding boxes): " & If(_pickError, "pick buffer unavailable")
            Return "GPU"
        End Get
    End Property

    ' ---- handles of the selected drawing -----------------------------------
    ' 0..3 corners (counter-clockwise from the lower-left; uniform scale),
    ' 4..7 side midpoints (bottom, right, top, left; one-way stretch),
    ' 8 the rotation knob above the top edge.

    Private Const RotateHandle As Integer = 8
    Private Const HandleGrabPixels As Integer = 8
    Private Const RotateKnobPixels As Double = 26
    Private Const MinObjectSize As Double = 0.02

    Private Function HandlePoints(o As DesignObject) As Pt2()
        Dim c = o.Corners()
        Dim pts(8) As Pt2
        For i = 0 To 3
            pts(i) = c(i)
        Next
        pts(4) = o.LocalToWorld(0, -o.Height / 2)
        pts(5) = o.LocalToWorld(o.Width / 2, 0)
        pts(6) = o.LocalToWorld(0, o.Height / 2)
        pts(7) = o.LocalToWorld(-o.Width / 2, 0)
        pts(RotateHandle) = o.LocalToWorld(0, o.Height / 2 + RotateKnobPixels * _unitsPerPixel)
        Return pts
    End Function

    ''' <summary>Side handles appear only where the drawing may stretch one way.</summary>
    Private Shared Function HandleVisible(o As DesignObject, handle As Integer) As Boolean
        If handle >= 4 AndAlso handle <= 7 Then Return Not o.LockAspect AndAlso o.Kind <> DrawingKind.Text
        Return True
    End Function

    ''' <summary>Handle of the selected drawing under the mouse (0..8), or -1.</summary>
    Private Function HandleHitTest(p As Point) As Integer
        If _tool <> DrawTool.Select OrElse _selected < 0 OrElse _selected >= _objects.Count Then Return -1
        Dim o = _objects(_selected)
        Dim pts = HandlePoints(o)
        ' A box only a few handles wide on screen: inside it the mouse grabs the drawing itself.
        Dim sp0 = WorldToScreen(pts(0).X, pts(0).Y), sp1 = WorldToScreen(pts(1).X, pts(1).Y), sp3 = WorldToScreen(pts(3).X, pts(3).Y)
        Dim screenW = Math.Sqrt(CDbl(sp1.X - sp0.X) ^ 2 + CDbl(sp1.Y - sp0.Y) ^ 2)
        Dim screenH = Math.Sqrt(CDbl(sp3.X - sp0.X) ^ 2 + CDbl(sp3.Y - sp0.Y) ^ 2)
        Dim small = Math.Min(screenW, screenH) < 3 * HandleGrabPixels
        Dim insideBox = small AndAlso o.HitTest(ScreenToWorld(p).X, ScreenToWorld(p).Y)
        ' The knob first: on a short box it sits close to the top corners.
        For Each i In New Integer() {RotateHandle, 0, 1, 2, 3, 4, 5, 6, 7}
            If Not HandleVisible(o, i) Then Continue For
            If insideBox AndAlso i <> RotateHandle Then Continue For
            Dim sp = WorldToScreen(pts(i).X, pts(i).Y)
            If Math.Abs(sp.X - p.X) <= HandleGrabPixels AndAlso Math.Abs(sp.Y - p.Y) <= HandleGrabPixels Then Return i
        Next
        Return -1
    End Function

    ''' <summary>Resize arrow along the handle's direction on screen (follows the drawing's rotation and the view).</summary>
    Private Function HandleCursor(handle As Integer) As Cursor
        If handle = RotateHandle OrElse _selected < 0 OrElse _selected >= _objects.Count Then Return RotateCursor()
        Dim o = _objects(_selected)
        ' Direction in the box's own frame: 45 degree diagonals for corners, the axes for sides.
        Dim dx As Double, dy As Double
        Select Case handle
            Case 0 : dx = -1 : dy = -1
            Case 1 : dx = 1 : dy = -1
            Case 2 : dx = 1 : dy = 1
            Case 3 : dx = -1 : dy = 1
            Case 4 : dx = 0 : dy = -1
            Case 5 : dx = 1 : dy = 0
            Case 6 : dx = 0 : dy = 1
            Case Else : dx = -1 : dy = 0
        End Select
        Dim c = o.LocalToWorld(0, 0)
        Dim rot = o.RotationDeg * Math.PI / 180
        Dim len = 20 * _unitsPerPixel
        Dim tipX = c.X + (dx * Math.Cos(rot) - dy * Math.Sin(rot)) * len
        Dim tipY = c.Y + (dx * Math.Sin(rot) + dy * Math.Cos(rot)) * len
        Dim s0 = WorldToScreen(c.X, c.Y), s1 = WorldToScreen(tipX, tipY)
        Dim a = Math.Atan2(s0.Y - s1.Y, s1.X - s0.X) * 180 / Math.PI   ' screen angle, up positive
        a = ((a Mod 180) + 180) Mod 180
        If a < 22.5 OrElse a >= 157.5 Then Return Cursors.SizeWE
        If a < 67.5 Then Return Cursors.SizeNESW
        If a < 112.5 Then Return Cursors.SizeNS
        Return Cursors.SizeNWSE
    End Function

    Private Function ToolCursor() As Cursor
        Return If(_tool = DrawTool.Text, Cursors.IBeam, Cursors.Cross)
    End Function

    Private Shared _rotateCursor As Cursor

    ''' <summary>A round-arrow cursor for the rotation knob (made once).</summary>
    Private Shared Function RotateCursor() As Cursor
        If _rotateCursor IsNot Nothing Then Return _rotateCursor
        Try
            Using bmp As New Bitmap(32, 32)
                Using g = Graphics.FromImage(bmp)
                    g.SmoothingMode = Drawing2D.SmoothingMode.AntiAlias
                    For Each pass In {New Pen(Color.White, 4.0F), New Pen(Color.Black, 2.0F)}
                        Using pass
                            g.DrawArc(pass, 8, 8, 16, 16, 200, 270)
                            g.DrawLines(pass, New PointF() {New PointF(9, 5), New PointF(9, 11), New PointF(15, 11)})
                        End Using
                    Next
                End Using
                _rotateCursor = New Cursor(bmp.GetHicon())
            End Using
        Catch
            _rotateCursor = Cursors.Cross
        End Try
        Return _rotateCursor
    End Function

    ''' <summary>Highlight of the selected object (with live drag preview), its handles, and any shape being drawn.</summary>
    Private Sub RebuildSelectionLayer()
        Dim buf As New List(Of Single)
        Dim hr As Single = 1.0F, hg As Single = 0.85F, hb As Single = 0.2F
        If _selected >= 0 AndAlso _selected < _objects.Count Then
            Dim o = _objects(_selected)
            Dim preview As DesignObject = o
            Select Case _mode
                Case DragMode.MoveObject
                    preview = o.Clone()
                    preview.X = o.X + _dragDx : preview.Y = o.Y + _dragDy
                Case DragMode.Resize
                    preview = o.Clone()
                    preview.X = _resizeX : preview.Y = _resizeY : preview.Width = _resizeW : preview.Height = _resizeH
                Case DragMode.Rotate
                    preview = o.Clone()
                    preview.RotationDeg = _rotDeg
            End Select
            AddPaths(buf, preview.PlacedPaths(_curveTolerance), 0.0005, hr, hg, hb)
            Dim c = preview.Corners()
            For i = 0 To 3
                Dim a = c(i), b = c((i + 1) Mod 4)
                AddLine(buf, a.X, a.Y, 0.001, b.X, b.Y, 0.001, 0.4F, 0.8F, 1.0F)
            Next
            ' Handles: small squares in screen size, a stem and a ring for the rotation knob.
            Dim h As Double = 5 * _unitsPerPixel
            Dim pts = HandlePoints(preview)
            For i = 0 To 7
                If HandleVisible(preview, i) Then AddSquare(buf, pts(i), h, 1, 1, 1)
            Next
            Dim top = pts(6)
            Dim knob = pts(RotateHandle)
            AddLine(buf, top.X, top.Y, 0.002, knob.X, knob.Y, 0.002, 0.4F, 0.8F, 1.0F)
            AddCircle(buf, knob, h * 1.1, 1, 1, 1)
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
        If _mode = DragMode.Create Then AddCreatePreview(buf)
        If _areaPoints.Count > 0 Then AddAreaPreview(buf)
        SetPending(_selectionLayer, buf)
    End Sub

    Private Shared Sub AddPaths(buf As List(Of Single), paths As Clipper2Lib.PathsD, z As Double, r As Single, g As Single, b As Single)
        For Each path In paths
            For i = 0 To path.Count - 1
                Dim a = path(i), c = path((i + 1) Mod path.Count)
                AddLine(buf, a.x, a.y, z, c.x, c.y, z, r, g, b)
            Next
        Next
    End Sub

    Private Shared Sub AddSquare(buf As List(Of Single), p As Pt2, h As Double, r As Single, g As Single, b As Single)
        AddLine(buf, p.X - h, p.Y - h, 0.002, p.X + h, p.Y - h, 0.002, r, g, b)
        AddLine(buf, p.X + h, p.Y - h, 0.002, p.X + h, p.Y + h, 0.002, r, g, b)
        AddLine(buf, p.X + h, p.Y + h, 0.002, p.X - h, p.Y + h, 0.002, r, g, b)
        AddLine(buf, p.X - h, p.Y + h, 0.002, p.X - h, p.Y - h, 0.002, r, g, b)
    End Sub

    Private Shared Sub AddCircle(buf As List(Of Single), p As Pt2, radius As Double, r As Single, g As Single, b As Single)
        Const n As Integer = 16
        For i = 0 To n - 1
            Dim a0 = 2 * Math.PI * i / n, a1 = 2 * Math.PI * (i + 1) / n
            AddLine(buf, p.X + radius * Math.Cos(a0), p.Y + radius * Math.Sin(a0), 0.002,
                    p.X + radius * Math.Cos(a1), p.Y + radius * Math.Sin(a1), 0.002, r, g, b)
        Next
    End Sub

    ''' <summary>The shape under the mouse while it is being dragged out.</summary>
    Private Sub AddCreatePreview(buf As List(Of Single))
        Dim minX, minY, maxX, maxY As Double
        CreateBox(minX, minY, maxX, maxY)
        If maxX - minX <= 0 OrElse maxY - minY <= 0 Then Return
        AddLine(buf, minX, minY, 0.001, maxX, minY, 0.001, 0.4F, 0.8F, 1.0F)
        AddLine(buf, maxX, minY, 0.001, maxX, maxY, 0.001, 0.4F, 0.8F, 1.0F)
        AddLine(buf, maxX, maxY, 0.001, minX, maxY, 0.001, 0.4F, 0.8F, 1.0F)
        AddLine(buf, minX, maxY, 0.001, minX, minY, 0.001, 0.4F, 0.8F, 1.0F)
        If _tool = DrawTool.Text Then Return
        Dim tmp As New DesignObject With {.Kind = KindOf(_tool), .X = minX, .Y = minY, .Width = maxX - minX, .Height = maxY - minY}
        AddPaths(buf, tmp.PlacedPaths(_curveTolerance), 0.0005, 1.0F, 0.85F, 0.2F)
    End Sub

    ''' <summary>The free-form area so far, the edge to the mouse and (dim) the closing edge.</summary>
    Private Sub AddAreaPreview(buf As List(Of Single))
        For i = 0 To _areaPoints.Count - 2
            AddLine(buf, _areaPoints(i).X, _areaPoints(i).Y, 0.001, _areaPoints(i + 1).X, _areaPoints(i + 1).Y, 0.001, 1.0F, 0.85F, 0.2F)
        Next
        Dim last = _areaPoints(_areaPoints.Count - 1)
        AddLine(buf, last.X, last.Y, 0.001, _areaHover.X, _areaHover.Y, 0.001, 1.0F, 0.85F, 0.2F)
        AddLine(buf, _areaHover.X, _areaHover.Y, 0.001, _areaPoints(0).X, _areaPoints(0).Y, 0.001, 0.45F, 0.45F, 0.4F)
        Dim h As Double = 3 * _unitsPerPixel
        For Each p In _areaPoints
            AddSquare(buf, p, h, 1, 1, 1)
        Next
    End Sub

    ''' <summary>
    ''' Live result of a handle drag in _resize*. Corners scale evenly about the opposite
    ''' corner (the mouse is followed along the diagonal); sides stretch one way.
    ''' </summary>
    Private Sub ComputeResize(wx As Double, wy As Double)
        Dim rot = _startRot * Math.PI / 180
        Dim c = Math.Cos(rot), s = Math.Sin(rot)
        Dim cx = _startX + _startW / 2, cy = _startY + _startH / 2
        ' Mouse in the box's own, unrotated frame (origin at the start centre).
        Dim dx = wx - cx, dy = wy - cy
        Dim lx = dx * c + dy * s, ly = -dx * s + dy * c
        Dim w = _startW, h = _startH
        Dim ncx As Double = 0, ncy As Double = 0
        If _resizeHandle <= 3 Then
            Dim sx = If(_resizeHandle = 1 OrElse _resizeHandle = 2, 1.0, -1.0)
            Dim sy = If(_resizeHandle >= 2, 1.0, -1.0)
            Dim fixX = -sx * _startW / 2, fixY = -sy * _startH / 2
            Dim ddx = sx * _startW, ddy = sy * _startH
            Dim k = ((lx - fixX) * ddx + (ly - fixY) * ddy) / (ddx * ddx + ddy * ddy)
            ' Never below the floor, but a drawing already smaller than it is not forced to grow.
            If GridSnapActive() Then
                ' The dragged corner (fixed corner + k * diagonal, in machine coordinates) onto the
                ' nearest vertical or horizontal grid line, whichever needs the smaller change.
                Dim fx = cx + fixX * c - fixY * s, fy = cy + fixX * s + fixY * c
                Dim dxw = ddx * c - ddy * s, dyw = ddx * s + ddy * c
                Dim best = k, bestChange = Double.MaxValue
                If Math.Abs(dxw) > 0.000001 Then
                    Dim kx = (Math.Round((fx + k * dxw) / _gridSpacing) * _gridSpacing - fx) / dxw
                    If kx > 0 AndAlso Math.Abs(kx - k) < bestChange Then best = kx : bestChange = Math.Abs(kx - k)
                End If
                If Math.Abs(dyw) > 0.000001 Then
                    Dim ky = (Math.Round((fy + k * dyw) / _gridSpacing) * _gridSpacing - fy) / dyw
                    If ky > 0 AndAlso Math.Abs(ky - k) < bestChange Then best = ky : bestChange = Math.Abs(ky - k)
                End If
                k = best
            End If
            k = Math.Max(k, Math.Min(1.0, MinObjectSize / Math.Max(0.0001, Math.Min(_startW, _startH))))
            w = _startW * k : h = _startH * k
            ncx = fixX + sx * w / 2 : ncy = fixY + sy * h / 2
        ElseIf _resizeHandle = 5 OrElse _resizeHandle = 7 Then
            Dim sx = If(_resizeHandle = 5, 1.0, -1.0)
            Dim fixX = -sx * _startW / 2
            w = Math.Max(Math.Min(MinObjectSize, _startW), (lx - fixX) * sx)
            ncx = fixX + sx * w / 2
        Else
            Dim sy = If(_resizeHandle = 6, 1.0, -1.0)
            Dim fixY = -sy * _startH / 2
            h = Math.Max(Math.Min(MinObjectSize, _startH), (ly - fixY) * sy)
            ncy = fixY + sy * h / 2
        End If
        Dim wcx = cx + ncx * c - ncy * s, wcy = cy + ncx * s + ncy * c
        _resizeW = w : _resizeH = h
        _resizeX = wcx - w / 2 : _resizeY = wcy - h / 2
    End Sub

    ''' <summary>Box being dragged out by a draw tool: Shift keeps the shape's proportions, Ctrl draws from the centre.</summary>
    Private Sub CreateBox(ByRef minX As Double, ByRef minY As Double, ByRef maxX As Double, ByRef maxY As Double)
        Dim dx = _createEnd.X - _createStart.X, dy = _createEnd.Y - _createStart.Y
        Dim keys = ModifierKeys
        If (keys And Keys.Shift) = Keys.Shift AndAlso _tool <> DrawTool.Text Then
            Dim a = DrawingShapes.DefaultAspect(KindOf(_tool))
            Dim w = Math.Abs(dx), h = Math.Abs(dy)
            If w / a > h Then h = w / a Else w = h * a
            dx = If(dx < 0, -w, w) : dy = If(dy < 0, -h, h)
        End If
        If (keys And Keys.Control) = Keys.Control Then
            minX = _createStart.X - Math.Abs(dx) : maxX = _createStart.X + Math.Abs(dx)
            minY = _createStart.Y - Math.Abs(dy) : maxY = _createStart.Y + Math.Abs(dy)
        Else
            minX = Math.Min(_createStart.X, _createStart.X + dx) : maxX = Math.Max(_createStart.X, _createStart.X + dx)
            minY = Math.Min(_createStart.Y, _createStart.Y + dy) : maxY = Math.Max(_createStart.Y, _createStart.Y + dy)
        End If
    End Sub

    ''' <summary>Ends a box drag of a draw tool: reports the new shape and returns to the select tool.</summary>
    Private Sub FinishCreate(p As Point)
        Dim isClick = Math.Abs(p.X - _createStartScreen.X) < 4 AndAlso Math.Abs(p.Y - _createStartScreen.Y) < 4
        Dim minX, minY, maxX, maxY As Double
        CreateBox(minX, minY, maxX, maxY)
        If isClick Then
            minX = _createStart.X : maxX = _createStart.X : minY = _createStart.Y : maxY = _createStart.Y
        End If
        Dim args As New ShapeDrawnEventArgs With {.Kind = KindOf(_tool), .IsClick = isClick, .MinX = minX, .MinY = minY, .MaxX = maxX, .MaxY = maxY}
        _mode = DragMode.None
        RebuildSelectionLayer()
        Invalidate()
        _justCreated = True
        RaiseEvent ShapeDrawn(Me, args)
        ActiveTool = DrawTool.Select
    End Sub

    ''' <summary>Area tool click: adds a corner, or closes the area on its first corner.</summary>
    Private Sub AddAreaPoint(p As Point)
        If _areaPoints.Count >= 3 AndAlso ScreenDistance(p, _areaPoints(0)) <= HandleGrabPixels Then
            FinishArea()
            Return
        End If
        If _areaPoints.Count > 0 AndAlso ScreenDistance(p, _areaPoints(_areaPoints.Count - 1)) <= 3 Then Return
        Dim w = ScreenToWorld(p)
        Dim corner = SnapPoint(w.X, w.Y)
        ' Two clicks on the same grid point make one corner.
        If _areaPoints.Count > 0 AndAlso _areaPoints(_areaPoints.Count - 1).DistanceTo(corner) < 0.000001 Then Return
        _areaPoints.Add(corner)
        _areaHover = corner
        RebuildSelectionLayer()
        Invalidate()
    End Sub

    ''' <summary>Closes the free-form area (three corners or more) and returns to the select tool.</summary>
    Private Sub FinishArea()
        If _areaPoints.Count > 0 AndAlso _areaPoints.Count < 3 Then
            RaiseFeedback("An area needs at least three corners (Esc cancels).")
            Return
        End If
        Dim pts = New List(Of Pt2)(_areaPoints)
        CancelDrawing()
        _justCreated = True
        If pts.Count >= 3 Then
            Dim minX = pts.Min(Function(q) q.X), maxX = pts.Max(Function(q) q.X)
            Dim minY = pts.Min(Function(q) q.Y), maxY = pts.Max(Function(q) q.Y)
            RaiseEvent ShapeDrawn(Me, New ShapeDrawnEventArgs With {.Kind = DrawingKind.Area, .Points = pts,
                                  .MinX = minX, .MinY = minY, .MaxX = maxX, .MaxY = maxY})
        End If
        ActiveTool = DrawTool.Select
    End Sub

    ''' <summary>Abandons a move, handle or draw drag: nothing changes.</summary>
    Private Sub CancelDrag()
        If _mode = DragMode.None Then Return
        _mode = DragMode.None
        _dragDx = 0 : _dragDy = 0
        _dragButton = MouseButtons.None
        Cursor = If(_tool = DrawTool.Select, Cursors.Default, ToolCursor())
        RebuildSelectionLayer()
        Invalidate()
    End Sub

    ''' <summary>Drops a shape or area that is being drawn.</summary>
    Private Sub CancelDrawing()
        Dim had = _areaPoints.Count > 0 OrElse _mode = DragMode.Create
        _areaPoints.Clear()
        If _mode = DragMode.Create Then _mode = DragMode.None
        If had Then
            RebuildSelectionLayer()
            Invalidate()
        End If
    End Sub

    Private Function ScreenDistance(p As Point, w As Pt2) As Double
        Dim s = WorldToScreen(w.X, w.Y)
        Return Math.Sqrt(CDbl(s.X - p.X) * (s.X - p.X) + CDbl(s.Y - p.Y) * (s.Y - p.Y))
    End Function

    Private Shared Function NormalizeAngle(deg As Double) As Double
        Dim a = deg Mod 360.0
        If a > 180 Then a -= 360
        If a <= -180 Then a += 360
        Return a
    End Function

    Private Sub RaiseFeedback(text As String)
        RaiseEvent DragFeedback(Me, New DragFeedbackEventArgs With {.Text = text})
    End Sub

    ''' <summary>Moves the selection by a keyboard nudge (inches).</summary>
    Public Sub NudgeSelection(dx As Double, dy As Double)
        If _selected >= 0 AndAlso _selected < _objects.Count Then
            _objects(_selected).X = Math.Round(_objects(_selected).X + dx, 4)
            _objects(_selected).Y = Math.Round(_objects(_selected).Y + dy, 4)
            _pickDirty = True
            RebuildSelectionLayer()
            Invalidate()
            RaiseEvent ObjectEdited(Me, New ObjectEditedEventArgs With {.Index = _selected})
        ElseIf _selected = TextSelection Then
            RaiseEvent TextMoved(Me, New TextMovedEventArgs With {.Dx = dx, .Dy = dy})
        End If
    End Sub

    Protected Overrides Function IsInputKey(keyData As Keys) As Boolean
        Select Case keyData And Keys.KeyCode
            Case Keys.Left, Keys.Right, Keys.Up, Keys.Down, Keys.Escape, Keys.Enter, Keys.Back, Keys.Delete
                Return True
        End Select
        Return MyBase.IsInputKey(keyData)
    End Function

    Protected Overrides Sub OnKeyUp(e As KeyEventArgs)
        MyBase.OnKeyUp(e)
        RefreshDragForModifiers(e.KeyCode)
    End Sub

    ''' <summary>Shift / Ctrl change the shape being drawn or the rotation snap, Alt the grid snap: update the preview now.</summary>
    Private Sub RefreshDragForModifiers(key As Keys)
        If key = Keys.Menu AndAlso _mode <> DragMode.None Then _altDuringDrag = True
        Dim affects = ((key = Keys.ShiftKey OrElse key = Keys.ControlKey) AndAlso (_mode = DragMode.Create OrElse _mode = DragMode.Rotate)) OrElse
                      (key = Keys.Menu AndAlso (_mode = DragMode.Create OrElse _mode = DragMode.MoveObject OrElse _mode = DragMode.MoveText OrElse _mode = DragMode.Resize))
        If affects Then
            UpdateDrag(PointToClient(MousePosition))
            Invalidate()
        End If
    End Sub

    Protected Overrides Sub OnKeyDown(e As KeyEventArgs)
        MyBase.OnKeyDown(e)
        If e.Handled Then Return
        RefreshDragForModifiers(e.KeyCode)
        Select Case e.KeyCode
            Case Keys.Escape
                If _mode = DragMode.MoveObject OrElse _mode = DragMode.MoveText OrElse _mode = DragMode.Resize OrElse _mode = DragMode.Rotate Then
                    CancelDrag()
                ElseIf _mode = DragMode.Create OrElse _areaPoints.Count > 0 Then
                    CancelDrawing()
                ElseIf _tool <> DrawTool.Select Then
                    ActiveTool = DrawTool.Select
                ElseIf _mode = DragMode.None Then
                    SelectedObject = -1
                End If
                e.Handled = True
                Return
            Case Keys.Enter
                If _tool = DrawTool.Area Then
                    FinishArea()
                    e.Handled = True
                    Return
                End If
            Case Keys.Back, Keys.Delete
                If _areaPoints.Count > 0 Then
                    _areaPoints.RemoveAt(_areaPoints.Count - 1)
                    RebuildSelectionLayer()
                    Invalidate()
                    e.Handled = True
                    Return
                End If
        End Select
        If e.KeyCode = Keys.Delete AndAlso _selected >= 0 AndAlso _mode = DragMode.None Then
            RaiseEvent DeleteRequested(Me, EventArgs.Empty)
            e.Handled = True
            Return
        End If
        ' Tool keys (no modifiers): V select, R rectangle, E ellipse, P polygon, S star, A area, T text.
        If Not e.Control AndAlso Not e.Alt AndAlso _mode = DragMode.None Then
            Dim pick As DrawTool? = Nothing
            Select Case e.KeyCode
                Case Keys.V : pick = DrawTool.Select
                Case Keys.R : pick = DrawTool.Rectangle
                Case Keys.E : pick = DrawTool.Ellipse
                Case Keys.P : pick = DrawTool.Polygon
                Case Keys.S : pick = DrawTool.Star
                Case Keys.A : pick = DrawTool.Area
                Case Keys.T : pick = DrawTool.Text
            End Select
            If pick.HasValue Then
                ActiveTool = pick.Value
                e.Handled = True
                Return
            End If
        End If
        If _selected = -1 OrElse _mode <> DragMode.None Then Return
        Dim stepIn As Double = If(e.Shift, 0.1, 0.01)
        Select Case e.KeyCode
            Case Keys.Left : NudgeSelection(-stepIn, 0) : e.Handled = True
            Case Keys.Right : NudgeSelection(stepIn, 0) : e.Handled = True
            Case Keys.Up : NudgeSelection(0, stepIn) : e.Handled = True
            Case Keys.Down : NudgeSelection(0, -stepIn) : e.Handled = True
        End Select
    End Sub

    ' ------------------------------------------------------ back-buffer pick
    ' Every visible drawing is drawn into a small offscreen buffer in a colour that
    ' encodes its number, with the view's own camera, and the pixels around the
    ' cursor are read back. Outlines are filled without triangulating them: each
    ' contour's triangle fan inverts the stencil (inside = odd count, so holes stay
    ' empty), then a cover quad paints the colour where the stencil is set and clears
    ' it. Later drawings paint over earlier ones, so the topmost one wins, exactly as
    ' the drawings overlap on screen. Text drawings and the text block are picked
    ' anywhere in their box, so the gaps between letters count too.

    Private Const PickRadius As Integer = 4        ' pixels around the cursor (thin strokes are easy to hit)
    Private Const PickBufferSize As Integer = 16   ' the buffer only ever holds the cursor's neighbourhood

    Private Structure PickRange
        Public First As Integer
        Public Count As Integer
    End Structure

    Private Class PickItem
        Public Id As Integer                   ' selection value: drawing index or TextSelection
        Public Fans As New List(Of PickRange)  ' outline contours (none = solid box)
        Public Cover As PickRange              ' quad over the whole item
    End Class

    Private _pickProgram As Integer
    Private _pickMvpLoc As Integer
    Private _pickColorLoc As Integer
    Private _pickVao As Integer
    Private _pickVbo As Integer
    Private _pickFbo As Integer
    Private _pickColorRb As Integer
    Private _pickStencilRb As Integer
    Private _pickDirty As Boolean = True
    Private _pickFailed As Boolean
    Private _pickError As String
    Private ReadOnly _pickItems As New List(Of PickItem)

    Private Function TryGpuPick(p As Point, ByRef result As Integer) As Boolean
        result = -1
        If _pickFailed OrElse _contextFailed OrElse _initError IsNot Nothing OrElse Not IsHandleCreated OrElse DesignMode Then Return False
        Dim w = Math.Max(ClientSize.Width, 1), h = Math.Max(ClientSize.Height, 1)
        Try
            MakeCurrent()
        Catch
            Return False          ' no context: no GL calls at all (the next paint reports it)
        End Try
        Try
            EnsureInitialized()
            If Not _initialized Then Return False
            EnsurePickTarget()
        Catch ex As Exception
            ' The buffer or shader cannot be made on this GPU: bounding boxes from now on.
            _pickFailed = True
            _pickError = ex.Message
            Return False
        End Try
        Try
            If _pickDirty Then BuildPickGeometry()

            ' Window pixels around the cursor (GL counts rows from the bottom).
            Dim gx = p.X, gy = h - 1 - p.Y
            Dim x0 = Math.Max(0, gx - PickRadius), y0 = Math.Max(0, gy - PickRadius)
            Dim x1 = Math.Min(w - 1, gx + PickRadius), y1 = Math.Min(h - 1, gy + PickRadius)
            If x1 < x0 OrElse y1 < y0 Then Return True      ' outside the view: nothing there
            Dim rw = x1 - x0 + 1, rh = y1 - y0 + 1

            GL.BindFramebuffer(FramebufferTarget.Framebuffer, _pickFbo)
            ' The whole view's viewport, shifted so window pixel (x0, y0) lands on buffer pixel (0, 0).
            GL.Viewport(-x0, -y0, w, h)
            GL.Disable(EnableCap.DepthTest)
            GL.Disable(EnableCap.Blend)
            GL.ColorMask(True, True, True, True)
            GL.StencilMask(&HFF)
            GL.ClearColor(0, 0, 0, 0)
            GL.ClearStencil(0)
            GL.Clear(ClearBufferMask.ColorBufferBit Or ClearBufferMask.StencilBufferBit)
            GL.Enable(EnableCap.StencilTest)
            GL.UseProgram(_pickProgram)
            Dim mvp As Matrix4 = ViewMatrix() * ProjectionMatrix()
            GL.UniformMatrix4(_pickMvpLoc, False, mvp)
            GL.BindVertexArray(_pickVao)
            For i = 0 To _pickItems.Count - 1
                Dim it = _pickItems(i)
                If it.Fans.Count > 0 Then
                    GL.ColorMask(False, False, False, False)
                    GL.StencilFunc(StencilFunction.Always, 0, &HFF)
                    GL.StencilOp(StencilOp.Keep, StencilOp.Keep, StencilOp.Invert)
                    For Each f In it.Fans
                        GL.DrawArrays(PrimitiveType.TriangleFan, f.First, f.Count)
                    Next
                    GL.ColorMask(True, True, True, True)
                    GL.StencilFunc(StencilFunction.Notequal, 0, &HFF)
                    GL.StencilOp(StencilOp.Zero, StencilOp.Zero, StencilOp.Zero)
                Else
                    GL.StencilFunc(StencilFunction.Always, 0, &HFF)
                    GL.StencilOp(StencilOp.Keep, StencilOp.Keep, StencilOp.Keep)
                End If
                Dim code = i + 1
                GL.Uniform4(_pickColorLoc, (code And &HFF) / 255.0F, ((code >> 8) And &HFF) / 255.0F, ((code >> 16) And &HFF) / 255.0F, 1.0F)
                GL.DrawArrays(PrimitiveType.TriangleFan, it.Cover.First, it.Cover.Count)
                ' Outlines too: a sliver thinner than a pixel covers no pixel centre, but its lines always draw.
                GL.StencilFunc(StencilFunction.Always, 0, &HFF)
                GL.StencilOp(StencilOp.Keep, StencilOp.Keep, StencilOp.Keep)
                If it.Fans.Count > 0 Then
                    For Each f In it.Fans
                        GL.DrawArrays(PrimitiveType.LineLoop, f.First, f.Count)
                    Next
                Else
                    GL.DrawArrays(PrimitiveType.LineLoop, it.Cover.First, it.Cover.Count)
                End If
            Next
            Dim px(rw * rh * 4 - 1) As Byte
            GL.PixelStore(PixelStoreParameter.PackAlignment, 1)
            GL.ReadPixels(0, 0, rw, rh, PixelFormat.Rgba, PixelType.UnsignedByte, px)
            RestoreAfterPick(w, h)

            ' The painted pixel nearest the cursor wins.
            Dim best = 0, bestD = Integer.MaxValue
            For yy = 0 To rh - 1
                For xx = 0 To rw - 1
                    Dim k = (yy * rw + xx) * 4
                    Dim code = CInt(px(k)) Or (CInt(px(k + 1)) << 8) Or (CInt(px(k + 2)) << 16)
                    If code = 0 OrElse code > _pickItems.Count Then Continue For
                    Dim ddx = x0 + xx - gx, ddy = y0 + yy - gy
                    Dim d = ddx * ddx + ddy * ddy
                    If d < bestD Then
                        bestD = d
                        best = code
                    End If
                Next
            Next
            result = If(best > 0, _pickItems(best - 1).Id, -1)
            Return True
        Catch ex As Exception
            ' One failed pick falls back to bounding boxes; the next pick tries the GPU again.
            _pickError = ex.Message
            Try
                RestoreAfterPick(w, h)
            Catch
            End Try
            Return False
        End Try
    End Function

    ''' <summary>Puts back the state the view's paint relies on.</summary>
    Private Shared Sub RestoreAfterPick(w As Integer, h As Integer)
        GL.BindVertexArray(0)
        GL.UseProgram(0)
        GL.Disable(EnableCap.StencilTest)
        GL.ColorMask(True, True, True, True)
        GL.PixelStore(PixelStoreParameter.PackAlignment, 4)
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0)
        GL.Enable(EnableCap.DepthTest)
        GL.Viewport(0, 0, w, h)
    End Sub

    ''' <summary>Creates the pick shader, vertex buffer and the small colour + stencil buffer once.</summary>
    Private Sub EnsurePickTarget()
        If _pickFbo <> 0 Then Return
        Dim vs As String =
            "#version 330 core" & vbLf &
            "layout(location = 0) in vec2 aPos;" & vbLf &
            "uniform mat4 uMvp;" & vbLf &
            "void main() { gl_Position = uMvp * vec4(aPos, 0.0, 1.0); }" & vbLf
        Dim fs As String =
            "#version 330 core" & vbLf &
            "uniform vec4 uColor;" & vbLf &
            "out vec4 FragColor;" & vbLf &
            "void main() { FragColor = uColor; }" & vbLf
        Dim v As Integer = CompileShader(ShaderType.VertexShader, vs)
        Dim f As Integer = CompileShader(ShaderType.FragmentShader, fs)
        _pickProgram = GL.CreateProgram()
        GL.AttachShader(_pickProgram, v)
        GL.AttachShader(_pickProgram, f)
        GL.LinkProgram(_pickProgram)
        Dim ok As Integer
        GL.GetProgram(_pickProgram, GetProgramParameterName.LinkStatus, ok)
        GL.DetachShader(_pickProgram, v)
        GL.DetachShader(_pickProgram, f)
        GL.DeleteShader(v)
        GL.DeleteShader(f)
        If ok = 0 Then Throw New InvalidOperationException("Pick shader link failed: " & GL.GetProgramInfoLog(_pickProgram))
        _pickMvpLoc = GL.GetUniformLocation(_pickProgram, "uMvp")
        _pickColorLoc = GL.GetUniformLocation(_pickProgram, "uColor")

        _pickVao = GL.GenVertexArray()
        _pickVbo = GL.GenBuffer()
        GL.BindVertexArray(_pickVao)
        GL.BindBuffer(BufferTarget.ArrayBuffer, _pickVbo)
        GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, False, 2 * 4, 0)
        GL.EnableVertexAttribArray(0)
        GL.BindVertexArray(0)

        _pickColorRb = GL.GenRenderbuffer()
        GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, _pickColorRb)
        GL.RenderbufferStorage(RenderbufferTarget.Renderbuffer, RenderbufferStorage.Rgba8, PickBufferSize, PickBufferSize)
        _pickStencilRb = GL.GenRenderbuffer()
        GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, _pickStencilRb)
        GL.RenderbufferStorage(RenderbufferTarget.Renderbuffer, RenderbufferStorage.Depth24Stencil8, PickBufferSize, PickBufferSize)
        GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, 0)
        _pickFbo = GL.GenFramebuffer()
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, _pickFbo)
        GL.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, RenderbufferTarget.Renderbuffer, _pickColorRb)
        GL.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthStencilAttachment, RenderbufferTarget.Renderbuffer, _pickStencilRb)
        Dim status = GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer)
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0)
        If status <> FramebufferErrorCode.FramebufferComplete Then Throw New InvalidOperationException("Pick buffer incomplete: " & status.ToString())
        _pickDirty = True
    End Sub

    ''' <summary>Uploads the pick shapes: the text block first, then the drawings bottom to top.</summary>
    Private Sub BuildPickGeometry()
        _pickDirty = False
        _pickItems.Clear()
        Dim v As New List(Of Single)
        If HasText() Then
            Dim tp = _toolpath
            AddPickItem(v, TextSelection, Nothing, {New Pt2(tp.TextMinX, tp.TextMinY), New Pt2(tp.TextMaxX, tp.TextMinY),
                                                   New Pt2(tp.TextMaxX, tp.TextMaxY), New Pt2(tp.TextMinX, tp.TextMaxY)})
        End If
        For i = 0 To _objects.Count - 1
            Dim o = _objects(i)
            If Not o.Visible Then Continue For
            Dim paths = If(o.Kind = DrawingKind.Text, Nothing, o.PlacedPaths(_curveTolerance))
            If paths IsNot Nothing AndAlso paths.Count > 0 Then
                Dim b = Clipper2Lib.Clipper.GetBounds(paths)
                AddPickItem(v, i, paths, {New Pt2(b.left, b.top), New Pt2(b.right, b.top), New Pt2(b.right, b.bottom), New Pt2(b.left, b.bottom)})
            Else
                ' Text, or a drawing without an outline: its whole box.
                AddPickItem(v, i, Nothing, o.Corners())
            End If
        Next
        Dim data = v.ToArray()
        GL.BindBuffer(BufferTarget.ArrayBuffer, _pickVbo)
        GL.BufferData(BufferTarget.ArrayBuffer, data.Length * 4, data, BufferUsageHint.DynamicDraw)
        GL.BindBuffer(BufferTarget.ArrayBuffer, 0)
    End Sub

    Private Sub AddPickItem(v As List(Of Single), id As Integer, paths As Clipper2Lib.PathsD, cover As Pt2())
        Dim it As New PickItem With {.Id = id}
        If paths IsNot Nothing Then
            For Each path In paths
                If path.Count < 3 Then Continue For
                it.Fans.Add(New PickRange With {.First = v.Count \ 2, .Count = path.Count})
                For Each pt In path
                    v.Add(CSng(pt.x)) : v.Add(CSng(pt.y))
                Next
            Next
        End If
        it.Cover = New PickRange With {.First = v.Count \ 2, .Count = cover.Length}
        For Each c In cover
            v.Add(CSng(c.X)) : v.Add(CSng(c.Y))
        Next
        _pickItems.Add(it)
    End Sub

    ''' <summary>Deletes the pick objects (the context is current) and forgets them.</summary>
    Private Sub ReleasePicking(deleteObjects As Boolean)
        If deleteObjects Then
            If _pickFbo <> 0 Then GL.DeleteFramebuffer(_pickFbo)
            If _pickColorRb <> 0 Then GL.DeleteRenderbuffer(_pickColorRb)
            If _pickStencilRb <> 0 Then GL.DeleteRenderbuffer(_pickStencilRb)
            If _pickVbo <> 0 Then GL.DeleteBuffer(_pickVbo)
            If _pickVao <> 0 Then GL.DeleteVertexArray(_pickVao)
            If _pickProgram <> 0 Then GL.DeleteProgram(_pickProgram)
        End If
        _pickFbo = 0 : _pickColorRb = 0 : _pickStencilRb = 0
        _pickVbo = 0 : _pickVao = 0 : _pickProgram = 0
        _pickDirty = True
        _pickFailed = False
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

    ' ---- design objects (select / move / resize / rotate / draw) -----------
    Private Enum DragMode
        None
        Pan
        Orbit
        MoveObject
        MoveText
        Resize
        Rotate
        Create
    End Enum

    ''' <summary>Selection value meaning "the text block".</summary>
    Public Const TextSelection As Integer = -2

    Private _objects As List(Of DesignObject) = New List(Of DesignObject)
    Private _selected As Integer = -1
    Private _curveTolerance As Double = 0.0005
    Private _mode As DragMode = DragMode.None
    Private _tool As DrawTool = DrawTool.Select
    Private _dragStart As Vector3                ' world point where the drag began
    Private _dragDx As Double, _dragDy As Double ' live move delta (inches)
    Private _resizeHandle As Integer = -1
    Private _resizeW As Double, _resizeH As Double, _resizeX As Double, _resizeY As Double   ' live resize result
    Private _startX, _startY, _startW, _startH, _startRot As Double
    Private _rotDeg As Double                    ' live rotation (degrees)
    Private _grabDx, _grabDy As Double           ' grabbed handle minus mouse (world) when the drag began
    Private _rotStartAngle As Double             ' mouse angle about the centre when the rotation began (radians)
    Private _createStart, _createEnd As Pt2      ' box being drawn (world)
    Private _createStartScreen As Point
    Private ReadOnly _areaPoints As New List(Of Pt2)   ' free-form area corners so far (world)
    Private _areaHover As Pt2
    Private _justCreated As Boolean              ' a draw tool just finished: the second click of a double-click is not a command
    Private _pressPoint As Point                 ' where the left button went down (click vs drag, double-click vs drag)
    Private _hoverPickAt As DateTime = DateTime.MinValue
    Private _hoverPickPoint As Point
    Private _hoverPickResult As Integer = -1
    Private ReadOnly _selectionLayer As New LineLayer()

    ''' <summary>Raised after the user moved, resized or rotated an object with the mouse or keyboard (index in Objects).</summary>
    Public Event ObjectEdited As EventHandler(Of ObjectEditedEventArgs)
    ''' <summary>Raised after the user dragged the text block (delta in inches).</summary>
    Public Event TextMoved As EventHandler(Of TextMovedEventArgs)
    ''' <summary>Raised when the selection changes (SelectedObject).</summary>
    Public Event SelectionChanged As EventHandler
    ''' <summary>Raised when a draw tool finished a shape; the form makes the drawing.</summary>
    Public Event ShapeDrawn As EventHandler(Of ShapeDrawnEventArgs)
    ''' <summary>Raised when ActiveTool changes (keys, menu, or back to Select after drawing).</summary>
    Public Event ToolChanged As EventHandler
    ''' <summary>Raised for Delete with a drawing selected.</summary>
    Public Event DeleteRequested As EventHandler
    ''' <summary>Raised on a double-click on a text drawing (index in Objects).</summary>
    Public Event EditTextRequested As EventHandler(Of ObjectEditedEventArgs)
    ''' <summary>Raised while dragging: size, letter height or angle, for the status bar.</summary>
    Public Event DragFeedback As EventHandler(Of DragFeedbackEventArgs)
    ''' <summary>Raised after the camera moved (pan, orbit, zoom, fit, top/iso view).</summary>
    Public Event CameraChanged As EventHandler

    Public Class ObjectEditedEventArgs
        Inherits EventArgs
        Public Property Index As Integer
    End Class

    Public Class TextMovedEventArgs
        Inherits EventArgs
        Public Property Dx As Double
        Public Property Dy As Double
    End Class

    Public Class ShapeDrawnEventArgs
        Inherits EventArgs
        Public Property Kind As DrawingKind
        ''' <summary>The box dragged out (machine inches); for a plain click both corners are the click point.</summary>
        Public Property MinX As Double
        Public Property MinY As Double
        Public Property MaxX As Double
        Public Property MaxY As Double
        ''' <summary>True when the mouse barely moved: make the shape at a default size.</summary>
        Public Property IsClick As Boolean
        ''' <summary>Corners of a free-form area (machine inches); Nothing for the other kinds.</summary>
        Public Property Points As List(Of Pt2)
    End Class

    Public Class DragFeedbackEventArgs
        Inherits EventArgs
        Public Property Text As String
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
        _pickDirty = True
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
            Dim ci = Globalization.CultureInfo.InvariantCulture
            Dim info = String.Format(ci, "{0} x {1} cells at {2:0.0000}"" ({3:0.#} MB)",
                                     _sim.TextureWidth, _sim.TextureHeight, _sim.CellSize, _sim.TextureWidth * CDbl(_sim.TextureHeight) * 4 / 1048576.0)
            If _sim.CellSize > _sim.RequestedCellSize * 1.001 Then
                info &= String.Format(ci, ", coarsened from {0:0.000}"" to fit the GPU", _sim.RequestedCellSize)
            End If
            If _sim.DisplayCellsX < _sim.TextureWidth OrElse _sim.DisplayCellsY < _sim.TextureHeight Then
                info &= String.Format(ci, ", drawn at {0} x {1}", _sim.DisplayCellsX, _sim.DisplayCellsY)
            End If
            Return info
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

    ''' <summary>
    ''' Applies simulation-only settings (precision) to the current toolpath without
    ''' regenerating it; the heightmap is rebuilt on the next paint and the clock is kept.
    ''' </summary>
    Public Sub UpdateSimulationSettings(settings As CarveSettings)
        If settings Is Nothing Then Return
        _simSettings = settings
        _simNeedsSetup = True
        Invalidate()
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
            ' The heightmap size is known only now: tell the form after this paint finishes.
            If IsHandleCreated Then BeginInvoke(Sub() RaiseEvent SimulationProgress(Me, EventArgs.Empty))
        End If
        If Not _sim.IsReady Then Return
        _sim.AdvanceTo(Math.Min(_simTime, _sim.TotalSeconds))
    End Sub

    Public Sub SetTopView()
        _yaw = 0
        _pitch = 0
        Invalidate()
        RaiseEvent CameraChanged(Me, EventArgs.Empty)
    End Sub

    Public Sub SetIsoView()
        _yaw = -Math.PI / 4
        _pitch = Math.PI / 3.2
        Invalidate()
        RaiseEvent CameraChanged(Me, EventArgs.Empty)
    End Sub

    Public Sub ZoomToFit()
        Dim size As Vector3 = _sceneMax - _sceneMin
        _target = (_sceneMin + _sceneMax) / 2.0F
        Dim w As Double = Math.Max(size.X, 0.5)
        Dim h As Double = Math.Max(size.Y, 0.5)
        Dim pw As Integer = Math.Max(ClientSize.Width, 1)
        Dim ph As Integer = Math.Max(ClientSize.Height, 1)
        _unitsPerPixel = Math.Max(w / pw, h / ph) * 1.15
        RebuildSelectionLayer()      ' handle size follows the zoom
        Invalidate()
        RaiseEvent CameraChanged(Me, EventArgs.Empty)
    End Sub

    ' --------------------------------------------------------------- buffers

    Private Sub BuildBuffers()
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

        _gridMinX = minX : _gridMinY = minY : _gridMaxX = maxX : _gridMaxY = maxY
        BuildGridLayer()

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

        SetPending(_blank, blank)
        SetPending(_outline, outline)
        SetPending(_cuts, cuts)
        SetPending(_rapids, rapids)
        Invalidate()
    End Sub

    ''' <summary>
    ''' Grid: 1" cells over the scene plus 2", bright axes through the origin, and while
    ''' snapping is on, dim lines at the snap spacing. With the simulation shown the grid
    ''' sits under the board so it does not show through the surface.
    ''' </summary>
    Private Sub BuildGridLayer()
        Dim grid As New List(Of Single)
        Dim gz As Double = If(_showSimulation AndAlso _simSettings IsNot Nothing, -Math.Max(0.05, Math.Min(_simSettings.StockThickness, 2.0)) - 0.001, 0.0)
        Dim g0x As Integer = CInt(Math.Floor(_gridMinX)) - 2
        Dim g1x As Integer = CInt(Math.Ceiling(_gridMaxX)) + 2
        Dim g0y As Integer = CInt(Math.Floor(_gridMinY)) - 2
        Dim g1y As Integer = CInt(Math.Ceiling(_gridMaxY)) + 2
        If g1x - g0x > 400 Then g1x = g0x + 400
        If g1y - g0y > 400 Then g1y = g0y + 400
        If _snapToGrid AndAlso _gridSpacing > 0 AndAlso _gridSpacing < 1 Then
            Dim perInch = CInt(Math.Round(1 / _gridSpacing))
            If (g1x - g0x) * perInch <= 4000 AndAlso (g1y - g0y) * perInch <= 4000 Then
                Const m As Single = 0.17F
                For i = 1 To (g1x - g0x) * perInch - 1
                    If i Mod perInch = 0 Then Continue For
                    Dim x = g0x + i / CDbl(perInch)
                    AddLine(grid, x, g0y, gz, x, g1y, gz, m, m, m)
                Next
                For i = 1 To (g1y - g0y) * perInch - 1
                    If i Mod perInch = 0 Then Continue For
                    Dim y = g0y + i / CDbl(perInch)
                    AddLine(grid, g0x, y, gz, g1x, y, gz, m, m, m)
                Next
            End If
        End If
        For x = g0x To g1x
            Dim c As Single = If(x = 0, 0.45F, 0.2F)
            AddLine(grid, x, g0y, gz, x, g1y, gz, If(x = 0, 0.3F, c), If(x = 0, 0.8F, c), If(x = 0, 0.3F, c))
        Next
        For y = g0y To g1y
            Dim c As Single = If(y = 0, 0.45F, 0.2F)
            AddLine(grid, g0x, y, gz, g1x, y, gz, If(y = 0, 0.9F, c), If(y = 0, 0.3F, c), If(y = 0, 0.3F, c))
        Next
        SetPending(_grid, grid)
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
                ReleasePicking(deleteObjects:=True)
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
        ReleasePicking(deleteObjects:=False)
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
    Public Function ScreenToWorld(p As Point) As Vector3
        Dim cx As Double = ClientSize.Width / 2.0
        Dim cy As Double = ClientSize.Height / 2.0
        Dim dx As Double = (p.X - cx) * _unitsPerPixel
        Dim dy As Double = (cy - p.Y) * _unitsPerPixel / PitchScale()
        Dim c As Double = Math.Cos(-_yaw), sn As Double = Math.Sin(-_yaw)
        Return New Vector3(CSng(_target.X + dx * c - dy * sn), CSng(_target.Y + dx * sn + dy * c), 0)
    End Function

    ''' <summary>World point on the Z=0 plane -> screen pixel (the inverse of ScreenToWorld).</summary>
    Public Function WorldToScreen(x As Double, y As Double) As Point
        Dim dx = x - _target.X, dy = y - _target.Y
        Dim c = Math.Cos(_yaw), sn = Math.Sin(_yaw)
        Dim sx = dx * c - dy * sn, sy = dx * sn + dy * c
        Return New Point(CInt(Math.Round(ClientSize.Width / 2.0 + sx / _unitsPerPixel)),
                         CInt(Math.Round(ClientSize.Height / 2.0 - sy * PitchScale() / _unitsPerPixel)))
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

        GL.ClearColor(0.12F, 0.12F, 0.13F, 1.0F)
        GL.Clear(ClearBufferMask.ColorBufferBit Or ClearBufferMask.DepthBufferBit)
        For Each layer In AllLayers
            UploadIfDirty(layer)
        Next

        Dim mvp As Matrix4 = ViewMatrix() * ProjectionMatrix()
        If _showSimulation AndAlso _sim.IsReady Then
            _sim.DrawBoard(mvp)
            If _simTime < _sim.TotalSeconds Then _sim.DrawTool(mvp, _sim.ToolPositionAt(_simTime), _sim.ToolIndexAt(_simTime))
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
        ' The second click of a double-click that finished a shape: not a new command (and keep
        ' the focus where the form put it, e.g. in the text editor of a new text).
        If _justCreated AndAlso e.Clicks >= 2 Then Return
        _justCreated = False
        Focus()
        CancelDrag()
        _altDuringDrag = (ModifierKeys And Keys.Alt) = Keys.Alt
        _dragButton = e.Button
        _lastMouse = e.Location
        _pressPoint = e.Location
        _mode = DragMode.None
        If e.Button = MouseButtons.Right Then
            _mode = DragMode.Orbit
        ElseIf e.Button = MouseButtons.Middle Then
            _mode = DragMode.Pan
        ElseIf e.Button = MouseButtons.Left Then
            Dim w = ScreenToWorld(e.Location)
            _dragStart = w
            _dragDx = 0 : _dragDy = 0
            Select Case _tool
                Case DrawTool.Select
                    BeginSelectDrag(e.Location, w)
                Case DrawTool.Area
                    AddAreaPoint(e.Location)
                Case Else
                    _createStartScreen = e.Location
                    _createStart = SnapPoint(w.X, w.Y)
                    _createEnd = _createStart
                    _mode = DragMode.Create
                    RebuildSelectionLayer()
                    Invalidate()
            End Select
        End If
    End Sub

    ''' <summary>Select tool: grab a handle of the selection, or pick and start moving what is under the mouse.</summary>
    Private Sub BeginSelectDrag(p As Point, w As Vector3)
        Dim handle = HandleHitTest(p)
        If handle >= 0 Then
            Dim o = _objects(_selected)
            _resizeHandle = handle
            _startX = o.X : _startY = o.Y : _startW = o.Width : _startH = o.Height : _startRot = o.RotationDeg
            _resizeX = o.X : _resizeY = o.Y : _resizeW = o.Width : _resizeH = o.Height
            Dim hp = HandlePoints(o)(handle)
            _grabDx = hp.X - w.X : _grabDy = hp.Y - w.Y
            If handle = RotateHandle Then
                Dim c = o.LocalToWorld(0, 0)
                _rotStartAngle = Math.Atan2(w.Y - c.Y, w.X - c.X)
                _rotDeg = o.RotationDeg
                _mode = DragMode.Rotate
            Else
                _mode = DragMode.Resize
            End If
            Cursor = HandleCursor(handle)
            Return
        End If
        Dim hit = PickAt(p)
        SelectedObject = hit
        If hit >= 0 Then
            _mode = DragMode.MoveObject
            Cursor = Cursors.SizeAll
        ElseIf hit = TextSelection Then
            _mode = DragMode.MoveText
        Else
            _mode = DragMode.Pan
        End If
    End Sub

    Protected Overrides Sub OnMouseUp(e As MouseEventArgs)
        MyBase.OnMouseUp(e)
        ' Another button of a chord: the drag belongs to the button that started it (losing the
        ' capture right after this cancels it cleanly).
        If _dragButton <> MouseButtons.None AndAlso e.Button <> _dragButton Then Return
        Dim mode = _mode
        ' The result is where the button was released (not wherever the last move message was).
        If mode <> DragMode.None AndAlso mode <> DragMode.Pan AndAlso mode <> DragMode.Orbit Then UpdateDrag(e.Location)
        ' A press that hardly moved is a click (select), not a move, resize or rotation.
        If (mode = DragMode.MoveObject OrElse mode = DragMode.MoveText OrElse mode = DragMode.Resize OrElse mode = DragMode.Rotate) AndAlso
           Math.Abs(e.X - _pressPoint.X) <= SystemInformation.DragSize.Width \ 2 AndAlso Math.Abs(e.Y - _pressPoint.Y) <= SystemInformation.DragSize.Height \ 2 Then
            CancelDrag()
            Return
        End If
        If mode = DragMode.Create Then
            _dragButton = MouseButtons.None
            FinishCreate(e.Location)
            Return
        End If
        _mode = DragMode.None
        _dragButton = MouseButtons.None
        Cursor = If(_tool = DrawTool.Select, Cursors.Default, ToolCursor())
        Dim valid = _selected >= 0 AndAlso _selected < _objects.Count
        Select Case mode
            Case DragMode.MoveObject
                If valid AndAlso (_dragDx <> 0 OrElse _dragDy <> 0) Then
                    _objects(_selected).X = Math.Round(_objects(_selected).X + _dragDx, 4)
                    _objects(_selected).Y = Math.Round(_objects(_selected).Y + _dragDy, 4)
                    _dragDx = 0 : _dragDy = 0
                    CommitEdit()
                End If
            Case DragMode.Resize
                If valid AndAlso (Math.Abs(_resizeW - _startW) > 0.00005 OrElse Math.Abs(_resizeH - _startH) > 0.00005) Then
                    Dim o = _objects(_selected)
                    If o.Kind = DrawingKind.Text Then
                        ' Lettering scales with the box: a new letter height, the opposite corner stays put.
                        o.ScaleText(_resizeW / _startW, (_resizeHandle + 2) Mod 4, _curveTolerance)
                    Else
                        o.X = Math.Round(_resizeX, 4) : o.Y = Math.Round(_resizeY, 4)
                        o.Width = Math.Round(_resizeW, 4) : o.Height = Math.Round(_resizeH, 4)
                    End If
                    CommitEdit()
                End If
            Case DragMode.Rotate
                If valid AndAlso Math.Abs(NormalizeAngle(_rotDeg - _startRot)) > 0.005 Then
                    _objects(_selected).RotationDeg = Math.Round(_rotDeg, 2)
                    CommitEdit()
                End If
            Case DragMode.MoveText
                Dim dx = _dragDx, dy = _dragDy
                _dragDx = 0 : _dragDy = 0
                RebuildSelectionLayer()
                Invalidate()
                If dx <> 0 OrElse dy <> 0 Then RaiseEvent TextMoved(Me, New TextMovedEventArgs With {.Dx = dx, .Dy = dy})
        End Select
        If mode = DragMode.Resize OrElse mode = DragMode.Rotate OrElse mode = DragMode.MoveObject Then
            RebuildSelectionLayer()
            Invalidate()
        End If
    End Sub

    Protected Overrides Sub OnMouseCaptureChanged(e As EventArgs)
        MyBase.OnMouseCaptureChanged(e)
        ' Mouse-up already ended a normal drag; losing the capture any other way abandons it.
        If Not Capture Then
            If _mode = DragMode.Create Then
                CancelDrawing()
            ElseIf _mode <> DragMode.None Then
                CancelDrag()
            End If
            _dragButton = MouseButtons.None
        End If
    End Sub

    ''' <summary>The selected drawing changed under the mouse: redraw and tell the form.</summary>
    Private Sub CommitEdit()
        _pickDirty = True
        RebuildSelectionLayer()
        Invalidate()
        RaiseEvent ObjectEdited(Me, New ObjectEditedEventArgs With {.Index = _selected})
    End Sub

    Protected Overrides Sub OnMouseMove(e As MouseEventArgs)
        MyBase.OnMouseMove(e)
        If _mode = DragMode.None Then
            If _dragButton = MouseButtons.None Then
                If _tool = DrawTool.Area AndAlso _areaPoints.Count > 0 Then
                    Dim w = ScreenToWorld(e.Location)
                    _areaHover = SnapPoint(w.X, w.Y)
                    RebuildSelectionLayer()
                    Invalidate()
                End If
                UpdateHoverCursor(e.Location)
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
                RaiseEvent CameraChanged(Me, EventArgs.Empty)
            Case DragMode.Pan
                ' Pan: move the target opposite to the mouse, in screen-aligned world axes.
                Dim wx As Double = -dx * _unitsPerPixel
                Dim wy As Double = dy * _unitsPerPixel / PitchScale()
                Dim c As Double = Math.Cos(-_yaw), sn As Double = Math.Sin(-_yaw)
                _target.X += CSng(wx * c - wy * sn)
                _target.Y += CSng(wx * sn + wy * c)
                RaiseEvent CameraChanged(Me, EventArgs.Empty)
            Case Else
                UpdateDrag(e.Location)
        End Select
        Invalidate()
    End Sub

    ''' <summary>Live result of a move, handle, rotation or draw drag for a mouse position.</summary>
    Private Sub UpdateDrag(p As Point)
        Dim ci = Globalization.CultureInfo.InvariantCulture
        Dim w = ScreenToWorld(p)
        If (ModifierKeys And Keys.Alt) = Keys.Alt Then _altDuringDrag = True
        Select Case _mode
            Case DragMode.MoveObject, DragMode.MoveText
                _dragDx = w.X - _dragStart.X
                _dragDy = w.Y - _dragStart.Y
                SnapMove(MoveReferencePoints(), _dragDx, _dragDy)
                RebuildSelectionLayer()
                If _mode = DragMode.MoveObject AndAlso _selected >= 0 AndAlso _selected < _objects.Count Then
                    RaiseFeedback(String.Format(ci, "X {0:0.000}""  Y {1:0.000}""", _objects(_selected).X + _dragDx, _objects(_selected).Y + _dragDy))
                End If
            Case DragMode.Resize
                ' The handle follows the mouse with the offset it was grabbed at (no jump on the first
                ' move). With grid snapping a side lands on the grid here, a corner in ComputeResize.
                Dim target = If(_resizeHandle >= 4, SnapPoint(w.X + _grabDx, w.Y + _grabDy), New Pt2(w.X + _grabDx, w.Y + _grabDy))
                ComputeResize(target.X, target.Y)
                RebuildSelectionLayer()
                If _selected >= 0 AndAlso _selected < _objects.Count AndAlso _objects(_selected).Kind = DrawingKind.Text Then
                    RaiseFeedback(String.Format(ci, "Letter height {0:0.000}""", _objects(_selected).LetterHeight * _resizeW / _startW))
                Else
                    RaiseFeedback(String.Format(ci, "{0:0.000}"" x {1:0.000}""", _resizeW, _resizeH))
                End If
            Case DragMode.Rotate
                If _selected >= 0 AndAlso _selected < _objects.Count Then
                    Dim c = _objects(_selected).LocalToWorld(0, 0)
                    ' Close to the centre the angle swings wildly: hold the last one there.
                    Dim cs = WorldToScreen(c.X, c.Y)
                    If Math.Sqrt(CDbl(p.X - cs.X) ^ 2 + CDbl(p.Y - cs.Y) ^ 2) < RotateDeadZonePixels Then Return
                    Dim a = Math.Atan2(w.Y - c.Y, w.X - c.X)
                    Dim deg = _startRot + (a - _rotStartAngle) * 180 / Math.PI
                    ' Snaps to the rotation step; Shift turns freely.
                    Dim stepDeg = If((ModifierKeys And Keys.Shift) = Keys.Shift, 0.0, _rotationSnapDeg)
                    If stepDeg > 0 Then deg = Math.Round(deg / stepDeg) * stepDeg
                    _rotDeg = NormalizeAngle(deg)
                    RebuildSelectionLayer()
                    RaiseFeedback(String.Format(ci, "Rotation {0:0.0}°", _rotDeg))
                End If
            Case DragMode.Create
                _createEnd = SnapPoint(w.X, w.Y)
                RebuildSelectionLayer()
                Dim minX, minY, maxX, maxY As Double
                CreateBox(minX, minY, maxX, maxY)
                RaiseFeedback(String.Format(ci, "{0:0.000}"" x {1:0.000}""", maxX - minX, maxY - minY))
        End Select
    End Sub

    ''' <summary>Cursor feedback without a button down: tool, handle, or something to pick.</summary>
    Private Sub UpdateHoverCursor(p As Point)
        If _tool <> DrawTool.Select Then
            Cursor = ToolCursor()
            Return
        End If
        Dim h = HandleHitTest(p)
        If h >= 0 Then
            Cursor = HandleCursor(h)
        Else
            Cursor = If(HoverPick(p) <> -1, Cursors.Hand, Cursors.Default)
        End If
    End Sub

    ''' <summary>
    ''' Pick for the hover cursor: each GPU pick waits for the GPU, so it runs at most every
    ''' 30 ms (or after a 3 px move), and while the simulation plays bounding boxes do.
    ''' </summary>
    Private Function HoverPick(p As Point) As Integer
        If _simPlaying Then
            Dim w = ScreenToWorld(p)
            Return CpuHitTest(w.X, w.Y)
        End If
        Dim now = DateTime.UtcNow
        If (now - _hoverPickAt).TotalMilliseconds < 30 AndAlso Math.Abs(p.X - _hoverPickPoint.X) < 3 AndAlso Math.Abs(p.Y - _hoverPickPoint.Y) < 3 Then
            Return _hoverPickResult
        End If
        _hoverPickResult = PickAt(p)
        _hoverPickAt = now
        _hoverPickPoint = p
        Return _hoverPickResult
    End Function

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
        RaiseEvent CameraChanged(Me, EventArgs.Empty)
    End Sub

    Protected Overrides Sub OnMouseDoubleClick(e As MouseEventArgs)
        MyBase.OnMouseDoubleClick(e)
        ' WinForms reports a double-click even when the second press was dragged away: that is a
        ' drag, and the mouse-up that follows commits it.
        If Math.Abs(e.X - _pressPoint.X) > SystemInformation.DoubleClickSize.Width \ 2 OrElse
           Math.Abs(e.Y - _pressPoint.Y) > SystemInformation.DoubleClickSize.Height \ 2 Then Return
        ' The second press already started a move, handle or draw drag; a double-click is not a drag
        ' (and the fit below changes the camera, which would turn the mouse-up into a jump).
        CancelDrag()
        If _justCreated Then
            _justCreated = False
            Return
        End If
        If _tool = DrawTool.Area Then
            If e.Button = MouseButtons.Left Then FinishArea()
            Return
        End If
        If _tool <> DrawTool.Select Then Return
        If e.Button = MouseButtons.Left Then
            Dim hit = PickAt(e.Location)
            If hit >= 0 AndAlso hit < _objects.Count AndAlso _objects(hit).Kind = DrawingKind.Text Then
                _mode = DragMode.None
                SelectedObject = hit
                RaiseEvent EditTextRequested(Me, New ObjectEditedEventArgs With {.Index = hit})
                Return
            End If
        End If
        ZoomToFit()
    End Sub
End Class

''' <summary>What a left drag in the view does.</summary>
Public Enum DrawTool
    [Select]
    Rectangle
    Ellipse
    Polygon
    Star
    Area
    Text
End Enum
