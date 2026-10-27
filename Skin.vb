' ============================================================================
'  Skin.vb
'  The dark look, after the buttons of Code Chop: near-black windows, glossy
'  command buttons whose lime bar glows under the mouse, round toggles that
'  turn yellow while they are on, groove spacers between groups, and toolbar
'  bands with a lime edge. All of it is drawn here in code, so it stays sharp
'  at any screen scale:
'   - Skin:            the colours, and painting of buttons, bands and spacers
'   - Skin.Apply:      dark colours for a window and everything in it
'   - Skin.StyleToolbar / SetIcon / MarkToggle: the toolbars and their icons
'   - SkinRenderer:    menus, toolbars and the status bar
'   - DarkTabControl:  tabs drawn dark (the system ones stay light)
' ============================================================================

Imports System.ComponentModel
Imports System.Drawing.Drawing2D
Imports System.Drawing.Imaging
Imports System.Runtime.InteropServices

Friend NotInheritable Class Skin

    Private Sub New()
    End Sub

    ' ------------------------------------------------------------- colours

    Public Shared ReadOnly Window As Color = Color.FromArgb(13, 13, 14)        ' windows and panels
    Public Shared ReadOnly Panel As Color = Color.FromArgb(19, 19, 21)         ' pages and docking areas
    Public Shared ReadOnly Field As Color = Color.FromArgb(27, 27, 29)         ' lists, grids, text boxes
    Public Shared ReadOnly Category As Color = Color.FromArgb(36, 36, 40)      ' grid category rows
    Public Shared ReadOnly Edge As Color = Color.FromArgb(58, 58, 62)          ' borders
    Public Shared ReadOnly Line As Color = Color.FromArgb(40, 40, 44)          ' grid lines, splitters
    Public Shared ReadOnly Ink As Color = Color.FromArgb(226, 226, 226)        ' text
    Public Shared ReadOnly InkDim As Color = Color.FromArgb(124, 124, 128)     ' disabled and hint text
    Public Shared ReadOnly InkOnYellow As Color = Color.FromArgb(26, 26, 20)   ' text on a toggle that is on
    Public Shared ReadOnly Led As Color = Color.FromArgb(154, 192, 1)          ' the bar of a command button
    Public Shared ReadOnly LedLit As Color = Color.FromArgb(197, 235, 9)
    Public Shared ReadOnly Yellow As Color = Color.FromArgb(242, 240, 119)     ' a toggle that is on
    Public Shared ReadOnly YellowDeep As Color = Color.FromArgb(232, 229, 62)
    Public Shared ReadOnly Selection As Color = Color.FromArgb(70, 84, 6)      ' selected rows and menu items
    Public Shared ReadOnly FieldError As Color = Color.FromArgb(92, 26, 22)    ' a box holding something that is not a number
    Public Shared ReadOnly ErrorInk As Color = Color.FromArgb(250, 96, 76)     ' error messages
    Public Shared ReadOnly WarningInk As Color = Color.FromArgb(240, 168, 48)  ' warnings

    ''' <summary>The renderer of every menu, toolbar and status bar.</summary>
    Public Shared ReadOnly Property Renderer As New SkinRenderer()

    ' --------------------------------------------------------- button faces

    Public Enum Look
        ''' <summary>A command: gloss above, black below, a lime bar that glows under the mouse.</summary>
        Square
        ''' <summary>A toggle: a rounded, striped face that turns yellow while it is on.</summary>
        Round
    End Enum

    ''' <summary>Paints a button face filling the rectangle.</summary>
    Public Shared Sub PaintButton(g As Graphics, r As Rectangle, look As Look, hot As Boolean, pressed As Boolean, locked As Boolean, enabled As Boolean)
        If r.Width < 4 OrElse r.Height < 4 Then Return
        Dim smoothing = g.SmoothingMode
        If look = Look.Round Then
            PaintRound(g, r, hot AndAlso enabled, pressed AndAlso enabled, locked, enabled)
        Else
            PaintSquare(g, r, hot AndAlso enabled, pressed AndAlso enabled, enabled)
        End If
        g.SmoothingMode = smoothing
    End Sub

    Private Shared Sub PaintSquare(g As Graphics, r As Rectangle, hot As Boolean, pressed As Boolean, enabled As Boolean)
        g.SmoothingMode = SmoothingMode.None
        Dim mid = r.Top + r.Height \ 2
        Dim upper As New Rectangle(r.Left, r.Top, r.Width, mid - r.Top)
        Dim lower As New Rectangle(r.Left, mid, r.Width, r.Bottom - mid)
        ' Gloss on the upper half; pressed, the gloss goes dark and the light moves down.
        Dim glossTop = If(pressed, Color.FromArgb(50, 50, 50), If(enabled, Color.FromArgb(92, 92, 92), Color.FromArgb(52, 52, 52)))
        Dim glossEnd = If(pressed, Color.FromArgb(4, 4, 4), If(enabled, Color.FromArgb(46, 46, 46), Color.FromArgb(28, 28, 28)))
        Using b As New LinearGradientBrush(New Rectangle(upper.X, upper.Y - 1, upper.Width, upper.Height + 2), glossTop, glossEnd, LinearGradientMode.Vertical)
            g.FillRectangle(b, upper)
        End Using
        g.FillRectangle(Brushes.Black, lower)
        Dim k = r.Height / 32.0F
        Dim ledY = r.Bottom - Math.Max(3, CInt(Math.Round(5 * k)))
        If hot OrElse pressed Then
            ' The glow around the bar: a wide ellipse of yellow light in the lower half.
            Dim saved = g.Save()
            g.SetClip(lower)
            g.SmoothingMode = SmoothingMode.AntiAlias
            Using glowPath As New GraphicsPath()
                Dim gw = r.Width * 0.62F, gh = (r.Bottom - mid) * If(pressed, 1.2F, 0.95F)
                glowPath.AddEllipse(r.Left + r.Width / 2.0F - gw, ledY - gh, 2 * gw, 2 * gh)
                Using pg As New PathGradientBrush(glowPath)
                    pg.CenterPoint = New PointF(r.Left + r.Width / 2.0F, ledY)
                    pg.CenterColor = If(pressed, Color.FromArgb(205, 172, 172, 36), Color.FromArgb(170, 140, 140, 0))
                    pg.SurroundColors = {Color.FromArgb(0, 0, 0, 0)}
                    g.FillPath(pg, glowPath)
                End Using
            End Using
            g.Restore(saved)
            g.SmoothingMode = SmoothingMode.None
        End If
        ' Frame: light along the top and sides, open at the bottom.
        Using framePen As New Pen(If(enabled, Color.FromArgb(150, 150, 150), Color.FromArgb(70, 70, 70)))
            g.DrawLine(framePen, r.Left, r.Top, r.Right - 1, r.Top)
        End Using
        Using sideBrush As New LinearGradientBrush(New Rectangle(r.Left, r.Top, 1, r.Height + 1),
                                                   If(enabled, Color.FromArgb(150, 150, 150), Color.FromArgb(70, 70, 70)),
                                                   If(pressed, Color.FromArgb(150, 150, 110), Color.FromArgb(40, 40, 40)), LinearGradientMode.Vertical)
            g.FillRectangle(sideBrush, r.Left, r.Top, 1, r.Height)
            g.FillRectangle(sideBrush, r.Right - 1, r.Top, 1, r.Height)
        End Using
        ' The bar, dimmer at its ends.
        Dim barH = Math.Max(1, CInt(Math.Round(k)))
        Dim x0 = r.Left + CInt(r.Width * 0.24F), x1 = r.Right - CInt(r.Width * 0.24F)
        If x1 - x0 >= 4 Then
            Dim c = If(Not enabled, Color.FromArgb(58, 58, 58), If(hot OrElse pressed, LedLit, Led))
            Using lb As New LinearGradientBrush(New Rectangle(x0 - 1, ledY, x1 - x0 + 2, barH), c, c, LinearGradientMode.Horizontal)
                lb.InterpolationColors = New ColorBlend(4) With {
                    .Colors = {Color.FromArgb(110, c), c, c, Color.FromArgb(110, c)},
                    .Positions = {0.0F, 0.14F, 0.86F, 1.0F}}
                g.FillRectangle(lb, x0, ledY, x1 - x0, barH)
            End Using
        End If
    End Sub

    Private Shared Sub PaintRound(g As Graphics, r As Rectangle, hot As Boolean, pressed As Boolean, locked As Boolean, enabled As Boolean)
        g.SmoothingMode = SmoothingMode.AntiAlias
        Dim outer As New RectangleF(r.X + 0.5F, r.Y + 0.5F, r.Width - 1.0F, r.Height - 1.0F)
        Dim radius = Math.Max(3.0F, Math.Min(outer.Width, outer.Height) * 0.24F)
        Using path = RoundRect(outer, radius)
            Using b As New SolidBrush(Color.FromArgb(8, 8, 8))
                g.FillPath(b, path)
            End Using
        End Using
        Dim inner = RectangleF.Inflate(outer, -1.5F, -1.5F)
        Using path = RoundRect(inner, radius - 1.2F)
            Dim lit = locked OrElse pressed
            Dim top, bottom As Color
            If lit Then
                top = If(enabled, Yellow, Color.FromArgb(130, 128, 70)) : bottom = If(enabled, YellowDeep, Color.FromArgb(110, 108, 50))
            ElseIf Not enabled Then
                top = Color.FromArgb(36, 36, 38) : bottom = Color.FromArgb(26, 26, 28)
            ElseIf hot Then
                top = Color.FromArgb(84, 84, 88) : bottom = Color.FromArgb(50, 50, 54)
            Else
                top = Color.FromArgb(66, 66, 70) : bottom = Color.FromArgb(38, 38, 42)
            End If
            Using b As New LinearGradientBrush(New RectangleF(inner.X, inner.Y - 1, inner.Width, inner.Height + 2), top, bottom, LinearGradientMode.Vertical)
                g.FillPath(b, path)
            End Using
            ' Fine diagonal stripes, as on Code Chop's round buttons.
            Using hatch As New HatchBrush(HatchStyle.LightUpwardDiagonal, Color.FromArgb(If(lit, 26, 16), Color.White), Color.Transparent)
                g.FillPath(hatch, path)
            End Using
            Dim rimTop = If(Not enabled, Color.FromArgb(64, 64, 64), If(hot OrElse pressed, Color.White, If(locked, Color.FromArgb(252, 252, 210), Color.FromArgb(160, 160, 160))))
            Dim rimEnd = If(Not enabled, Color.FromArgb(40, 40, 40), If(hot OrElse pressed, Color.FromArgb(220, 220, 220), If(locked, Color.FromArgb(170, 168, 70), Color.FromArgb(70, 70, 70))))
            Using rb As New LinearGradientBrush(New RectangleF(inner.X, inner.Y - 1, inner.Width, inner.Height + 2), rimTop, rimEnd, LinearGradientMode.Vertical)
                Using rim As New Pen(rb, If(hot OrElse pressed, 1.6F, 1.1F))
                    g.DrawPath(rim, path)
                End Using
            End Using
        End Using
    End Sub

    ''' <summary>A rounded rectangle path.</summary>
    Public Shared Function RoundRect(r As RectangleF, radius As Single) As GraphicsPath
        Dim d = Math.Max(0.5F, Math.Min(radius * 2, Math.Min(r.Width, r.Height)))
        Dim p As New GraphicsPath()
        p.AddArc(r.X, r.Y, d, d, 180, 90)
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90)
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90)
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90)
        p.CloseFigure()
        Return p
    End Function

    ''' <summary>
    ''' A toolbar band, after Code Chop's bar background: a lime edge, gloss fading to black,
    ''' a dark foot. Vertical bands (toolbars docked at a side) run across.
    ''' </summary>
    Public Shared Sub PaintBand(g As Graphics, r As Rectangle, vertical As Boolean, limeEdge As Boolean)
        If r.Width <= 0 OrElse r.Height <= 0 Then Return
        Dim mode = If(vertical, LinearGradientMode.Horizontal, LinearGradientMode.Vertical)
        Using b As New LinearGradientBrush(r, Color.Black, Color.Black, mode)
            b.InterpolationColors = New ColorBlend(5) With {
                .Colors = {Color.FromArgb(58, 58, 60), Color.FromArgb(10, 10, 10), Color.FromArgb(3, 3, 3), Color.FromArgb(3, 3, 3), Color.FromArgb(24, 24, 26)},
                .Positions = {0.0F, 0.44F, 0.46F, 0.78F, 1.0F}}
            g.FillRectangle(b, r)
        End Using
        If limeEdge Then
            Using p As New Pen(Led)
                If vertical Then g.DrawLine(p, r.Left, r.Top, r.Left, r.Bottom - 1) Else g.DrawLine(p, r.Left, r.Top, r.Right - 1, r.Top)
            End Using
        End If
    End Sub

    ''' <summary>A spacer between groups: a light groove with rounded ends, like Code Chop's spacer bars.</summary>
    Public Shared Sub PaintSpacer(g As Graphics, r As Rectangle, vertical As Boolean)
        Dim smoothing = g.SmoothingMode
        g.SmoothingMode = SmoothingMode.AntiAlias
        Dim inset = Math.Max(3, If(vertical, r.Height, r.Width) \ 7)
        Dim bar = If(vertical,
                     New RectangleF(r.Left + r.Width / 2.0F - 1.5F, r.Top + inset, 3, Math.Max(2, r.Height - 2 * inset)),
                     New RectangleF(r.Left + inset, r.Top + r.Height / 2.0F - 1.5F, Math.Max(2, r.Width - 2 * inset), 3))
        Using path = RoundRect(bar, 1.5F)
            Using b As New LinearGradientBrush(RectangleF.Inflate(bar, 1, 1), Color.Black, Color.Black, If(vertical, LinearGradientMode.Horizontal, LinearGradientMode.Vertical))
                b.InterpolationColors = New ColorBlend(3) With {.Colors = {Color.FromArgb(60, 60, 60), Color.FromArgb(185, 185, 185), Color.FromArgb(60, 60, 60)}, .Positions = {0.0F, 0.5F, 1.0F}}
                g.FillPath(b, path)
            End Using
            Using p As New Pen(Color.FromArgb(160, 0, 0, 0))
                g.DrawPath(p, path)
            End Using
        End Using
        g.SmoothingMode = smoothing
    End Sub

    ' ---------------------------------------------------------------- icons

    ' Toggles that are drawn round even while off (draw tools, Bold, panel buttons).
    Private Shared ReadOnly _toggles As New HashSet(Of ToolStripItem)
    ' What each icon is, so its twin in dark ink (for a toggle that is on, yellow) can be drawn
    ' when first needed. Weak: an entry goes with its icon (a closed Tool Library's buttons).
    Private NotInheritable Class IconInfo
        Public Kind As IconKind
        Public Size As Integer
        Public Twin As Image
    End Class
    Private Shared ReadOnly _icons As New Runtime.CompilerServices.ConditionalWeakTable(Of Image, IconInfo)

    ''' <summary>Marks toolbar buttons as toggles: round, and yellow while checked.</summary>
    Public Shared Sub MarkToggle(ParamArray items As ToolStripItem())
        For Each it In items
            _toggles.Add(it)
        Next
    End Sub

    Public Shared Function IsToggle(item As ToolStripItem) As Boolean
        Dim b = TryCast(item, ToolStripButton)
        Return _toggles.Contains(item) OrElse (b IsNot Nothing AndAlso b.CheckOnClick)
    End Function

    ''' <summary>An icon for a button or menu at this pixel size (its dark twin is made when a toggle first lights up).</summary>
    Public Shared Function Icon(kind As IconKind, size As Integer) As Bitmap
        Dim light = Icons.Make(kind, size, onYellow:=False)
        _icons.Add(light, New IconInfo With {.Kind = kind, .Size = size})
        Return light
    End Function

    ''' <summary>Puts an icon on a toolbar button or menu item, sized for where it sits.</summary>
    Public Shared Sub SetIcon(item As ToolStripItem, kind As IconKind)
        Dim size As Integer
        If TypeOf item Is ToolStripMenuItem OrElse item.Owner Is Nothing OrElse TypeOf item.Owner Is ToolStripDropDown Then
            size = Scaled(16, If(item.Owner, CType(Form.ActiveForm, Control)))
        Else
            size = item.Owner.ImageScalingSize.Width
        End If
        item.Image = Icon(kind, size)
        ' A styled toolbar's text button: keep it as tall as the others now it has a picture.
        If item.Owner IsNot Nothing AndAlso item.Owner.Renderer Is Renderer AndAlso Not TypeOf item.Owner Is ToolStripDropDown AndAlso
           (TypeOf item Is ToolStripButton OrElse TypeOf item Is ToolStripDropDownButton) AndAlso item.DisplayStyle <> ToolStripItemDisplayStyle.Image Then
            PadToButtonHeight(item, item.Owner)
        End If
    End Sub

    ''' <summary>The dark twin of an icon made by Icon, or Nothing for any other picture.</summary>
    Public Shared Function YellowTwin(img As Image) As Image
        Dim info As IconInfo = Nothing
        If img Is Nothing OrElse Not _icons.TryGetValue(img, info) Then Return Nothing
        If info.Twin Is Nothing Then info.Twin = Icons.Make(info.Kind, info.Size, onYellow:=True)
        Return info.Twin
    End Function

    ''' <summary>Pixels at the control's screen scale (96 DPI = 1). DeviceDpi is known before the handle is made.</summary>
    Public Shared Function Scaled(px As Integer, c As Control) As Integer
        Dim dpi = If(c IsNot Nothing, c.DeviceDpi, 96)
        Return Math.Max(1, CInt(Math.Round(px * dpi / 96.0)))
    End Function

    ' ------------------------------------------------------------- toolbars

    ''' <summary>
    ''' The big-button toolbar over the view: 32 pixel buttons (at 100 %) with 20 pixel icons,
    ''' text buttons as tall, groove spacers, and dark lists.
    ''' </summary>
    Public Shared Sub StyleToolbar(ts As ToolStrip)
        ts.Renderer = Renderer
        ts.BackColor = Window
        ts.ForeColor = Ink
        ts.ImageScalingSize = New Size(Scaled(20, ts), Scaled(20, ts))
        ts.Padding = New Padding(Scaled(3, ts), Scaled(2, ts), Scaled(3, ts), Scaled(2, ts))
        Dim button = Scaled(32, ts)
        For Each item As ToolStripItem In ts.Items
            item.Margin = New Padding(Scaled(1, ts))
            If TypeOf item Is ToolStripButton OrElse TypeOf item Is ToolStripDropDownButton Then
                item.ImageScaling = ToolStripItemImageScaling.None
                If item.DisplayStyle = ToolStripItemDisplayStyle.Image Then
                    item.AutoSize = False
                    item.Size = New Size(button, button)
                Else
                    PadToButtonHeight(item, ts)
                End If
            ElseIf TypeOf item Is ToolStripSeparator Then
                item.Margin = New Padding(Scaled(3, ts))
            ElseIf TypeOf item Is ToolStripComboBox Then
                StyleList(DirectCast(item, ToolStripComboBox).ComboBox)
            ElseIf TypeOf item Is ToolStripLabel Then
                item.ForeColor = Ink
            End If
        Next
    End Sub

    ''' <summary>
    ''' A text button sizes to its text, padded up to the icon buttons' height (again once its
    ''' picture is set: a picture can be taller than the text).
    ''' </summary>
    Private Shared Sub PadToButtonHeight(item As ToolStripItem, ts As ToolStrip)
        item.AutoSize = True
        item.Padding = New Padding(Scaled(6, ts), 0, Scaled(6, ts), 0)
        Dim extra = Math.Max(0, Scaled(32, ts) - item.GetPreferredSize(Size.Empty).Height)
        Dim half = extra \ 2
        item.Padding = New Padding(Scaled(6, ts), half, Scaled(6, ts), extra - half)
    End Sub

    Private Shared Sub StyleList(cb As ComboBox)
        cb.FlatStyle = FlatStyle.Flat
        cb.BackColor = Field
        cb.ForeColor = Ink
        DarkTheme(cb, "DarkMode_CFD")
        If cb.DropDownStyle = ComboBoxStyle.DropDownList AndAlso cb.DrawMode = DrawMode.Normal Then
            ' A pick-only list draws its box light whatever its colours: drawn here instead.
            cb.DrawMode = DrawMode.OwnerDrawFixed
            AddHandler cb.DrawItem, AddressOf DrawListChoice
        End If
        ' The flat frame and drop-down button are painted in system (light) colours: paint over them.
        Dim frame As ComboFrame = Nothing
        If Not _framed.TryGetValue(cb, frame) Then _framed.Add(cb, New ComboFrame(cb))
    End Sub

    Private Shared ReadOnly _framed As New Runtime.CompilerServices.ConditionalWeakTable(Of ComboBox, ComboFrame)

    ''' <summary>Draws a list box's frame and drop-down button dark, after the list has painted itself.</summary>
    Private NotInheritable Class ComboFrame
        Inherits NativeWindow

        Private Const WM_PAINT As Integer = &HF
        Private ReadOnly _cb As ComboBox

        Public Sub New(cb As ComboBox)
            _cb = cb
            AddHandler cb.HandleCreated, Sub(s, e) AssignHandle(cb.Handle)
            AddHandler cb.HandleDestroyed, Sub(s, e) ReleaseHandle()
            If cb.IsHandleCreated Then AssignHandle(cb.Handle)
        End Sub

        Protected Overrides Sub WndProc(ByRef m As Message)
            MyBase.WndProc(m)
            If m.Msg <> WM_PAINT OrElse _cb.IsDisposed Then Return
            Dim r = _cb.ClientRectangle
            If r.Width < 8 OrElse r.Height < 8 Then Return
            Using g = Graphics.FromHwnd(_cb.Handle)
                Dim bw = SystemInformation.VerticalScrollBarWidth
                Dim button As New Rectangle(r.Right - bw - 1, r.Top + 1, bw, r.Height - 2)
                Using b As New SolidBrush(Category)
                    g.FillRectangle(b, button)
                End Using
                ' The arrow.
                g.SmoothingMode = SmoothingMode.AntiAlias
                Dim cx = button.Left + button.Width / 2.0F, cy = button.Top + button.Height / 2.0F
                Dim s = Math.Max(2.5F, bw / 5.0F)
                Using b As New SolidBrush(If(_cb.Enabled, Ink, InkDim))
                    g.FillPolygon(b, {New PointF(cx - s, cy - s / 2), New PointF(cx + s, cy - s / 2), New PointF(cx, cy + s / 2)})
                End Using
                g.SmoothingMode = SmoothingMode.None
                Using p As New Pen(If(_cb.Focused OrElse _cb.DroppedDown, Led, Edge))
                    g.DrawRectangle(p, 0, 0, r.Width - 1, r.Height - 1)
                End Using
            End Using
        End Sub
    End Class

    Private Shared Sub DrawListChoice(sender As Object, e As DrawItemEventArgs)
        Dim cb = DirectCast(sender, ComboBox)
        Dim selected = (e.State And DrawItemState.Selected) <> 0 AndAlso (e.State And DrawItemState.ComboBoxEdit) = 0
        Using b As New SolidBrush(If(selected, Selection, Field))
            e.Graphics.FillRectangle(b, e.Bounds)
        End Using
        If e.Index < 0 OrElse e.Index >= cb.Items.Count Then Return
        TextRenderer.DrawText(e.Graphics, cb.GetItemText(cb.Items(e.Index)), e.Font, New Rectangle(e.Bounds.X + 2, e.Bounds.Y, e.Bounds.Width - 2, e.Bounds.Height),
                              If(cb.Enabled, If(selected, Color.White, Ink), InkDim), TextFormatFlags.VerticalCenter Or TextFormatFlags.NoPrefix Or TextFormatFlags.EndEllipsis)
    End Sub

    ' --------------------------------------------------------------- windows

    ''' <summary>Dark colours for a window (or any control) and everything in it.</summary>
    Public Shared Sub Apply(root As Control)
        ApplyOne(root)
        ' Composite controls (grids, number boxes, lists, the GL view) keep their insides as they are.
        If TypeOf root Is PropertyGrid OrElse TypeOf root Is UpDownBase OrElse TypeOf root Is ComboBox OrElse
           TypeOf root Is TextBoxBase OrElse TypeOf root Is ToolStrip OrElse TypeOf root Is ListControl OrElse
           TypeOf root Is TreeView OrElse TypeOf root Is ToolpathView OrElse TypeOf root Is DataGridView Then Return
        For Each c As Control In root.Controls
            Apply(c)
        Next
    End Sub

    Private Shared Sub ApplyOne(c As Control)
        If TypeOf c Is Form Then
            Dim f = DirectCast(c, Form)
            f.BackColor = Window
            f.ForeColor = Ink
            DarkTitleBar(f)
        ElseIf TypeOf c Is PropertyGrid Then
            StyleGrid(DirectCast(c, PropertyGrid))
        ElseIf TypeOf c Is ToolStrip Then
            Dim ts = DirectCast(c, ToolStrip)
            ts.Renderer = Renderer
            ts.BackColor = Window
            ts.ForeColor = Ink
            For Each item As ToolStripItem In ts.Items
                If TypeOf item Is ToolStripComboBox Then StyleList(DirectCast(item, ToolStripComboBox).ComboBox)
                If TypeOf item Is ToolStripLabel AndAlso DirectCast(item, ToolStripLabel).IsLink Then
                    Dim link = DirectCast(item, ToolStripLabel)
                    link.LinkColor = LedLit : link.ActiveLinkColor = Yellow : link.VisitedLinkColor = LedLit
                End If
            Next
        ElseIf TypeOf c Is TextBoxBase Then
            c.BackColor = Field
            c.ForeColor = Ink
            If TypeOf c Is TextBox Then
                DirectCast(c, TextBox).BorderStyle = BorderStyle.FixedSingle
                ' The dark edit theme also keeps a disabled box dark.
                DarkTheme(c, "DarkMode_CFD")
            Else
                ' A rich text box's border stays light whatever its style: none, its darker field shows its edge.
                DirectCast(c, RichTextBox).BorderStyle = BorderStyle.None
                DarkScrollbars(c)
            End If
        ElseIf TypeOf c Is ListBox Then
            Dim lb = DirectCast(c, ListBox)
            lb.BackColor = Field
            lb.ForeColor = Ink
            lb.BorderStyle = BorderStyle.FixedSingle
            If lb.DrawMode = DrawMode.Normal Then
                ' Selected rows in the skin's colour instead of the system blue. Owner-drawn rows keep a
                ' fixed height, so size them from the font (the designer's height is for 100 %).
                lb.DrawMode = DrawMode.OwnerDrawFixed
                lb.ItemHeight = Math.Max(lb.ItemHeight, TextRenderer.MeasureText("Ag", lb.Font).Height + 2)
                AddHandler lb.DrawItem, AddressOf DrawListItem
            End If
            DarkScrollbars(c)
        ElseIf TypeOf c Is DataGridView Then
            StyleTable(DirectCast(c, DataGridView))
        ElseIf TypeOf c Is TreeView Then
            Dim tv = DirectCast(c, TreeView)
            tv.BackColor = Field
            tv.ForeColor = Ink
            tv.LineColor = InkDim
            tv.BorderStyle = BorderStyle.FixedSingle
            DarkScrollbars(c)
        ElseIf TypeOf c Is ComboBox Then
            StyleList(DirectCast(c, ComboBox))
        ElseIf TypeOf c Is NumericUpDown Then
            c.BackColor = Field
            c.ForeColor = Ink
        ElseIf TypeOf c Is CheckBox AndAlso DirectCast(c, CheckBox).Appearance = Appearance.Button Then
            SkinToggleButton(DirectCast(c, CheckBox))
        ElseIf TypeOf c Is Button Then
            SkinCommandButton(DirectCast(c, Button))
        ElseIf TypeOf c Is CheckBox OrElse TypeOf c Is RadioButton Then
            c.ForeColor = Ink
            AddHandler c.Paint, AddressOf PaintDisabledCheckText
        ElseIf TypeOf c Is LinkLabel Then
            Dim ll = DirectCast(c, LinkLabel)
            ll.LinkColor = LedLit : ll.ActiveLinkColor = Yellow : ll.VisitedLinkColor = LedLit
        ElseIf TypeOf c Is Label Then
            c.ForeColor = If(c.ForeColor = SystemColors.GrayText, InkDim, Ink)
            If c.BackColor.IsSystemColor Then c.BackColor = Color.Transparent
            AddHandler c.Paint, AddressOf PaintDisabledLabel
        ElseIf TypeOf c Is TrackBar Then
            c.BackColor = Window
        ElseIf TypeOf c Is SplitContainer Then
            ' The bar between the two sides shows the container's own colour.
            Dim sc = DirectCast(c, SplitContainer)
            sc.BackColor = Line
            sc.Panel1.BackColor = Window
            sc.Panel2.BackColor = Window
        ElseIf TypeOf c Is ToolStripPanel Then
            c.BackColor = Window
        ElseIf TypeOf c Is TabPage Then
            Dim tp = DirectCast(c, TabPage)
            tp.UseVisualStyleBackColor = False
            tp.BackColor = Panel
            tp.ForeColor = Ink
        ElseIf TypeOf c Is ToolDrawing Then
            c.BackColor = Field
            c.ForeColor = Ink
        ElseIf TypeOf c Is GroupBox Then
            c.ForeColor = Ink
        ElseIf TypeOf c Is Panel OrElse TypeOf c Is TableLayoutPanel OrElse TypeOf c Is FlowLayoutPanel OrElse TypeOf c Is UserControl Then
            ' Containers take the window's colour unless they chose one of their own.
            If c.BackColor.IsSystemColor Then c.BackColor = Window
            c.ForeColor = Ink
        End If
    End Sub

    ' Windows draws disabled label and check box text a shade darker than the background, which
    ' on these near-black windows is black on black: draw it again in the dim ink.

    Private Shared Sub PaintDisabledLabel(sender As Object, e As PaintEventArgs)
        Dim l = DirectCast(sender, Label)
        If l.Enabled OrElse String.IsNullOrEmpty(l.Text) Then Return
        Dim r = l.ClientRectangle
        r = New Rectangle(r.X + l.Padding.Left, r.Y + l.Padding.Top, r.Width - l.Padding.Horizontal, r.Height - l.Padding.Vertical)
        Dim flags = AlignFlags(l.TextAlign) Or If(l.AutoSize, TextFormatFlags.SingleLine, TextFormatFlags.WordBreak)
        If Not l.UseMnemonic Then flags = flags Or TextFormatFlags.NoPrefix
        PaintOverDisabledText(e.Graphics, l, r, flags)
    End Sub

    Private Shared Sub PaintDisabledCheckText(sender As Object, e As PaintEventArgs)
        Dim b = DirectCast(sender, ButtonBase)
        If b.Enabled OrElse String.IsNullOrEmpty(b.Text) Then Return
        ' Only the usual layout (box on the left, text beside it) is redrawn.
        Dim checkAlign = If(TypeOf b Is CheckBox, DirectCast(b, CheckBox).CheckAlign, DirectCast(b, RadioButton).CheckAlign)
        If checkAlign <> ContentAlignment.MiddleLeft AndAlso checkAlign <> ContentAlignment.TopLeft Then Return
        Dim glyph = CheckBoxRenderer.GetGlyphSize(e.Graphics, VisualStyles.CheckBoxState.UncheckedDisabled).Width + Scaled(3, b)
        Dim r As New Rectangle(glyph, 0, Math.Max(0, b.ClientSize.Width - glyph), b.ClientSize.Height)
        PaintOverDisabledText(e.Graphics, b, r, AlignFlags(b.TextAlign) Or TextFormatFlags.WordBreak)
    End Sub

    Private Shared Sub PaintOverDisabledText(g As Graphics, c As Control, r As Rectangle, flags As TextFormatFlags)
        Dim back = c.BackColor
        If back.A < 255 Then back = If(c.Parent?.BackColor, Window)
        Using b As New SolidBrush(back)
            g.FillRectangle(b, r)
        End Using
        TextRenderer.DrawText(g, c.Text, c.Font, r, InkDim, back, flags)
    End Sub

    Private Shared Function AlignFlags(a As ContentAlignment) As TextFormatFlags
        Dim f As TextFormatFlags
        Select Case a
            Case ContentAlignment.TopLeft, ContentAlignment.TopCenter, ContentAlignment.TopRight : f = TextFormatFlags.Top
            Case ContentAlignment.BottomLeft, ContentAlignment.BottomCenter, ContentAlignment.BottomRight : f = TextFormatFlags.Bottom
            Case Else : f = TextFormatFlags.VerticalCenter
        End Select
        Select Case a
            Case ContentAlignment.TopCenter, ContentAlignment.MiddleCenter, ContentAlignment.BottomCenter : f = f Or TextFormatFlags.HorizontalCenter
            Case ContentAlignment.TopRight, ContentAlignment.MiddleRight, ContentAlignment.BottomRight : f = f Or TextFormatFlags.Right
        End Select
        Return f
    End Function

    ''' <summary>A table (spreadsheet grid) in the skin's colours: dark rows, a header band, the skin's selection.</summary>
    Public Shared Sub StyleTable(t As DataGridView)
        t.BackgroundColor = Field
        t.BorderStyle = BorderStyle.None
        t.GridColor = Line
        t.EnableHeadersVisualStyles = False
        t.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single
        With t.ColumnHeadersDefaultCellStyle
            .BackColor = Category : .ForeColor = Ink
            .SelectionBackColor = Category : .SelectionForeColor = Ink
        End With
        With t.DefaultCellStyle
            .BackColor = Field : .ForeColor = Ink
            .SelectionBackColor = Selection : .SelectionForeColor = Color.White
        End With
        t.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(32, 32, 35)
        DarkScrollbars(t)
    End Sub

    ''' <summary>A property grid in the skin's colours (rows, categories, help pane, its own toolbar).</summary>
    Public Shared Sub StyleGrid(pg As PropertyGrid)
        pg.BackColor = Window
        pg.ViewBackColor = Field
        pg.ViewForeColor = Ink
        pg.ViewBorderColor = Edge
        pg.LineColor = Category
        pg.CategoryForeColor = Ink
        pg.CategorySplitterColor = Category
        pg.HelpBackColor = Panel
        pg.HelpForeColor = Ink
        pg.HelpBorderColor = Edge
        pg.CommandsBackColor = Panel
        pg.CommandsForeColor = Ink
        pg.CommandsBorderColor = Edge
        pg.CommandsLinkColor = LedLit
        pg.CommandsActiveLinkColor = Yellow
        pg.SelectedItemWithFocusBackColor = Selection
        pg.SelectedItemWithFocusForeColor = Color.White
        pg.DisabledItemForeColor = InkDim
        ' The scrollbar belongs to the grid's inner view; the grid's own toolbar takes the renderer.
        Dim themeKids = Sub()
                            For Each kid As Control In pg.Controls
                                DarkScrollbars(kid)
                                If TypeOf kid Is ToolStrip Then DirectCast(kid, ToolStrip).Renderer = Renderer
                            Next
                        End Sub
        If pg.IsHandleCreated Then themeKids() Else AddHandler pg.HandleCreated, Sub(s, e) themeKids()
    End Sub

    Private Shared Sub DrawListItem(sender As Object, e As DrawItemEventArgs)
        Dim lb = DirectCast(sender, ListBox)
        If e.Index < 0 OrElse e.Index >= lb.Items.Count Then Return
        Dim selected = (e.State And DrawItemState.Selected) <> 0
        Using b As New SolidBrush(If(selected, Selection, lb.BackColor))
            e.Graphics.FillRectangle(b, e.Bounds)
        End Using
        Dim text = lb.GetItemText(lb.Items(e.Index))
        TextRenderer.DrawText(e.Graphics, text, e.Font, New Rectangle(e.Bounds.X + 2, e.Bounds.Y, e.Bounds.Width - 2, e.Bounds.Height),
                              If(selected, Color.White, lb.ForeColor), TextFormatFlags.VerticalCenter Or TextFormatFlags.NoPrefix Or TextFormatFlags.EndEllipsis)
        If selected Then
            Using p As New Pen(Led)
                e.Graphics.DrawLine(p, e.Bounds.Left, e.Bounds.Top, e.Bounds.Left, e.Bounds.Bottom - 1)
            End Using
        End If
    End Sub

    ' ------------------------------------------------------- dialog buttons

    ''' <summary>A plain button drawn as a Code Chop command button.</summary>
    Public Shared Sub SkinCommandButton(b As Button)
        If b.Tag Is SkinnedTag Then Return
        b.Tag = If(b.Tag, SkinnedTag)
        b.FlatStyle = FlatStyle.Flat
        b.FlatAppearance.BorderSize = 0
        b.UseVisualStyleBackColor = False
        b.BackColor = Window
        b.ForeColor = Ink
        AddHandler b.Paint, AddressOf PaintCommandButton
        HookRepaint(b)
    End Sub

    ''' <summary>A check box shown as a button, drawn as a Code Chop round toggle.</summary>
    Public Shared Sub SkinToggleButton(cb As CheckBox)
        If cb.Tag Is SkinnedTag Then Return
        cb.Tag = If(cb.Tag, SkinnedTag)
        cb.FlatStyle = FlatStyle.Flat
        cb.FlatAppearance.BorderSize = 0
        cb.UseVisualStyleBackColor = False
        cb.BackColor = Window
        cb.ForeColor = Ink
        AddHandler cb.Paint, AddressOf PaintToggleButton
        AddHandler cb.CheckedChanged, Sub(s, e) cb.Invalidate()
        HookRepaint(cb)
    End Sub

    Private Shared ReadOnly SkinnedTag As New Object()

    Private Shared Sub HookRepaint(c As Control)
        AddHandler c.MouseEnter, Sub(s, e) c.Invalidate()
        AddHandler c.MouseLeave, Sub(s, e) c.Invalidate()
        AddHandler c.MouseDown, Sub(s, e) c.Invalidate()
        AddHandler c.MouseUp, Sub(s, e) c.Invalidate()
        AddHandler c.GotFocus, Sub(s, e) c.Invalidate()
        AddHandler c.LostFocus, Sub(s, e) c.Invalidate()
        AddHandler c.EnabledChanged, Sub(s, e) c.Invalidate()
    End Sub

    Private Shared Sub PaintCommandButton(sender As Object, e As PaintEventArgs)
        PaintControlButton(DirectCast(sender, ButtonBase), e, Look.Square, locked:=False)
    End Sub

    Private Shared Sub PaintToggleButton(sender As Object, e As PaintEventArgs)
        Dim cb = DirectCast(sender, CheckBox)
        PaintControlButton(cb, e, Look.Round, locked:=cb.Checked)
    End Sub

    Private Shared Sub PaintControlButton(b As ButtonBase, e As PaintEventArgs, look As Look, locked As Boolean)
        Dim g = e.Graphics
        Dim hot = b.Enabled AndAlso b.ClientRectangle.Contains(b.PointToClient(Control.MousePosition))
        Dim pressed = hot AndAlso b.Capture AndAlso (Control.MouseButtons And MouseButtons.Left) <> 0
        Using back As New SolidBrush(If(b.Parent?.BackColor, Window))
            g.FillRectangle(back, b.ClientRectangle)
        End Using
        PaintButton(g, b.ClientRectangle, look, hot, pressed, locked, b.Enabled)
        ' A toggle's face is yellow while on and while held down: dark text and icon on it.
        Dim lit = b.Enabled AndAlso (locked OrElse (look = Look.Round AndAlso pressed))
        Dim textColor = If(Not b.Enabled, InkDim, If(lit, InkOnYellow, Ink))
        Dim textRect = b.ClientRectangle
        If look = Look.Square Then textRect.Height -= Math.Max(3, b.Height \ 8)
        Dim img = b.Image
        If img IsNot Nothing Then
            If lit Then img = If(YellowTwin(img), img)
            Dim gap = Scaled(4, b)
            Dim textW = If(String.IsNullOrEmpty(b.Text), 0, TextRenderer.MeasureText(g, b.Text, b.Font).Width)
            Dim total = img.Width + If(textW > 0, gap + textW, 0)
            Dim x = textRect.Left + (textRect.Width - total) \ 2
            Dim y = textRect.Top + (textRect.Height - img.Height) \ 2
            DrawIcon(g, img, New Rectangle(x, y, img.Width, img.Height), b.Enabled)
            textRect = New Rectangle(x + img.Width + gap, textRect.Top, Math.Max(0, textRect.Right - (x + img.Width + gap)), textRect.Height)
            If textW > 0 Then TextRenderer.DrawText(g, b.Text, b.Font, textRect, textColor, TextFormatFlags.Left Or TextFormatFlags.VerticalCenter)
        Else
            TextRenderer.DrawText(g, b.Text, b.Font, textRect, textColor, TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter Or TextFormatFlags.WordBreak)
        End If
        If b.Focused Then
            Using p As New Pen(Color.FromArgb(150, LedLit)) With {.DashStyle = DashStyle.Dot}
                g.DrawRectangle(p, 3, 3, b.Width - 7, b.Height - 7)
            End Using
        End If
    End Sub

    ''' <summary>Draws an icon; disabled, faded.</summary>
    Public Shared Sub DrawIcon(g As Graphics, img As Image, r As Rectangle, enabled As Boolean)
        If enabled Then
            g.DrawImage(img, r)
            Return
        End If
        Using ia As New ImageAttributes()
            Dim m As New ColorMatrix(New Single()() {
                New Single() {0.3F, 0.3F, 0.3F, 0, 0},
                New Single() {0.59F, 0.59F, 0.59F, 0, 0},
                New Single() {0.11F, 0.11F, 0.11F, 0, 0},
                New Single() {0, 0, 0, 0.32F, 0},
                New Single() {0, 0, 0, 0, 1}})
            ia.SetColorMatrix(m)
            g.DrawImage(img, r, 0, 0, img.Width, img.Height, GraphicsUnit.Pixel, ia)
        End Using
    End Sub

    ' ---------------------------------------------------- Windows plumbing

    <DllImport("dwmapi.dll")>
    Private Shared Function DwmSetWindowAttribute(hwnd As IntPtr, attribute As Integer, ByRef value As Integer, size As Integer) As Integer
    End Function

    <DllImport("uxtheme.dll", CharSet:=CharSet.Unicode)>
    Private Shared Function SetWindowTheme(hwnd As IntPtr, appName As String, idList As String) As Integer
    End Function

    ''' <summary>A dark title bar (Windows 10 2004 and later; Windows 11 also takes the caption colour).</summary>
    Public Shared Sub DarkTitleBar(f As Form)
        Dim applyIt = Sub()
                          Try
                              Dim isOn = 1
                              DwmSetWindowAttribute(f.Handle, 20, isOn, 4)          ' DWMWA_USE_IMMERSIVE_DARK_MODE
                              Dim caption = ColorTranslator.ToWin32(Window)
                              DwmSetWindowAttribute(f.Handle, 35, caption, 4)       ' DWMWA_CAPTION_COLOR
                              Dim border = ColorTranslator.ToWin32(Color.FromArgb(44, 44, 46))
                              DwmSetWindowAttribute(f.Handle, 34, border, 4)        ' DWMWA_BORDER_COLOR
                          Catch ex As DllNotFoundException
                          Catch ex As EntryPointNotFoundException
                          End Try
                      End Sub
        AddHandler f.HandleCreated, Sub(s, e) applyIt()
        If f.IsHandleCreated Then applyIt()
    End Sub

    ''' <summary>Dark scrollbars for a control (Windows 10 1809 and later), also after its handle is made again.</summary>
    Public Shared Sub DarkScrollbars(c As Control)
        DarkTheme(c, "DarkMode_Explorer")
    End Sub

    ''' <summary>One of Windows' dark control themes for a control, also after its handle is made again.</summary>
    Public Shared Sub DarkTheme(c As Control, theme As String)
        Dim applyIt = Sub()
                          Try
                              SetWindowTheme(c.Handle, theme, Nothing)
                          Catch ex As DllNotFoundException
                          Catch ex As EntryPointNotFoundException
                          End Try
                      End Sub
        AddHandler c.HandleCreated, Sub(s, e) applyIt()
        If c.IsHandleCreated Then applyIt()
    End Sub
End Class

''' <summary>Menus, toolbars and the status bar in the skin.</summary>
Friend Class SkinRenderer
    Inherits ToolStripProfessionalRenderer

    Public Sub New()
        MyBase.New(New SkinColorTable())
        RoundedEdges = False
    End Sub

    Private Shared Function IsMenu(ts As ToolStrip) As Boolean
        Return TypeOf ts Is ToolStripDropDown
    End Function

    Private Shared Function IsLocked(item As ToolStripItem) As Boolean
        Dim b = TryCast(item, ToolStripButton)
        Return b IsNot Nothing AndAlso b.Checked AndAlso Skin.IsToggle(item)
    End Function

    ''' <summary>A toggle's face is yellow while it is on and while it is held down (dark text and icon on it).</summary>
    Private Shared Function IsLit(item As ToolStripItem) As Boolean
        Return item.Enabled AndAlso (IsLocked(item) OrElse (item.Pressed AndAlso Skin.IsToggle(item)))
    End Function

    Protected Overrides Sub OnRenderToolStripBackground(e As ToolStripRenderEventArgs)
        Dim ts = e.ToolStrip
        Dim r As New Rectangle(Point.Empty, ts.Size)
        If IsMenu(ts) Then
            Using b As New SolidBrush(Color.FromArgb(22, 22, 24))
                e.Graphics.FillRectangle(b, r)
            End Using
        ElseIf TypeOf ts Is StatusStrip Then
            Using b As New LinearGradientBrush(r, Color.FromArgb(26, 26, 28), Color.FromArgb(8, 8, 9), LinearGradientMode.Vertical)
                e.Graphics.FillRectangle(b, r)
            End Using
            Using p As New Pen(Color.FromArgb(48, 48, 52))
                e.Graphics.DrawLine(p, 0, 0, r.Width, 0)
            End Using
        ElseIf TypeOf ts Is MenuStrip Then
            Skin.PaintBand(e.Graphics, r, vertical:=False, limeEdge:=False)
        Else
            Skin.PaintBand(e.Graphics, r, vertical:=ts.Orientation = Orientation.Vertical, limeEdge:=True)
        End If
    End Sub

    Protected Overrides Sub OnRenderToolStripBorder(e As ToolStripRenderEventArgs)
        If IsMenu(e.ToolStrip) Then
            Using p As New Pen(Color.FromArgb(70, 70, 74))
                e.Graphics.DrawRectangle(p, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1)
            End Using
        End If
    End Sub

    Protected Overrides Sub OnRenderGrip(e As ToolStripGripRenderEventArgs)
        Dim r = e.GripBounds
        If r.Width <= 0 OrElse r.Height <= 0 Then Return
        Dim vertical = e.GripDisplayStyle = ToolStripGripDisplayStyle.Vertical
        Dim stepPx = Math.Max(3, Skin.Scaled(4, e.ToolStrip))
        Using light As New SolidBrush(Color.FromArgb(130, 130, 130)), dark As New SolidBrush(Color.Black)
            If vertical Then
                Dim x = r.Left + r.Width \ 2 - 1
                For y = r.Top + 4 To r.Bottom - 5 Step stepPx
                    e.Graphics.FillRectangle(dark, x + 1, y + 1, 2, 2)
                    e.Graphics.FillRectangle(light, x, y, 2, 2)
                Next
            Else
                Dim y = r.Top + r.Height \ 2 - 1
                For x = r.Left + 4 To r.Right - 5 Step stepPx
                    e.Graphics.FillRectangle(dark, x + 1, y + 1, 2, 2)
                    e.Graphics.FillRectangle(light, x, y, 2, 2)
                Next
            End If
        End Using
    End Sub

    Protected Overrides Sub OnRenderButtonBackground(e As ToolStripItemRenderEventArgs)
        Dim item = e.Item
        Dim look = If(Skin.IsToggle(item), Skin.Look.Round, Skin.Look.Square)
        Skin.PaintButton(e.Graphics, New Rectangle(Point.Empty, item.Size), look, item.Selected, item.Pressed, IsLocked(item), item.Enabled)
    End Sub

    Protected Overrides Sub OnRenderDropDownButtonBackground(e As ToolStripItemRenderEventArgs)
        Skin.PaintButton(e.Graphics, New Rectangle(Point.Empty, e.Item.Size), Skin.Look.Square, e.Item.Selected, e.Item.Pressed, False, e.Item.Enabled)
    End Sub

    Protected Overrides Sub OnRenderOverflowButtonBackground(e As ToolStripItemRenderEventArgs)
        Dim item = e.Item
        Dim r As New Rectangle(Point.Empty, item.Size)
        If item.Selected OrElse item.Pressed Then Skin.PaintButton(e.Graphics, r, Skin.Look.Square, True, item.Pressed, False, True)
        ' A chevron pointing at the hidden buttons.
        Dim smoothing = e.Graphics.SmoothingMode
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias
        Dim cx = r.Left + r.Width / 2.0F, cy = r.Top + r.Height / 2.0F
        Dim s = Math.Max(2.5F, Math.Min(r.Width, r.Height) * 0.14F)
        Using p As New Pen(Skin.Ink, 1.6F)
            If e.ToolStrip.Orientation = Orientation.Vertical Then
                e.Graphics.DrawLines(p, {New PointF(cx - s, cy - s * 1.2F), New PointF(cx, cy - s * 0.2F), New PointF(cx + s, cy - s * 1.2F)})
                e.Graphics.DrawLines(p, {New PointF(cx - s, cy + s * 0.2F), New PointF(cx, cy + s * 1.2F), New PointF(cx + s, cy + s * 0.2F)})
            Else
                e.Graphics.DrawLines(p, {New PointF(cx - s * 1.2F, cy - s), New PointF(cx - s * 0.2F, cy), New PointF(cx - s * 1.2F, cy + s)})
                e.Graphics.DrawLines(p, {New PointF(cx + s * 0.2F, cy - s), New PointF(cx + s * 1.2F, cy), New PointF(cx + s * 0.2F, cy + s)})
            End If
        End Using
        e.Graphics.SmoothingMode = smoothing
    End Sub

    Protected Overrides Sub OnRenderSeparator(e As ToolStripSeparatorRenderEventArgs)
        Dim r As New Rectangle(Point.Empty, e.Item.Size)
        If IsMenu(e.ToolStrip) Then
            Dim left = If(TypeOf e.ToolStrip Is ToolStripDropDownMenu, DirectCast(e.ToolStrip, ToolStripDropDownMenu).Padding.Left + 26, 4)
            Dim y = r.Height \ 2
            Using p As New Pen(Color.FromArgb(56, 56, 60))
                e.Graphics.DrawLine(p, left, y, r.Right - 4, y)
            End Using
        Else
            Skin.PaintSpacer(e.Graphics, r, vertical:=e.Vertical)
        End If
    End Sub

    Protected Overrides Sub OnRenderMenuItemBackground(e As ToolStripItemRenderEventArgs)
        Dim item = e.Item
        If Not (item.Selected OrElse item.Pressed) OrElse Not item.Enabled AndAlso Not item.Pressed Then Return
        Dim g = e.Graphics
        Dim r As New Rectangle(Point.Empty, item.Size)
        If item.IsOnDropDown Then
            r = New Rectangle(2, 0, r.Width - 4, r.Height)
            Using b As New SolidBrush(Skin.Selection)
                g.FillRectangle(b, r)
            End Using
            Using p As New Pen(Skin.Led)
                g.DrawLine(p, r.Left, r.Top, r.Left, r.Bottom - 1)
            End Using
        Else
            ' A top-level menu: lit while its list is open or the mouse is on it.
            Using b As New SolidBrush(If(item.Pressed, Color.FromArgb(34, 34, 36), Color.FromArgb(44, 44, 48)))
                g.FillRectangle(b, r)
            End Using
            Using p As New Pen(Skin.Led)
                g.DrawLine(p, r.Left + 3, r.Bottom - 2, r.Right - 4, r.Bottom - 2)
            End Using
        End If
    End Sub

    Protected Overrides Sub OnRenderImageMargin(e As ToolStripRenderEventArgs)
        Using b As New SolidBrush(Color.FromArgb(17, 17, 19))
            e.Graphics.FillRectangle(b, e.AffectedBounds)
        End Using
    End Sub

    Protected Overrides Sub OnRenderItemCheck(e As ToolStripItemImageRenderEventArgs)
        Dim g = e.Graphics
        Dim r = Rectangle.Inflate(e.ImageRectangle, 2, 2)
        Using b As New SolidBrush(Color.FromArgb(52, 60, 10)), p As New Pen(Skin.Led)
            g.FillRectangle(b, r)
            g.DrawRectangle(p, r.X, r.Y, r.Width - 1, r.Height - 1)
        End Using
        If e.Item.Image Is Nothing Then
            Dim smoothing = g.SmoothingMode
            g.SmoothingMode = SmoothingMode.AntiAlias
            Using p As New Pen(Skin.LedLit, 1.8F) With {.StartCap = LineCap.Round, .EndCap = LineCap.Round, .LineJoin = LineJoin.Round}
                Dim ir = e.ImageRectangle
                g.DrawLines(p, {New PointF(ir.Left + ir.Width * 0.18F, ir.Top + ir.Height * 0.52F),
                                New PointF(ir.Left + ir.Width * 0.42F, ir.Top + ir.Height * 0.76F),
                                New PointF(ir.Left + ir.Width * 0.84F, ir.Top + ir.Height * 0.24F)})
            End Using
            g.SmoothingMode = smoothing
        End If
    End Sub

    Protected Overrides Sub OnRenderItemImage(e As ToolStripItemImageRenderEventArgs)
        Dim img = e.Image
        If img Is Nothing Then Return
        If IsLit(e.Item) Then img = If(Skin.YellowTwin(img), img)
        Skin.DrawIcon(e.Graphics, img, e.ImageRectangle, e.Item.Enabled)
    End Sub

    Protected Overrides Sub OnRenderItemText(e As ToolStripItemTextRenderEventArgs)
        Dim item = e.Item
        Dim label = TryCast(item, ToolStripLabel)
        If label IsNot Nothing AndAlso label.IsLink Then
            MyBase.OnRenderItemText(e)
            Return
        End If
        e.TextColor = If(Not item.Enabled, Skin.InkDim, If(IsLit(item), Skin.InkOnYellow, Skin.Ink))
        MyBase.OnRenderItemText(e)
    End Sub

    Protected Overrides Sub OnRenderArrow(e As ToolStripArrowRenderEventArgs)
        e.ArrowColor = If(e.Item Is Nothing OrElse e.Item.Enabled, Skin.Ink, Skin.InkDim)
        MyBase.OnRenderArrow(e)
    End Sub

    Protected Overrides Sub OnRenderLabelBackground(e As ToolStripItemRenderEventArgs)
        ' Labels sit on the band.
    End Sub

    Protected Overrides Sub OnRenderStatusStripSizingGrip(e As ToolStripRenderEventArgs)
        Dim r = e.AffectedBounds
        Using light As New SolidBrush(Color.FromArgb(110, 110, 110))
            For i = 0 To 2
                For j = 0 To i
                    e.Graphics.FillRectangle(light, r.Right - 4 - j * 4, r.Bottom - 4 - (i - j) * 4, 2, 2)
                Next
            Next
        End Using
    End Sub
End Class

''' <summary>Dark colours for anything the renderer leaves to the professional look.</summary>
Friend Class SkinColorTable
    Inherits ProfessionalColorTable

    Public Sub New()
        UseSystemColors = False
    End Sub

    Public Overrides ReadOnly Property MenuBorder As Color
        Get
            Return Color.FromArgb(70, 70, 74)
        End Get
    End Property
    Public Overrides ReadOnly Property MenuItemBorder As Color
        Get
            Return Skin.Led
        End Get
    End Property
    Public Overrides ReadOnly Property MenuItemSelected As Color
        Get
            Return Skin.Selection
        End Get
    End Property
    Public Overrides ReadOnly Property MenuItemSelectedGradientBegin As Color
        Get
            Return Color.FromArgb(44, 44, 48)
        End Get
    End Property
    Public Overrides ReadOnly Property MenuItemSelectedGradientEnd As Color
        Get
            Return Color.FromArgb(44, 44, 48)
        End Get
    End Property
    Public Overrides ReadOnly Property MenuItemPressedGradientBegin As Color
        Get
            Return Color.FromArgb(34, 34, 36)
        End Get
    End Property
    Public Overrides ReadOnly Property MenuItemPressedGradientEnd As Color
        Get
            Return Color.FromArgb(34, 34, 36)
        End Get
    End Property
    Public Overrides ReadOnly Property ToolStripDropDownBackground As Color
        Get
            Return Color.FromArgb(22, 22, 24)
        End Get
    End Property
    Public Overrides ReadOnly Property ImageMarginGradientBegin As Color
        Get
            Return Color.FromArgb(17, 17, 19)
        End Get
    End Property
    Public Overrides ReadOnly Property ImageMarginGradientMiddle As Color
        Get
            Return Color.FromArgb(17, 17, 19)
        End Get
    End Property
    Public Overrides ReadOnly Property ImageMarginGradientEnd As Color
        Get
            Return Color.FromArgb(17, 17, 19)
        End Get
    End Property
    Public Overrides ReadOnly Property SeparatorDark As Color
        Get
            Return Color.FromArgb(56, 56, 60)
        End Get
    End Property
    Public Overrides ReadOnly Property SeparatorLight As Color
        Get
            Return Color.FromArgb(20, 20, 22)
        End Get
    End Property
    Public Overrides ReadOnly Property ToolStripBorder As Color
        Get
            Return Color.Black
        End Get
    End Property
    Public Overrides ReadOnly Property ToolStripGradientBegin As Color
        Get
            Return Skin.Window
        End Get
    End Property
    Public Overrides ReadOnly Property ToolStripGradientMiddle As Color
        Get
            Return Skin.Window
        End Get
    End Property
    Public Overrides ReadOnly Property ToolStripGradientEnd As Color
        Get
            Return Skin.Window
        End Get
    End Property
    Public Overrides ReadOnly Property ToolStripContentPanelGradientBegin As Color
        Get
            Return Skin.Window
        End Get
    End Property
    Public Overrides ReadOnly Property ToolStripContentPanelGradientEnd As Color
        Get
            Return Skin.Window
        End Get
    End Property
    Public Overrides ReadOnly Property ToolStripPanelGradientBegin As Color
        Get
            Return Skin.Window
        End Get
    End Property
    Public Overrides ReadOnly Property ToolStripPanelGradientEnd As Color
        Get
            Return Skin.Window
        End Get
    End Property
    Public Overrides ReadOnly Property MenuStripGradientBegin As Color
        Get
            Return Skin.Window
        End Get
    End Property
    Public Overrides ReadOnly Property MenuStripGradientEnd As Color
        Get
            Return Skin.Window
        End Get
    End Property
    Public Overrides ReadOnly Property StatusStripGradientBegin As Color
        Get
            Return Skin.Window
        End Get
    End Property
    Public Overrides ReadOnly Property StatusStripGradientEnd As Color
        Get
            Return Skin.Window
        End Get
    End Property
    Public Overrides ReadOnly Property CheckBackground As Color
        Get
            Return Color.FromArgb(52, 60, 10)
        End Get
    End Property
    Public Overrides ReadOnly Property CheckSelectedBackground As Color
        Get
            Return Color.FromArgb(70, 84, 6)
        End Get
    End Property
    Public Overrides ReadOnly Property CheckPressedBackground As Color
        Get
            Return Color.FromArgb(70, 84, 6)
        End Get
    End Property
    Public Overrides ReadOnly Property ButtonSelectedBorder As Color
        Get
            Return Skin.Led
        End Get
    End Property
    Public Overrides ReadOnly Property OverflowButtonGradientBegin As Color
        Get
            Return Skin.Window
        End Get
    End Property
    Public Overrides ReadOnly Property OverflowButtonGradientMiddle As Color
        Get
            Return Skin.Window
        End Get
    End Property
    Public Overrides ReadOnly Property OverflowButtonGradientEnd As Color
        Get
            Return Skin.Window
        End Get
    End Property
    Public Overrides ReadOnly Property GripDark As Color
        Get
            Return Color.Black
        End Get
    End Property
    Public Overrides ReadOnly Property GripLight As Color
        Get
            Return Color.FromArgb(130, 130, 130)
        End Get
    End Property
End Class

''' <summary>A tab control drawn dark: tabs with rounded tops (Code Chop's tab heads), the open one lit.</summary>
Friend Class DarkTabControl
    Inherits TabControl

    Private _hot As Integer = -1

    Public Sub New()
        SetStyle(ControlStyles.UserPaint Or ControlStyles.AllPaintingInWmPaint Or ControlStyles.OptimizedDoubleBuffer Or ControlStyles.ResizeRedraw, True)
    End Sub

    Protected Overrides Sub OnPaint(e As PaintEventArgs)
        Dim g = e.Graphics
        g.Clear(Skin.Window)
        g.SmoothingMode = SmoothingMode.AntiAlias
        If TabCount = 0 Then Return
        ' The page frame.
        Dim page = DisplayRectangle
        page.Inflate(1, 1)
        Using p As New Pen(Skin.Edge)
            g.DrawRectangle(p, page.X, page.Y - 1, page.Width - 1, page.Height)
        End Using
        For i = 0 To TabCount - 1
            Dim r = GetTabRect(i)
            Dim selected = i = SelectedIndex
            Dim tab As New RectangleF(r.X + 1, r.Y + If(selected, 0, 2), r.Width - 2, r.Height + If(selected, 1, -2))
            Using path As New GraphicsPath()
                Dim rad = Math.Min(6.0F, tab.Height / 2)
                path.AddLine(tab.Left, tab.Bottom, tab.Left, tab.Top + rad)
                path.AddArc(tab.Left, tab.Top, rad * 2, rad * 2, 180, 90)
                path.AddArc(tab.Right - rad * 2, tab.Top, rad * 2, rad * 2, 270, 90)
                path.AddLine(tab.Right, tab.Top + rad, tab.Right, tab.Bottom)
                Using b As New LinearGradientBrush(tab, If(selected, Color.FromArgb(60, 60, 64), If(i = _hot, Color.FromArgb(44, 44, 48), Color.FromArgb(30, 30, 32))),
                                                   If(selected, Skin.Panel, Color.FromArgb(16, 16, 18)), LinearGradientMode.Vertical)
                    g.FillPath(b, path)
                End Using
                Using p As New Pen(If(selected, Color.FromArgb(150, 150, 150), Skin.Edge))
                    g.DrawPath(p, path)
                End Using
            End Using
            If selected Then
                Using p As New Pen(Skin.Led, 2)
                    g.DrawLine(p, tab.Left + 4, tab.Top + 1.5F, tab.Right - 4, tab.Top + 1.5F)
                End Using
            End If
            TextRenderer.DrawText(g, TabPages(i).Text, Font, Rectangle.Round(tab), If(selected, Skin.Ink, Skin.InkDim),
                                  TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter Or TextFormatFlags.NoPrefix)
        Next
    End Sub

    Protected Overrides Sub OnMouseMove(e As MouseEventArgs)
        MyBase.OnMouseMove(e)
        Dim hot = -1
        For i = 0 To TabCount - 1
            If GetTabRect(i).Contains(e.Location) Then hot = i
        Next
        If hot <> _hot Then
            _hot = hot
            Invalidate()
        End If
    End Sub

    Protected Overrides Sub OnMouseLeave(e As EventArgs)
        MyBase.OnMouseLeave(e)
        If _hot <> -1 Then
            _hot = -1
            Invalidate()
        End If
    End Sub

    Protected Overrides Sub OnSelectedIndexChanged(e As EventArgs)
        MyBase.OnSelectedIndexChanged(e)
        Invalidate()
    End Sub
End Class
