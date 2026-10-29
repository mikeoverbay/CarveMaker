' ============================================================================
'  Icons.vb
'  The pictures on the buttons and in the menus, drawn in code on a 20-unit
'  grid so they come out sharp at any size: light ink for the dark buttons,
'  dark ink for a toggle that is on (yellow). The tool library's button shows
'  a real tool, drawn by ToolDrawing.
' ============================================================================

Imports System.Drawing.Drawing2D
Imports System.Drawing.Imaging
Imports System.Drawing.Text

Friend Enum IconKind
    SelectArrow
    Rectangle
    Ellipse
    Polygon
    Star
    Area
    Text
    Bold
    Italic
    AlignLeft
    AlignCenter
    AlignRight
    SnapGrid
    Simulation
    Toolpath
    CutMoves
    RapidMoves
    Font
    Hide
    ShowAll
    Move
    Scale
    Rotate
    Center
    Mirror
    NewFile
    Duplicate
    Open
    Save
    Generate
    StopGenerate
    Export
    Machine
    ToolLibrary
    Play
    Pause
    Rewind
    ImportSvg
    Import3D
    Remove
    FitToBlank
    Stock
    ObjectSettings
    TextJob
    Drawings
    StatusOff
    StatusBusy
    StatusOk
    StatusProblem
    StatusProblemDim
End Enum

Friend Module Icons

    ''' <summary>The colours of one icon: light on the dark buttons, dark on a yellow one.</summary>
    Private NotInheritable Class Palette
        Public ReadOnly Main, Soft, Cyan, Lime, Red, Yellow, Wood, WoodDeep, Blue, Shadow As Color
        Public ReadOnly OnYellow As Boolean

        Public Sub New(onYellow As Boolean)
            Me.OnYellow = onYellow
            If onYellow Then
                Main = Color.FromArgb(28, 28, 22) : Soft = Color.FromArgb(96, 94, 60)
                Cyan = Color.FromArgb(0, 84, 130) : Lime = Color.FromArgb(52, 80, 0)
                Red = Color.FromArgb(186, 28, 18) : Yellow = Color.FromArgb(120, 96, 0)
                Wood = Color.FromArgb(168, 104, 40) : WoodDeep = Color.FromArgb(92, 54, 18)
                Blue = Color.FromArgb(20, 58, 170) : Shadow = Color.FromArgb(0, 0, 0, 0)
            Else
                Main = Color.FromArgb(238, 238, 238) : Soft = Color.FromArgb(140, 140, 146)
                Cyan = Color.FromArgb(72, 204, 244) : Lime = Color.FromArgb(178, 222, 24)
                Red = Color.FromArgb(244, 72, 56) : Yellow = Color.FromArgb(246, 222, 80)
                Wood = Color.FromArgb(226, 168, 92) : WoodDeep = Color.FromArgb(120, 74, 32)
                Blue = Color.FromArgb(92, 150, 255) : Shadow = Color.FromArgb(150, 0, 0, 0)
            End If
        End Sub

        Public Function Pen(c As Color, Optional width As Single = 1.5F) As Pen
            Return New Pen(c, width) With {.LineJoin = LineJoin.Round, .StartCap = LineCap.Round, .EndCap = LineCap.Round}
        End Function
    End Class

    ''' <summary>One icon, size x size pixels.</summary>
    Public Function Make(kind As IconKind, size As Integer, Optional onYellow As Boolean = False) As Bitmap
        Dim bmp As New Bitmap(size, size, PixelFormat.Format32bppPArgb)
        Using g = Graphics.FromImage(bmp)
            g.Clear(Color.Transparent)
            g.SmoothingMode = SmoothingMode.AntiAlias
            g.PixelOffsetMode = PixelOffsetMode.HighQuality
            g.TextRenderingHint = TextRenderingHint.AntiAlias
            If kind = IconKind.ToolLibrary Then
                ' A real tool, from the tool drawing (a ball nose end mill).
                Using tool = ToolDrawing.RenderIcon(ToolType.BallNose, size)
                    g.DrawImage(tool, 0, 0, size, size)
                End Using
            Else
                g.ScaleTransform(size / 20.0F, size / 20.0F)
                Paint(g, kind, New Palette(onYellow))
            End If
        End Using
        Return bmp
    End Function

    Private Function Pt(x As Single, y As Single) As PointF
        Return New PointF(x, y)
    End Function

    ''' <summary>A filled arrow head at the tip, pointing along (dx, dy).</summary>
    Private Sub ArrowHead(g As Graphics, b As Brush, tip As PointF, dx As Single, dy As Single, size As Single)
        Dim len = CSng(Math.Sqrt(dx * dx + dy * dy))
        If len <= 0 Then Return
        dx /= len : dy /= len
        Dim baseX = tip.X - dx * size, baseY = tip.Y - dy * size
        Dim px = -dy * size * 0.62F, py = dx * size * 0.62F
        g.FillPolygon(b, {tip, Pt(baseX + px, baseY + py), Pt(baseX - px, baseY - py)})
    End Sub

    ''' <summary>Text fitted into a box (keeping its shape), filled.</summary>
    Private Sub FitText(g As Graphics, text As String, family As String, style As FontStyle, box As RectangleF, b As Brush)
        Using path As New GraphicsPath()
            Using fam = New FontFamily(family)
                path.AddString(text, fam, CInt(style), 100, PointF.Empty, StringFormat.GenericTypographic)
            End Using
            Dim r = path.GetBounds()
            If r.Width <= 0 OrElse r.Height <= 0 Then Return
            Dim k = Math.Min(box.Width / r.Width, box.Height / r.Height)
            Using m As New Matrix()
                m.Translate(box.X + (box.Width - r.Width * k) / 2, box.Y + (box.Height - r.Height * k) / 2)
                m.Scale(k, k)
                m.Translate(-r.X, -r.Y)
                path.Transform(m)
            End Using
            g.FillPath(b, path)
        End Using
    End Sub

    Private Function Regular(cx As Single, cy As Single, r As Single, n As Integer, Optional startDeg As Single = -90) As PointF()
        Dim pts(n - 1) As PointF
        For i = 0 To n - 1
            Dim a = (startDeg + 360.0 * i / n) * Math.PI / 180
            pts(i) = Pt(CSng(cx + r * Math.Cos(a)), CSng(cy + r * Math.Sin(a)))
        Next
        Return pts
    End Function

    Private Function StarPoints(cx As Single, cy As Single, outer As Single, inner As Single, points As Integer) As PointF()
        Dim pts(2 * points - 1) As PointF
        For i = 0 To 2 * points - 1
            Dim a = (-90 + 180.0 * i / points) * Math.PI / 180
            Dim r = If(i Mod 2 = 0, outer, inner)
            pts(i) = Pt(CSng(cx + r * Math.Cos(a)), CSng(cy + r * Math.Sin(a)))
        Next
        Return pts
    End Function

    ''' <summary>An eye (Hide crosses it out).</summary>
    Private Sub Eye(g As Graphics, p As Palette, pupil As Color)
        Using path As New GraphicsPath()
            path.AddBezier(Pt(1.8F, 10), Pt(5.5F, 3.6F), Pt(14.5F, 3.6F), Pt(18.2F, 10))
            path.AddBezier(Pt(18.2F, 10), Pt(14.5F, 16.4F), Pt(5.5F, 16.4F), Pt(1.8F, 10))
            Using pen = p.Pen(p.Main, 1.5F)
                g.DrawPath(pen, path)
            End Using
        End Using
        Using b As New SolidBrush(pupil)
            g.FillEllipse(b, 7.2F, 7.2F, 5.6F, 5.6F)
        End Using
        Using b As New SolidBrush(p.Main)
            g.FillEllipse(b, 9.6F, 8.2F, 1.8F, 1.8F)
        End Using
    End Sub

    ''' <summary>A page with a folded corner.</summary>
    Private Sub Page(g As Graphics, p As Palette, left As Single, top As Single, right As Single, bottom As Single)
        Dim fold = 3.6F
        Using pen = p.Pen(p.Main, 1.4F)
            g.DrawPolygon(pen, {Pt(left, top), Pt(right - fold, top), Pt(right, top + fold), Pt(right, bottom), Pt(left, bottom)})
            g.DrawLines(pen, {Pt(right - fold, top), Pt(right - fold, top + fold), Pt(right, top + fold)})
        End Using
    End Sub

    ''' <summary>A status light: a lit dome in the colour, with a sign on it.</summary>
    Private Sub StatusLight(g As Graphics, lit As Color, sign As IconKind)
        Dim r As New RectangleF(2, 2, 16, 16)
        Using dome As New GraphicsPath()
            dome.AddEllipse(r)
            Using b As New PathGradientBrush(dome)
                b.CenterPoint = New PointF(8.2F, 7.4F)
                b.CenterColor = ControlPaint.Light(lit, 0.6F)
                b.SurroundColors = {ControlPaint.Dark(lit, 0.15F)}
                g.FillPath(b, dome)
            End Using
            Using rim As New Pen(Color.FromArgb(200, 0, 0, 0), 1.1F)
                g.DrawPath(rim, dome)
            End Using
        End Using
        Using shine As New SolidBrush(Color.FromArgb(110, Color.White))
            g.FillEllipse(shine, 5.6F, 4.2F, 5.2F, 3.2F)
        End Using
        Using ink As New Pen(Color.FromArgb(235, 255, 255, 255), 2.0F) With {.StartCap = LineCap.Round, .EndCap = LineCap.Round, .LineJoin = LineJoin.Round}
            Select Case sign
                Case IconKind.StatusOk
                    g.DrawLines(ink, {Pt(6.2F, 10.4F), Pt(8.9F, 13.1F), Pt(14, 7.4F)})
                Case IconKind.StatusProblem
                    g.DrawLine(ink, 10, 5.8F, 10, 11.2F)
                    g.DrawLine(ink, 10, 14.2F, 10, 14.3F)
                Case IconKind.StatusBusy
                    For Each x In {6.4F, 10.0F, 13.6F}
                        g.DrawLine(ink, x, 10.4F, x, 10.5F)
                    Next
            End Select
        End Using
    End Sub

    Private Sub Paint(g As Graphics, kind As IconKind, p As Palette)
        Select Case kind
            Case IconKind.StatusOff : StatusLight(g, Color.FromArgb(78, 78, 84), kind) : Return
            Case IconKind.StatusBusy : StatusLight(g, Color.FromArgb(244, 176, 32), kind) : Return
            Case IconKind.StatusOk : StatusLight(g, Color.FromArgb(52, 196, 72), kind) : Return
            Case IconKind.StatusProblem : StatusLight(g, Color.FromArgb(232, 40, 32), kind) : Return
            Case IconKind.StatusProblemDim : StatusLight(g, Color.FromArgb(90, 22, 20), IconKind.StatusProblem) : Return
        End Select
        Using main As New SolidBrush(p.Main), soft As New SolidBrush(p.Soft), cyan As New SolidBrush(p.Cyan),
              lime As New SolidBrush(p.Lime), red As New SolidBrush(p.Red), yellow As New SolidBrush(p.Yellow),
              line = p.Pen(p.Main, 1.6F), thin = p.Pen(p.Main, 1.1F), softLine = p.Pen(p.Soft, 1.1F), cyanLine = p.Pen(p.Cyan, 1.6F)
            Select Case kind
                Case IconKind.SelectArrow
                    Dim arrow = {Pt(5, 2.2F), Pt(5, 16.6F), Pt(8.5F, 13.3F), Pt(10.8F, 18.3F), Pt(13.2F, 17.2F), Pt(10.9F, 12.3F), Pt(15.5F, 12.1F)}
                    g.FillPolygon(main, arrow)
                Case IconKind.Rectangle
                    g.DrawRectangle(line, 2.5F, 5, 15, 10)
                Case IconKind.Ellipse
                    g.DrawEllipse(line, 2.2F, 4.4F, 15.6F, 11.2F)
                Case IconKind.Polygon
                    g.DrawPolygon(line, Regular(10, 10.4F, 7.8F, 6))
                Case IconKind.Star
                    g.DrawPolygon(line, StarPoints(10, 10.8F, 8.6F, 3.6F, 5))
                Case IconKind.Area
                    Dim pts = {Pt(3, 15.5F), Pt(5, 4.5F), Pt(11, 7.2F), Pt(16.8F, 3.4F), Pt(17, 15.8F), Pt(10, 12.6F)}
                    Using pen = p.Pen(p.Main, 1.3F)
                        g.DrawPolygon(pen, pts)
                    End Using
                    For Each q In pts
                        g.FillRectangle(lime, q.X - 1.3F, q.Y - 1.3F, 2.6F, 2.6F)
                    Next
                Case IconKind.Text
                    g.FillRectangle(main, 3.2F, 2.8F, 13.6F, 2.6F)
                    g.FillRectangle(main, 8.7F, 2.8F, 2.6F, 14.6F)
                    g.FillRectangle(main, 6.6F, 15.8F, 6.8F, 1.6F)
                Case IconKind.Bold
                    FitText(g, "B", "Arial", FontStyle.Bold, New RectangleF(4.5F, 3, 11, 14), main)
                Case IconKind.Italic
                    FitText(g, "I", "Times New Roman", FontStyle.Italic, New RectangleF(5.5F, 3, 9, 14), main)
                Case IconKind.AlignLeft, IconKind.AlignCenter, IconKind.AlignRight
                    Dim widths = {14.0F, 9.0F, 12.0F, 7.0F}
                    Using pen = p.Pen(p.Main, 1.7F)
                        For i = 0 To 3
                            Dim y = 4.0F + i * 4
                            Dim w = widths(i)
                            Dim x0 = If(kind = IconKind.AlignLeft, 3.0F, If(kind = IconKind.AlignRight, 17 - w, 10 - w / 2))
                            g.DrawLine(pen, x0, y, x0 + w, y)
                        Next
                    End Using
                Case IconKind.SnapGrid
                    For Each v In {3.5F, 10.0F, 16.5F}
                        g.DrawLine(softLine, v, 2, v, 18)
                        g.DrawLine(softLine, 2, v, 18, v)
                    Next
                    g.FillEllipse(lime, 7.2F, 7.2F, 5.6F, 5.6F)
                    g.DrawEllipse(thin, 7.2F, 7.2F, 5.6F, 5.6F)
                Case IconKind.Simulation
                    Using wood As New SolidBrush(p.Wood), groove As New SolidBrush(p.WoodDeep)
                        Using board = Skin.RoundRect(New RectangleF(1.5F, 4.5F, 17, 11), 1.5F)
                            g.FillPath(wood, board)
                        End Using
                        Using cut As New GraphicsPath()
                            cut.AddBezier(Pt(4, 8.5F), Pt(7, 6.5F), Pt(9, 12.5F), Pt(16, 9))
                            cut.AddBezier(Pt(16, 11.5F), Pt(9, 15), Pt(7, 9), Pt(4, 11))
                            cut.CloseFigure()
                            g.FillPath(groove, cut)
                        End Using
                    End Using
                Case IconKind.Toolpath
                    g.DrawLines(cyanLine, {Pt(3, 3.5F), Pt(17, 3.5F), Pt(17, 7.8F), Pt(3, 7.8F), Pt(3, 12.1F), Pt(17, 12.1F)})
                    Using rapid = p.Pen(p.Red, 1.3F)
                        rapid.DashStyle = DashStyle.Dash
                        g.DrawLine(rapid, 3, 16.5F, 17, 16.5F)
                    End Using
                Case IconKind.CutMoves
                    ' The toolpath lines' colours: shallow passes cyan, the deepest yellow.
                    g.DrawLines(cyanLine, {Pt(3, 3.5F), Pt(17, 3.5F), Pt(17, 8.2F), Pt(3, 8.2F), Pt(3, 12.9F)})
                    Using deep = p.Pen(p.Yellow, 1.6F)
                        g.DrawLines(deep, {Pt(3, 12.9F), Pt(17, 12.9F), Pt(17, 16.8F), Pt(9, 16.8F)})
                    End Using
                Case IconKind.RapidMoves
                    ' A hop over the stock: up, across, down, dashed red like the rapids in the view.
                    Using rapid = p.Pen(p.Red, 1.6F)
                        rapid.DashStyle = DashStyle.Dash
                        g.DrawLines(rapid, {Pt(3.5F, 17), Pt(3.5F, 4), Pt(16.5F, 4), Pt(16.5F, 12.5F)})
                    End Using
                    Using head As New SolidBrush(p.Red)
                        ArrowHead(g, head, Pt(16.5F, 18), 0, 1, 5)
                    End Using
                Case IconKind.Hide
                    Eye(g, p, p.Main)
                    Using slash = p.Pen(p.Red, 2.1F)
                        g.DrawLine(slash, 3.2F, 17, 16.8F, 3)
                    End Using
                Case IconKind.ShowAll
                    Eye(g, p, p.Cyan)
                Case IconKind.Move
                    g.DrawLine(line, 10, 4, 10, 16)
                    g.DrawLine(line, 4, 10, 16, 10)
                    ArrowHead(g, main, Pt(10, 1.4F), 0, -1, 4)
                    ArrowHead(g, main, Pt(10, 18.6F), 0, 1, 4)
                    ArrowHead(g, main, Pt(1.4F, 10), -1, 0, 4)
                    ArrowHead(g, main, Pt(18.6F, 10), 1, 0, 4)
                    g.FillEllipse(cyan, 7.9F, 7.9F, 4.2F, 4.2F)
                Case IconKind.Scale
                    Using dash = p.Pen(p.Soft, 1.1F)
                        dash.DashStyle = DashStyle.Dash
                        g.DrawRectangle(dash, 2.5F, 2.5F, 15, 15)
                    End Using
                    g.FillRectangle(main, 2.5F, 10.5F, 7, 7)
                    Using pen = p.Pen(p.Lime, 1.7F)
                        g.DrawLine(pen, 8.5F, 11.5F, 14.6F, 5.4F)
                    End Using
                    ArrowHead(g, lime, Pt(17.4F, 2.6F), 1, -1, 5)
                Case IconKind.Rotate
                    Dim cx = 10.0F, cy = 10.0F, r = 6.6F
                    Dim a0 = -50.0F, sweep = 285.0F
                    Using pen = p.Pen(p.Cyan, 1.9F)
                        g.DrawArc(pen, cx - r, cy - r, 2 * r, 2 * r, a0, sweep)
                    End Using
                    Dim aEnd = (a0 + sweep) * Math.PI / 180
                    Dim ex = CSng(cx + r * Math.Cos(aEnd)), ey = CSng(cy + r * Math.Sin(aEnd))
                    Dim tx = CSng(-Math.Sin(aEnd)), ty = CSng(Math.Cos(aEnd))
                    ArrowHead(g, cyan, Pt(ex + tx * 2.6F, ey + ty * 2.6F), tx, ty, 5)
                    g.FillRectangle(main, 8.3F, 8.3F, 3.4F, 3.4F)
                Case IconKind.Center
                    g.DrawRectangle(softLine, 2.5F, 2.5F, 15, 15)
                    Using ring = p.Pen(p.Red, 1.7F)
                        g.DrawEllipse(ring, 5.4F, 5.4F, 9.2F, 9.2F)
                    End Using
                    g.DrawLine(thin, 10, 3.5F, 10, 16.5F)
                    g.DrawLine(thin, 3.5F, 10, 16.5F, 10)
                    g.FillEllipse(main, 8.8F, 8.8F, 2.4F, 2.4F)
                Case IconKind.Mirror
                    Using dash = p.Pen(p.Soft, 1.1F)
                        dash.DashStyle = DashStyle.Dash
                        g.DrawLine(dash, 10, 1.5F, 10, 18.5F)
                    End Using
                    g.FillPolygon(main, {Pt(8.3F, 4), Pt(8.3F, 16), Pt(2, 16)})
                    Using pen = p.Pen(p.Cyan, 1.4F)
                        g.DrawPolygon(pen, {Pt(11.7F, 4), Pt(11.7F, 16), Pt(18, 16)})
                    End Using
                Case IconKind.NewFile
                    Page(g, p, 4, 2.2F, 15.5F, 17.8F)
                    g.FillPolygon(yellow, StarPoints(14.4F, 14.4F, 4.6F, 1.6F, 4))
                Case IconKind.Duplicate
                    Page(g, p, 2, 1.5F, 11.5F, 13.5F)
                    Using fill As New SolidBrush(Skin.Window)
                        g.FillRectangle(fill, 7.5F, 6, 10.5F, 12.5F)
                    End Using
                    Page(g, p, 7.5F, 6, 18, 18.5F)
                Case IconKind.Open
                    Using back As New SolidBrush(If(p.OnYellow, Color.FromArgb(150, 110, 20), Color.FromArgb(200, 150, 50))),
                          front As New SolidBrush(p.Yellow), edge = p.Pen(p.WoodDeep, 1.0F)
                        Dim backPts = {Pt(1.5F, 4.5F), Pt(7, 4.5F), Pt(8.6F, 6.2F), Pt(16.5F, 6.2F), Pt(16.5F, 15.8F), Pt(1.5F, 15.8F)}
                        g.FillPolygon(back, backPts)
                        Dim frontPts = {Pt(3.6F, 8.8F), Pt(19, 8.8F), Pt(16.6F, 15.8F), Pt(1.5F, 15.8F)}
                        g.FillPolygon(front, frontPts)
                        g.DrawPolygon(edge, frontPts)
                    End Using
                Case IconKind.Save
                    Using disk = Skin.RoundRect(New RectangleF(2.5F, 2.5F, 15, 15), 1.8F), blue As New SolidBrush(p.Blue),
                          shutter As New SolidBrush(Color.FromArgb(214, 218, 226)), label As New SolidBrush(Color.FromArgb(244, 244, 244))
                        g.FillPath(blue, disk)
                        g.FillRectangle(shutter, 6, 2.5F, 8, 5.2F)
                        g.FillRectangle(blue, 11, 3.4F, 1.8F, 3.2F)
                        g.FillRectangle(label, 5, 10.5F, 10, 7)
                        Using rule As New Pen(Color.FromArgb(150, 150, 160), 0.8F)
                            g.DrawLine(rule, 6.5F, 13, 13.5F, 13)
                            g.DrawLine(rule, 6.5F, 15.2F, 13.5F, 15.2F)
                        End Using
                    End Using
                Case IconKind.Generate
                    Using faint = p.Pen(Color.FromArgb(150, p.Cyan), 1.4F)
                        g.DrawLines(faint, {Pt(2, 4), Pt(18, 4), Pt(18, 9), Pt(2, 9), Pt(2, 14), Pt(18, 14)})
                    End Using
                    Dim bolt = {Pt(12.6F, 1.2F), Pt(6.4F, 10.6F), Pt(9.8F, 10.6F), Pt(7.6F, 18.8F), Pt(14.6F, 8.4F), Pt(11.1F, 8.4F), Pt(13.6F, 1.2F)}
                    If p.Shadow.A > 0 Then
                        Using sh As New Pen(p.Shadow, 2.2F) With {.LineJoin = LineJoin.Round}
                            g.DrawPolygon(sh, bolt)
                        End Using
                    End If
                    g.FillPolygon(yellow, bolt)
                Case IconKind.StopGenerate
                    Using sq = Skin.RoundRect(New RectangleF(4, 4, 12, 12), 2)
                        g.FillPath(red, sq)
                        g.DrawPath(thin, sq)
                    End Using
                Case IconKind.Export
                    Page(g, p, 2.5F, 2.2F, 13.5F, 17.8F)
                    Using code = p.Pen(p.Yellow, 1.3F)
                        g.DrawLine(code, 5, 7.5F, 9.5F, 7.5F)
                        g.DrawLine(code, 5, 10.5F, 11, 10.5F)
                        g.DrawLine(code, 5, 13.5F, 8.5F, 13.5F)
                    End Using
                    Using pen = p.Pen(p.Lime, 1.8F)
                        g.DrawLine(pen, 10.5F, 14.5F, 16, 14.5F)
                    End Using
                    ArrowHead(g, lime, Pt(19.4F, 14.5F), 1, 0, 4.4F)
                Case IconKind.Machine
                    ' A CNC router: a gantry over the table, the spindle and bit, a board.
                    g.FillRectangle(soft, 1.5F, 16.6F, 17, 1.8F)
                    g.FillRectangle(main, 3, 5.8F, 2.2F, 10.8F)
                    g.FillRectangle(main, 14.8F, 5.8F, 2.2F, 10.8F)
                    g.FillRectangle(main, 2.4F, 3.6F, 15.2F, 2.6F)
                    g.FillRectangle(cyan, 7.9F, 6.2F, 4.2F, 5.4F)
                    Using bit = p.Pen(p.Main, 1.2F)
                        g.DrawLine(bit, 10, 11.6F, 10, 14.2F)
                    End Using
                    Using wood As New SolidBrush(p.Wood)
                        g.FillRectangle(wood, 6, 14.6F, 8, 2)
                    End Using
                Case IconKind.Play
                    g.FillPolygon(lime, {Pt(5.5F, 3.5F), Pt(5.5F, 16.5F), Pt(16.5F, 10)})
                Case IconKind.Pause
                    g.FillRectangle(main, 5, 4, 3.4F, 12)
                    g.FillRectangle(main, 11.6F, 4, 3.4F, 12)
                Case IconKind.Rewind
                    g.FillRectangle(main, 3, 4.5F, 2.2F, 11)
                    g.FillPolygon(main, {Pt(11.5F, 4.5F), Pt(11.5F, 15.5F), Pt(5.8F, 10)})
                    g.FillPolygon(main, {Pt(17.5F, 4.5F), Pt(17.5F, 15.5F), Pt(11.8F, 10)})
                Case IconKind.ImportSvg
                    Using curve = p.Pen(p.Cyan, 1.7F)
                        g.DrawBezier(curve, Pt(2.5F, 15.5F), Pt(4, 4), Pt(14, 17), Pt(17.5F, 4.5F))
                    End Using
                    g.DrawLine(softLine, 2.5F, 15.5F, 4, 4)
                    g.DrawLine(softLine, 17.5F, 4.5F, 14, 17)
                    g.FillRectangle(lime, 1.2F, 14.2F, 2.6F, 2.6F)
                    g.FillRectangle(lime, 16.2F, 3.2F, 2.6F, 2.6F)
                    g.FillEllipse(main, 2.9F, 2.9F, 2.2F, 2.2F)
                    g.FillEllipse(main, 12.9F, 15.9F, 2.2F, 2.2F)
                Case IconKind.Import3D
                    Dim top = {Pt(10, 2.2F), Pt(17.2F, 6), Pt(10, 9.8F), Pt(2.8F, 6)}
                    Dim leftFace = {Pt(2.8F, 6), Pt(10, 9.8F), Pt(10, 17.8F), Pt(2.8F, 14)}
                    Dim rightFace = {Pt(17.2F, 6), Pt(10, 9.8F), Pt(10, 17.8F), Pt(17.2F, 14)}
                    Using t As New SolidBrush(Color.FromArgb(200, p.Cyan)), l As New SolidBrush(Color.FromArgb(120, p.Cyan)), r As New SolidBrush(Color.FromArgb(60, p.Cyan))
                        g.FillPolygon(t, top) : g.FillPolygon(l, leftFace) : g.FillPolygon(r, rightFace)
                    End Using
                    Using pen = p.Pen(p.Main, 1.1F)
                        g.DrawPolygon(pen, top) : g.DrawPolygon(pen, leftFace) : g.DrawPolygon(pen, rightFace)
                    End Using
                Case IconKind.Remove
                    Using pen = p.Pen(p.Red, 2.4F)
                        g.DrawLine(pen, 5, 5, 15, 15)
                        g.DrawLine(pen, 15, 5, 5, 15)
                    End Using
                Case IconKind.FitToBlank
                    Using blank = p.Pen(p.Yellow, 1.3F)
                        g.DrawRectangle(blank, 1.8F, 3.8F, 16.4F, 12.4F)
                    End Using
                    g.FillRectangle(cyan, 7, 8, 6, 4)
                    ArrowHead(g, main, Pt(3.6F, 10), -1, 0, 3.2F)
                    ArrowHead(g, main, Pt(16.4F, 10), 1, 0, 3.2F)
                    ArrowHead(g, main, Pt(10, 5.6F), 0, -1, 3.2F)
                    ArrowHead(g, main, Pt(10, 14.4F), 0, 1, 3.2F)
                Case IconKind.Stock
                    ' A block of wood: top, front and side faces with grain.
                    Dim topFace = {Pt(2, 7), Pt(7, 3), Pt(18, 3), Pt(13, 7)}
                    Dim front = {Pt(2, 7), Pt(13, 7), Pt(13, 17), Pt(2, 17)}
                    Dim side = {Pt(13, 7), Pt(18, 3), Pt(18, 13), Pt(13, 17)}
                    Using t As New SolidBrush(ControlPaint.Light(p.Wood, 0.35F)), f As New SolidBrush(p.Wood), s As New SolidBrush(ControlPaint.Dark(p.Wood, 0.15F))
                        g.FillPolygon(t, topFace) : g.FillPolygon(f, front) : g.FillPolygon(s, side)
                    End Using
                    Using grain = p.Pen(p.WoodDeep, 0.8F)
                        g.DrawBezier(grain, Pt(3, 10), Pt(6, 9), Pt(8, 11.5F), Pt(12, 10.5F))
                        g.DrawBezier(grain, Pt(3, 13.5F), Pt(6, 12.5F), Pt(9, 15), Pt(12, 14))
                    End Using
                    Using edge = p.Pen(p.WoodDeep, 0.9F)
                        g.DrawPolygon(edge, front) : g.DrawPolygon(edge, side) : g.DrawPolygon(edge, topFace)
                    End Using
                Case IconKind.ObjectSettings
                    ' A selected drawing: its box with handles, a star inside, and a slider.
                    Using box = p.Pen(p.Soft, 1.0F)
                        box.DashStyle = DashStyle.Dash
                        g.DrawRectangle(box, 2.5F, 2.5F, 15, 15)
                    End Using
                    For Each q In {Pt(2.5F, 2.5F), Pt(17.5F, 2.5F), Pt(2.5F, 17.5F), Pt(17.5F, 17.5F)}
                        g.FillRectangle(main, q.X - 1.5F, q.Y - 1.5F, 3, 3)
                    Next
                    Using pen = p.Pen(p.Cyan, 1.4F)
                        g.DrawPolygon(pen, StarPoints(10, 10.6F, 5.6F, 2.4F, 5))
                    End Using
                Case IconKind.Font
                    ' "Aa": a serif capital and a plain small letter, two faces side by side.
                    FitText(g, "A", "Times New Roman", FontStyle.Bold, New RectangleF(1.2F, 2.5F, 11.5F, 15), main)
                    FitText(g, "a", "Arial", FontStyle.Regular, New RectangleF(12.2F, 8.8F, 6.6F, 8.7F), cyan)
                Case IconKind.TextJob
                    FitText(g, "A", "Times New Roman", FontStyle.Bold, New RectangleF(2.5F, 2, 15, 13.5F), main)
                    Using pen = p.Pen(p.Cyan, 1.3F)
                        g.DrawLine(pen, 2.5F, 18, 17.5F, 18)
                    End Using
                Case IconKind.Drawings
                    Using pen = p.Pen(p.Cyan, 1.2F)
                        g.DrawRectangle(pen, 2, 2.6F, 3.6F, 3.6F)
                        g.DrawEllipse(pen, 2, 8.2F, 3.6F, 3.6F)
                        g.DrawPolygon(pen, StarPoints(3.8F, 15.9F, 2.4F, 1.0F, 5))
                    End Using
                    For Each y In {4.4F, 10.0F, 15.6F}
                        g.DrawLine(line, 8, y, 18, y)
                    Next
            End Select
        End Using
    End Sub
End Module
