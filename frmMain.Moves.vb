' ============================================================================
'  frmMain.Moves.vb
'  What the toolpath lines show: the Cuts and Rapids switches on the View
'  toolbar (with Show Cutting Moves / Show Rapid Moves in the View menu), and
'  each toolpath's own Show in view check box (Machining.ShowInView, in the
'  Objects and Text tabs). All display only: nothing is generated again, and
'  hidden lines are still cut, exported and simulated.
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
