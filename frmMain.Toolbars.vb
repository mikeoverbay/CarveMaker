' ============================================================================
'  frmMain.Toolbars.vb
'  The toolbars over the view (Project, Draw Tools, Text Style, Snap, View). Each can be
'  docked on any edge of the view (drag it by its grip), floated in a small
'  window (drag it off the view, or right-click > Float), and hidden; View >
'  Toolbars shows them again or puts them all back along the top. Where each
'  one is goes into the user's preferences.
' ============================================================================

Partial Class frmMain

    Private _toolbarMenu As ContextMenuStrip
    Private ReadOnly _floatingToolbars As New Dictionary(Of ToolStrip, ToolbarWindow)
    Private ReadOnly _hiddenToolbars As New HashSet(Of ToolStrip)
    Private ReadOnly _lastDock As New Dictionary(Of ToolStrip, String)
    Private _restoringToolbars As Boolean

    ''' <summary>The movable toolbars, in their default order along the top.</summary>
    Private ReadOnly Property ViewToolbars As ToolStrip()
        Get
            Return {tsProject, tsDrawTools, tsTextStyle, tsSnap, tsView}
        End Get
    End Property

    Private Function PanelOf(place As String) As ToolStripPanel
        Select Case place
            Case "Bottom" : Return tspBottom
            Case "Left" : Return tspLeft
            Case "Right" : Return tspRight
            Case Else : Return tspTop
        End Select
    End Function

    ''' <summary>Top, Bottom, Left, Right or Float.</summary>
    Private Function PlaceOf(ts As ToolStrip) As String
        If _floatingToolbars.ContainsKey(ts) Then Return "Float"
        If ts.Parent Is tspBottom Then Return "Bottom"
        If ts.Parent Is tspLeft Then Return "Left"
        If ts.Parent Is tspRight Then Return "Right"
        Return "Top"
    End Function

    ''' <summary>Grips, the right-click menu, View > Toolbars, and the saved layout (called from SetupDrawing).</summary>
    Private Sub SetupToolbars()
        _toolbarMenu = New ContextMenuStrip()
        For Each entry In {("Dock &Top", "Top"), ("Dock &Left", "Left"), ("Dock &Right", "Right"), ("Dock &Bottom", "Bottom")}
            Dim place = entry.Item2
            _toolbarMenu.Items.Add(entry.Item1, Nothing, Sub(o, ev) If MenuToolbar() IsNot Nothing Then DockToolbar(MenuToolbar(), place))
        Next
        _toolbarMenu.Items.Add("&Float", Nothing, Sub(o, ev)
                                                     Dim ts = MenuToolbar()
                                                     If ts IsNot Nothing AndAlso Not _floatingToolbars.ContainsKey(ts) Then FloatToolbar(ts, ts.PointToScreen(New Point(0, ts.Height + 8)))
                                                 End Sub)
        AddHandler _toolbarMenu.Opening, Sub(o, ev)
                                             Dim ts = MenuToolbar()
                                             If ts Is Nothing Then Return
                                             Dim place = PlaceOf(ts)
                                             For Each mi In _toolbarMenu.Items.OfType(Of ToolStripMenuItem)()
                                                 Dim target = mi.Text.Replace("&", "")
                                                 If target.StartsWith("Dock ") Then mi.Enabled = target.Substring(5) <> place
                                                 If target = "Float" Then mi.Enabled = place <> "Float"
                                             Next
                                         End Sub
        _toolbarMenu.Items.Add(New ToolStripSeparator())
        _toolbarMenu.Items.Add("&Hide", Nothing, Sub(o, ev) If MenuToolbar() IsNot Nothing Then SetToolbarVisible(MenuToolbar(), False))

        For Each ts In ViewToolbars
            ts.GripStyle = ToolStripGripStyle.Visible
            ts.ContextMenuStrip = _toolbarMenu
            AddHandler ts.EndDrag, AddressOf Toolbar_EndDrag
            Dim bar = ts
            Dim mi As New ToolStripMenuItem(ts.Text) With {.Tag = ts}
            AddHandler mi.Click, Sub(o, ev) SetToolbarVisible(bar, Not IsToolbarVisible(bar))
            mnuViewToolbars.DropDownItems.Add(mi)
        Next
        mnuViewToolbars.DropDownItems.Add(New ToolStripSeparator())
        mnuViewToolbars.DropDownItems.Add("&Reset Toolbars", Nothing, Sub(o, ev) ResetToolbars())
        AddHandler mnuViewToolbars.DropDownOpening, Sub(o, ev)
                                                        For Each mi In mnuViewToolbars.DropDownItems.OfType(Of ToolStripMenuItem)()
                                                            Dim ts = TryCast(mi.Tag, ToolStrip)
                                                            If ts IsNot Nothing Then mi.Checked = IsToolbarVisible(ts)
                                                        Next
                                                    End Sub
        AddHandler Shown, Sub(o, ev) ShowFloatingToolbars()
        AddHandler FormClosing, Sub(o, ev) SaveToolbarLayout()
        RestoreToolbars()
    End Sub

    ''' <summary>The toolbar the right-click menu was opened on.</summary>
    Private Function MenuToolbar() As ToolStrip
        Return TryCast(_toolbarMenu.SourceControl, ToolStrip)
    End Function

    Private Function IsToolbarVisible(ts As ToolStrip) As Boolean
        Return Not _hiddenToolbars.Contains(ts)
    End Function

    ''' <summary>Shows or hides a toolbar where it is (docked or floating).</summary>
    Private Sub SetToolbarVisible(ts As ToolStrip, visible As Boolean)
        If visible Then _hiddenToolbars.Remove(ts) Else _hiddenToolbars.Add(ts)
        Dim w As ToolbarWindow = Nothing
        If _floatingToolbars.TryGetValue(ts, w) Then
            If Not visible Then
                w.Hide()
            ElseIf Me.Visible Then
                w.Show()
            End If
        Else
            ts.Visible = visible
        End If
        SaveToolbarLayout()
    End Sub

    ''' <summary>Where a toolbar dropped into a panel goes: the end of the panel's first row.</summary>
    Private Shared Function EndOfFirstRow(panel As ToolStripPanel) As Point
        Return EndOfRow(panel, 0)
    End Function

    ''' <summary>The spot just after the last toolbar of a row (a new row's spot when the row does not exist yet).</summary>
    Private Shared Function EndOfRow(panel As ToolStripPanel, rowIndex As Integer) As Point
        If panel.Rows.Length = 0 Then Return Point.Empty
        If rowIndex >= panel.Rows.Length Then
            Dim lastRow = panel.Rows(panel.Rows.Length - 1).Bounds
            Return If(panel.Orientation = Orientation.Horizontal, New Point(0, lastRow.Bottom + 1), New Point(lastRow.Right + 1, 0))
        End If
        Dim row = panel.Rows(rowIndex)
        Dim lastEdge = 0
        For Each c In row.Controls
            lastEdge = Math.Max(lastEdge, If(panel.Orientation = Orientation.Horizontal, c.Right, c.Bottom))
        Next
        Return If(panel.Orientation = Orientation.Horizontal, New Point(lastEdge, row.Bounds.Top), New Point(row.Bounds.Left, lastEdge))
    End Function

    ''' <summary>
    ''' Where a toolbar docked without a spot goes: the end of the first row with room for
    ''' it, or a new row (so a long row does not hide buttons behind its overflow chevron).
    ''' </summary>
    Private Shared Function EndOfRoomyRow(panel As ToolStripPanel, ts As ToolStrip) As Point
        If RoomAlong(panel) <= 0 Then Return EndOfFirstRow(panel)
        For r = 0 To panel.Rows.Length - 1
            If FitsAtEndOfRow(panel, r, ts) Then Return EndOfRow(panel, r)
        Next
        Return EndOfRow(panel, panel.Rows.Length)
    End Function

    ''' <summary>The saved spot (end of its saved row), or a row with room when the toolbar no longer fits there.</summary>
    Private Shared Function SavedSpot(panel As ToolStripPanel, row As Integer, ts As ToolStrip) As Point
        If row >= panel.Rows.Length OrElse RoomAlong(panel) <= 0 OrElse FitsAtEndOfRow(panel, row, ts) Then Return EndOfRow(panel, row)
        Return EndOfRoomyRow(panel, ts)
    End Function

    Private Shared Function RoomAlong(panel As ToolStripPanel) As Integer
        Return If(panel.Orientation = Orientation.Horizontal, panel.Width, panel.Height)
    End Function

    Private Shared Function FitsAtEndOfRow(panel As ToolStripPanel, row As Integer, ts As ToolStrip) As Boolean
        Dim spot = EndOfRow(panel, row)
        Return If(panel.Orientation = Orientation.Horizontal, spot.X, spot.Y) + ToolbarLength(ts) <= RoomAlong(panel)
    End Function

    ''' <summary>A toolbar's length along its buttons, whichever way it stands now (it turns when it joins a side).</summary>
    Private Shared Function ToolbarLength(ts As ToolStrip) As Integer
        Return If(ts.Orientation = Orientation.Horizontal, ts.PreferredSize.Width, ts.PreferredSize.Height)
    End Function

    ''' <summary>Docks a toolbar on an edge of the view (at the end of a row with room unless a spot is given).</summary>
    Private Sub DockToolbar(ts As ToolStrip, place As String, Optional at As Point? = Nothing)
        UnfloatToolbar(ts)
        Dim panel = PanelOf(place)
        If ts.Parent IsNot Nothing Then ts.Parent.Controls.Remove(ts)
        ts.Visible = IsToolbarVisible(ts)
        panel.Join(ts, If(at, EndOfRoomyRow(panel, ts)))
        _lastDock(ts) = place
        SaveToolbarLayout()
    End Sub

    ''' <summary>Floats a toolbar in its own small window at a screen point (kept on a screen).</summary>
    Private Sub FloatToolbar(ts As ToolStrip, screenPoint As Point)
        If Not _floatingToolbars.ContainsKey(ts) Then
            If ts.Parent IsNot Nothing Then _lastDock(ts) = PlaceOf(ts)
            Dim w As New ToolbarWindow(ts) With {.Owner = Me}
            AddHandler w.HiddenByUser, Sub(o, ev) SetToolbarVisible(ts, False)
            AddHandler w.DockRequested, Sub(o, ev)
                                            Dim place As String = Nothing
                                            DockToolbar(ts, If(_lastDock.TryGetValue(ts, place), place, "Top"))
                                        End Sub
            AddHandler w.ResizeEnd, Sub(o, ev) SaveToolbarLayout()
            _floatingToolbars(ts) = w
        End If
        Dim win = _floatingToolbars(ts)
        ts.Visible = True
        win.PerformLayout()
        ' On a screen: at least the title bar must stay reachable.
        Dim area = Screen.FromPoint(screenPoint).WorkingArea
        win.Location = New Point(Math.Max(area.Left, Math.Min(screenPoint.X, area.Right - win.Width)),
                                 Math.Max(area.Top, Math.Min(screenPoint.Y, area.Bottom - win.Height)))
        If IsToolbarVisible(ts) AndAlso Me.Visible Then win.Show()
        SaveToolbarLayout()
    End Sub

    ''' <summary>Takes a toolbar out of its floating window (the window goes away).</summary>
    Private Sub UnfloatToolbar(ts As ToolStrip)
        Dim w As ToolbarWindow = Nothing
        If Not _floatingToolbars.TryGetValue(ts, w) Then Return
        _floatingToolbars.Remove(ts)
        w.Release()
        w.Dispose()
    End Sub

    ''' <summary>Floating toolbars restored during Load appear with the main window.</summary>
    Private Sub ShowFloatingToolbars()
        For Each kv In _floatingToolbars
            If IsToolbarVisible(kv.Key) Then kv.Value.Show()
        Next
    End Sub

    ''' <summary>A grip drag that ends away from the view floats the toolbar there.</summary>
    Private Sub Toolbar_EndDrag(sender As Object, e As EventArgs)
        Dim ts = DirectCast(sender, ToolStrip)
        Dim area = pnlView.RectangleToScreen(pnlView.ClientRectangle)
        area.Inflate(24, 24)
        If Not area.Contains(MousePosition) Then
            FloatToolbar(ts, New Point(MousePosition.X - 24, MousePosition.Y - 12))
        Else
            _lastDock(ts) = PlaceOf(ts)
            SaveToolbarLayout()
        End If
    End Sub

    ''' <summary>All toolbars shown and docked along the top in their usual order.</summary>
    Private Sub ResetToolbars()
        _restoringToolbars = True
        Try
            For Each ts In ViewToolbars
                UnfloatToolbar(ts)
                If ts.Parent IsNot Nothing Then ts.Parent.Controls.Remove(ts)
            Next
            _hiddenToolbars.Clear()
            _lastDock.Clear()
            For Each ts In ViewToolbars
                DockToolbar(ts, "Top")
            Next
        Finally
            _restoringToolbars = False
        End Try
        SaveToolbarLayout()
    End Sub

    ''' <summary>Puts the toolbars where the preferences say (new ones along the top).</summary>
    Private Sub RestoreToolbars()
        _restoringToolbars = True
        Try
            Dim placed As New HashSet(Of ToolStrip)
            ' Row by row, left to right: each one joins the end of its row, so the order holds whatever the widths.
            For Each st In _prefs.Toolbars.OrderBy(Function(t) t.Place).ThenBy(Function(t) t.Row).ThenBy(Function(t) t.Order).ThenBy(Function(t) t.X)
                Dim ts = ViewToolbars.FirstOrDefault(Function(t) t.Name = st.Name)
                If ts Is Nothing OrElse placed.Contains(ts) Then Continue For
                placed.Add(ts)
                If Not st.Visible Then _hiddenToolbars.Add(ts)
                If st.Place = "Float" AndAlso {"Top", "Bottom", "Left", "Right"}.Contains(st.LastDock) Then _lastDock(ts) = st.LastDock
                Select Case st.Place
                    Case "Float" : FloatToolbar(ts, New Point(st.X, st.Y))
                    Case "Top", "Bottom", "Left", "Right" : DockToolbar(ts, st.Place, SavedSpot(PanelOf(st.Place), Math.Max(0, st.Row), ts))
                    Case Else : DockToolbar(ts, "Top")
                End Select
            Next
            For Each ts In ViewToolbars
                If Not placed.Contains(ts) Then DockToolbar(ts, "Top")
            Next
        Finally
            _restoringToolbars = False
        End Try
    End Sub

    ''' <summary>Which row of its panel a docked toolbar is in, and how many come before it in that row.</summary>
    Private Shared Sub RowAndOrder(ts As ToolStrip, ByRef row As Integer, ByRef order As Integer)
        row = 0 : order = 0
        Dim panel = TryCast(ts.Parent, ToolStripPanel)
        If panel Is Nothing Then Return
        For r = 0 To panel.Rows.Length - 1
            Dim inRow = panel.Rows(r).Controls.ToList()
            If Not inRow.Contains(ts) Then Continue For
            row = r
            Dim horizontal = panel.Orientation = Orientation.Horizontal
            order = inRow.Where(Function(c) If(horizontal, c.Left < ts.Left, c.Top < ts.Top)).Count()
            Return
        Next
    End Sub

    ''' <summary>Remembers where every toolbar is.</summary>
    Private Sub SaveToolbarLayout()
        If _restoringToolbars OrElse _prefs Is Nothing Then Return
        _prefs.Toolbars = ViewToolbars.Select(Function(ts)
                                                  Dim p As New ToolbarPlacement With {.Name = ts.Name, .Place = PlaceOf(ts), .Visible = IsToolbarVisible(ts)}
                                                  Dim w As ToolbarWindow = Nothing
                                                  If _floatingToolbars.TryGetValue(ts, w) Then
                                                      p.X = w.Location.X : p.Y = w.Location.Y
                                                      Dim last As String = Nothing
                                                      If _lastDock.TryGetValue(ts, last) Then p.LastDock = last
                                                  Else
                                                      RowAndOrder(ts, p.Row, p.Order)
                                                  End If
                                                  Return p
                                              End Function).ToList()
        _prefs.Save()
    End Sub
End Class

''' <summary>
''' A floating toolbar: a small tool window holding one toolbar. Closing it hides the
''' toolbar; double-clicking its title docks it again. It never takes the focus from
''' the main window for a button click, so a tool or style click works the first time
''' and the keyboard stays with the view or the text being typed. The lists (font,
''' grid, rotation) do get the keyboard while open and hand it back when closed.
''' </summary>
Friend Class ToolbarWindow
    Inherits Form

    Private Const WM_NCLBUTTONDBLCLK As Integer = &HA3
    Private _strip As ToolStrip
    Private _clickThrough As ClickThrough
    Private ReadOnly _combos As New List(Of ToolStripComboBox)

    ''' <summary>Turns "activate and eat the click" into "no activation" for the toolbar's buttons.</summary>
    Private NotInheritable Class ClickThrough
        Inherits NativeWindow
        Private Const WM_MOUSEACTIVATE As Integer = &H21
        Private Const MA_NOACTIVATE As Integer = 3
        Private ReadOnly _strip As ToolStrip

        Public Sub New(strip As ToolStrip)
            _strip = strip
            AssignHandle(strip.Handle)
        End Sub

        Protected Overrides Sub WndProc(ByRef m As Message)
            If m.Msg = WM_MOUSEACTIVATE Then
                ' Lists take the keyboard (typing a font name), so they activate as usual.
                Dim item = _strip.GetItemAt(_strip.PointToClient(Control.MousePosition))
                If Not TypeOf item Is ToolStripControlHost Then
                    m.Result = New IntPtr(MA_NOACTIVATE)
                    Return
                End If
            End If
            MyBase.WndProc(m)
        End Sub
    End Class

    Protected Overrides ReadOnly Property ShowWithoutActivation As Boolean
        Get
            Return True
        End Get
    End Property

    ''' <summary>The user closed the window (the toolbar is hidden, not lost).</summary>
    Public Event HiddenByUser As EventHandler
    ''' <summary>Double-click on the title: put the toolbar back where it was docked.</summary>
    Public Event DockRequested As EventHandler

    Public Sub New(strip As ToolStrip)
        _strip = strip
        Text = strip.Text
        FormBorderStyle = FormBorderStyle.FixedToolWindow
        ShowInTaskbar = False
        StartPosition = FormStartPosition.Manual
        MaximizeBox = False
        MinimizeBox = False
        AutoSize = True
        AutoSizeMode = AutoSizeMode.GrowAndShrink
        If strip.Parent IsNot Nothing Then strip.Parent.Controls.Remove(strip)
        strip.GripStyle = ToolStripGripStyle.Hidden
        strip.Dock = DockStyle.None
        strip.LayoutStyle = ToolStripLayoutStyle.HorizontalStackWithOverflow
        strip.Location = Point.Empty
        Controls.Add(strip)
        _clickThrough = New ClickThrough(strip)
        BackColor = Skin.Window
        Skin.DarkTitleBar(Me)
        For Each combo In strip.Items.OfType(Of ToolStripComboBox)()
            _combos.Add(combo)
            AddHandler combo.DropDownClosed, AddressOf ReturnFocus
            AddHandler combo.KeyDown, AddressOf Combo_KeyDown
        Next
    End Sub

    ''' <summary>After a list is used, the main window (and the view or text editor in it) gets the keyboard back.</summary>
    Private Sub ReturnFocus(sender As Object, e As EventArgs)
        If IsDisposed OrElse Owner Is Nothing Then Return
        BeginInvoke(Sub() If Owner IsNot Nothing AndAlso Not Owner.IsDisposed Then Owner.Activate())
    End Sub

    Private Sub Combo_KeyDown(sender As Object, e As KeyEventArgs)
        If e.KeyCode = Keys.Enter OrElse e.KeyCode = Keys.Escape Then ReturnFocus(sender, e)
    End Sub

    ''' <summary>A move of the window by its title bar leaves the keyboard with the main window.</summary>
    Protected Overrides Sub OnResizeEnd(e As EventArgs)
        MyBase.OnResizeEnd(e)
        ReturnFocus(Me, e)
    End Sub

    ''' <summary>Hands the toolbar back (before it is docked again).</summary>
    Public Sub Release()
        If _strip Is Nothing Then Return
        For Each combo In _combos
            RemoveHandler combo.DropDownClosed, AddressOf ReturnFocus
            RemoveHandler combo.KeyDown, AddressOf Combo_KeyDown
        Next
        _combos.Clear()
        _clickThrough?.ReleaseHandle()
        _clickThrough = Nothing
        Controls.Remove(_strip)
        _strip.GripStyle = ToolStripGripStyle.Visible
        _strip.LayoutStyle = ToolStripLayoutStyle.StackWithOverflow
        _strip = Nothing
    End Sub

    ''' <summary>Menu shortcuts of the main window work here too (as in Machine Settings).</summary>
    Protected Overrides Function ProcessCmdKey(ByRef msg As Message, keyData As Keys) As Boolean
        If MyBase.ProcessCmdKey(msg, keyData) Then Return True
        Dim item = frmMachineSettings.FindShortcut(Owner?.MainMenuStrip?.Items, keyData)
        If item Is Nothing Then Return False
        item.PerformClick()
        Return True
    End Function

    Protected Overrides Sub WndProc(ByRef m As Message)
        If m.Msg = WM_NCLBUTTONDBLCLK Then
            RaiseEvent DockRequested(Me, EventArgs.Empty)
            Return
        End If
        MyBase.WndProc(m)
    End Sub

    Protected Overrides Sub OnFormClosing(e As FormClosingEventArgs)
        If e.CloseReason = CloseReason.UserClosing Then
            e.Cancel = True
            Hide()
            RaiseEvent HiddenByUser(Me, EventArgs.Empty)
        End If
        MyBase.OnFormClosing(e)
    End Sub
End Class
