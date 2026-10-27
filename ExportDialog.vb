' ============================================================================
'  ExportDialog.vb
'  Asked when a job with more than one toolpath is exported: one program per
'  tool (a new program only where the tool changes) or one program per
'  toolpath (each to run, re-run or skip on its own). The choice is kept in
'  the user's preferences for the next export.
' ============================================================================

Friend NotInheritable Class ExportDialog
    Inherits Form

    Private ReadOnly _byTool As New RadioButton()
    Private ReadOnly _byToolpath As New RadioButton()

    ''' <summary>One program per toolpath was chosen.</summary>
    Public ReadOnly Property PerToolpath As Boolean
        Get
            Return _byToolpath.Checked
        End Get
    End Property

    Public Sub New(toolPrograms As Integer, toolpathPrograms As Integer, perToolpath As Boolean)
        Text = "Export G-code"
        FormBorderStyle = FormBorderStyle.FixedDialog
        MaximizeBox = False
        MinimizeBox = False
        ShowInTaskbar = False
        StartPosition = FormStartPosition.CenterParent
        AutoScaleMode = AutoScaleMode.Font
        Font = New Font("Segoe UI", 9.5F)
        AutoSize = True
        AutoSizeMode = AutoSizeMode.GrowAndShrink

        Dim wide = Skin.Scaled(440, Me)
        ' One auto-sized layout (docked panels do not give an auto-sized form their width).
        Dim root As New TableLayoutPanel With {.ColumnCount = 1, .RowCount = 2, .AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink, .Location = Point.Empty}
        Dim body As New TableLayoutPanel With {.ColumnCount = 1, .AutoSize = True, .Padding = New Padding(18, 16, 22, 4)}
        Dim hint = Function(text As String) New Label With {.Text = text, .AutoSize = True, .MaximumSize = New Size(wide, 0),
                                                            .ForeColor = SystemColors.GrayText, .Margin = New Padding(22, 0, 0, 12)}
        body.Controls.Add(New Label With {.Text = "How should this job be saved?", .AutoSize = True, .Margin = New Padding(0, 0, 0, 12)})
        _byTool.Text = "One program per tool: " & Count(toolPrograms)
        _byTool.AutoSize = True
        body.Controls.Add(_byTool)
        body.Controls.Add(hint(If(toolPrograms > 1,
            "A new program only where the tool changes: fit the next tool and re-zero Z between them.",
            "Every toolpath in one program (they all use the same tool).")))
        _byToolpath.Text = "One program per toolpath: " & Count(toolpathPrograms)
        _byToolpath.AutoSize = True
        body.Controls.Add(_byToolpath)
        body.Controls.Add(hint("Each toolpath in its own program: every pocket and profile drawing, each 3D pass, and the V-carve (shapes V-carved with the same bit are carved as one, so they share a program). Numbered in the order to run them, so each can be run, run again or skipped on its own. Where the next one uses the same tool, keep the tool and the Z zero."))
        _byTool.Checked = Not perToolpath
        _byToolpath.Checked = perToolpath

        Dim buttons As New FlowLayoutPanel With {.FlowDirection = FlowDirection.RightToLeft, .AutoSize = True, .Anchor = AnchorStyles.Right, .Padding = New Padding(12, 4, 12, 12), .WrapContents = False}
        Dim cancel As New Button With {.Text = "Cancel", .DialogResult = DialogResult.Cancel, .Size = New Size(Skin.Scaled(110, Me), Skin.Scaled(32, Me))}
        Dim export As New Button With {.Text = "Export...", .DialogResult = DialogResult.OK, .Size = cancel.Size}
        buttons.Controls.Add(cancel)
        buttons.Controls.Add(export)
        root.Controls.Add(body, 0, 0)
        root.Controls.Add(buttons, 0, 1)
        Controls.Add(root)
        AcceptButton = export
        CancelButton = cancel
        ActiveControl = If(perToolpath, _byToolpath, _byTool)
        Skin.Apply(Me)
    End Sub

    Private Shared Function Count(n As Integer) As String
        Return n.ToString(Globalization.CultureInfo.InvariantCulture) & If(n = 1, " program", " programs")
    End Function
End Class
