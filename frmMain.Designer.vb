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
        mnuToolpath = New ToolStripMenuItem()
        mnuToolpathGenerate = New ToolStripMenuItem()
        mnuToolpathAuto = New ToolStripMenuItem()
        stsMain = New StatusStrip()
        lblStatus = New ToolStripStatusLabel()
        lblStats = New ToolStripStatusLabel()
        splitMain = New SplitContainer()
        tlpLeft = New TableLayoutPanel()
        pnlEditor = New Panel()
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
        pgSettings = New PropertyGrid()
        pnlView = New Panel()
        tmrRegen = New Timer(components)
        dlgFont = New FontDialog()
        dlgSave = New SaveFileDialog()
        dlgOpen = New OpenFileDialog()
        dlgOpenProject = New OpenFileDialog()
        dlgSaveProject = New SaveFileDialog()
        mnuMain.SuspendLayout()
        stsMain.SuspendLayout()
        CType(splitMain, System.ComponentModel.ISupportInitialize).BeginInit()
        splitMain.Panel1.SuspendLayout()
        splitMain.Panel2.SuspendLayout()
        splitMain.SuspendLayout()
        tlpLeft.SuspendLayout()
        pnlEditor.SuspendLayout()
        tsFormat.SuspendLayout()
        SuspendLayout()
        '
        ' mnuMain
        '
        mnuMain.Items.AddRange(New ToolStripItem() {mnuFile, mnuFont, mnuView, mnuToolpath})
        mnuMain.Location = New Point(0, 0)
        mnuMain.Name = "mnuMain"
        mnuMain.Size = New Size(1200, 24)
        mnuMain.TabIndex = 0
        mnuMain.Text = "mnuMain"
        '
        ' mnuFile
        '
        mnuFile.DropDownItems.AddRange(New ToolStripItem() {mnuFileNew, mnuFileOpenProject, mnuFileSaveProject, mnuFileSaveProjectAs, mnuFileSep0, mnuFileImportText, mnuFileSaveGcode, mnuFileSep1, mnuFileExit})
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
        mnuView.DropDownItems.AddRange(New ToolStripItem() {mnuViewTop, mnuViewIso, mnuViewFit, mnuViewSep1, mnuViewRapids, mnuViewOutline, mnuViewGrid})
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
        ' mnuToolpath
        '
        mnuToolpath.DropDownItems.AddRange(New ToolStripItem() {mnuToolpathGenerate, mnuToolpathAuto})
        mnuToolpath.Name = "mnuToolpath"
        mnuToolpath.Size = New Size(64, 20)
        mnuToolpath.Text = "&Toolpath"
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
        tlpLeft.Controls.Add(pnlEditor, 0, 0)
        tlpLeft.Controls.Add(pgSettings, 0, 1)
        tlpLeft.Dock = DockStyle.Fill
        tlpLeft.Location = New Point(0, 0)
        tlpLeft.Name = "tlpLeft"
        tlpLeft.RowCount = 2
        tlpLeft.RowStyles.Add(New RowStyle(SizeType.Percent, 42.0F))
        tlpLeft.RowStyles.Add(New RowStyle(SizeType.Percent, 58.0F))
        tlpLeft.Size = New Size(420, 704)
        tlpLeft.TabIndex = 0
        '
        ' pnlEditor
        '
        pnlEditor.Controls.Add(rtbText)
        pnlEditor.Controls.Add(tsFormat)
        pnlEditor.Dock = DockStyle.Fill
        pnlEditor.Location = New Point(3, 3)
        pnlEditor.Name = "pnlEditor"
        pnlEditor.Size = New Size(414, 289)
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
        ' pgSettings
        '
        pgSettings.Dock = DockStyle.Fill
        pgSettings.Location = New Point(3, 298)
        pgSettings.Name = "pgSettings"
        pgSettings.PropertySort = PropertySort.Categorized
        pgSettings.Size = New Size(414, 403)
        pgSettings.TabIndex = 1
        pgSettings.ToolbarVisible = False
        '
        ' pnlView
        '
        pnlView.BackColor = Color.FromArgb(CType(CType(30, Byte), Integer), CType(CType(30, Byte), Integer), CType(CType(34, Byte), Integer))
        pnlView.Dock = DockStyle.Fill
        pnlView.Location = New Point(0, 0)
        pnlView.Name = "pnlView"
        pnlView.Size = New Size(774, 704)
        pnlView.TabIndex = 0
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
        dlgOpenProject.Filter = "Text to CNC project (*.prj)|*.prj|All files (*.*)|*.*"
        dlgOpenProject.Title = "Open Project"
        ' 
        ' dlgSaveProject
        ' 
        dlgSaveProject.DefaultExt = "prj"
        dlgSaveProject.Filter = "Text to CNC project (*.prj)|*.prj|All files (*.*)|*.*"
        dlgSaveProject.Title = "Save Project"
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
        Text = "Text to CNC Path"
        mnuMain.ResumeLayout(False)
        mnuMain.PerformLayout()
        stsMain.ResumeLayout(False)
        stsMain.PerformLayout()
        splitMain.Panel1.ResumeLayout(False)
        splitMain.Panel2.ResumeLayout(False)
        CType(splitMain, System.ComponentModel.ISupportInitialize).EndInit()
        splitMain.ResumeLayout(False)
        tlpLeft.ResumeLayout(False)
        pnlEditor.ResumeLayout(False)
        pnlEditor.PerformLayout()
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
    Friend WithEvents mnuToolpath As ToolStripMenuItem
    Friend WithEvents mnuToolpathGenerate As ToolStripMenuItem
    Friend WithEvents mnuToolpathAuto As ToolStripMenuItem
    Friend WithEvents stsMain As StatusStrip
    Friend WithEvents lblStatus As ToolStripStatusLabel
    Friend WithEvents lblStats As ToolStripStatusLabel
    Friend WithEvents splitMain As SplitContainer
    Friend WithEvents tlpLeft As TableLayoutPanel
    Friend WithEvents pnlEditor As Panel
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
    Friend WithEvents pgSettings As PropertyGrid
    Friend WithEvents pnlView As Panel
    Friend WithEvents tmrRegen As Timer
    Friend WithEvents dlgFont As FontDialog
    Friend WithEvents dlgSave As SaveFileDialog
    Friend WithEvents dlgOpen As OpenFileDialog
    Friend WithEvents dlgOpenProject As OpenFileDialog
    Friend WithEvents dlgSaveProject As SaveFileDialog

End Class
