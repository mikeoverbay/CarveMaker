<Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()>
Partial Class frmMachineSettings
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
        pgMachine = New PropertyGrid()
        SuspendLayout()
        '
        ' pgMachine
        '
        pgMachine.Dock = DockStyle.Fill
        pgMachine.Location = New Point(0, 0)
        pgMachine.Name = "pgMachine"
        pgMachine.PropertySort = PropertySort.Categorized
        pgMachine.Size = New Size(404, 661)
        pgMachine.TabIndex = 0
        pgMachine.ToolbarVisible = False
        '
        ' frmMachineSettings
        '
        AutoScaleDimensions = New SizeF(7.0F, 15.0F)
        AutoScaleMode = AutoScaleMode.Font
        ClientSize = New Size(404, 661)
        Controls.Add(pgMachine)
        Font = New Font("Segoe UI", 9.0F)
        FormBorderStyle = FormBorderStyle.SizableToolWindow
        MinimumSize = New Size(320, 360)
        Name = "frmMachineSettings"
        ShowInTaskbar = False
        StartPosition = FormStartPosition.Manual
        Text = "Machine Settings"
        ResumeLayout(False)
    End Sub

    Friend WithEvents pgMachine As PropertyGrid
End Class
