<Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()>
Partial Class frmMain
    Inherits System.Windows.Forms.Form

    'Form overrides dispose to clean up the component list.
    <System.Diagnostics.DebuggerNonUserCode()>
    Protected Overrides Sub Dispose(disposing As Boolean)
        Try
            If disposing AndAlso components IsNot Nothing Then
                components.Dispose()
            End If
        Finally
            MyBase.Dispose(disposing)
        End Try
    End Sub

    'Required by the Windows Form Designer
    Private components As System.ComponentModel.IContainer

    'NOTE: The following procedure is required by the Windows Form Designer
    'It can be modified using the Windows Form Designer.
    'Do not modify it using the code editor.
    <System.Diagnostics.DebuggerStepThrough()>
    Private Sub InitializeComponent()
        components = New System.ComponentModel.Container()
        mnuMain = New MenuStrip()
        mnuFile = New ToolStripMenuItem()
        mnuFileNew = New ToolStripMenuItem()
        mnuFileOpenProject = New ToolStripMenuItem()
        mnuFileSaveProject = New ToolStripMenuItem()
        mnuFileSaveProjectAs = New ToolStripMenuItem()
        mnuFileSep0 = New ToolStripSeparator()
        mnuFileImportText = New ToolStripMenuItem()
        mnuFileImportSvg = New ToolStripMenuItem()
        mnuFileImportModel = New ToolStripMenuItem()
        mnuFileSaveGcode = New ToolStripMenuItem()
        mnuFileSep1 = New ToolStripSeparator()
        mnuFileExit = New ToolStripMenuItem()
        mnuFont = New ToolStripMenuItem()
        mnuFontChoose = New ToolStripMenuItem()
        mnuView = New ToolStripMenuItem()
        mnuViewTop = New ToolStripMenuItem()
        mnuViewIso = New ToolStripMenuItem()
        mnuViewFit = New ToolStripMenuItem()
        mnuViewSep1 = New ToolStripSeparator()
        mnuViewRapids = New ToolStripMenuItem()
        mnuViewOutline = New ToolStripMenuItem()
        mnuViewGrid = New ToolStripMenuItem()
        mnuViewSep2 = New ToolStripSeparator()
        mnuViewSim = New ToolStripMenuItem()
        mnuToolpath = New ToolStripMenuItem()
        mnuMachine = New ToolStripMenuItem()
        mnuMachineSettings = New ToolStripMenuItem()
        mnuToolpathGenerate = New ToolStripMenuItem()
        mnuToolpathAuto = New ToolStripMenuItem()
        mnuToolpathSep1 = New ToolStripSeparator()
        mnuToolLibrary = New ToolStripMenuItem()
        mnuDraw = New ToolStripMenuItem()
        mnuStyle = New ToolStripMenuItem()
        mnuDrawSelect = New ToolStripMenuItem()
        mnuDrawSep0 = New ToolStripSeparator()
        mnuDrawRectangle = New ToolStripMenuItem()
        mnuDrawEllipse = New ToolStripMenuItem()
        mnuDrawPolygon = New ToolStripMenuItem()
        mnuDrawStar = New ToolStripMenuItem()
        mnuDrawArea = New ToolStripMenuItem()
        mnuDrawText = New ToolStripMenuItem()
        mnuDrawSep1 = New ToolStripSeparator()
        mnuDrawEditText = New ToolStripMenuItem()
        mnuDrawSep2 = New ToolStripSeparator()
        mnuDrawRotateLeft = New ToolStripMenuItem()
        mnuDrawRotateRight = New ToolStripMenuItem()
        mnuDrawMirror = New ToolStripMenuItem()
        mnuDrawScale = New ToolStripMenuItem()
        mnuDrawFront = New ToolStripMenuItem()
        mnuDrawBack = New ToolStripMenuItem()
        mnuDrawSep3 = New ToolStripSeparator()
        mnuDrawDelete = New ToolStripMenuItem()
        mnuStyleFont = New ToolStripMenuItem()
        mnuStyleBold = New ToolStripMenuItem()
        mnuStyleItalic = New ToolStripMenuItem()
        mnuStyleSep1 = New ToolStripSeparator()
        mnuStyleAlignLeft = New ToolStripMenuItem()
        mnuStyleAlignCenter = New ToolStripMenuItem()
        mnuStyleAlignRight = New ToolStripMenuItem()
        tsDrawTools = New ToolStrip()
        tsTextStyle = New ToolStrip()
        tsSnap = New ToolStrip()
        tspTop = New ToolStripPanel()
        tspBottom = New ToolStripPanel()
        tspLeft = New ToolStripPanel()
        tspRight = New ToolStripPanel()
        mnuViewSep3 = New ToolStripSeparator()
        mnuViewToolbars = New ToolStripMenuItem()
        tsbToolSelect = New ToolStripButton()
        tsbToolRectangle = New ToolStripButton()
        tsbToolEllipse = New ToolStripButton()
        tsbToolPolygon = New ToolStripButton()
        tsbToolStar = New ToolStripButton()
        tsbToolArea = New ToolStripButton()
        tsbToolText = New ToolStripButton()
        tslDrawFont = New ToolStripLabel()
        tscDrawFont = New ToolStripComboBox()
        tsbDrawBold = New ToolStripButton()
        tsbDrawItalic = New ToolStripButton()
        tssDraw2 = New ToolStripSeparator()
        tsbDrawAlignLeft = New ToolStripButton()
        tsbDrawAlignCenter = New ToolStripButton()
        tsbDrawAlignRight = New ToolStripButton()
        mnuDrawSep4 = New ToolStripSeparator()
        mnuDrawSnapGrid = New ToolStripMenuItem()
        mnuDrawGridSpacing = New ToolStripMenuItem()
        mnuDrawRotationSnap = New ToolStripMenuItem()
        tsbSnapGrid = New ToolStripButton()
        tscSnapGrid = New ToolStripComboBox()
        tslSnapRot = New ToolStripLabel()
        tscSnapRot = New ToolStripComboBox()
        mnuHelp = New ToolStripMenuItem()
        mnuHelpGuide = New ToolStripMenuItem()
        mnuHelpReleases = New ToolStripMenuItem()
        mnuHelpSep1 = New ToolStripSeparator()
        mnuHelpAbout = New ToolStripMenuItem()
        stsMain = New StatusStrip()
        lblStatus = New ToolStripStatusLabel()
        lblStats = New ToolStripStatusLabel()
        splitMain = New SplitContainer()
        tlpLeft = New TableLayoutPanel()
        pnlEditor = New Panel()
        tabLeft = New TabControl()
        tabText = New TabPage()
        tabObjects = New TabPage()
        tsObjects = New ToolStrip()
        tsbObjImport = New ToolStripButton()
        tsbObjImportModel = New ToolStripButton()
        tsbObjRemove = New ToolStripButton()
        tsbObjFit = New ToolStripButton()
        tsbObjCenter = New ToolStripButton()
        lstObjects = New ListBox()
        pgObject = New PropertyGrid()
        rtbText = New RichTextBox()
        tsFormat = New ToolStrip()
        tslSize = New ToolStripLabel()
        tsbSizeLarge = New ToolStripButton()
        tsbSizeMedium = New ToolStripButton()
        tsbSizeSmall = New ToolStripButton()
        tssSep1 = New ToolStripSeparator()
        tslAlign = New ToolStripLabel()
        tsbAlignLeft = New ToolStripButton()
        tsbAlignCenter = New ToolStripButton()
        tsbAlignRight = New ToolStripButton()
        splitText = New SplitContainer()
        pgText = New PropertyGrid()
        pnlView = New Panel()
        pnlSim = New Panel()
        tbSim = New TrackBar()
        tsSim = New ToolStrip()
        tsbSimPlay = New ToolStripButton()
        tsbSimReset = New ToolStripButton()
        tslSimSpeed = New ToolStripLabel()
        tscSimSpeed = New ToolStripComboBox()
        tslSimTime = New ToolStripLabel()
        tssSimInfo = New ToolStripSeparator()
        tslSimInfo = New ToolStripLabel()
        tmrRegen = New Timer(components)
        dlgFont = New FontDialog()
        dlgSave = New SaveFileDialog()
        dlgOpen = New OpenFileDialog()
        dlgOpenProject = New OpenFileDialog()
        dlgSaveProject = New SaveFileDialog()
        dlgSvg = New OpenFileDialog()
        dlgModel = New OpenFileDialog()
        mnuMain.SuspendLayout()
        stsMain.SuspendLayout()
        CType(splitMain, System.ComponentModel.ISupportInitialize).BeginInit()
        splitMain.Panel1.SuspendLayout()
        splitMain.Panel2.SuspendLayout()
        pnlView.SuspendLayout()
        pnlSim.SuspendLayout()
        CType(tbSim, System.ComponentModel.ISupportInitialize).BeginInit()
        tsSim.SuspendLayout()
        tsDrawTools.SuspendLayout()
        tsTextStyle.SuspendLayout()
        tsSnap.SuspendLayout()
        splitMain.SuspendLayout()
        tlpLeft.SuspendLayout()
        pnlEditor.SuspendLayout()
        tabLeft.SuspendLayout()
        tabText.SuspendLayout()
        CType(splitText, System.ComponentModel.ISupportInitialize).BeginInit()
        splitText.Panel1.SuspendLayout()
        splitText.Panel2.SuspendLayout()
        splitText.SuspendLayout()
        tabObjects.SuspendLayout()
        tsObjects.SuspendLayout()
        tsFormat.SuspendLayout()
        SuspendLayout()
        '
        ' mnuMain
        '
        mnuMain.Items.AddRange(New ToolStripItem() {mnuFile, mnuFont, mnuDraw, mnuStyle, mnuView, mnuToolpath, mnuMachine, mnuHelp})
        mnuMain.Location = New Point(0, 0)
        mnuMain.Name = "mnuMain"
        mnuMain.Size = New Size(1200, 24)
        mnuMain.TabIndex = 0
        mnuMain.Text = "mnuMain"
        '
        ' mnuFile
        '
        mnuFile.DropDownItems.AddRange(New ToolStripItem() {mnuFileNew, mnuFileOpenProject, mnuFileSaveProject, mnuFileSaveProjectAs, mnuFileSep0, mnuFileImportText, mnuFileImportSvg, mnuFileImportModel, mnuFileSaveGcode, mnuFileSep1, mnuFileExit})
        mnuFile.Name = "mnuFile"
        mnuFile.Size = New Size(37, 20)
        mnuFile.Text = "&File"
        ' 
        ' mnuFileNew
        ' 
        mnuFileNew.Name = "mnuFileNew"
        mnuFileNew.ShortcutKeys = Keys.Control Or Keys.N
        mnuFileNew.Size = New Size(230, 22)
        mnuFileNew.Text = "&New Project"
        ' 
        ' mnuFileOpenProject
        ' 
        mnuFileOpenProject.Name = "mnuFileOpenProject"
        mnuFileOpenProject.ShortcutKeys = Keys.Control Or Keys.O
        mnuFileOpenProject.Size = New Size(230, 22)
        mnuFileOpenProject.Text = "&Open Project..."
        ' 
        ' mnuFileSaveProject
        ' 
        mnuFileSaveProject.Name = "mnuFileSaveProject"
        mnuFileSaveProject.ShortcutKeys = Keys.Control Or Keys.S
        mnuFileSaveProject.Size = New Size(230, 22)
        mnuFileSaveProject.Text = "&Save Project"
        ' 
        ' mnuFileSaveProjectAs
        ' 
        mnuFileSaveProjectAs.Name = "mnuFileSaveProjectAs"
        mnuFileSaveProjectAs.Size = New Size(230, 22)
        mnuFileSaveProjectAs.Text = "Save Project &As..."
        ' 
        ' mnuFileSep0
        ' 
        mnuFileSep0.Name = "mnuFileSep0"
        mnuFileSep0.Size = New Size(227, 6)
        ' 
        ' mnuFileImportText
        ' 
        mnuFileImportText.Name = "mnuFileImportText"
        mnuFileImportText.Size = New Size(230, 22)
        mnuFileImportText.Text = "&Import Text (.txt / .rtf)..."
        ' 
        ' mnuFileImportSvg
        ' 
        mnuFileImportSvg.Name = "mnuFileImportSvg"
        mnuFileImportSvg.Size = New Size(230, 22)
        mnuFileImportSvg.Text = "Import SVG &Drawing..."
        '
        ' mnuFileImportModel
        '
        mnuFileImportModel.Name = "mnuFileImportModel"
        mnuFileImportModel.ShortcutKeys = Keys.Control Or Keys.Shift Or Keys.I
        mnuFileImportModel.Size = New Size(230, 22)
        mnuFileImportModel.Text = "Import 3D &Model / Depth Image..."
        ' 
        ' mnuFileSaveGcode
        ' 
        mnuFileSaveGcode.Name = "mnuFileSaveGcode"
        mnuFileSaveGcode.ShortcutKeys = Keys.Control Or Keys.G
        mnuFileSaveGcode.Size = New Size(230, 22)
        mnuFileSaveGcode.Text = "Export &G-code..."
        '
        ' mnuFileSep1
        '
        mnuFileSep1.Name = "mnuFileSep1"
        mnuFileSep1.Size = New Size(227, 6)
        '
        ' mnuFileExit
        '
        mnuFileExit.Name = "mnuFileExit"
        mnuFileExit.Size = New Size(230, 22)
        mnuFileExit.Text = "E&xit"
        '
        ' mnuFont
        '
        mnuFont.DropDownItems.AddRange(New ToolStripItem() {mnuFontChoose})
        mnuFont.Name = "mnuFont"
        mnuFont.Size = New Size(43, 20)
        mnuFont.Text = "F&ont"
        '
        ' mnuFontChoose
        '
        mnuFontChoose.Name = "mnuFontChoose"
        mnuFontChoose.ShortcutKeys = Keys.Control Or Keys.F
        mnuFontChoose.Size = New Size(200, 22)
        mnuFontChoose.Text = "&Choose Font for Current Line's Size..."
        '
        ' mnuView
        '
        mnuView.DropDownItems.AddRange(New ToolStripItem() {mnuViewTop, mnuViewIso, mnuViewFit, mnuViewSep1, mnuViewRapids, mnuViewOutline, mnuViewGrid, mnuViewSep2, mnuViewSim, mnuViewSep3, mnuViewToolbars})
        mnuView.Name = "mnuView"
        mnuView.Size = New Size(44, 20)
        mnuView.Text = "&View"
        '
        ' mnuViewTop
        '
        mnuViewTop.Name = "mnuViewTop"
        mnuViewTop.ShortcutKeys = Keys.Control Or Keys.T
        mnuViewTop.Size = New Size(200, 22)
        mnuViewTop.Text = "&Top View"
        '
        ' mnuViewIso
        '
        mnuViewIso.Name = "mnuViewIso"
        mnuViewIso.ShortcutKeys = Keys.Control Or Keys.I
        mnuViewIso.Size = New Size(200, 22)
        mnuViewIso.Text = "&Isometric View"
        '
        ' mnuViewFit
        '
        mnuViewFit.Name = "mnuViewFit"
        mnuViewFit.ShortcutKeys = Keys.Control Or Keys.E
        mnuViewFit.Size = New Size(200, 22)
        mnuViewFit.Text = "Zoom to &Fit"
        '
        ' mnuViewSep1
        '
        mnuViewSep1.Name = "mnuViewSep1"
        mnuViewSep1.Size = New Size(197, 6)
        '
        ' mnuViewRapids
        '
        mnuViewRapids.Checked = True
        mnuViewRapids.CheckOnClick = True
        mnuViewRapids.CheckState = CheckState.Checked
        mnuViewRapids.Name = "mnuViewRapids"
        mnuViewRapids.Size = New Size(200, 22)
        mnuViewRapids.Text = "Show &Rapid Moves"
        '
        ' mnuViewOutline
        '
        mnuViewOutline.Checked = True
        mnuViewOutline.CheckOnClick = True
        mnuViewOutline.CheckState = CheckState.Checked
        mnuViewOutline.Name = "mnuViewOutline"
        mnuViewOutline.Size = New Size(200, 22)
        mnuViewOutline.Text = "Show Text &Outline"
        '
        ' mnuViewGrid
        '
        mnuViewGrid.Checked = True
        mnuViewGrid.CheckOnClick = True
        mnuViewGrid.CheckState = CheckState.Checked
        mnuViewGrid.Name = "mnuViewGrid"
        mnuViewGrid.Size = New Size(200, 22)
        mnuViewGrid.Text = "Show 1"" &Grid"
        ' 
        ' mnuViewSep2
        ' 
        mnuViewSep2.Name = "mnuViewSep2"
        mnuViewSep2.Size = New Size(197, 6)
        ' 
        ' mnuViewSim
        ' 
        mnuViewSim.CheckOnClick = True
        mnuViewSim.Name = "mnuViewSim"
        mnuViewSim.ShortcutKeys = Keys.Control Or Keys.M
        mnuViewSim.Size = New Size(200, 22)
        mnuViewSim.Text = "Show &Simulation"
        '
        ' mnuToolpath
        '
        mnuToolpath.DropDownItems.AddRange(New ToolStripItem() {mnuToolpathGenerate, mnuToolpathAuto, mnuToolpathSep1, mnuToolLibrary})
        mnuToolpath.Name = "mnuToolpath"
        mnuToolpath.Size = New Size(64, 20)
        mnuToolpath.Text = "&Toolpath"
        '
        ' mnuMachine
        '
        mnuMachine.DropDownItems.AddRange(New ToolStripItem() {mnuMachineSettings})
        mnuMachine.Name = "mnuMachine"
        mnuMachine.Size = New Size(65, 20)
        mnuMachine.Text = "&Machine"
        '
        ' mnuMachineSettings
        '
        mnuMachineSettings.Name = "mnuMachineSettings"
        mnuMachineSettings.ShortcutKeys = Keys.Control Or Keys.K
        mnuMachineSettings.Size = New Size(220, 22)
        mnuMachineSettings.Text = "Machine &Settings..."
        '
        ' mnuToolpathGenerate
        '
        mnuToolpathGenerate.Name = "mnuToolpathGenerate"
        mnuToolpathGenerate.ShortcutKeys = Keys.F5
        mnuToolpathGenerate.Size = New Size(200, 22)
        mnuToolpathGenerate.Text = "&Generate Now"
        '
        ' mnuToolpathAuto
        '
        mnuToolpathAuto.Checked = True
        mnuToolpathAuto.CheckOnClick = True
        mnuToolpathAuto.CheckState = CheckState.Checked
        mnuToolpathAuto.Name = "mnuToolpathAuto"
        mnuToolpathAuto.Size = New Size(200, 22)
        mnuToolpathAuto.Text = "&Auto-regenerate"
        '
        ' mnuToolpathSep1
        '
        mnuToolpathSep1.Name = "mnuToolpathSep1"
        mnuToolpathSep1.Size = New Size(197, 6)
        '
        ' mnuToolLibrary
        '
        mnuToolLibrary.Name = "mnuToolLibrary"
        mnuToolLibrary.ShortcutKeys = Keys.Control Or Keys.L
        mnuToolLibrary.Size = New Size(200, 22)
        mnuToolLibrary.Text = "Tool &Library..."
        '
        ' mnuDraw
        '
        mnuDraw.DropDownItems.AddRange(New ToolStripItem() {mnuDrawSelect, mnuDrawSep0, mnuDrawRectangle, mnuDrawEllipse, mnuDrawPolygon, mnuDrawStar, mnuDrawArea, mnuDrawText, mnuDrawSep1, mnuDrawEditText, mnuDrawSep2, mnuDrawScale, mnuDrawRotateLeft, mnuDrawRotateRight, mnuDrawMirror, mnuDrawFront, mnuDrawBack, mnuDrawSep4, mnuDrawSnapGrid, mnuDrawGridSpacing, mnuDrawRotationSnap, mnuDrawSep3, mnuDrawDelete})
        mnuDraw.Name = "mnuDraw"
        mnuDraw.Size = New Size(46, 20)
        mnuDraw.Text = "&Draw"
        '
        ' mnuDrawSelect
        '
        mnuDrawSelect.Name = "mnuDrawSelect"
        mnuDrawSelect.ShortcutKeyDisplayString = "V"
        mnuDrawSelect.Size = New Size(220, 22)
        mnuDrawSelect.Text = "&Select"
        '
        ' mnuDrawSep0
        '
        mnuDrawSep0.Name = "mnuDrawSep0"
        mnuDrawSep0.Size = New Size(217, 6)
        '
        ' mnuDrawRectangle
        '
        mnuDrawRectangle.Name = "mnuDrawRectangle"
        mnuDrawRectangle.ShortcutKeyDisplayString = "R"
        mnuDrawRectangle.Size = New Size(220, 22)
        mnuDrawRectangle.Text = "&Rectangle"
        '
        ' mnuDrawEllipse
        '
        mnuDrawEllipse.Name = "mnuDrawEllipse"
        mnuDrawEllipse.ShortcutKeyDisplayString = "E"
        mnuDrawEllipse.Size = New Size(220, 22)
        mnuDrawEllipse.Text = "&Ellipse"
        '
        ' mnuDrawPolygon
        '
        mnuDrawPolygon.Name = "mnuDrawPolygon"
        mnuDrawPolygon.ShortcutKeyDisplayString = "P"
        mnuDrawPolygon.Size = New Size(220, 22)
        mnuDrawPolygon.Text = "&Polygon"
        '
        ' mnuDrawStar
        '
        mnuDrawStar.Name = "mnuDrawStar"
        mnuDrawStar.ShortcutKeyDisplayString = "S"
        mnuDrawStar.Size = New Size(220, 22)
        mnuDrawStar.Text = "S&tar"
        '
        ' mnuDrawArea
        '
        mnuDrawArea.Name = "mnuDrawArea"
        mnuDrawArea.ShortcutKeyDisplayString = "A"
        mnuDrawArea.Size = New Size(220, 22)
        mnuDrawArea.Text = "Free-form &Area"
        '
        ' mnuDrawText
        '
        mnuDrawText.Name = "mnuDrawText"
        mnuDrawText.ShortcutKeyDisplayString = "T"
        mnuDrawText.Size = New Size(220, 22)
        mnuDrawText.Text = "Te&xt"
        '
        ' mnuDrawSep1
        '
        mnuDrawSep1.Name = "mnuDrawSep1"
        mnuDrawSep1.Size = New Size(217, 6)
        '
        ' mnuDrawEditText
        '
        mnuDrawEditText.Name = "mnuDrawEditText"
        mnuDrawEditText.ShortcutKeyDisplayString = "Double-click"
        mnuDrawEditText.Size = New Size(220, 22)
        mnuDrawEditText.Text = "Ed&it Text"
        '
        ' mnuDrawSep2
        '
        mnuDrawSep2.Name = "mnuDrawSep2"
        mnuDrawSep2.Size = New Size(217, 6)
        '
        ' mnuDrawRotateLeft
        '
        mnuDrawRotateLeft.Name = "mnuDrawRotateLeft"
        mnuDrawRotateLeft.Size = New Size(220, 22)
        mnuDrawRotateLeft.Text = "Rotate 90° &Left"
        '
        ' mnuDrawRotateRight
        '
        mnuDrawRotateRight.Name = "mnuDrawRotateRight"
        mnuDrawRotateRight.Size = New Size(220, 22)
        mnuDrawRotateRight.Text = "Rotate 90° Ri&ght"
        '
        ' mnuDrawMirror
        '
        mnuDrawMirror.Name = "mnuDrawMirror"
        mnuDrawMirror.Size = New Size(220, 22)
        mnuDrawMirror.Text = "&Mirror"
        '
        ' mnuDrawScale
        '
        mnuDrawScale.Name = "mnuDrawScale"
        mnuDrawScale.Size = New Size(220, 22)
        mnuDrawScale.Text = "Scal&e..."
        '
        ' mnuDrawFront
        '
        mnuDrawFront.Name = "mnuDrawFront"
        mnuDrawFront.Size = New Size(220, 22)
        mnuDrawFront.Text = "Bring to &Front"
        '
        ' mnuDrawBack
        '
        mnuDrawBack.Name = "mnuDrawBack"
        mnuDrawBack.Size = New Size(220, 22)
        mnuDrawBack.Text = "Send to &Back"
        '
        ' mnuDrawSep3
        '
        mnuDrawSep3.Name = "mnuDrawSep3"
        mnuDrawSep3.Size = New Size(217, 6)
        '
        ' mnuDrawDelete
        '
        mnuDrawDelete.Name = "mnuDrawDelete"
        mnuDrawDelete.ShortcutKeyDisplayString = "Del"
        mnuDrawDelete.Size = New Size(220, 22)
        mnuDrawDelete.Text = "&Delete"
        '
        ' mnuStyle
        '
        mnuStyle.DropDownItems.AddRange(New ToolStripItem() {mnuStyleFont, mnuStyleBold, mnuStyleItalic, mnuStyleSep1, mnuStyleAlignLeft, mnuStyleAlignCenter, mnuStyleAlignRight})
        mnuStyle.Name = "mnuStyle"
        mnuStyle.Size = New Size(44, 20)
        mnuStyle.Text = "&Style"
        '
        ' mnuStyleFont
        '
        mnuStyleFont.Name = "mnuStyleFont"
        mnuStyleFont.Size = New Size(220, 22)
        mnuStyleFont.Text = "&Font..."
        '
        ' mnuStyleBold
        '
        mnuStyleBold.Name = "mnuStyleBold"
        mnuStyleBold.Size = New Size(220, 22)
        mnuStyleBold.Text = "&Bold"
        '
        ' mnuStyleItalic
        '
        mnuStyleItalic.Name = "mnuStyleItalic"
        mnuStyleItalic.Size = New Size(220, 22)
        mnuStyleItalic.Text = "&Italic"
        '
        ' mnuStyleSep1
        '
        mnuStyleSep1.Name = "mnuStyleSep1"
        mnuStyleSep1.Size = New Size(217, 6)
        '
        ' mnuStyleAlignLeft
        '
        mnuStyleAlignLeft.Name = "mnuStyleAlignLeft"
        mnuStyleAlignLeft.Size = New Size(220, 22)
        mnuStyleAlignLeft.Text = "Align &Left"
        '
        ' mnuStyleAlignCenter
        '
        mnuStyleAlignCenter.Name = "mnuStyleAlignCenter"
        mnuStyleAlignCenter.Size = New Size(220, 22)
        mnuStyleAlignCenter.Text = "Align &Center"
        '
        ' mnuStyleAlignRight
        '
        mnuStyleAlignRight.Name = "mnuStyleAlignRight"
        mnuStyleAlignRight.Size = New Size(220, 22)
        mnuStyleAlignRight.Text = "Align &Right"
        '
        ' tsDrawTools
        '
        tsDrawTools.BackColor = SystemColors.Control
        tsDrawTools.Dock = DockStyle.None
        tsDrawTools.Items.AddRange(New ToolStripItem() {tsbToolSelect, tsbToolRectangle, tsbToolEllipse, tsbToolPolygon, tsbToolStar, tsbToolArea, tsbToolText})
        tsDrawTools.Name = "tsDrawTools"
        tsDrawTools.RenderMode = ToolStripRenderMode.System
        tsDrawTools.TabIndex = 2
        tsDrawTools.Text = "Draw Tools"
        '
        ' tsTextStyle
        '
        tsTextStyle.BackColor = SystemColors.Control
        tsTextStyle.Dock = DockStyle.None
        tsTextStyle.Items.AddRange(New ToolStripItem() {tslDrawFont, tscDrawFont, tsbDrawBold, tsbDrawItalic, tssDraw2, tsbDrawAlignLeft, tsbDrawAlignCenter, tsbDrawAlignRight})
        tsTextStyle.Name = "tsTextStyle"
        tsTextStyle.RenderMode = ToolStripRenderMode.System
        tsTextStyle.TabIndex = 3
        tsTextStyle.Text = "Text Style"
        '
        ' tsSnap
        '
        tsSnap.BackColor = SystemColors.Control
        tsSnap.Dock = DockStyle.None
        tsSnap.Items.AddRange(New ToolStripItem() {tsbSnapGrid, tscSnapGrid, tslSnapRot, tscSnapRot})
        tsSnap.Name = "tsSnap"
        tsSnap.RenderMode = ToolStripRenderMode.System
        tsSnap.TabIndex = 4
        tsSnap.Text = "Snap"
        '
        ' tspTop
        '
        tspTop.BackColor = SystemColors.Control
        tspTop.Dock = DockStyle.Top
        tspTop.Name = "tspTop"
        tspTop.RowMargin = New Padding(3, 0, 0, 0)
        '
        ' tspBottom
        '
        tspBottom.BackColor = SystemColors.Control
        tspBottom.Dock = DockStyle.Bottom
        tspBottom.Name = "tspBottom"
        tspBottom.RowMargin = New Padding(3, 0, 0, 0)
        '
        ' tspLeft
        '
        tspLeft.BackColor = SystemColors.Control
        tspLeft.Dock = DockStyle.Left
        tspLeft.Name = "tspLeft"
        tspLeft.RowMargin = New Padding(3, 0, 0, 0)
        '
        ' tspRight
        '
        tspRight.BackColor = SystemColors.Control
        tspRight.Dock = DockStyle.Right
        tspRight.Name = "tspRight"
        tspRight.RowMargin = New Padding(3, 0, 0, 0)
        '
        ' mnuViewSep3
        '
        mnuViewSep3.Name = "mnuViewSep3"
        mnuViewSep3.Size = New Size(197, 6)
        '
        ' mnuViewToolbars
        '
        mnuViewToolbars.Name = "mnuViewToolbars"
        mnuViewToolbars.Size = New Size(200, 22)
        mnuViewToolbars.Text = "Tool&bars"
        '
        ' tsbToolSelect
        '
        tsbToolSelect.DisplayStyle = ToolStripItemDisplayStyle.Image
        tsbToolSelect.Name = "tsbToolSelect"
        tsbToolSelect.Size = New Size(23, 22)
        tsbToolSelect.ToolTipText = "Select, move, scale and rotate (V)"
        '
        ' tsbToolRectangle
        '
        tsbToolRectangle.DisplayStyle = ToolStripItemDisplayStyle.Image
        tsbToolRectangle.Name = "tsbToolRectangle"
        tsbToolRectangle.Size = New Size(23, 22)
        tsbToolRectangle.ToolTipText = "Draw a rectangle (R). Shift: square, Ctrl: from the centre"
        '
        ' tsbToolEllipse
        '
        tsbToolEllipse.DisplayStyle = ToolStripItemDisplayStyle.Image
        tsbToolEllipse.Name = "tsbToolEllipse"
        tsbToolEllipse.Size = New Size(23, 22)
        tsbToolEllipse.ToolTipText = "Draw an ellipse (E). Shift: circle, Ctrl: from the centre"
        '
        ' tsbToolPolygon
        '
        tsbToolPolygon.DisplayStyle = ToolStripItemDisplayStyle.Image
        tsbToolPolygon.Name = "tsbToolPolygon"
        tsbToolPolygon.Size = New Size(23, 22)
        tsbToolPolygon.ToolTipText = "Draw a polygon (P). Shift: regular, Ctrl: from the centre"
        '
        ' tsbToolStar
        '
        tsbToolStar.DisplayStyle = ToolStripItemDisplayStyle.Image
        tsbToolStar.Name = "tsbToolStar"
        tsbToolStar.Size = New Size(23, 22)
        tsbToolStar.ToolTipText = "Draw a star (S). Shift: regular, Ctrl: from the centre"
        '
        ' tsbToolArea
        '
        tsbToolArea.DisplayStyle = ToolStripItemDisplayStyle.Image
        tsbToolArea.Name = "tsbToolArea"
        tsbToolArea.Size = New Size(23, 22)
        tsbToolArea.ToolTipText = "Draw a free-form area (A): click the corners; click the first corner, double-click or press Enter to close"
        '
        ' tsbToolText
        '
        tsbToolText.DisplayStyle = ToolStripItemDisplayStyle.Image
        tsbToolText.Name = "tsbToolText"
        tsbToolText.Size = New Size(23, 22)
        tsbToolText.ToolTipText = "Draw text (T): drag a box as tall as a capital letter, then type"
        '
        ' tslDrawFont
        '
        tslDrawFont.Name = "tslDrawFont"
        tslDrawFont.Size = New Size(34, 22)
        tslDrawFont.Text = "Font:"
        '
        ' tscDrawFont
        '
        tscDrawFont.AutoCompleteMode = AutoCompleteMode.SuggestAppend
        tscDrawFont.AutoCompleteSource = AutoCompleteSource.ListItems
        tscDrawFont.DropDownWidth = 220
        tscDrawFont.MaxDropDownItems = 20
        tscDrawFont.Name = "tscDrawFont"
        tscDrawFont.Size = New Size(150, 25)
        tscDrawFont.ToolTipText = "Font of the selected text drawing (and of new text)"
        '
        ' tsbDrawBold
        '
        tsbDrawBold.DisplayStyle = ToolStripItemDisplayStyle.Text
        tsbDrawBold.Font = New Font("Segoe UI", 9.0F, FontStyle.Bold)
        tsbDrawBold.Name = "tsbDrawBold"
        tsbDrawBold.Size = New Size(23, 22)
        tsbDrawBold.Text = "B"
        tsbDrawBold.ToolTipText = "Bold"
        '
        ' tsbDrawItalic
        '
        tsbDrawItalic.DisplayStyle = ToolStripItemDisplayStyle.Text
        tsbDrawItalic.Font = New Font("Segoe UI", 9.0F, FontStyle.Italic)
        tsbDrawItalic.Name = "tsbDrawItalic"
        tsbDrawItalic.Size = New Size(23, 22)
        tsbDrawItalic.Text = "I"
        tsbDrawItalic.ToolTipText = "Italic"
        '
        ' tssDraw2
        '
        tssDraw2.Name = "tssDraw2"
        tssDraw2.Size = New Size(6, 25)
        '
        ' tsbDrawAlignLeft
        '
        tsbDrawAlignLeft.DisplayStyle = ToolStripItemDisplayStyle.Image
        tsbDrawAlignLeft.Name = "tsbDrawAlignLeft"
        tsbDrawAlignLeft.Size = New Size(23, 22)
        tsbDrawAlignLeft.ToolTipText = "Align the lines left"
        '
        ' tsbDrawAlignCenter
        '
        tsbDrawAlignCenter.DisplayStyle = ToolStripItemDisplayStyle.Image
        tsbDrawAlignCenter.Name = "tsbDrawAlignCenter"
        tsbDrawAlignCenter.Size = New Size(23, 22)
        tsbDrawAlignCenter.ToolTipText = "Centre the lines"
        '
        ' tsbDrawAlignRight
        '
        tsbDrawAlignRight.DisplayStyle = ToolStripItemDisplayStyle.Image
        tsbDrawAlignRight.Name = "tsbDrawAlignRight"
        tsbDrawAlignRight.Size = New Size(23, 22)
        tsbDrawAlignRight.ToolTipText = "Align the lines right"
        '
        ' mnuDrawSep4
        '
        mnuDrawSep4.Name = "mnuDrawSep4"
        mnuDrawSep4.Size = New Size(217, 6)
        '
        ' mnuDrawSnapGrid
        '
        mnuDrawSnapGrid.Name = "mnuDrawSnapGrid"
        mnuDrawSnapGrid.ShortcutKeyDisplayString = "Alt: ignore"
        mnuDrawSnapGrid.Size = New Size(220, 22)
        mnuDrawSnapGrid.Text = "S&nap to Grid"
        '
        ' mnuDrawGridSpacing
        '
        mnuDrawGridSpacing.Name = "mnuDrawGridSpacing"
        mnuDrawGridSpacing.Size = New Size(220, 22)
        mnuDrawGridSpacing.Text = "Grid Spa&cing"
        '
        ' mnuDrawRotationSnap
        '
        mnuDrawRotationSnap.Name = "mnuDrawRotationSnap"
        mnuDrawRotationSnap.Size = New Size(220, 22)
        mnuDrawRotationSnap.Text = "R&otation Snap"
        '
        ' tsbSnapGrid
        '
        tsbSnapGrid.CheckOnClick = True
        tsbSnapGrid.DisplayStyle = ToolStripItemDisplayStyle.Image
        tsbSnapGrid.Name = "tsbSnapGrid"
        tsbSnapGrid.Size = New Size(23, 22)
        tsbSnapGrid.ToolTipText = "Snap to grid: moving, resizing and drawing land on the grid (hold Alt while dragging to ignore it)"
        '
        ' tscSnapGrid
        '
        tscSnapGrid.DropDownStyle = ComboBoxStyle.DropDownList
        tscSnapGrid.Name = "tscSnapGrid"
        tscSnapGrid.Size = New Size(60, 25)
        tscSnapGrid.ToolTipText = "Grid spacing for snapping"
        '
        ' tslSnapRot
        '
        tslSnapRot.Name = "tslSnapRot"
        tslSnapRot.Size = New Size(44, 22)
        tslSnapRot.Text = "Rotate:"
        '
        ' tscSnapRot
        '
        tscSnapRot.DropDownStyle = ComboBoxStyle.DropDownList
        tscSnapRot.Name = "tscSnapRot"
        tscSnapRot.Size = New Size(56, 25)
        tscSnapRot.ToolTipText = "Rotation snap: the round knob turns in steps of this angle (hold Shift to turn freely)"
        '
        ' mnuHelp
        '
        mnuHelp.DropDownItems.AddRange(New ToolStripItem() {mnuHelpGuide, mnuHelpReleases, mnuHelpSep1, mnuHelpAbout})
        mnuHelp.Name = "mnuHelp"
        mnuHelp.Size = New Size(44, 20)
        mnuHelp.Text = "&Help"
        '
        ' mnuHelpGuide
        '
        mnuHelpGuide.Name = "mnuHelpGuide"
        mnuHelpGuide.ShortcutKeys = Keys.F1
        mnuHelpGuide.Size = New Size(220, 22)
        mnuHelpGuide.Text = "&User Guide"
        '
        ' mnuHelpReleases
        '
        mnuHelpReleases.Name = "mnuHelpReleases"
        mnuHelpReleases.Size = New Size(220, 22)
        mnuHelpReleases.Text = "&Newer Versions (Web)"
        '
        ' mnuHelpSep1
        '
        mnuHelpSep1.Name = "mnuHelpSep1"
        mnuHelpSep1.Size = New Size(217, 6)
        '
        ' mnuHelpAbout
        '
        mnuHelpAbout.Name = "mnuHelpAbout"
        mnuHelpAbout.Size = New Size(220, 22)
        mnuHelpAbout.Text = "&About CarveMaker"
        '
        ' stsMain
        '
        stsMain.Items.AddRange(New ToolStripItem() {lblStatus, lblStats})
        stsMain.Location = New Point(0, 728)
        stsMain.Name = "stsMain"
        stsMain.Size = New Size(1200, 22)
        stsMain.TabIndex = 2
        stsMain.Text = "stsMain"
        '
        ' lblStatus
        '
        lblStatus.Name = "lblStatus"
        lblStatus.Size = New Size(1046, 17)
        lblStatus.Spring = True
        lblStatus.Text = "Ready"
        lblStatus.TextAlign = ContentAlignment.MiddleLeft
        '
        ' lblStats
        '
        lblStats.Name = "lblStats"
        lblStats.Size = New Size(139, 17)
        lblStats.Text = "No toolpath"
        '
        ' splitMain
        '
        splitMain.Dock = DockStyle.Fill
        splitMain.Location = New Point(0, 24)
        splitMain.Name = "splitMain"
        '
        ' splitMain.Panel1
        '
        splitMain.Panel1.Controls.Add(tlpLeft)
        splitMain.Panel1MinSize = 260
        '
        ' splitMain.Panel2
        '
        splitMain.Panel2.Controls.Add(pnlView)
        splitMain.Panel2MinSize = 200
        splitMain.Size = New Size(1200, 704)
        splitMain.SplitterDistance = 420
        splitMain.SplitterWidth = 6
        splitMain.TabIndex = 1
        '
        ' tlpLeft
        '
        tlpLeft.ColumnCount = 1
        tlpLeft.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0F))
        tlpLeft.Controls.Add(tabLeft, 0, 0)
        tlpLeft.Dock = DockStyle.Fill
        tlpLeft.Location = New Point(0, 0)
        tlpLeft.Name = "tlpLeft"
        tlpLeft.RowCount = 1
        tlpLeft.RowStyles.Add(New RowStyle(SizeType.Percent, 100.0F))
        tlpLeft.Size = New Size(420, 704)
        tlpLeft.TabIndex = 0
        ' 
        ' tabLeft
        ' 
        tabLeft.Controls.Add(tabText)
        tabLeft.Controls.Add(tabObjects)
        tabLeft.Dock = DockStyle.Fill
        tabLeft.Location = New Point(3, 3)
        tabLeft.Name = "tabLeft"
        tabLeft.SelectedIndex = 0
        tabLeft.Size = New Size(414, 698)
        tabLeft.TabIndex = 0
        ' 
        ' tabText
        ' 
        tabText.Controls.Add(splitText)
        tabText.Location = New Point(4, 24)
        tabText.Name = "tabText"
        tabText.Padding = New Padding(3)
        tabText.Size = New Size(406, 670)
        tabText.TabIndex = 0
        tabText.Text = "Text"
        tabText.UseVisualStyleBackColor = True
        ' 
        ' tabObjects
        ' 
        tabObjects.Controls.Add(pgObject)
        tabObjects.Controls.Add(lstObjects)
        tabObjects.Controls.Add(tsObjects)
        tabObjects.Location = New Point(4, 24)
        tabObjects.Name = "tabObjects"
        tabObjects.Padding = New Padding(3)
        tabObjects.Size = New Size(406, 261)
        tabObjects.TabIndex = 1
        tabObjects.Text = "Drawings"
        tabObjects.UseVisualStyleBackColor = True
        ' 
        ' tsObjects
        ' 
        tsObjects.GripStyle = ToolStripGripStyle.Hidden
        tsObjects.Items.AddRange(New ToolStripItem() {tsbObjImport, tsbObjImportModel, tsbObjRemove, tsbObjFit, tsbObjCenter})
        tsObjects.Location = New Point(3, 3)
        tsObjects.Name = "tsObjects"
        tsObjects.RenderMode = ToolStripRenderMode.System
        tsObjects.Size = New Size(400, 25)
        tsObjects.TabIndex = 0
        ' 
        ' tsbObjImport
        ' 
        tsbObjImport.DisplayStyle = ToolStripItemDisplayStyle.Text
        tsbObjImport.Name = "tsbObjImport"
        tsbObjImport.Size = New Size(78, 22)
        tsbObjImport.Text = "Import SVG..."
        tsbObjImport.ToolTipText = "Add an SVG drawing to the blank"
        '
        ' tsbObjImportModel
        '
        tsbObjImportModel.DisplayStyle = ToolStripItemDisplayStyle.Text
        tsbObjImportModel.Name = "tsbObjImportModel"
        tsbObjImportModel.Size = New Size(72, 22)
        tsbObjImportModel.Text = "Import 3D..."
        tsbObjImportModel.ToolTipText = "Add a 3D model (STL, OBJ, PLY, 3MF, OFF) or a depth image to the blank, carved as a relief"
        ' 
        ' tsbObjRemove
        ' 
        tsbObjRemove.DisplayStyle = ToolStripItemDisplayStyle.Text
        tsbObjRemove.Name = "tsbObjRemove"
        tsbObjRemove.Size = New Size(54, 22)
        tsbObjRemove.Text = "Remove"
        tsbObjRemove.ToolTipText = "Remove the selected drawing"
        ' 
        ' tsbObjFit
        ' 
        tsbObjFit.DisplayStyle = ToolStripItemDisplayStyle.Text
        tsbObjFit.Name = "tsbObjFit"
        tsbObjFit.Size = New Size(72, 22)
        tsbObjFit.Text = "Fit to Blank"
        tsbObjFit.ToolTipText = "Scale the drawing to fill the blank inside the margins (Machine Settings > 1. Blank > Margin)"
        ' 
        ' tsbObjCenter
        ' 
        tsbObjCenter.DisplayStyle = ToolStripItemDisplayStyle.Text
        tsbObjCenter.Name = "tsbObjCenter"
        tsbObjCenter.Size = New Size(46, 22)
        tsbObjCenter.Text = "Center"
        tsbObjCenter.ToolTipText = "Center the drawing on the blank"
        ' 
        ' lstObjects
        ' 
        lstObjects.Dock = DockStyle.Top
        lstObjects.FormattingEnabled = True
        lstObjects.IntegralHeight = False
        lstObjects.ItemHeight = 15
        lstObjects.Location = New Point(3, 28)
        lstObjects.Name = "lstObjects"
        lstObjects.Size = New Size(400, 100)
        lstObjects.TabIndex = 1
        ' 
        ' pgObject
        ' 
        pgObject.Dock = DockStyle.Fill
        pgObject.HelpVisible = False
        pgObject.Location = New Point(3, 128)
        pgObject.Name = "pgObject"
        pgObject.PropertySort = PropertySort.Categorized
        pgObject.Size = New Size(400, 130)
        pgObject.TabIndex = 2
        pgObject.ToolbarVisible = False
        '
        ' splitText
        '
        splitText.Dock = DockStyle.Fill
        splitText.Location = New Point(3, 3)
        splitText.Name = "splitText"
        splitText.Orientation = Orientation.Horizontal
        '
        ' splitText.Panel1
        '
        splitText.Panel1.Controls.Add(pnlEditor)
        splitText.Panel1MinSize = 120
        '
        ' splitText.Panel2
        '
        splitText.Panel2.Controls.Add(pgText)
        splitText.Panel2MinSize = 120
        splitText.Size = New Size(400, 664)
        splitText.SplitterDistance = 200
        splitText.SplitterWidth = 6
        splitText.TabIndex = 0
        '
        ' pgText
        '
        pgText.Dock = DockStyle.Fill
        pgText.Location = New Point(0, 0)
        pgText.Name = "pgText"
        pgText.PropertySort = PropertySort.Categorized
        pgText.Size = New Size(400, 358)
        pgText.TabIndex = 0
        pgText.ToolbarVisible = False
        '
        ' pnlEditor
        '
        pnlEditor.Controls.Add(rtbText)
        pnlEditor.Controls.Add(tsFormat)
        pnlEditor.Dock = DockStyle.Fill
        pnlEditor.Location = New Point(0, 0)
        pnlEditor.Name = "pnlEditor"
        pnlEditor.Size = New Size(400, 300)
        pnlEditor.TabIndex = 0
        '
        ' rtbText
        '
        rtbText.AcceptsTab = False
        rtbText.DetectUrls = False
        rtbText.Dock = DockStyle.Fill
        rtbText.Font = New Font("Arial", 24.0F, FontStyle.Regular, GraphicsUnit.Point)
        rtbText.HideSelection = False
        rtbText.Location = New Point(0, 25)
        rtbText.Name = "rtbText"
        rtbText.ScrollBars = RichTextBoxScrollBars.Both
        rtbText.Size = New Size(414, 264)
        rtbText.TabIndex = 1
        rtbText.Text = ""
        rtbText.WordWrap = False
        '
        ' tsFormat
        '
        tsFormat.GripStyle = ToolStripGripStyle.Hidden
        tsFormat.Items.AddRange(New ToolStripItem() {tslSize, tsbSizeLarge, tsbSizeMedium, tsbSizeSmall, tssSep1, tslAlign, tsbAlignLeft, tsbAlignCenter, tsbAlignRight})
        tsFormat.Location = New Point(0, 0)
        tsFormat.Name = "tsFormat"
        tsFormat.RenderMode = ToolStripRenderMode.System
        tsFormat.Size = New Size(414, 25)
        tsFormat.TabIndex = 0
        '
        ' tslSize
        '
        tslSize.Name = "tslSize"
        tslSize.Size = New Size(30, 22)
        tslSize.Text = "Size:"
        '
        ' tsbSizeLarge
        '
        tsbSizeLarge.DisplayStyle = ToolStripItemDisplayStyle.Text
        tsbSizeLarge.Name = "tsbSizeLarge"
        tsbSizeLarge.Size = New Size(40, 22)
        tsbSizeLarge.Text = "Large"
        tsbSizeLarge.ToolTipText = "Format the selected line(s) with size 1 (large)"
        '
        ' tsbSizeMedium
        '
        tsbSizeMedium.DisplayStyle = ToolStripItemDisplayStyle.Text
        tsbSizeMedium.Name = "tsbSizeMedium"
        tsbSizeMedium.Size = New Size(53, 22)
        tsbSizeMedium.Text = "Medium"
        tsbSizeMedium.ToolTipText = "Format the selected line(s) with size 2 (medium)"
        '
        ' tsbSizeSmall
        '
        tsbSizeSmall.DisplayStyle = ToolStripItemDisplayStyle.Text
        tsbSizeSmall.Name = "tsbSizeSmall"
        tsbSizeSmall.Size = New Size(40, 22)
        tsbSizeSmall.Text = "Small"
        tsbSizeSmall.ToolTipText = "Format the selected line(s) with size 3 (small)"
        '
        ' tssSep1
        '
        tssSep1.Name = "tssSep1"
        tssSep1.Size = New Size(6, 25)
        '
        ' tslAlign
        '
        tslAlign.Name = "tslAlign"
        tslAlign.Size = New Size(36, 22)
        tslAlign.Text = "Align:"
        '
        ' tsbAlignLeft
        '
        tsbAlignLeft.DisplayStyle = ToolStripItemDisplayStyle.Text
        tsbAlignLeft.Name = "tsbAlignLeft"
        tsbAlignLeft.Size = New Size(31, 22)
        tsbAlignLeft.Text = "Left"
        tsbAlignLeft.ToolTipText = "Left-align the selected line(s)"
        '
        ' tsbAlignCenter
        '
        tsbAlignCenter.DisplayStyle = ToolStripItemDisplayStyle.Text
        tsbAlignCenter.Name = "tsbAlignCenter"
        tsbAlignCenter.Size = New Size(46, 22)
        tsbAlignCenter.Text = "Center"
        tsbAlignCenter.ToolTipText = "Center the selected line(s)"
        '
        ' tsbAlignRight
        '
        tsbAlignRight.DisplayStyle = ToolStripItemDisplayStyle.Text
        tsbAlignRight.Name = "tsbAlignRight"
        tsbAlignRight.Size = New Size(39, 22)
        tsbAlignRight.Text = "Right"
        tsbAlignRight.ToolTipText = "Right-align the selected line(s)"
        '
        ' pnlView
        '
        pnlView.BackColor = Color.FromArgb(CType(CType(30, Byte), Integer), CType(CType(30, Byte), Integer), CType(CType(34, Byte), Integer))
        pnlView.Controls.Add(tspRight)
        pnlView.Controls.Add(tspLeft)
        pnlView.Controls.Add(tspBottom)
        pnlView.Controls.Add(tspTop)
        pnlView.Controls.Add(pnlSim)
        pnlView.Dock = DockStyle.Fill
        pnlView.Location = New Point(0, 0)
        pnlView.Name = "pnlView"
        pnlView.Size = New Size(774, 704)
        pnlView.TabIndex = 0
        ' 
        ' pnlSim
        ' 
        pnlSim.Controls.Add(tbSim)
        pnlSim.Controls.Add(tsSim)
        pnlSim.Dock = DockStyle.Bottom
        pnlSim.BackColor = SystemColors.Control
        pnlSim.Location = New Point(0, 644)
        pnlSim.Name = "pnlSim"
        pnlSim.Size = New Size(774, 60)
        pnlSim.TabIndex = 1
        pnlSim.Visible = False
        ' 
        ' tbSim
        ' 
        tbSim.AutoSize = False
        tbSim.Dock = DockStyle.Fill
        tbSim.LargeChange = 50
        tbSim.Location = New Point(0, 25)
        tbSim.Maximum = 1000
        tbSim.Name = "tbSim"
        tbSim.Size = New Size(774, 35)
        tbSim.SmallChange = 5
        tbSim.TabIndex = 1
        tbSim.TickFrequency = 50
        tbSim.TickStyle = TickStyle.None
        ' 
        ' tsSim
        ' 
        tsSim.GripStyle = ToolStripGripStyle.Hidden
        tsSim.Items.AddRange(New ToolStripItem() {tsbSimPlay, tsbSimReset, tslSimSpeed, tscSimSpeed, tslSimTime, tssSimInfo, tslSimInfo})
        tsSim.Location = New Point(0, 0)
        tsSim.Name = "tsSim"
        tsSim.RenderMode = ToolStripRenderMode.System
        tsSim.Size = New Size(774, 25)
        tsSim.TabIndex = 0
        ' 
        ' tsbSimPlay
        ' 
        tsbSimPlay.DisplayStyle = ToolStripItemDisplayStyle.Text
        tsbSimPlay.Name = "tsbSimPlay"
        tsbSimPlay.Size = New Size(35, 22)
        tsbSimPlay.Text = "Play"
        tsbSimPlay.ToolTipText = "Animate the cut (Play / Pause)"
        ' 
        ' tsbSimReset
        ' 
        tsbSimReset.DisplayStyle = ToolStripItemDisplayStyle.Text
        tsbSimReset.Name = "tsbSimReset"
        tsbSimReset.Size = New Size(39, 22)
        tsbSimReset.Text = "Reset"
        tsbSimReset.ToolTipText = "Back to the uncut blank"
        ' 
        ' tslSimSpeed
        ' 
        tslSimSpeed.Name = "tslSimSpeed"
        tslSimSpeed.Size = New Size(42, 22)
        tslSimSpeed.Text = "Speed:"
        ' 
        ' tscSimSpeed
        ' 
        tscSimSpeed.DropDownStyle = ComboBoxStyle.DropDownList
        tscSimSpeed.Items.AddRange(New Object() {"1x", "2x", "5x", "10x", "25x", "100x", "Instant"})
        tscSimSpeed.Name = "tscSimSpeed"
        tscSimSpeed.Size = New Size(90, 25)
        ' 
        ' tslSimTime
        ' 
        tslSimTime.Name = "tslSimTime"
        tslSimTime.Size = New Size(60, 22)
        tslSimTime.Text = "0:00 / 0:00"
        '
        ' tssSimInfo
        '
        tssSimInfo.Name = "tssSimInfo"
        tssSimInfo.Size = New Size(6, 25)
        '
        ' tslSimInfo
        '
        tslSimInfo.Name = "tslSimInfo"
        tslSimInfo.Overflow = ToolStripItemOverflow.Never
        tslSimInfo.Size = New Size(0, 22)
        tslSimInfo.Text = ""
        '
        ' tmrRegen
        '
        tmrRegen.Interval = 400
        '
        ' dlgFont
        '
        dlgFont.AllowVerticalFonts = False
        dlgFont.FontMustExist = True
        dlgFont.ShowEffects = False
        '
        ' dlgSave
        '
        dlgSave.DefaultExt = "nc"
        dlgSave.Filter = "G-code (*.nc;*.gcode;*.tap;*.ngc)|*.nc;*.gcode;*.tap;*.ngc|All files (*.*)|*.*"
        dlgSave.Title = "Save G-code"
        ' 
        ' dlgOpen
        ' 
        dlgOpen.Filter = "Text or layout (*.txt;*.rtf)|*.txt;*.rtf|All files (*.*)|*.*"
        dlgOpen.Title = "Import Text"
        ' 
        ' dlgOpenProject
        ' 
        dlgOpenProject.DefaultExt = "prj"
        dlgOpenProject.Filter = "CarveMaker project (*.prj)|*.prj|All files (*.*)|*.*"
        dlgOpenProject.Title = "Open Project"
        ' 
        ' dlgSaveProject
        ' 
        dlgSaveProject.DefaultExt = "prj"
        dlgSaveProject.Filter = "CarveMaker project (*.prj)|*.prj|All files (*.*)|*.*"
        dlgSaveProject.Title = "Save Project"
        ' 
        ' dlgSvg
        ' 
        dlgSvg.DefaultExt = "svg"
        dlgSvg.Filter = "SVG drawings (*.svg)|*.svg|All files (*.*)|*.*"
        dlgSvg.Title = "Import SVG"
        '
        ' dlgModel
        '
        dlgModel.Title = "Import 3D Model or Depth Image"
        '
        ' frmMain
        '
        AutoScaleDimensions = New SizeF(7.0F, 15.0F)
        AutoScaleMode = AutoScaleMode.Font
        ClientSize = New Size(1200, 750)
        Controls.Add(splitMain)
        Controls.Add(stsMain)
        Controls.Add(mnuMain)
        MainMenuStrip = mnuMain
        MinimumSize = New Size(800, 500)
        Name = "frmMain"
        StartPosition = FormStartPosition.CenterScreen
        Text = "CarveMaker"
        mnuMain.ResumeLayout(False)
        mnuMain.PerformLayout()
        stsMain.ResumeLayout(False)
        stsMain.PerformLayout()
        splitMain.Panel1.ResumeLayout(False)
        pnlView.ResumeLayout(False)
        pnlSim.ResumeLayout(False)
        pnlSim.PerformLayout()
        CType(tbSim, System.ComponentModel.ISupportInitialize).EndInit()
        tsSim.ResumeLayout(False)
        tsSim.PerformLayout()
        tsDrawTools.ResumeLayout(False)
        tsDrawTools.PerformLayout()
        tsTextStyle.ResumeLayout(False)
        tsTextStyle.PerformLayout()
        tsSnap.ResumeLayout(False)
        tsSnap.PerformLayout()
        splitMain.Panel2.ResumeLayout(False)
        CType(splitMain, System.ComponentModel.ISupportInitialize).EndInit()
        splitMain.ResumeLayout(False)
        tlpLeft.ResumeLayout(False)
        pnlEditor.ResumeLayout(False)
        pnlEditor.PerformLayout()
        tabLeft.ResumeLayout(False)
        tabText.ResumeLayout(False)
        splitText.Panel1.ResumeLayout(False)
        splitText.Panel2.ResumeLayout(False)
        CType(splitText, System.ComponentModel.ISupportInitialize).EndInit()
        splitText.ResumeLayout(False)
        tabObjects.ResumeLayout(False)
        tabObjects.PerformLayout()
        tsObjects.ResumeLayout(False)
        tsObjects.PerformLayout()
        tsFormat.ResumeLayout(False)
        tsFormat.PerformLayout()
        ResumeLayout(False)
        PerformLayout()
    End Sub

    Friend WithEvents mnuMain As MenuStrip
    Friend WithEvents mnuFile As ToolStripMenuItem
    Friend WithEvents mnuFileNew As ToolStripMenuItem
    Friend WithEvents mnuFileOpenProject As ToolStripMenuItem
    Friend WithEvents mnuFileSaveProject As ToolStripMenuItem
    Friend WithEvents mnuFileSaveProjectAs As ToolStripMenuItem
    Friend WithEvents mnuFileSep0 As ToolStripSeparator
    Friend WithEvents mnuFileImportText As ToolStripMenuItem
    Friend WithEvents mnuFileImportSvg As ToolStripMenuItem
    Friend WithEvents mnuFileImportModel As ToolStripMenuItem
    Friend WithEvents mnuFileSaveGcode As ToolStripMenuItem
    Friend WithEvents mnuFileSep1 As ToolStripSeparator
    Friend WithEvents mnuFileExit As ToolStripMenuItem
    Friend WithEvents mnuFont As ToolStripMenuItem
    Friend WithEvents mnuFontChoose As ToolStripMenuItem
    Friend WithEvents mnuView As ToolStripMenuItem
    Friend WithEvents mnuViewTop As ToolStripMenuItem
    Friend WithEvents mnuViewIso As ToolStripMenuItem
    Friend WithEvents mnuViewFit As ToolStripMenuItem
    Friend WithEvents mnuViewSep1 As ToolStripSeparator
    Friend WithEvents mnuViewRapids As ToolStripMenuItem
    Friend WithEvents mnuViewOutline As ToolStripMenuItem
    Friend WithEvents mnuViewGrid As ToolStripMenuItem
    Friend WithEvents mnuViewSep2 As ToolStripSeparator
    Friend WithEvents mnuViewSim As ToolStripMenuItem
    Friend WithEvents mnuToolpath As ToolStripMenuItem
    Friend WithEvents mnuMachine As ToolStripMenuItem
    Friend WithEvents mnuMachineSettings As ToolStripMenuItem
    Friend WithEvents mnuToolpathGenerate As ToolStripMenuItem
    Friend WithEvents mnuToolpathAuto As ToolStripMenuItem
    Friend WithEvents mnuToolpathSep1 As ToolStripSeparator
    Friend WithEvents mnuToolLibrary As ToolStripMenuItem
    Friend WithEvents stsMain As StatusStrip
    Friend WithEvents lblStatus As ToolStripStatusLabel
    Friend WithEvents lblStats As ToolStripStatusLabel
    Friend WithEvents splitMain As SplitContainer
    Friend WithEvents tlpLeft As TableLayoutPanel
    Friend WithEvents pnlEditor As Panel
    Friend WithEvents tabLeft As TabControl
    Friend WithEvents tabText As TabPage
    Friend WithEvents tabObjects As TabPage
    Friend WithEvents tsObjects As ToolStrip
    Friend WithEvents tsbObjImport As ToolStripButton
    Friend WithEvents tsbObjImportModel As ToolStripButton
    Friend WithEvents tsbObjRemove As ToolStripButton
    Friend WithEvents tsbObjFit As ToolStripButton
    Friend WithEvents tsbObjCenter As ToolStripButton
    Friend WithEvents lstObjects As ListBox
    Friend WithEvents pgObject As PropertyGrid
    Friend WithEvents rtbText As RichTextBox
    Friend WithEvents tsFormat As ToolStrip
    Friend WithEvents tslSize As ToolStripLabel
    Friend WithEvents tsbSizeLarge As ToolStripButton
    Friend WithEvents tsbSizeMedium As ToolStripButton
    Friend WithEvents tsbSizeSmall As ToolStripButton
    Friend WithEvents tssSep1 As ToolStripSeparator
    Friend WithEvents tslAlign As ToolStripLabel
    Friend WithEvents tsbAlignLeft As ToolStripButton
    Friend WithEvents tsbAlignCenter As ToolStripButton
    Friend WithEvents tsbAlignRight As ToolStripButton
    Friend WithEvents splitText As SplitContainer
    Friend WithEvents pgText As PropertyGrid
    Friend WithEvents pnlView As Panel
    Friend WithEvents pnlSim As Panel
    Friend WithEvents tbSim As TrackBar
    Friend WithEvents tsSim As ToolStrip
    Friend WithEvents tsbSimPlay As ToolStripButton
    Friend WithEvents tsbSimReset As ToolStripButton
    Friend WithEvents tslSimSpeed As ToolStripLabel
    Friend WithEvents tscSimSpeed As ToolStripComboBox
    Friend WithEvents tslSimTime As ToolStripLabel
    Friend WithEvents tssSimInfo As ToolStripSeparator
    Friend WithEvents tslSimInfo As ToolStripLabel
    Friend WithEvents tmrRegen As Timer
    Friend WithEvents dlgFont As FontDialog
    Friend WithEvents dlgSave As SaveFileDialog
    Friend WithEvents dlgOpen As OpenFileDialog
    Friend WithEvents dlgOpenProject As OpenFileDialog
    Friend WithEvents dlgSaveProject As SaveFileDialog
    Friend WithEvents dlgSvg As OpenFileDialog
    Friend WithEvents dlgModel As OpenFileDialog
    Friend WithEvents mnuHelp As ToolStripMenuItem
    Friend WithEvents mnuHelpGuide As ToolStripMenuItem
    Friend WithEvents mnuHelpReleases As ToolStripMenuItem
    Friend WithEvents mnuHelpSep1 As ToolStripSeparator
    Friend WithEvents mnuHelpAbout As ToolStripMenuItem
    Friend WithEvents mnuDrawSep4 As ToolStripSeparator
    Friend WithEvents mnuDrawSnapGrid As ToolStripMenuItem
    Friend WithEvents mnuDrawGridSpacing As ToolStripMenuItem
    Friend WithEvents mnuDrawRotationSnap As ToolStripMenuItem
    Friend WithEvents tsbSnapGrid As ToolStripButton
    Friend WithEvents tscSnapGrid As ToolStripComboBox
    Friend WithEvents tslSnapRot As ToolStripLabel
    Friend WithEvents tscSnapRot As ToolStripComboBox
    Friend WithEvents mnuDraw As ToolStripMenuItem
    Friend WithEvents mnuStyle As ToolStripMenuItem
    Friend WithEvents mnuDrawSelect As ToolStripMenuItem
    Friend WithEvents mnuDrawSep0 As ToolStripSeparator
    Friend WithEvents mnuDrawRectangle As ToolStripMenuItem
    Friend WithEvents mnuDrawEllipse As ToolStripMenuItem
    Friend WithEvents mnuDrawPolygon As ToolStripMenuItem
    Friend WithEvents mnuDrawStar As ToolStripMenuItem
    Friend WithEvents mnuDrawArea As ToolStripMenuItem
    Friend WithEvents mnuDrawText As ToolStripMenuItem
    Friend WithEvents mnuDrawSep1 As ToolStripSeparator
    Friend WithEvents mnuDrawEditText As ToolStripMenuItem
    Friend WithEvents mnuDrawSep2 As ToolStripSeparator
    Friend WithEvents mnuDrawRotateLeft As ToolStripMenuItem
    Friend WithEvents mnuDrawRotateRight As ToolStripMenuItem
    Friend WithEvents mnuDrawMirror As ToolStripMenuItem
    Friend WithEvents mnuDrawScale As ToolStripMenuItem
    Friend WithEvents mnuDrawFront As ToolStripMenuItem
    Friend WithEvents mnuDrawBack As ToolStripMenuItem
    Friend WithEvents mnuDrawSep3 As ToolStripSeparator
    Friend WithEvents mnuDrawDelete As ToolStripMenuItem
    Friend WithEvents mnuStyleFont As ToolStripMenuItem
    Friend WithEvents mnuStyleBold As ToolStripMenuItem
    Friend WithEvents mnuStyleItalic As ToolStripMenuItem
    Friend WithEvents mnuStyleSep1 As ToolStripSeparator
    Friend WithEvents mnuStyleAlignLeft As ToolStripMenuItem
    Friend WithEvents mnuStyleAlignCenter As ToolStripMenuItem
    Friend WithEvents mnuStyleAlignRight As ToolStripMenuItem
    Friend WithEvents tsDrawTools As ToolStrip
    Friend WithEvents tsTextStyle As ToolStrip
    Friend WithEvents tsSnap As ToolStrip
    Friend WithEvents tspTop As ToolStripPanel
    Friend WithEvents tspBottom As ToolStripPanel
    Friend WithEvents tspLeft As ToolStripPanel
    Friend WithEvents tspRight As ToolStripPanel
    Friend WithEvents mnuViewSep3 As ToolStripSeparator
    Friend WithEvents mnuViewToolbars As ToolStripMenuItem
    Friend WithEvents tsbToolSelect As ToolStripButton
    Friend WithEvents tsbToolRectangle As ToolStripButton
    Friend WithEvents tsbToolEllipse As ToolStripButton
    Friend WithEvents tsbToolPolygon As ToolStripButton
    Friend WithEvents tsbToolStar As ToolStripButton
    Friend WithEvents tsbToolArea As ToolStripButton
    Friend WithEvents tsbToolText As ToolStripButton
    Friend WithEvents tslDrawFont As ToolStripLabel
    Friend WithEvents tscDrawFont As ToolStripComboBox
    Friend WithEvents tsbDrawBold As ToolStripButton
    Friend WithEvents tsbDrawItalic As ToolStripButton
    Friend WithEvents tssDraw2 As ToolStripSeparator
    Friend WithEvents tsbDrawAlignLeft As ToolStripButton
    Friend WithEvents tsbDrawAlignCenter As ToolStripButton
    Friend WithEvents tsbDrawAlignRight As ToolStripButton

End Class
