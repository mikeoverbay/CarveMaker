<Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()>
Partial Class frmToolLibrary
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
        tlpMain = New TableLayoutPanel()
        pnlList = New Panel()
        tvTools = New TreeView()
        imlTypes = New ImageList(components)
        tsList = New ToolStrip()
        tsbNew = New ToolStripDropDownButton()
        tsbDuplicate = New ToolStripButton()
        tsbDelete = New ToolStripButton()
        tlpEdit = New TableLayoutPanel()
        tlpFields = New TableLayoutPanel()
        lblName = New Label()
        txtName = New TextBox()
        chkAutoName = New CheckBox()
        lblType = New Label()
        cboType = New ComboBox()
        lblDiameter = New Label()
        txtDiameter = New TextBox()
        lblDiameterHint = New Label()
        lblLength = New Label()
        txtLength = New TextBox()
        lblLengthHint = New Label()
        lblRadius = New Label()
        txtRadius = New TextBox()
        lblRadiusHint = New Label()
        lblAngle = New Label()
        txtAngle = New TextBox()
        lblAngleHint = New Label()
        drawTool = New ToolDrawing()
        pnlButtons = New Panel()
        lblMessage = New Label()
        btnOK = New Button()
        pnlButtonGap = New Panel()
        btnCancel = New Button()
        tlpMain.SuspendLayout()
        pnlList.SuspendLayout()
        tsList.SuspendLayout()
        tlpEdit.SuspendLayout()
        tlpFields.SuspendLayout()
        pnlButtons.SuspendLayout()
        SuspendLayout()
        '
        ' tlpMain
        '
        tlpMain.ColumnCount = 2
        tlpMain.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 290.0F))
        tlpMain.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0F))
        tlpMain.Controls.Add(pnlList, 0, 0)
        tlpMain.Controls.Add(tlpEdit, 1, 0)
        tlpMain.Controls.Add(pnlButtons, 0, 1)
        tlpMain.Dock = DockStyle.Fill
        tlpMain.Location = New Point(0, 0)
        tlpMain.Name = "tlpMain"
        tlpMain.Padding = New Padding(6)
        tlpMain.RowCount = 2
        tlpMain.RowStyles.Add(New RowStyle(SizeType.Percent, 100.0F))
        tlpMain.RowStyles.Add(New RowStyle(SizeType.Absolute, 48.0F))
        tlpMain.SetColumnSpan(pnlButtons, 2)
        tlpMain.Size = New Size(944, 621)
        tlpMain.TabIndex = 0
        '
        ' pnlList
        '
        pnlList.BorderStyle = BorderStyle.FixedSingle
        pnlList.Controls.Add(tvTools)
        pnlList.Controls.Add(tsList)
        pnlList.Dock = DockStyle.Fill
        pnlList.Location = New Point(9, 9)
        pnlList.Name = "pnlList"
        pnlList.Size = New Size(284, 555)
        pnlList.TabIndex = 0
        '
        ' tvTools
        '
        tvTools.BorderStyle = BorderStyle.None
        tvTools.Dock = DockStyle.Fill
        tvTools.HideSelection = False
        tvTools.ImageIndex = 0
        tvTools.ImageList = imlTypes
        tvTools.ItemHeight = 22
        tvTools.Location = New Point(0, 25)
        tvTools.Name = "tvTools"
        tvTools.SelectedImageIndex = 0
        tvTools.Size = New Size(282, 528)
        tvTools.TabIndex = 1
        '
        ' imlTypes
        '
        imlTypes.ColorDepth = ColorDepth.Depth32Bit
        imlTypes.ImageSize = New Size(16, 16)
        imlTypes.TransparentColor = Color.Transparent
        '
        ' tsList
        '
        tsList.GripStyle = ToolStripGripStyle.Hidden
        tsList.Items.AddRange(New ToolStripItem() {tsbNew, tsbDuplicate, tsbDelete})
        tsList.Location = New Point(0, 0)
        tsList.Name = "tsList"
        tsList.RenderMode = ToolStripRenderMode.System
        tsList.Size = New Size(282, 25)
        tsList.TabIndex = 0
        '
        ' tsbNew
        '
        tsbNew.DisplayStyle = ToolStripItemDisplayStyle.Text
        tsbNew.Name = "tsbNew"
        tsbNew.Size = New Size(44, 22)
        tsbNew.Text = "New"
        tsbNew.ToolTipText = "Add a tool of a type"
        '
        ' tsbDuplicate
        '
        tsbDuplicate.DisplayStyle = ToolStripItemDisplayStyle.Text
        tsbDuplicate.Name = "tsbDuplicate"
        tsbDuplicate.Size = New Size(61, 22)
        tsbDuplicate.Text = "Duplicate"
        tsbDuplicate.ToolTipText = "Copy the selected tool"
        '
        ' tsbDelete
        '
        tsbDelete.DisplayStyle = ToolStripItemDisplayStyle.Text
        tsbDelete.Name = "tsbDelete"
        tsbDelete.Size = New Size(44, 22)
        tsbDelete.Text = "Delete"
        tsbDelete.ToolTipText = "Remove the selected tool from the library"
        '
        ' tlpEdit
        '
        tlpEdit.ColumnCount = 1
        tlpEdit.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0F))
        tlpEdit.Controls.Add(tlpFields, 0, 0)
        tlpEdit.Controls.Add(drawTool, 0, 1)
        tlpEdit.Dock = DockStyle.Fill
        tlpEdit.Location = New Point(299, 9)
        tlpEdit.Name = "tlpEdit"
        tlpEdit.RowCount = 2
        tlpEdit.RowStyles.Add(New RowStyle())
        tlpEdit.RowStyles.Add(New RowStyle(SizeType.Percent, 100.0F))
        tlpEdit.Size = New Size(636, 555)
        tlpEdit.TabIndex = 1
        '
        ' tlpFields
        '
        tlpFields.AutoSize = True
        tlpFields.ColumnCount = 3
        tlpFields.ColumnStyles.Add(New ColumnStyle())
        tlpFields.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 150.0F))
        tlpFields.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0F))
        tlpFields.Controls.Add(lblName, 0, 0)
        tlpFields.Controls.Add(txtName, 1, 0)
        tlpFields.Controls.Add(chkAutoName, 1, 1)
        tlpFields.Controls.Add(lblType, 0, 2)
        tlpFields.Controls.Add(cboType, 1, 2)
        tlpFields.Controls.Add(lblDiameter, 0, 3)
        tlpFields.Controls.Add(txtDiameter, 1, 3)
        tlpFields.Controls.Add(lblDiameterHint, 2, 3)
        tlpFields.Controls.Add(lblLength, 0, 4)
        tlpFields.Controls.Add(txtLength, 1, 4)
        tlpFields.Controls.Add(lblLengthHint, 2, 4)
        tlpFields.Controls.Add(lblRadius, 0, 5)
        tlpFields.Controls.Add(txtRadius, 1, 5)
        tlpFields.Controls.Add(lblRadiusHint, 2, 5)
        tlpFields.Controls.Add(lblAngle, 0, 6)
        tlpFields.Controls.Add(txtAngle, 1, 6)
        tlpFields.Controls.Add(lblAngleHint, 2, 6)
        tlpFields.Dock = DockStyle.Fill
        tlpFields.Location = New Point(3, 3)
        tlpFields.Name = "tlpFields"
        tlpFields.RowCount = 7
        tlpFields.RowStyles.Add(New RowStyle())
        tlpFields.RowStyles.Add(New RowStyle())
        tlpFields.RowStyles.Add(New RowStyle())
        tlpFields.RowStyles.Add(New RowStyle())
        tlpFields.RowStyles.Add(New RowStyle())
        tlpFields.RowStyles.Add(New RowStyle())
        tlpFields.RowStyles.Add(New RowStyle())
        tlpFields.SetColumnSpan(txtName, 2)
        tlpFields.SetColumnSpan(chkAutoName, 2)
        tlpFields.SetColumnSpan(cboType, 2)
        tlpFields.Size = New Size(630, 203)
        tlpFields.TabIndex = 0
        '
        ' lblName
        '
        lblName.Anchor = AnchorStyles.Left
        lblName.AutoSize = True
        lblName.Name = "lblName"
        lblName.Text = "Name"
        '
        ' txtName
        '
        txtName.Anchor = AnchorStyles.Left Or AnchorStyles.Right
        txtName.Name = "txtName"
        txtName.Size = New Size(470, 23)
        txtName.TabIndex = 0
        '
        ' chkAutoName
        '
        chkAutoName.AutoSize = True
        chkAutoName.Name = "chkAutoName"
        chkAutoName.TabIndex = 1
        chkAutoName.Text = "Name from the numbers"
        chkAutoName.UseVisualStyleBackColor = True
        '
        ' lblType
        '
        lblType.Anchor = AnchorStyles.Left
        lblType.AutoSize = True
        lblType.Name = "lblType"
        lblType.Text = "Type"
        '
        ' cboType
        '
        cboType.DropDownStyle = ComboBoxStyle.DropDownList
        cboType.Name = "cboType"
        cboType.Size = New Size(230, 23)
        cboType.TabIndex = 2
        '
        ' lblDiameter
        '
        lblDiameter.Anchor = AnchorStyles.Left
        lblDiameter.AutoSize = True
        lblDiameter.Name = "lblDiameter"
        lblDiameter.Text = "Diameter (in)"
        '
        ' txtDiameter
        '
        txtDiameter.Anchor = AnchorStyles.Left Or AnchorStyles.Right
        txtDiameter.Name = "txtDiameter"
        txtDiameter.Size = New Size(144, 23)
        txtDiameter.TabIndex = 3
        '
        ' lblDiameterHint
        '
        lblDiameterHint.Anchor = AnchorStyles.Left
        lblDiameterHint.AutoSize = True
        lblDiameterHint.ForeColor = SystemColors.GrayText
        lblDiameterHint.Name = "lblDiameterHint"
        '
        ' lblLength
        '
        lblLength.Anchor = AnchorStyles.Left
        lblLength.AutoSize = True
        lblLength.Name = "lblLength"
        lblLength.Text = "Flute length (in)"
        '
        ' txtLength
        '
        txtLength.Anchor = AnchorStyles.Left Or AnchorStyles.Right
        txtLength.Name = "txtLength"
        txtLength.Size = New Size(144, 23)
        txtLength.TabIndex = 4
        '
        ' lblLengthHint
        '
        lblLengthHint.Anchor = AnchorStyles.Left
        lblLengthHint.AutoSize = True
        lblLengthHint.ForeColor = SystemColors.GrayText
        lblLengthHint.Name = "lblLengthHint"
        '
        ' lblRadius
        '
        lblRadius.Anchor = AnchorStyles.Left
        lblRadius.AutoSize = True
        lblRadius.Name = "lblRadius"
        lblRadius.Text = "End radius (in)"
        '
        ' txtRadius
        '
        txtRadius.Anchor = AnchorStyles.Left Or AnchorStyles.Right
        txtRadius.Name = "txtRadius"
        txtRadius.Size = New Size(144, 23)
        txtRadius.TabIndex = 5
        '
        ' lblRadiusHint
        '
        lblRadiusHint.Anchor = AnchorStyles.Left
        lblRadiusHint.AutoSize = True
        lblRadiusHint.ForeColor = SystemColors.GrayText
        lblRadiusHint.Name = "lblRadiusHint"
        '
        ' lblAngle
        '
        lblAngle.Anchor = AnchorStyles.Left
        lblAngle.AutoSize = True
        lblAngle.Name = "lblAngle"
        lblAngle.Text = "Angle (deg)"
        '
        ' txtAngle
        '
        txtAngle.Anchor = AnchorStyles.Left Or AnchorStyles.Right
        txtAngle.Name = "txtAngle"
        txtAngle.Size = New Size(144, 23)
        txtAngle.TabIndex = 6
        '
        ' lblAngleHint
        '
        lblAngleHint.Anchor = AnchorStyles.Left
        lblAngleHint.AutoSize = True
        lblAngleHint.ForeColor = SystemColors.GrayText
        lblAngleHint.Name = "lblAngleHint"
        '
        ' drawTool
        '
        drawTool.BackColor = Color.White
        drawTool.Dock = DockStyle.Fill
        drawTool.Location = New Point(3, 212)
        drawTool.Name = "drawTool"
        drawTool.Size = New Size(630, 340)
        drawTool.TabIndex = 1
        '
        ' pnlButtons
        '
        pnlButtons.Controls.Add(lblMessage)
        pnlButtons.Controls.Add(btnOK)
        pnlButtons.Controls.Add(pnlButtonGap)
        pnlButtons.Controls.Add(btnCancel)
        pnlButtons.Dock = DockStyle.Fill
        pnlButtons.Location = New Point(9, 570)
        pnlButtons.Name = "pnlButtons"
        pnlButtons.Padding = New Padding(0, 8, 0, 6)
        pnlButtons.Size = New Size(926, 42)
        pnlButtons.TabIndex = 2
        '
        ' lblMessage
        '
        lblMessage.Dock = DockStyle.Fill
        lblMessage.Name = "lblMessage"
        lblMessage.TextAlign = ContentAlignment.MiddleLeft
        '
        ' btnOK
        '
        btnOK.Dock = DockStyle.Right
        btnOK.Name = "btnOK"
        btnOK.Size = New Size(120, 28)
        btnOK.TabIndex = 0
        btnOK.Text = "Save"
        btnOK.UseVisualStyleBackColor = True
        '
        ' pnlButtonGap
        '
        pnlButtonGap.Dock = DockStyle.Right
        pnlButtonGap.Name = "pnlButtonGap"
        pnlButtonGap.Size = New Size(8, 28)
        '
        ' btnCancel
        '
        btnCancel.DialogResult = DialogResult.Cancel
        btnCancel.Dock = DockStyle.Right
        btnCancel.Name = "btnCancel"
        btnCancel.Size = New Size(100, 28)
        btnCancel.TabIndex = 1
        btnCancel.Text = "Cancel"
        btnCancel.UseVisualStyleBackColor = True
        '
        ' frmToolLibrary
        '
        AutoScaleDimensions = New SizeF(7.0F, 15.0F)
        AutoScaleMode = AutoScaleMode.Font
        CancelButton = btnCancel
        ClientSize = New Size(944, 621)
        Controls.Add(tlpMain)
        Font = New Font("Segoe UI", 9.0F)
        MinimizeBox = False
        MinimumSize = New Size(780, 540)
        Name = "frmToolLibrary"
        ShowIcon = False
        ShowInTaskbar = False
        StartPosition = FormStartPosition.CenterParent
        Text = "Tool Library"
        tlpMain.ResumeLayout(False)
        pnlList.ResumeLayout(False)
        pnlList.PerformLayout()
        tsList.ResumeLayout(False)
        tsList.PerformLayout()
        tlpEdit.ResumeLayout(False)
        tlpEdit.PerformLayout()
        tlpFields.ResumeLayout(False)
        tlpFields.PerformLayout()
        pnlButtons.ResumeLayout(False)
        ResumeLayout(False)
    End Sub

    Friend WithEvents tlpMain As TableLayoutPanel
    Friend WithEvents pnlList As Panel
    Friend WithEvents tvTools As TreeView
    Friend WithEvents imlTypes As ImageList
    Friend WithEvents tsList As ToolStrip
    Friend WithEvents tsbNew As ToolStripDropDownButton
    Friend WithEvents tsbDuplicate As ToolStripButton
    Friend WithEvents tsbDelete As ToolStripButton
    Friend WithEvents tlpEdit As TableLayoutPanel
    Friend WithEvents tlpFields As TableLayoutPanel
    Friend WithEvents lblName As Label
    Friend WithEvents txtName As TextBox
    Friend WithEvents chkAutoName As CheckBox
    Friend WithEvents lblType As Label
    Friend WithEvents cboType As ComboBox
    Friend WithEvents lblDiameter As Label
    Friend WithEvents txtDiameter As TextBox
    Friend WithEvents lblDiameterHint As Label
    Friend WithEvents lblLength As Label
    Friend WithEvents txtLength As TextBox
    Friend WithEvents lblLengthHint As Label
    Friend WithEvents lblRadius As Label
    Friend WithEvents txtRadius As TextBox
    Friend WithEvents lblRadiusHint As Label
    Friend WithEvents lblAngle As Label
    Friend WithEvents txtAngle As TextBox
    Friend WithEvents lblAngleHint As Label
    Friend WithEvents drawTool As ToolDrawing
    Friend WithEvents pnlButtons As Panel
    Friend WithEvents lblMessage As Label
    Friend WithEvents btnOK As Button
    Friend WithEvents pnlButtonGap As Panel
    Friend WithEvents btnCancel As Button
End Class
