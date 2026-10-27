' ============================================================================
'  ConfirmDialog.vb
'  A small dark question box (the system message box stays light): a picture,
'  the question, and two skinned buttons. The safe answer is the default, so a
'  stray Enter or Esc never does the risky thing.
' ============================================================================

Friend NotInheritable Class ConfirmDialog
    Inherits Form

    Public Sub New(title As String, message As String, doText As String, keepText As String, icon As IconKind)
        Text = title
        FormBorderStyle = FormBorderStyle.FixedDialog
        MaximizeBox = False
        MinimizeBox = False
        ShowInTaskbar = False
        StartPosition = FormStartPosition.CenterParent
        AutoScaleMode = AutoScaleMode.Font
        Font = New Font("Segoe UI", 9.5F)
        AutoSize = True
        AutoSizeMode = AutoSizeMode.GrowAndShrink

        ' One auto-sized layout (docked panels do not give an auto-sized form their width).
        Dim root As New TableLayoutPanel With {.ColumnCount = 1, .RowCount = 2, .AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink, .Location = Point.Empty}
        Dim body As New TableLayoutPanel With {.ColumnCount = 2, .RowCount = 1, .AutoSize = True, .Padding = New Padding(16, 16, 20, 8)}
        body.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
        body.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
        Dim picture As New PictureBox With {.Image = Icons.Make(icon, Skin.Scaled(32, Me)), .SizeMode = PictureBoxSizeMode.AutoSize, .Margin = New Padding(0, 2, 14, 0)}
        Dim question As New Label With {.Text = message, .AutoSize = True, .MaximumSize = New Size(Skin.Scaled(400, Me), 0), .Margin = New Padding(0, 4, 0, 8)}
        body.Controls.Add(picture, 0, 0)
        body.Controls.Add(question, 1, 0)

        Dim buttons As New FlowLayoutPanel With {.FlowDirection = FlowDirection.RightToLeft, .AutoSize = True, .Anchor = AnchorStyles.Right, .Padding = New Padding(12, 4, 12, 12), .WrapContents = False}
        Dim keep As New Button With {.Text = keepText, .DialogResult = DialogResult.No, .Size = New Size(Skin.Scaled(110, Me), Skin.Scaled(32, Me))}
        Dim doIt As New Button With {.Text = doText, .DialogResult = DialogResult.Yes, .Size = keep.Size}
        buttons.Controls.Add(keep)
        buttons.Controls.Add(doIt)
        root.Controls.Add(body, 0, 0)
        root.Controls.Add(buttons, 0, 1)
        Controls.Add(root)
        AcceptButton = keep
        CancelButton = keep
        ActiveControl = keep
        Skin.Apply(Me)
    End Sub

    ''' <summary>Asks; True only when the risky button was pressed.</summary>
    Public Shared Function Ask(owner As IWin32Window, title As String, message As String, doText As String, keepText As String,
                               Optional icon As IconKind = IconKind.Remove) As Boolean
        Using d As New ConfirmDialog(title, message, doText, keepText, icon)
            Return d.ShowDialog(owner) = DialogResult.Yes
        End Using
    End Function
End Class
