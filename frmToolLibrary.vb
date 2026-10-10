' ============================================================================
'  frmToolLibrary.vb
'  Tool Library dialog. Lists the tools grouped by type, edits the four numbers
'  (diameter, flute length, end radius, angle) with a scale drawing, and saves
'  the library on OK. In pick mode (the "..." button on the V-carve tool row)
'  it also returns the selected tool, which must pass the caller's filter.
'  Numbers accept decimals or fractions (0.25, 1/4, 1-1/4).
' ============================================================================

Imports System.Globalization

Public Class frmToolLibrary
    Private ReadOnly _pickFilter As Func(Of ToolDefinition, String)
    Private ReadOnly _initial As ToolDefinition
    Private _library As ToolLibrary
    Private _current As ToolDefinition
    Private _loading As Boolean
    Private _dirty As Boolean
    Private _selectedTool As ToolDefinition
    Private _notice As String

    ''' <summary>Manage mode (designer and Toolpath > Tool Library).</summary>
    Public Sub New()
        Me.New(Nothing, Nothing)
    End Sub

    ''' <param name="pickFilter">Nothing for manage mode; otherwise returns why a tool cannot be used (Nothing when it can).</param>
    ''' <param name="current">Tool to select first (matched by Id, then by its numbers).</param>
    Public Sub New(pickFilter As Func(Of ToolDefinition, String), current As ToolDefinition)
        InitializeComponent()
        _pickFilter = pickFilter
        _initial = current
    End Sub

    ''' <summary>The chosen tool after OK in pick mode.</summary>
    Public ReadOnly Property SelectedTool As ToolDefinition
        Get
            Return _selectedTool
        End Get
    End Property

    Private ReadOnly Property IsPickMode As Boolean
        Get
            Return _pickFilter IsNot Nothing
        End Get
    End Property

    ' ------------------------------------------------------------ setup

    Private Sub frmToolLibrary_Load(sender As Object, e As EventArgs) Handles MyBase.Load
        If IsPickMode Then
            Text = "Tool Library - choose the V-carve tool"
            btnOK.Text = "Use This Tool"
        Else
            btnOK.Text = "Save"
        End If

        For Each t As ToolType In [Enum].GetValues(GetType(ToolType))
            imlTypes.Images.Add(t.ToString(), ToolDrawing.RenderIcon(t, 16))
            cboType.Items.Add(ToolDefinition.DisplayTypeName(t))
            Dim mi As New ToolStripMenuItem(ToolDefinition.DisplayTypeName(t), imlTypes.Images(t.ToString())) With {.Tag = t}
            AddHandler mi.Click, AddressOf NewTool_Click
            tsbNew.DropDownItems.Add(mi)
        Next

        _library = ToolLibrary.Load()
        _notice = _library.LoadWarning
        RebuildTree(FindInitial())
        If tvTools.SelectedNode Is Nothing Then ShowTool(Nothing)
    End Sub

    Private Function FindInitial() As ToolDefinition
        If _initial IsNot Nothing Then
            Dim t = _library.FindById(_initial.Id)
            If t Is Nothing Then t = _library.Tools.FirstOrDefault(Function(x) x.SameGeometry(_initial))
            If t IsNot Nothing Then Return t
            _notice = "The project's tool """ & _initial.DisplayName() & """ is not in this library."
        End If
        If IsPickMode Then Return _library.Tools.FirstOrDefault(Function(x) _pickFilter(x) Is Nothing)
        Return _library.Tools.FirstOrDefault()
    End Function

    ''' <summary>Fills the tree (types in order, tools by size) and selects a tool.</summary>
    Private Sub RebuildTree(selectTool As ToolDefinition)
        tvTools.BeginUpdate()
        Try
            tvTools.Nodes.Clear()
            Dim selectNode As TreeNode = Nothing
            For Each type As ToolType In [Enum].GetValues(GetType(ToolType))
                Dim key = type.ToString()
                Dim tools = _library.Tools.Where(Function(x) x.Type = type).
                    OrderBy(Function(x) x.Diameter).ThenBy(Function(x) x.AngleDeg).ThenBy(Function(x) x.EndRadius).
                    ThenBy(Function(x) x.DisplayName(), StringComparer.CurrentCultureIgnoreCase).ToList()
                Dim grp = tvTools.Nodes.Add(key, ToolDefinition.DisplayTypeName(type) & "  (" & tools.Count & ")", key, key)
                For Each t In tools
                    Dim n = grp.Nodes.Add(t.Id, t.DisplayName(), key, key)
                    n.Tag = t
                    If IsPickMode AndAlso _pickFilter(t) IsNot Nothing Then n.ForeColor = SystemColors.GrayText
                    If t Is selectTool Then selectNode = n
                Next
                grp.Expand()
            Next
            If selectNode IsNot Nothing Then
                tvTools.SelectedNode = selectNode
                selectNode.EnsureVisible()
            End If
        Finally
            tvTools.EndUpdate()
        End Try
    End Sub

    Private Function FindNode(t As ToolDefinition) As TreeNode
        For Each g As TreeNode In tvTools.Nodes
            For Each n As TreeNode In g.Nodes
                If n.Tag Is t Then Return n
            Next
        Next
        Return Nothing
    End Function

    ' ------------------------------------------------------------ showing a tool

    Private Sub tvTools_AfterSelect(sender As Object, e As TreeViewEventArgs) Handles tvTools.AfterSelect
        ShowTool(TryCast(e.Node?.Tag, ToolDefinition))
    End Sub

    Private Sub tvTools_NodeMouseDoubleClick(sender As Object, e As TreeNodeMouseClickEventArgs) Handles tvTools.NodeMouseDoubleClick
        If IsPickMode AndAlso TypeOf e.Node.Tag Is ToolDefinition AndAlso btnOK.Enabled Then btnOK.PerformClick()
    End Sub

    Private Sub ShowTool(t As ToolDefinition)
        _current = t
        _loading = True
        Try
            Dim has = t IsNot Nothing
            tlpFields.Enabled = has
            tsbDuplicate.Enabled = has
            tsbDelete.Enabled = has
            If has Then
                txtName.Text = t.Name
                chkAutoName.Checked = String.IsNullOrWhiteSpace(t.Name) OrElse t.Name = t.AutoName()
                cboType.SelectedIndex = CInt(t.Type)
                txtDiameter.Text = InchFormat.EditText(t.Diameter)
                txtLength.Text = InchFormat.EditText(t.Length)
                txtRadius.Text = If(t.UsesRadius, InchFormat.EditText(t.EffectiveEndRadius), "")
                txtAngle.Text = If(t.UsesAngle, t.AngleDeg.ToString("0.###", CultureInfo.InvariantCulture), "")
            Else
                txtName.Text = ""
                chkAutoName.Checked = False
                cboType.SelectedIndex = -1
                txtDiameter.Text = "" : txtLength.Text = "" : txtRadius.Text = "" : txtAngle.Text = ""
            End If
            For Each tb In {txtDiameter, txtLength, txtRadius, txtAngle}
                tb.BackColor = SystemColors.Window
            Next
        Finally
            _loading = False
        End Try
        UpdateFieldStates()
        UpdateHints()
        drawTool.Tool = t
        UpdateStatus()
    End Sub

    ''' <summary>Enables the radius and angle boxes only for types that have them, with type-specific labels.</summary>
    Private Sub UpdateFieldStates()
        Dim t = _current
        Dim type = If(t Is Nothing, ToolType.FlatEndMill, t.Type)
        txtRadius.Enabled = t IsNot Nothing AndAlso t.UsesRadius AndAlso type <> ToolType.BallNose
        txtAngle.Enabled = t IsNot Nothing AndAlso t.UsesAngle
        Select Case type
            Case ToolType.BallNose : lblRadius.Text = "Ball radius (in)"
            Case ToolType.BullNose : lblRadius.Text = "Corner radius (in)"
            Case ToolType.TaperedBallNose : lblRadius.Text = "Tip radius (in)"
            Case Else : lblRadius.Text = "End radius (in)"
        End Select
        Select Case type
            Case ToolType.Drill : lblAngle.Text = "Point angle (deg)"
            Case ToolType.TaperedBallNose : lblAngle.Text = "Taper, included (deg)"
            Case Else : lblAngle.Text = "Angle, included (deg)"
        End Select
    End Sub

    Private Sub UpdateHints()
        Dim t = _current
        If t Is Nothing Then
            lblDiameterHint.Text = "" : lblLengthHint.Text = "" : lblRadiusHint.Text = "" : lblAngleHint.Text = ""
            Return
        End If
        Dim f = InchFormat.Fraction(t.Diameter)
        lblDiameterHint.Text = If(f Is Nothing, "", "= " & f & """")

        lblLengthHint.Text = ""
        If t.UsesAngle AndAlso t.Diameter > 0 AndAlso t.Length > 0 Then
            Dim hD = t.FullDiameterHeight
            If hD <= t.Length + 0.0000001 Then
                lblLengthHint.Text = "full diameter at " & InchFormat.EditText(Math.Round(hD, 4)) & """ from the tip"
            Else
                Dim prof = t.FluteProfile(8)
                lblLengthHint.Text = "flutes end at " & InchFormat.EditText(Math.Round(2 * prof(prof.Count - 1).R, 4)) & """ wide (full diameter at " & InchFormat.EditText(Math.Round(hD, 4)) & """)"
            End If
        End If

        Select Case t.Type
            Case ToolType.BallNose : lblRadiusHint.Text = "always half the diameter"
            Case ToolType.BullNose : lblRadiusHint.Text = "rounded corner of the flat end"
            Case ToolType.TaperedBallNose : lblRadiusHint.Text = If(t.EndRadius > 0, "tip ball " & InchFormat.EditText(Math.Round(2 * t.EndRadius, 4)) & """ across", "")
            Case Else : lblRadiusHint.Text = "not used by this type"
        End Select
        If t.UsesAngle Then
            lblAngleHint.Text = (t.AngleDeg / 2).ToString("0.###", CultureInfo.InvariantCulture) & "° per side"
        Else
            lblAngleHint.Text = "not used by this type"
        End If
    End Sub

    Private Sub UpdateStatus()
        Dim t = _current
        Dim msg As String = Nothing
        Dim isError As Boolean = False
        Dim okEnabled As Boolean = Not IsPickMode
        If t IsNot Nothing Then
            Dim probs = t.Problems()
            Dim reason = If(IsPickMode, _pickFilter(t), Nothing)
            If probs.Count > 0 Then
                msg = String.Join("  ", probs)
                isError = True
            ElseIf reason IsNot Nothing Then
                msg = reason & " Pick one from the V-bit group, or add one with New > V-bit."
            End If
            If IsPickMode Then okEnabled = probs.Count = 0 AndAlso reason Is Nothing
        ElseIf IsPickMode Then
            msg = "Select a V-bit."
        End If
        If msg Is Nothing Then msg = _notice
        lblMessage.Text = If(msg, "")
        lblMessage.ForeColor = If(isError, Color.Firebrick, If(msg IsNot Nothing AndAlso msg Is _notice, SystemColors.GrayText, Color.FromArgb(160, 90, 0)))
        btnOK.Enabled = okEnabled
    End Sub

    ' ------------------------------------------------------------ editing

    Private Sub NumberField_TextChanged(sender As Object, e As EventArgs) Handles txtDiameter.TextChanged, txtLength.TextChanged, txtRadius.TextChanged, txtAngle.TextChanged
        If _loading OrElse _current Is Nothing Then Return
        Dim tb = DirectCast(sender, TextBox)
        If Not tb.Enabled Then Return
        Dim v As Double
        Dim ok = If(tb Is txtAngle, TryParseAngle(tb.Text, v), InchFormat.TryParse(tb.Text, v)) AndAlso v >= 0
        tb.BackColor = If(ok, SystemColors.Window, Color.MistyRose)
        If Not ok Then
            lblMessage.Text = "Enter a number such as 0.25 or 1/4."
            lblMessage.ForeColor = Color.Firebrick
            Return
        End If
        If tb Is txtDiameter Then
            _current.Diameter = v
            If _current.Type = ToolType.BallNose Then
                _current.EndRadius = v / 2
                SetTextQuietly(txtRadius, InchFormat.EditText(_current.EndRadius))
            End If
        ElseIf tb Is txtLength Then
            _current.Length = v
        ElseIf tb Is txtRadius Then
            _current.EndRadius = v
        Else
            _current.AngleDeg = v
        End If
        ToolChanged()
    End Sub

    Private Shared Function TryParseAngle(text As String, ByRef v As Double) As Boolean
        Dim t = If(text, "").Trim().ToLowerInvariant().Replace("°", "").Replace("deg", "").Trim()
        Return Double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, v) AndAlso Not Double.IsNaN(v) AndAlso Not Double.IsInfinity(v)
    End Function

    Private Sub NumberField_Leave(sender As Object, e As EventArgs) Handles txtDiameter.Leave, txtLength.Leave, txtRadius.Leave, txtAngle.Leave
        If _current Is Nothing Then Return
        Dim tb = DirectCast(sender, TextBox)
        If Not tb.Enabled OrElse tb.BackColor <> SystemColors.Window Then Return
        If tb Is txtDiameter Then SetTextQuietly(tb, InchFormat.EditText(_current.Diameter))
        If tb Is txtLength Then SetTextQuietly(tb, InchFormat.EditText(_current.Length))
        If tb Is txtRadius Then SetTextQuietly(tb, InchFormat.EditText(_current.EffectiveEndRadius))
        If tb Is txtAngle Then SetTextQuietly(tb, _current.AngleDeg.ToString("0.###", CultureInfo.InvariantCulture))
    End Sub

    Private Sub SetTextQuietly(tb As TextBox, text As String)
        _loading = True
        Try
            tb.Text = text
        Finally
            _loading = False
        End Try
    End Sub

    ''' <summary>After any edit: auto name, list text, hints, drawing, status.</summary>
    Private Sub ToolChanged()
        _dirty = True
        If chkAutoName.Checked Then
            _current.Name = _current.AutoName()
            SetTextQuietly(txtName, _current.Name)
        End If
        Dim n = FindNode(_current)
        If n IsNot Nothing Then n.Text = _current.DisplayName()
        UpdateHints()
        drawTool.Invalidate()
        UpdateStatus()
    End Sub

    Private Sub txtName_TextChanged(sender As Object, e As EventArgs) Handles txtName.TextChanged
        If _loading OrElse _current Is Nothing Then Return
        _current.Name = txtName.Text
        _dirty = True
        _loading = True
        Try
            chkAutoName.Checked = txtName.Text = _current.AutoName()
        Finally
            _loading = False
        End Try
        Dim n = FindNode(_current)
        If n IsNot Nothing Then n.Text = _current.DisplayName()
    End Sub

    Private Sub chkAutoName_CheckedChanged(sender As Object, e As EventArgs) Handles chkAutoName.CheckedChanged
        If _loading OrElse _current Is Nothing OrElse Not chkAutoName.Checked Then Return
        ToolChanged()
    End Sub

    Private Sub cboType_SelectedIndexChanged(sender As Object, e As EventArgs) Handles cboType.SelectedIndexChanged
        If _loading OrElse _current Is Nothing OrElse cboType.SelectedIndex < 0 Then Return
        Dim newType = CType(cboType.SelectedIndex, ToolType)
        If newType = _current.Type Then Return
        Dim autoName = chkAutoName.Checked
        _current.Type = newType
        _current.ApplyTypeDefaults()
        If autoName Then _current.Name = _current.AutoName()
        _dirty = True
        Dim t = _current
        RebuildTree(t)          ' moves the tool to its new group and re-shows it
        ShowTool(t)
    End Sub

    ' ------------------------------------------------------------ list commands

    Private Sub NewTool_Click(sender As Object, e As EventArgs)
        Dim type = DirectCast(DirectCast(sender, ToolStripItem).Tag, ToolType)
        Dim t = ToolDefinition.NewOfType(type)
        _library.Tools.Add(t)
        _dirty = True
        RebuildTree(t)
        txtDiameter.Focus()
        txtDiameter.SelectAll()
    End Sub

    Private Sub tsbDuplicate_Click(sender As Object, e As EventArgs) Handles tsbDuplicate.Click
        If _current Is Nothing Then Return
        Dim c = _current.CloneAsNew()
        c.Name = _current.DisplayName() & " (copy)"
        _library.Tools.Add(c)
        _dirty = True
        RebuildTree(c)
        txtName.Focus()
        txtName.SelectAll()
    End Sub

    Private Sub tsbDelete_Click(sender As Object, e As EventArgs) Handles tsbDelete.Click
        If _current Is Nothing Then Return
        Dim gone = _current
        Dim siblings = _library.Tools.Where(Function(x) x.Type = gone.Type AndAlso x IsNot gone).ToList()
        _library.Tools.Remove(gone)
        _dirty = True
        Dim nextTool = If(siblings.FirstOrDefault(), _library.Tools.FirstOrDefault())
        RebuildTree(nextTool)
        If nextTool Is Nothing Then ShowTool(Nothing)
    End Sub

    ' ------------------------------------------------------------ closing

    Private Sub btnOK_Click(sender As Object, e As EventArgs) Handles btnOK.Click
        For Each t In _library.Tools
            t.Normalize()
            Dim probs = t.Problems()
            If probs.Count > 0 Then
                RebuildTree(t)
                MessageBox.Show(Me, """" & t.DisplayName() & """: " & String.Join(" ", probs), "Tool Library", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                Return
            End If
        Next
        Try
            _library.Save()
        Catch ex As Exception
            MessageBox.Show(Me, "The tool library could not be saved:" & Environment.NewLine & ex.Message, "Tool Library", MessageBoxButtons.OK, MessageBoxIcon.Error)
            Return
        End Try
        If IsPickMode Then _selectedTool = _current?.Clone()
        _dirty = False
        DialogResult = DialogResult.OK
        Close()
    End Sub

    Private Sub frmToolLibrary_FormClosing(sender As Object, e As FormClosingEventArgs) Handles MyBase.FormClosing
        If DialogResult = DialogResult.OK OrElse Not _dirty Then Return
        Dim answer = MessageBox.Show(Me, "Discard the changes made to the tool library?", "Tool Library", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2)
        If answer = DialogResult.No Then
            e.Cancel = True
            DialogResult = DialogResult.None
        End If
    End Sub
End Class
