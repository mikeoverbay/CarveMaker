' ============================================================================
'  JobStatusWindow.vb
'  The Status window (the Status button on the View toolbar): a table of
'  everything wrong with the job, one row per problem (where it is and what
'  it is), then the job's numbers. Copy All puts the table on the clipboard
'  as tab-separated text, so it pastes into a spreadsheet as rows and columns.
'  A tool window owned by the main form: closing it only hides it.
' ============================================================================

''' <summary>One row of the Status window.</summary>
Friend NotInheritable Class JobStatusRow
    Public ReadOnly IsProblem As Boolean
    Public ReadOnly Where As String
    Public ReadOnly What As String

    Public Sub New(isProblem As Boolean, where As String, what As String)
        Me.IsProblem = isProblem
        Me.Where = where
        Me.What = what
    End Sub
End Class

Friend NotInheritable Class JobStatusWindow
    Inherits Form

    Private ReadOnly _grid As New DataGridView()
    Private ReadOnly _summary As New Label()
    Private Shared ReadOnly ProblemInk As Color = Color.FromArgb(255, 92, 72)
    Private Shared ReadOnly FineInk As Color = Color.FromArgb(120, 220, 90)

    Public Sub New()
        Text = "Job Status"
        FormBorderStyle = FormBorderStyle.SizableToolWindow
        ShowInTaskbar = False
        StartPosition = FormStartPosition.Manual
        AutoScaleMode = AutoScaleMode.Font
        Font = New Font("Segoe UI", 9.5F)
        Size = New Size(Skin.Scaled(760, Me), Skin.Scaled(380, Me))
        MinimumSize = New Size(Skin.Scaled(420, Me), Skin.Scaled(200, Me))
        KeyPreview = True

        With _grid
            .Dock = DockStyle.Fill
            .ReadOnly = True
            .AllowUserToAddRows = False
            .AllowUserToDeleteRows = False
            .AllowUserToResizeRows = False
            .RowHeadersVisible = False
            .SelectionMode = DataGridViewSelectionMode.FullRowSelect
            .MultiSelect = True
            .AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells
            .ClipboardCopyMode = DataGridViewClipboardCopyMode.EnableAlwaysIncludeHeaderText
            .Columns.Add("Status", "Status")
            .Columns.Add("Where", "Where")
            .Columns.Add("What", "What")
            .Columns(0).Width = Skin.Scaled(90, Me)
            .Columns(1).Width = Skin.Scaled(170, Me)
            .Columns(2).AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
            .Columns(2).DefaultCellStyle.WrapMode = DataGridViewTriState.True
        End With

        Dim bottom As New TableLayoutPanel With {.Dock = DockStyle.Bottom, .ColumnCount = 3, .RowCount = 1, .AutoSize = True, .Padding = New Padding(8, 6, 8, 8)}
        bottom.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        bottom.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
        bottom.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
        _summary.AutoSize = True
        _summary.Anchor = AnchorStyles.Left
        Dim copyButton As New Button With {.Text = "Copy All", .Size = New Size(Skin.Scaled(110, Me), Skin.Scaled(32, Me))}
        Dim closeButton As New Button With {.Text = "Close", .Size = copyButton.Size}
        AddHandler copyButton.Click, Sub(s, e) CopyAll()
        AddHandler closeButton.Click, Sub(s, e) Hide()
        bottom.Controls.Add(_summary, 0, 0)
        bottom.Controls.Add(copyButton, 1, 0)
        bottom.Controls.Add(closeButton, 2, 0)
        Controls.Add(_grid)
        Controls.Add(bottom)
        CancelButton = closeButton
        Skin.Apply(Me)
    End Sub

    ''' <summary>Shows the rows (problems first, as given) and a one-line summary.</summary>
    Public Sub ShowRows(rows As IList(Of JobStatusRow), summary As String, summaryIsProblem As Boolean)
        _grid.SuspendLayout()
        _grid.Rows.Clear()
        For Each r In rows
            Dim i = _grid.Rows.Add(If(r.IsProblem, "Problem", "OK"), r.Where, r.What)
            Dim cell = _grid.Rows(i).Cells(0)
            cell.Style.ForeColor = If(r.IsProblem, ProblemInk, FineInk)
            cell.Style.SelectionForeColor = cell.Style.ForeColor
            cell.Style.Font = New Font(_grid.Font, FontStyle.Bold)
        Next
        _grid.ClearSelection()
        _grid.ResumeLayout()
        _summary.Text = summary
        _summary.ForeColor = If(summaryIsProblem, ProblemInk, FineInk)
    End Sub

    ''' <summary>The whole table as tab-separated text (pastes into a spreadsheet as rows and columns).</summary>
    Private Sub CopyAll()
        Dim sb As New Text.StringBuilder()
        sb.AppendLine("Status" & vbTab & "Where" & vbTab & "What")
        For Each row As DataGridViewRow In _grid.Rows
            sb.AppendLine(String.Join(vbTab, row.Cells.Cast(Of DataGridViewCell)().Select(Function(c) If(c.Value?.ToString(), "").Replace(vbTab, " ").Replace(vbCr, " ").Replace(vbLf, " "))))
        Next
        Try
            Clipboard.SetText(sb.ToString())
            _summary.Text = "Copied " & _grid.Rows.Count & " rows: paste them into a spreadsheet."
        Catch ex As Runtime.InteropServices.ExternalException
            _summary.Text = "The clipboard is busy; try again."
        End Try
    End Sub

    ''' <summary>Closing only hides it; the main form keeps it current.</summary>
    Protected Overrides Sub OnFormClosing(e As FormClosingEventArgs)
        If e.CloseReason = CloseReason.UserClosing Then
            e.Cancel = True
            Hide()
        End If
        MyBase.OnFormClosing(e)
    End Sub

    ''' <summary>Menu shortcuts of the main window work here too (as in Machine Settings).</summary>
    Protected Overrides Function ProcessCmdKey(ByRef msg As Message, keyData As Keys) As Boolean
        If MyBase.ProcessCmdKey(msg, keyData) Then Return True
        Dim item = frmMachineSettings.FindShortcut(Owner?.MainMenuStrip?.Items, keyData)
        If item Is Nothing Then Return False
        item.PerformClick()
        Return True
    End Function
End Class
