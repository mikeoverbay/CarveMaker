' ============================================================================
'  frmMain.Status.vb
'  The Status button on the View toolbar, next to the Toolpath / Simulation
'  switch: green when the job is fine, red and blinking when something is
'  wrong (a toolpath warning such as a bit too shallow for the depth or text
'  off the blank, a drawing that cannot be carved, settings out of range, a
'  failed generation, rapids inside the extra stock), amber while a toolpath
'  is computed, grey with nothing to cut. Clicking it opens the Status window
'  (JobStatusWindow.vb): every problem as a row of a table, then the job's
'  numbers.
' ============================================================================

Imports System.Globalization

Partial Class frmMain

    Private Enum JobState
        Empty
        Busy
        Fine
        Problem
    End Enum

    Private _tsbStatus As ToolStripButton
    Private _jobState As JobState = JobState.Empty
    Private _stateBeforeBusy As JobState = JobState.Empty
    Private ReadOnly _statusRows As New List(Of JobStatusRow)
    Private _statusIcons As Dictionary(Of JobState, Image)
    Private _statusIconDim As Image
    Private _statusBlink As Timer
    Private _blinkLit As Boolean
    Private _statusWindow As JobStatusWindow

    ''' <summary>Adds the Status button after the Toolpath / Simulation switch (from SetupSkin, before styling).</summary>
    Private Sub SetupStatusButton(px As Integer)
        _statusIcons = New Dictionary(Of JobState, Image) From {
            {JobState.Empty, Skin.Icon(IconKind.StatusOff, px)},
            {JobState.Busy, Skin.Icon(IconKind.StatusBusy, px)},
            {JobState.Fine, Skin.Icon(IconKind.StatusOk, px)},
            {JobState.Problem, Skin.Icon(IconKind.StatusProblem, px)}}
        _statusIconDim = Skin.Icon(IconKind.StatusProblemDim, px)
        _tsbStatus = New ToolStripButton("Status") With {.Name = "tsbStatus", .DisplayStyle = ToolStripItemDisplayStyle.ImageAndText, .Image = _statusIcons(JobState.Empty)}
        tsView.Items.Insert(tsView.Items.IndexOf(tsbSimToggle) + 1, _tsbStatus)
        AddHandler _tsbStatus.Click, Sub(o, ev) ToggleStatusWindow()
        _statusBlink = New Timer With {.Interval = 450}
        AddHandler _statusBlink.Tick, Sub(o, ev)
                                          _blinkLit = Not _blinkLit
                                          _tsbStatus.Image = If(_blinkLit, _statusIcons(JobState.Problem), _statusIconDim)
                                      End Sub
        ShowJobState()
    End Sub

    ''' <summary>A toolpath is being computed (amber), or that finished without a new result (back as it was).</summary>
    Private Sub SetJobBusy(busy As Boolean)
        If busy Then
            If _jobState <> JobState.Busy Then _stateBeforeBusy = _jobState
            _jobState = JobState.Busy
        ElseIf _jobState = JobState.Busy Then
            _jobState = _stateBeforeBusy
        End If
        ShowJobState()
    End Sub

    ''' <summary>New status: problems (each a row) and the job's numbers. Nothing to cut and no problems = grey.</summary>
    Private Sub SetJobStatus(problems As IEnumerable(Of String), tp As Toolpath)
        _statusRows.Clear()
        For Each p In problems
            _statusRows.Add(ProblemRow(p))
        Next
        If tp IsNot Nothing AndAlso Not tp.IsEmpty Then _statusRows.AddRange(JobNumbers(tp))
        Dim anyProblem = _statusRows.Any(Function(r) r.IsProblem)
        _jobState = If(anyProblem, JobState.Problem, If(tp Is Nothing OrElse tp.IsEmpty, JobState.Empty, JobState.Fine))
        ShowJobState()
    End Sub

    ''' <summary>The extra stock changed (it does not regenerate): its row follows the current settings.</summary>
    Private Sub RefreshExtraStockStatus()
        _statusRows.RemoveAll(Function(r) r.IsProblem AndAlso r.Where = "Blank (extra stock)")
        Dim note = _settings.ExtraStockNote()
        If note IsNot Nothing Then _statusRows.Insert(0, ProblemRow(note))
        If _jobState <> JobState.Busy Then
            Dim anyProblem = _statusRows.Any(Function(r) r.IsProblem)
            _jobState = If(anyProblem, JobState.Problem, If(_toolpath Is Nothing OrElse _toolpath.IsEmpty, JobState.Empty, JobState.Fine))
        Else
            _stateBeforeBusy = If(_statusRows.Any(Function(r) r.IsProblem), JobState.Problem, _stateBeforeBusy)
        End If
        ShowJobState()
    End Sub

    ''' <summary>A problem message as a row: where it is (a drawing, the text, the blank, the settings) and what.</summary>
    Private Function ProblemRow(note As String) As JobStatusRow
        For Each o In _objects.OrderByDescending(Function(x) x.Name.Length)
            If note.StartsWith(o.Name & ": ", StringComparison.Ordinal) Then Return New JobStatusRow(True, o.Name, note.Substring(o.Name.Length + 2))
        Next
        For Each prefix In {"Settings: ", "Error: ", "Roughing: ", "Finishing: "}
            If note.StartsWith(prefix, StringComparison.Ordinal) Then Return New JobStatusRow(True, prefix.TrimEnd(" "c, ":"c), note.Substring(prefix.Length))
        Next
        If note.StartsWith("Extra stock", StringComparison.Ordinal) Then Return New JobStatusRow(True, "Blank (extra stock)", note)
        If note.StartsWith("Text ", StringComparison.Ordinal) OrElse note.StartsWith("Flat depth", StringComparison.Ordinal) Then Return New JobStatusRow(True, "Text", note)
        Return New JobStatusRow(True, "Job", note)
    End Function

    ''' <summary>The job's numbers, as rows after the problems.</summary>
    Private Shared Function JobNumbers(tp As Toolpath) As List(Of JobStatusRow)
        Dim ci = CultureInfo.InvariantCulture
        Dim rows As New List(Of JobStatusRow)
        Dim add = Sub(where As String, what As String) rows.Add(New JobStatusRow(False, where, what))
        If tp.RegionCount > 0 Then add("Objects", tp.RegionCount.ToString(ci) & " (each letter, drawing region, pocket, profile or 3D model)")
        add("Passes", (tp.Contours.Count + tp.ReliefPassCount).ToString(ci))
        add("Size", String.Format(ci, "{0:0.00}"" x {1:0.00}""", tp.Width, tp.Height))
        add("Deepest cut", String.Format(ci, "{0:0.000}""", -tp.MinZ))
        add("Cut length", String.Format(ci, "{0:0.0}"" cutting, {1:0.0}"" rapid", tp.CutLength, tp.RapidLength))
        add("Time", String.Format(ci, "about {0:0.0} min", tp.EstimatedMinutes))
        Dim groups = GCodeWriter.ToolGroups(tp)
        For i = 0 To groups.Count - 1
            Dim g = groups(i)
            add(If(groups.Count = 1, "Tool", "Program " & (i + 1).ToString(ci)),
                String.Format(ci, "{0}: {1} (about {2:0.0} min)", If(g.Tool?.DisplayName(), "tool"), g.Operations, g.EstimatedMinutes))
        Next
        Return rows
    End Function

    ''' <summary>The button's picture, blinking and tip; the open window's table.</summary>
    Private Sub ShowJobState()
        If _tsbStatus Is Nothing Then Return
        Dim problems = _statusRows.Where(Function(r) r.IsProblem).Count()
        Dim summary As String
        Select Case _jobState
            Case JobState.Busy : summary = "Computing the toolpath..."
            Case JobState.Problem : summary = problems & If(problems = 1, " problem", " problems") & ": click for the list."
            Case JobState.Fine : summary = "No problems: click for the job's numbers."
            Case Else : summary = "Nothing to cut yet."
        End Select
        _tsbStatus.ToolTipText = "Status: " & summary
        If _jobState = JobState.Problem Then
            _blinkLit = True
            _tsbStatus.Image = _statusIcons(JobState.Problem)
            _statusBlink.Start()
        Else
            _statusBlink.Stop()
            _tsbStatus.Image = _statusIcons(_jobState)
        End If
        If _statusWindow IsNot Nothing AndAlso _statusWindow.Visible Then
            _statusWindow.ShowRows(_statusRows, If(_jobState = JobState.Problem, problems & If(problems = 1, " problem", " problems"), summary.Replace(": click for the job's numbers.", ".")),
                                   _jobState = JobState.Problem)
        End If
    End Sub

    ''' <summary>Opens the Status window under the button (or hides it).</summary>
    Private Sub ToggleStatusWindow()
        If _statusWindow Is Nothing Then
            _statusWindow = New JobStatusWindow() With {.Owner = Me}
        End If
        If _statusWindow.Visible Then
            _statusWindow.Hide()
            Return
        End If
        Dim owner = If(_tsbStatus.Owner, CType(tsView, ToolStrip))
        Dim at = owner.PointToScreen(New Point(_tsbStatus.Bounds.Left, _tsbStatus.Bounds.Bottom + 4))
        Dim area = Screen.FromPoint(at).WorkingArea
        _statusWindow.Location = New Point(Math.Max(area.Left, Math.Min(at.X, area.Right - _statusWindow.Width)),
                                           Math.Max(area.Top, Math.Min(at.Y, area.Bottom - _statusWindow.Height)))
        _statusWindow.Show()
        ShowJobState()
    End Sub
End Class
