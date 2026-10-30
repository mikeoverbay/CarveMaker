' ============================================================================
'  frmMain.Moves.vb
'  What the toolpath lines show: the Cuts and Rapids switches on the View
'  toolbar (with Show Cutting Moves / Show Rapid Moves in the View menu), and
'  each toolpath's own Show in view check box (Machining.ShowInView, in the
'  Objects and Text tabs), which Hide / Show All (View toolbar, Draw menu)
'  untick and tick. All display only: nothing is generated again, and hidden
'  lines are still cut, exported and simulated. The drawing itself (its
'  Visible row: carved or not) is left alone.
' ============================================================================

Partial Class frmMain

    Private _tsbShowCuts, _tsbShowRapids As ToolStripButton
    Private _mnuViewCuts As ToolStripMenuItem

    ''' <summary>The Cuts and Rapids switches after the Status button, and the View menu's cutting moves item (from SetupSkin, before styling).</summary>
    Private Sub SetupMoveButtons(px As Integer)
        _tsbShowCuts = New ToolStripButton("Cuts") With {
            .Name = "tsbShowCuts", .CheckOnClick = True, .Checked = True, .DisplayStyle = ToolStripItemDisplayStyle.ImageAndText,
            .Image = Skin.Icon(IconKind.CutMoves, px), .ToolTipText = "Show the cutting moves (the toolpath lines)"}
        _tsbShowRapids = New ToolStripButton("Rapids") With {
            .Name = "tsbShowRapids", .CheckOnClick = True, .Checked = mnuViewRapids.Checked, .DisplayStyle = ToolStripItemDisplayStyle.ImageAndText,
            .Image = Skin.Icon(IconKind.RapidMoves, px), .ToolTipText = "Show the rapid moves (red: the tool travelling above the stock)"}
        Dim at = tsView.Items.IndexOf(_tsbStatus) + 1
        tsView.Items.Insert(at, New ToolStripSeparator() With {.Name = "tssViewMoves"})
        tsView.Items.Insert(at + 1, _tsbShowCuts)
        tsView.Items.Insert(at + 2, _tsbShowRapids)

        _mnuViewCuts = New ToolStripMenuItem("Show &Cutting Moves") With {.Name = "mnuViewCuts", .Checked = True, .CheckOnClick = True}
        mnuView.DropDownItems.Insert(mnuView.DropDownItems.IndexOf(mnuViewRapids), _mnuViewCuts)

        ' The toolbar switches and the menu items follow each other.
        AddHandler _tsbShowCuts.CheckedChanged, Sub(o, ev) If _mnuViewCuts.Checked <> _tsbShowCuts.Checked Then _mnuViewCuts.Checked = _tsbShowCuts.Checked
        AddHandler _tsbShowRapids.CheckedChanged, Sub(o, ev) If mnuViewRapids.Checked <> _tsbShowRapids.Checked Then mnuViewRapids.Checked = _tsbShowRapids.Checked
        AddHandler _mnuViewCuts.CheckedChanged, Sub(o, ev)
                                                    If _glView IsNot Nothing Then _glView.ShowCuts = _mnuViewCuts.Checked
                                                    If _tsbShowCuts.Checked <> _mnuViewCuts.Checked Then _tsbShowCuts.Checked = _mnuViewCuts.Checked
                                                End Sub
        _glView.ToolpathShown = AddressOf IsToolpathShown

        ' Hide and Show All are about the toolpaths in the view.
        mnuDrawHide.Text = "&Hide Toolpath"
        mnuDrawShowAll.Text = "Show A&ll Toolpaths"
        tsbHide.ToolTipText = "Hide the selected drawing's toolpath in the view; it is still cut (Ctrl+H)"
        tsbShowAll.ToolTipText = "Show every toolpath in the view again (Ctrl+Shift+H)"
    End Sub

    ''' <summary>The toolpaths of what is selected: the text's, or a drawing's (a 3D model's two).</summary>
    Private Function SelectedToolpaths() As List(Of Machining)
        Dim list As New List(Of Machining)
        Dim idx = If(_glView Is Nothing, -1, _glView.SelectedObject)
        If idx = ToolpathView.TextSelection Then
            If _settings.TextMachining IsNot Nothing Then list.Add(_settings.TextMachining)
        ElseIf idx >= 0 AndAlso idx < _objects.Count Then
            Dim o = _objects(idx)
            If o.Machining IsNot Nothing Then list.Add(o.Machining)
            If o.FinishMachining IsNot Nothing Then list.Add(o.FinishMachining)
        End If
        Return list
    End Function

    ''' <summary>Every toolpath of the job: the text's and each drawing's.</summary>
    Private Function AllToolpaths() As List(Of Machining)
        Dim list As New List(Of Machining)
        If _settings.TextMachining IsNot Nothing Then list.Add(_settings.TextMachining)
        For Each o In _objects
            If o.Machining IsNot Nothing Then list.Add(o.Machining)
            If o.FinishMachining IsNot Nothing Then list.Add(o.FinishMachining)
        Next
        Return list
    End Function

    ''' <summary>Hide: unticks Show in view of the selected toolpath(s). The drawing stays, selected, and is still cut.</summary>
    Private Sub HideSelected_Click(sender As Object, e As EventArgs) Handles mnuDrawHide.Click, tsbHide.Click
        Dim shown = SelectedToolpaths().Where(Function(m) m.ShowInView).ToList()
        If shown.Count = 0 Then Return
        For Each m In shown
            m.ShowInView = False
        Next
        Dim idx = _glView.SelectedObject
        Dim name = If(idx >= 0 AndAlso idx < _objects.Count, _objects(idx).Name, "The text")
        ToolpathVisibilityChanged()
        _statusNote = name & ": toolpath hidden in the view (it is still cut). Show All (Ctrl+Shift+H) shows it again."
        lblStatus.Text = _statusNote
    End Sub

    ''' <summary>Show All: ticks Show in view of every toolpath again.</summary>
    Private Sub ShowAll_Click(sender As Object, e As EventArgs) Handles mnuDrawShowAll.Click, tsbShowAll.Click
        Dim hidden = AllToolpaths().Where(Function(m) Not m.ShowInView).ToList()
        If hidden.Count = 0 Then Return
        For Each m In hidden
            m.ShowInView = True
        Next
        ToolpathVisibilityChanged()
        _statusNote = If(hidden.Count = 1, "The hidden toolpath is shown again.", hidden.Count & " hidden toolpaths are shown again.")
        lblStatus.Text = _statusNote
    End Sub

    ''' <summary>After a Show in view change: the grids' boxes, the view, the buttons; the project changed, nothing to generate.</summary>
    Private Sub ToolpathVisibilityChanged()
        pgObject.Refresh()
        RefreshSettingsGrids()
        _glView.Invalidate()
        MarkDirty()
        UpdateDrawUi()
    End Sub

    ''' <summary>The View menu's Show Rapid Moves: the view and the toolbar's Rapids switch.</summary>
    Private Sub mnuViewRapids_CheckedChanged(sender As Object, e As EventArgs) Handles mnuViewRapids.CheckedChanged
        If _glView IsNot Nothing Then _glView.ShowRapids = mnuViewRapids.Checked
        If _tsbShowRapids IsNot Nothing AndAlso _tsbShowRapids.Checked <> mnuViewRapids.Checked Then _tsbShowRapids.Checked = mnuViewRapids.Checked
    End Sub

    ''' <summary>
    ''' A toolpath's Show in view switch, read from the live text settings and drawings (not the
    ''' generation's copies), so a switch shows at once.
    ''' </summary>
    Private Function IsToolpathShown(owner As ToolpathOwner) As Boolean
        Dim m As Machining
        If owner.IsText Then
            m = _settings.TextMachining
        Else
            Dim o = TryCast(owner.Source, DesignObject)
            If o Is Nothing Then Return True
            m = If(owner.Pass = 1, o.FinishMachining, o.Machining)
        End If
        Return m Is Nothing OrElse m.ShowInView
    End Function
End Class
