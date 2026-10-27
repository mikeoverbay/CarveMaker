' ============================================================================
'  frmMain.Skin.vb
'  The dark look on the main window (Skin.vb): colours for everything in it,
'  the big Code Chop style buttons on every toolbar, the icons on buttons and
'  menus, and the Project toolbar, whose buttons do what the File, Toolpath
'  and Machine menu items do (and are enabled when those are).
' ============================================================================

Partial Class frmMain

    Private tsProject As ToolStrip
    ' The View toolbar's switch shows the board (simulation) or toolpath lines.
    Private _simIcon, _pathIcon As Image
    Private _playIcon, _pauseIcon As Image

    ''' <summary>Dark colours, big buttons and icons (from SetupDrawing, before the toolbars are placed).</summary>
    Private Sub SetupSkin()
        ' Context menus (the toolbars' right-click menu, the grids') take the skin too.
        ToolStripManager.Renderer = Skin.Renderer
        CreateProjectToolbar()
        Skin.Apply(Me)
        pnlView.BackColor = Skin.Window
        For Each tsp In {tspTop, tspBottom, tspLeft, tspRight}
            tsp.BackColor = Skin.Window
        Next

        ' Pictures instead of letters, before the buttons are sized.
        tsbDrawBold.DisplayStyle = ToolStripItemDisplayStyle.Image
        tsbDrawItalic.DisplayStyle = ToolStripItemDisplayStyle.Image
        For Each b In {tsbObjImport, tsbObjImportModel, tsbObjRemove, tsbObjFit, tsbObjCenter, tsbSimPlay, tsbSimReset}
            b.DisplayStyle = ToolStripItemDisplayStyle.ImageAndText
        Next
        ' Pictures that change with the state are made first, so their buttons are sized with them.
        Dim px = Skin.Scaled(20, tsView)
        _simIcon = Skin.Icon(IconKind.Simulation, px)
        _pathIcon = Skin.Icon(IconKind.Toolpath, px)
        _playIcon = Skin.Icon(IconKind.Play, px)
        _pauseIcon = Skin.Icon(IconKind.Pause, px)
        tsbSimToggle.Image = _pathIcon
        tsbSimToggle.Text = "Toolpath"
        tsbSimPlay.Image = _playIcon
        Dim simBarBefore = tsSim.PreferredSize.Height
        For Each ts In {tsProject, tsDrawTools, tsTextStyle, tsSnap, tsView, tsObjects, tsFormat, tsSim}
            Skin.StyleToolbar(ts)
        Next
        ' The side panel's bars wrap onto a second row rather than hide buttons behind a chevron.
        tsFormat.LayoutStyle = ToolStripLayoutStyle.Flow
        tsObjects.LayoutStyle = ToolStripLayoutStyle.Flow
        ' The simulation bar grew: the panel under the view grows with it, so the slider keeps its height.
        pnlSim.Height += Math.Max(0, tsSim.PreferredSize.Height - simBarBefore)
        ' Toggles: round, and yellow while on.
        Skin.MarkToggle(tsbToolSelect, tsbToolRectangle, tsbToolEllipse, tsbToolPolygon, tsbToolStar, tsbToolArea, tsbToolText,
                        tsbDrawBold, tsbDrawItalic, tsbDrawAlignLeft, tsbDrawAlignCenter, tsbDrawAlignRight,
                        tsbSizeLarge, tsbSizeMedium, tsbSizeSmall, tsbAlignLeft, tsbAlignCenter, tsbAlignRight)

        ' Toolbar icons.
        Skin.SetIcon(tsbToolSelect, IconKind.SelectArrow)
        Skin.SetIcon(tsbToolRectangle, IconKind.Rectangle)
        Skin.SetIcon(tsbToolEllipse, IconKind.Ellipse)
        Skin.SetIcon(tsbToolPolygon, IconKind.Polygon)
        Skin.SetIcon(tsbToolStar, IconKind.Star)
        Skin.SetIcon(tsbToolArea, IconKind.Area)
        Skin.SetIcon(tsbToolText, IconKind.Text)
        Skin.SetIcon(tsbDrawBold, IconKind.Bold)
        Skin.SetIcon(tsbDrawItalic, IconKind.Italic)
        Skin.SetIcon(tsbDrawAlignLeft, IconKind.AlignLeft)
        Skin.SetIcon(tsbDrawAlignCenter, IconKind.AlignCenter)
        Skin.SetIcon(tsbDrawAlignRight, IconKind.AlignRight)
        Skin.SetIcon(tsbSnapGrid, IconKind.SnapGrid)
        Skin.SetIcon(tsbHide, IconKind.Hide)
        Skin.SetIcon(tsbShowAll, IconKind.ShowAll)
        Skin.SetIcon(tsbObjImport, IconKind.ImportSvg)
        Skin.SetIcon(tsbObjImportModel, IconKind.Import3D)
        Skin.SetIcon(tsbObjRemove, IconKind.Remove)
        Skin.SetIcon(tsbObjFit, IconKind.FitToBlank)
        Skin.SetIcon(tsbObjCenter, IconKind.Center)
        Skin.SetIcon(tsbSimReset, IconKind.Rewind)
        For Each item As ToolStripItem In tsProject.Items
            If TypeOf item.Tag Is IconKind Then Skin.SetIcon(item, DirectCast(item.Tag, IconKind))
        Next

        ' Menu icons (16 pixels at 100 %).
        Skin.SetIcon(mnuFileNew, IconKind.NewFile)
        Skin.SetIcon(mnuFileOpenProject, IconKind.Open)
        Skin.SetIcon(mnuFileSaveProject, IconKind.Save)
        Skin.SetIcon(mnuFileImportSvg, IconKind.ImportSvg)
        Skin.SetIcon(mnuFileImportModel, IconKind.Import3D)
        Skin.SetIcon(mnuFileSaveGcode, IconKind.Export)
        Skin.SetIcon(mnuDrawSelect, IconKind.SelectArrow)
        Skin.SetIcon(mnuDrawRectangle, IconKind.Rectangle)
        Skin.SetIcon(mnuDrawEllipse, IconKind.Ellipse)
        Skin.SetIcon(mnuDrawPolygon, IconKind.Polygon)
        Skin.SetIcon(mnuDrawStar, IconKind.Star)
        Skin.SetIcon(mnuDrawArea, IconKind.Area)
        Skin.SetIcon(mnuDrawText, IconKind.Text)
        Skin.SetIcon(mnuDrawRotateLeft, IconKind.Rotate)
        Skin.SetIcon(mnuDrawMirror, IconKind.Mirror)
        Skin.SetIcon(mnuDrawScale, IconKind.Scale)
        Skin.SetIcon(mnuDrawHide, IconKind.Hide)
        Skin.SetIcon(mnuDrawShowAll, IconKind.ShowAll)
        Skin.SetIcon(mnuDrawDelete, IconKind.Remove)
        Skin.SetIcon(mnuDrawSnapGrid, IconKind.SnapGrid)
        Skin.SetIcon(mnuStyleBold, IconKind.Bold)
        Skin.SetIcon(mnuStyleItalic, IconKind.Italic)
        Skin.SetIcon(mnuStyleAlignLeft, IconKind.AlignLeft)
        Skin.SetIcon(mnuStyleAlignCenter, IconKind.AlignCenter)
        Skin.SetIcon(mnuStyleAlignRight, IconKind.AlignRight)
        Skin.SetIcon(mnuViewSim, IconKind.Simulation)
        Skin.SetIcon(mnuToolpathGenerate, IconKind.Generate)
        Skin.SetIcon(_mnuToolpathAbort, IconKind.StopGenerate)
        Skin.SetIcon(mnuToolLibrary, IconKind.ToolLibrary)
        Skin.SetIcon(mnuMachineSettings, IconKind.Machine)
    End Sub

    ''' <summary>
    ''' The Project toolbar: New, Open, Save | Generate, Stop, Export G-code | Tool Library,
    ''' Machine Settings. Each button clicks its menu item and follows its enabled state.
    ''' </summary>
    Private Sub CreateProjectToolbar()
        tsProject = New ToolStrip() With {.Name = "tsProject", .Text = "Project", .Dock = DockStyle.None, .GripStyle = ToolStripGripStyle.Visible}
        tsProject.Items.AddRange(New ToolStripItem() {
            MenuButton(mnuFileNew, IconKind.NewFile, "tsbProjNew", "New project"),
            MenuButton(mnuFileOpenProject, IconKind.Open, "tsbProjOpen", "Open a project"),
            MenuButton(mnuFileSaveProject, IconKind.Save, "tsbProjSave", "Save the project"),
            New ToolStripSeparator(),
            MenuButton(mnuToolpathGenerate, IconKind.Generate, "tsbProjGenerate", "Generate the toolpath now"),
            MenuButton(_mnuToolpathAbort, IconKind.StopGenerate, "tsbProjStop", "Stop generating (the last finished toolpath stays)"),
            MenuButton(mnuFileSaveGcode, IconKind.Export, "tsbProjExport", "Export G-code"),
            New ToolStripSeparator(),
            MenuButton(mnuToolLibrary, IconKind.ToolLibrary, "tsbProjTools", "Tool library"),
            MenuButton(mnuMachineSettings, IconKind.Machine, "tsbProjMachine", "Machine settings: blank, stock, machine")})
    End Sub

    ''' <summary>A toolbar button that does what a menu item does, enabled when it is.</summary>
    Private Shared Function MenuButton(menu As ToolStripMenuItem, kind As IconKind, name As String, tip As String) As ToolStripButton
        Dim b As New ToolStripButton With {.Name = name, .DisplayStyle = ToolStripItemDisplayStyle.Image, .Text = tip, .Tag = kind, .Enabled = menu.Enabled}
        b.ToolTipText = tip & ShortcutSuffix(menu)
        AddHandler b.Click, Sub(s, e) menu.PerformClick()
        AddHandler menu.EnabledChanged, Sub(s, e) b.Enabled = menu.Enabled
        Return b
    End Function

    ''' <summary>" (Ctrl+S)" for a menu item with a shortcut.</summary>
    Private Shared Function ShortcutSuffix(menu As ToolStripMenuItem) As String
        If Not String.IsNullOrEmpty(menu.ShortcutKeyDisplayString) Then Return " (" & menu.ShortcutKeyDisplayString & ")"
        If menu.ShortcutKeys = Keys.None Then Return ""
        Return " (" & New KeysConverter().ConvertToString(menu.ShortcutKeys) & ")"
    End Function
End Class
