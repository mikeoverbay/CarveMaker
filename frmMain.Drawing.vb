' ============================================================================
'  frmMain.Drawing.vb
'  Drawing on the blank: the Draw and Style menus and the draw toolbar over
'  the view, turning the view's draw tools into drawings (shapes, free-form
'  areas, text), typing text in place, the text style (font, bold, italic,
'  alignment), the arrange commands (rotate, mirror, order, delete) and
'  snapping (grid and rotation steps, kept in the user's preferences).
' ============================================================================

Imports System.Drawing.Drawing2D
Imports System.Drawing.Text

Partial Class frmMain

    ' Style for new text drawings; the toolbar and Style menu edit the selected text
    ' drawing and remember its style here for the next one.
    Private _newTextFont As String = "Arial"
    Private _newTextBold As Boolean
    Private _newTextItalic As Boolean
    Private _newTextAlign As TextAlign = TextAlign.Left
    Private _syncingDrawUi As Boolean

    ' In-place text editor over the view.
    Private _textEditor As TextBox
    Private _textEditorFont As Font           ' the editor's own font (disposed when replaced)
    Private _editingObject As DesignObject
    Private _editingOldText As String
    Private _editingIsNew As Boolean
    Private _endingTextEdit As Boolean
    Private _loadingTextEditor As Boolean
    Private _styleDialogFont As Font          ' the font handed to the font dialog (disposed when replaced)
    Private _dirtyBeforeEdit As Boolean        ' project state before the text (or the new text) was touched
    Private _otherChangeDuringEdit As Boolean  ' something besides the typing changed the project meanwhile
    Private _typingInEditor As Boolean         ' MarkDirty comes from the editor's own typing

    ' Snapping (grid, rotation steps): the user's preferences, not the project's.
    Private _prefs As UserPreferences
    Private _syncingSnapUi As Boolean
    ' Shown instead of "Ready" when the next toolpath arrives (the generation would hide it otherwise).
    Private _statusNote As String
    Private Const TypingHint As String = "Type the text. Enter starts a new line; Ctrl+Enter or a click elsewhere finishes, Esc cancels."

    ''' <summary>Hooks the view's draw events and builds the toolbar icons and font list (called from Load).</summary>
    Private Sub SetupDrawing()
        AddHandler _glView.ShapeDrawn, AddressOf OnViewShapeDrawn
        AddHandler _glView.ToolChanged, Sub(o, ev)
                                            ' Back on Select (a shape was drawn or the tool dropped): the tool's hint is done.
                                            If _glView.ActiveTool = DrawTool.Select Then _statusNote = Nothing
                                            UpdateDrawUi()
                                        End Sub
        AddHandler _glView.DeleteRequested, Sub(o, ev) RemoveDrawingAt(_glView.SelectedObject, selectNext:=False)
        AddHandler _glView.EditTextRequested, Sub(o, ev) BeginTextEdit(_objects(ev.Index), isNew:=False)
        AddHandler _glView.DragFeedback, Sub(o, ev) lblStatus.Text = ev.Text
        AddHandler _glView.Resize, Sub(o, ev) PositionTextEditor()
        AddHandler _glView.CameraChanged, Sub(o, ev) PositionTextEditor()

        tsbToolSelect.Image = DrawIcon(DrawTool.Select)
        tsbToolRectangle.Image = DrawIcon(DrawTool.Rectangle)
        tsbToolEllipse.Image = DrawIcon(DrawTool.Ellipse)
        tsbToolPolygon.Image = DrawIcon(DrawTool.Polygon)
        tsbToolStar.Image = DrawIcon(DrawTool.Star)
        tsbToolArea.Image = DrawIcon(DrawTool.Area)
        tsbToolText.Image = DrawIcon(DrawTool.Text)
        mnuDrawSelect.Image = tsbToolSelect.Image
        mnuDrawRectangle.Image = tsbToolRectangle.Image
        mnuDrawEllipse.Image = tsbToolEllipse.Image
        mnuDrawPolygon.Image = tsbToolPolygon.Image
        mnuDrawStar.Image = tsbToolStar.Image
        mnuDrawArea.Image = tsbToolArea.Image
        mnuDrawText.Image = tsbToolText.Image
        tsbDrawAlignLeft.Image = AlignIcon(TextAlign.Left)
        tsbDrawAlignCenter.Image = AlignIcon(TextAlign.Center)
        tsbDrawAlignRight.Image = AlignIcon(TextAlign.Right)
        mnuStyleAlignLeft.Image = tsbDrawAlignLeft.Image
        mnuStyleAlignCenter.Image = tsbDrawAlignCenter.Image
        mnuStyleAlignRight.Image = tsbDrawAlignRight.Image

        Dim names As New List(Of String)
        Using coll As New InstalledFontCollection()
            For Each fam In coll.Families
                names.Add(fam.Name)
            Next
        End Using
        names.Sort(StringComparer.CurrentCultureIgnoreCase)
        tscDrawFont.Items.AddRange(names.Cast(Of Object)().ToArray())
        _newTextFont = If(_settings.FontLarge?.Family, "Arial")

        ' Snapping: toolbar lists and menu choices from the same tables.
        _prefs = UserPreferences.Load()
        tsbSnapGrid.Image = SnapIcon()
        mnuDrawSnapGrid.Image = tsbSnapGrid.Image
        tsbHide.Image = EyeIcon(crossed:=True)
        tsbShowAll.Image = EyeIcon(crossed:=False)
        mnuDrawHide.Image = tsbHide.Image
        mnuDrawShowAll.Image = tsbShowAll.Image
        For Each spacing In UserPreferences.GridSpacings
            Dim value = spacing
            tscSnapGrid.Items.Add(UserPreferences.SpacingText(value))
            Dim mi As New ToolStripMenuItem(UserPreferences.SpacingText(value)) With {.Tag = value}
            AddHandler mi.Click, Sub(o, ev) ChooseGridSpacing(value)
            mnuDrawGridSpacing.DropDownItems.Add(mi)
        Next
        For Each stepDeg In UserPreferences.RotationSteps
            Dim value = stepDeg
            tscSnapRot.Items.Add(UserPreferences.StepText(value))
            Dim mi As New ToolStripMenuItem(UserPreferences.StepText(value)) With {.Tag = value}
            AddHandler mi.Click, Sub(o, ev) ChangeSnap(Sub(p) p.RotationSnapDeg = value)
            mnuDrawRotationSnap.DropDownItems.Add(mi)
        Next
        ApplySnapPreferences()
        SetupToolbars()
        UpdateDrawUi()
    End Sub

    ' ---------------------------------------------------------------- snapping

    ''' <summary>Hands the snap settings to the view and shows them on the toolbar and in the menu.</summary>
    Private Sub ApplySnapPreferences()
        _glView.SnapToGrid = _prefs.SnapToGrid
        _glView.GridSpacing = _prefs.GridSpacing
        _glView.RotationSnapDeg = _prefs.RotationSnapDeg
        _syncingSnapUi = True
        Try
            tsbSnapGrid.Checked = _prefs.SnapToGrid
            mnuDrawSnapGrid.Checked = _prefs.SnapToGrid
            tscSnapGrid.SelectedIndex = Array.IndexOf(UserPreferences.GridSpacings, _prefs.GridSpacing)
            tscSnapRot.SelectedIndex = Array.IndexOf(UserPreferences.RotationSteps, _prefs.RotationSnapDeg)
            For Each mi In mnuDrawGridSpacing.DropDownItems.OfType(Of ToolStripMenuItem)()
                mi.Checked = CDbl(mi.Tag) = _prefs.GridSpacing
            Next
            For Each mi In mnuDrawRotationSnap.DropDownItems.OfType(Of ToolStripMenuItem)()
                mi.Checked = CDbl(mi.Tag) = _prefs.RotationSnapDeg
            Next
        Finally
            _syncingSnapUi = False
        End Try
    End Sub

    ''' <summary>Changes, remembers and applies a snap setting.</summary>
    Private Sub ChangeSnap(change As Action(Of UserPreferences))
        change(_prefs)
        _prefs.Save()
        ApplySnapPreferences()
        lblStatus.Text = If(_prefs.SnapToGrid, "Snapping to a " & UserPreferences.SpacingText(_prefs.GridSpacing) & " grid (hold Alt while dragging to ignore it)", "Grid snapping off") &
                         If(_prefs.RotationSnapDeg > 0, "; rotation in " & UserPreferences.StepText(_prefs.RotationSnapDeg) & " steps (Shift turns freely).", "; rotation free.")
    End Sub

    ''' <summary>Picking a spacing also turns grid snapping on: that is what it is picked for.</summary>
    Private Sub ChooseGridSpacing(spacing As Double)
        ChangeSnap(Sub(p)
                       p.GridSpacing = spacing
                       p.SnapToGrid = True
                   End Sub)
    End Sub

    Private Sub tsbSnapGrid_CheckedChanged(sender As Object, e As EventArgs) Handles tsbSnapGrid.CheckedChanged
        If _syncingSnapUi Then Return
        Dim isOn = tsbSnapGrid.Checked
        ChangeSnap(Sub(p) p.SnapToGrid = isOn)
    End Sub

    Private Sub mnuDrawSnapGrid_Click(sender As Object, e As EventArgs) Handles mnuDrawSnapGrid.Click
        ChangeSnap(Sub(p) p.SnapToGrid = Not p.SnapToGrid)
    End Sub

    Private Sub tscSnapGrid_SelectedIndexChanged(sender As Object, e As EventArgs) Handles tscSnapGrid.SelectedIndexChanged
        If _syncingSnapUi OrElse tscSnapGrid.SelectedIndex < 0 Then Return
        ChooseGridSpacing(UserPreferences.GridSpacings(tscSnapGrid.SelectedIndex))
    End Sub

    Private Sub tscSnapRot_SelectedIndexChanged(sender As Object, e As EventArgs) Handles tscSnapRot.SelectedIndexChanged
        If _syncingSnapUi OrElse tscSnapRot.SelectedIndex < 0 Then Return
        Dim stepDeg = UserPreferences.RotationSteps(tscSnapRot.SelectedIndex)
        ChangeSnap(Sub(p) p.RotationSnapDeg = stepDeg)
    End Sub

    ' ------------------------------------------------------------ state / UI

    ''' <summary>The drawing selected in the view and list, or Nothing.</summary>
    Private Function SelectedDrawing() As DesignObject
        Dim i = _glView.SelectedObject
        Return If(i >= 0 AndAlso i < _objects.Count, _objects(i), Nothing)
    End Function

    ''' <summary>The selected drawing when it is text, or Nothing.</summary>
    Private Function SelectedTextDrawing() As DesignObject
        Dim o = SelectedDrawing()
        Return If(o IsNot Nothing AndAlso o.Kind = DrawingKind.Text, o, Nothing)
    End Function

    ''' <summary>Checks the active tool and shows the selected text's style (or the style for new text).</summary>
    Private Sub UpdateDrawUi()
        If _glView Is Nothing Then Return
        Dim t = _glView.ActiveTool
        tsbToolSelect.Checked = t = DrawTool.Select : mnuDrawSelect.Checked = tsbToolSelect.Checked
        tsbToolRectangle.Checked = t = DrawTool.Rectangle : mnuDrawRectangle.Checked = tsbToolRectangle.Checked
        tsbToolEllipse.Checked = t = DrawTool.Ellipse : mnuDrawEllipse.Checked = tsbToolEllipse.Checked
        tsbToolPolygon.Checked = t = DrawTool.Polygon : mnuDrawPolygon.Checked = tsbToolPolygon.Checked
        tsbToolStar.Checked = t = DrawTool.Star : mnuDrawStar.Checked = tsbToolStar.Checked
        tsbToolArea.Checked = t = DrawTool.Area : mnuDrawArea.Checked = tsbToolArea.Checked
        tsbToolText.Checked = t = DrawTool.Text : mnuDrawText.Checked = tsbToolText.Checked

        Dim txt = SelectedTextDrawing()
        Dim align = If(txt IsNot Nothing, txt.Align, _newTextAlign)
        _syncingDrawUi = True
        Try
            Dim fontName = If(txt IsNot Nothing, txt.FontName, _newTextFont)
            If Not String.Equals(tscDrawFont.Text, fontName, StringComparison.Ordinal) Then tscDrawFont.Text = fontName
            tsbDrawBold.Checked = If(txt IsNot Nothing, txt.Bold, _newTextBold)
            tsbDrawItalic.Checked = If(txt IsNot Nothing, txt.Italic, _newTextItalic)
            tsbDrawAlignLeft.Checked = align = TextAlign.Left
            tsbDrawAlignCenter.Checked = align = TextAlign.Center
            tsbDrawAlignRight.Checked = align = TextAlign.Right
            mnuStyleBold.Checked = tsbDrawBold.Checked
            mnuStyleItalic.Checked = tsbDrawItalic.Checked
            mnuStyleAlignLeft.Checked = tsbDrawAlignLeft.Checked
            mnuStyleAlignCenter.Checked = tsbDrawAlignCenter.Checked
            mnuStyleAlignRight.Checked = tsbDrawAlignRight.Checked
        Finally
            _syncingDrawUi = False
        End Try

        Dim has = SelectedDrawing() IsNot Nothing
        mnuDrawEditText.Enabled = txt IsNot Nothing
        mnuDrawRotateLeft.Enabled = has
        mnuDrawRotateRight.Enabled = has
        mnuDrawMirror.Enabled = has
        mnuDrawFront.Enabled = has AndAlso _glView.SelectedObject < _objects.Count - 1
        mnuDrawBack.Enabled = has AndAlso _glView.SelectedObject > 0
        mnuDrawDelete.Enabled = has
        Dim canHide = has AndAlso SelectedDrawing().Visible
        Dim anyHidden = _objects.Any(Function(o) Not o.Visible)
        mnuDrawHide.Enabled = canHide : tsbHide.Enabled = canHide
        mnuDrawShowAll.Enabled = anyHidden : tsbShowAll.Enabled = anyHidden
    End Sub

    ''' <summary>Picks a draw tool from the menu or toolbar and hands the keyboard to the view.</summary>
    Private Sub SetDrawTool(tool As DrawTool)
        EndTextEdit(commit:=True, focusView:=False)
        _glView.ActiveTool = tool
        _glView.Focus()
        UpdateDrawUi()
        Dim hint As String = Nothing
        Select Case tool
            Case DrawTool.Rectangle, DrawTool.Ellipse, DrawTool.Polygon, DrawTool.Star
                hint = "Drag a box to draw the " & tool.ToString().ToLowerInvariant() & ". Shift keeps its proportions, Ctrl draws from the centre, Esc cancels."
            Case DrawTool.Area
                hint = "Click the corners of the area. Click the first corner, double-click or press Enter to close it; Backspace removes the last corner, Esc cancels."
            Case DrawTool.Text
                hint = "Drag a box as tall as a capital letter (or just click) where the text starts, then type."
        End Select
        If hint IsNot Nothing Then
            lblStatus.Text = hint
            ' A generation the text edit just started would replace the hint with "Ready": keep it.
            If _running Then _statusNote = hint
        End If
    End Sub

    Private Sub tsbToolSelect_Click(sender As Object, e As EventArgs) Handles tsbToolSelect.Click, mnuDrawSelect.Click
        SetDrawTool(DrawTool.Select)
    End Sub

    Private Sub tsbToolRectangle_Click(sender As Object, e As EventArgs) Handles tsbToolRectangle.Click, mnuDrawRectangle.Click
        SetDrawTool(DrawTool.Rectangle)
    End Sub

    Private Sub tsbToolEllipse_Click(sender As Object, e As EventArgs) Handles tsbToolEllipse.Click, mnuDrawEllipse.Click
        SetDrawTool(DrawTool.Ellipse)
    End Sub

    Private Sub tsbToolPolygon_Click(sender As Object, e As EventArgs) Handles tsbToolPolygon.Click, mnuDrawPolygon.Click
        SetDrawTool(DrawTool.Polygon)
    End Sub

    Private Sub tsbToolStar_Click(sender As Object, e As EventArgs) Handles tsbToolStar.Click, mnuDrawStar.Click
        SetDrawTool(DrawTool.Star)
    End Sub

    Private Sub tsbToolArea_Click(sender As Object, e As EventArgs) Handles tsbToolArea.Click, mnuDrawArea.Click
        SetDrawTool(DrawTool.Area)
    End Sub

    Private Sub tsbToolText_Click(sender As Object, e As EventArgs) Handles tsbToolText.Click, mnuDrawText.Click
        SetDrawTool(DrawTool.Text)
    End Sub

    Private Sub mnuDraw_DropDownOpening(sender As Object, e As EventArgs) Handles mnuDraw.DropDownOpening, mnuStyle.DropDownOpening
        UpdateDrawUi()
    End Sub

    ' ------------------------------------------------------- new drawings

    ''' <summary>A draw tool finished: make the drawing, select it, and for text start typing.</summary>
    Private Sub OnViewShapeDrawn(sender As Object, e As ToolpathView.ShapeDrawnEventArgs)
        Dim wasDirty = _dirty
        Dim o As New DesignObject With {.Kind = e.Kind, .Name = NextDrawingName(e.Kind), .LockAspect = False}
        Dim w = e.MaxX - e.MinX, h = e.MaxY - e.MinY
        Select Case e.Kind
            Case DrawingKind.Text
                o.Text = "Text"
                o.FontName = _newTextFont
                o.Bold = _newTextBold
                o.Italic = _newTextItalic
                o.Align = _newTextAlign
                o.LetterHeight = Math.Round(If(e.IsClick OrElse h < 0.02, _settings.SizeLargeIn, h), 4)
                o.LockAspect = True
                o.Normalize()
                o.EnsureParsed(_settings.CurveTolerance)
                If o.Shape IsNot Nothing AndAlso o.Shape.Width > 0 Then
                    o.Width = Math.Round(o.Shape.Width, 4)
                    o.Height = Math.Round(o.Shape.Height, 4)
                End If
                ' The text hangs from the top-left corner of the box (or the click).
                o.PlaceCorner(3, New Pt2(e.MinX, e.MaxY))
            Case DrawingKind.Area
                If e.Points Is Nothing OrElse e.Points.Count < 3 Then Return
                Dim path As New Clipper2Lib.PathD(e.Points.Select(Function(p) New Clipper2Lib.PointD(p.X, p.Y)))
                If Math.Abs(Clipper2Lib.Clipper.Area(path)) < 0.0001 Then
                    lblStatus.Text = "An area needs at least three corners that are not in a line."
                    Return
                End If
                For Each p In e.Points
                    o.AreaPoints.Add(Math.Round(p.X - e.MinX, 5))
                    o.AreaPoints.Add(Math.Round(p.Y - e.MinY, 5))
                Next
                o.X = Math.Round(e.MinX, 4) : o.Y = Math.Round(e.MinY, 4)
                o.Width = Math.Round(Math.Max(0.01, w), 4) : o.Height = Math.Round(Math.Max(0.01, h), 4)
            Case Else
                If e.IsClick OrElse w < 0.02 OrElse h < 0.02 Then
                    ' A click: a 1" shape (its own proportions) centred on the click.
                    Dim a = DrawingShapes.DefaultAspect(e.Kind)
                    w = If(a >= 1, 1.0, a) : h = w / a
                    o.X = Math.Round((e.MinX + e.MaxX) / 2 - w / 2, 4) : o.Y = Math.Round((e.MinY + e.MaxY) / 2 - h / 2, 4)
                Else
                    o.X = Math.Round(e.MinX, 4) : o.Y = Math.Round(e.MinY, 4)
                End If
                o.Width = Math.Round(w, 4) : o.Height = Math.Round(h, 4)
        End Select
        o.Normalize()
        SeedMachining(o)
        _objects.Add(o)
        RefreshObjectList(_objects.Count - 1)
        tabLeft.SelectedTab = tabObjects
        MarkDirty()
        RequestRegenerate(immediate:=True)
        If o.Kind = DrawingKind.Text Then
            BeginTextEdit(o, isNew:=True)
            _dirtyBeforeEdit = wasDirty      ' cancelling the new text puts the project back as it was
        End If
    End Sub

    ''' <summary>"Rectangle 3": the kind and the first number not used yet.</summary>
    Private Function NextDrawingName(kind As DrawingKind) As String
        Dim baseName = If(kind = DrawingKind.Area, "Area", kind.ToString())
        Dim n = 1
        While _objects.Any(Function(o) String.Equals(o.Name, baseName & " " & n, StringComparison.OrdinalIgnoreCase))
            n += 1
        End While
        Return baseName & " " & n
    End Function

    ''' <summary>A new drawing starts with the text's V-bit, speed and feeds.</summary>
    Private Sub SeedMachining(o As DesignObject)
        Dim textPath = _settings.TextMachining
        If textPath IsNot Nothing AndAlso textPath.Operation = CutOperation.VCarve AndAlso textPath.Tool IsNot Nothing Then
            o.Machining.Tool = textPath.Tool.Clone()
            o.Machining.SpindleRpm = textPath.SpindleRpm
            o.Machining.FeedRate = textPath.FeedRate
            o.Machining.PlungeRate = textPath.PlungeRate
        End If
    End Sub

    ' ------------------------------------------------- typing text in place

    ''' <summary>Opens the in-place editor for a text drawing (below it in the view).</summary>
    Private Sub BeginTextEdit(o As DesignObject, isNew As Boolean)
        EndTextEdit(commit:=True, focusView:=False)
        If o Is Nothing OrElse o.Kind <> DrawingKind.Text OrElse Not _objects.Contains(o) Then Return
        If _textEditor Is Nothing Then
            _textEditor = New TextBox With {
                .Name = "txtDrawText",
                .Multiline = True,
                .AcceptsReturn = True,
                .WordWrap = False,
                .ScrollBars = ScrollBars.None,
                .BorderStyle = BorderStyle.FixedSingle,
                .Visible = False
            }
            AddHandler _textEditor.TextChanged, AddressOf TextEditor_TextChanged
            AddHandler _textEditor.KeyDown, AddressOf TextEditor_KeyDown
            AddHandler _textEditor.Leave, Sub(s, ev) EndTextEdit(commit:=True, focusView:=False)
            pnlView.Controls.Add(_textEditor)
        End If
        _editingObject = o
        _editingOldText = If(o.Text, "")
        _editingIsNew = isNew
        If Not isNew Then _dirtyBeforeEdit = _dirty
        _otherChangeDuringEdit = False
        SetTextEditorFont(o)
        _loadingTextEditor = True
        Try
            _textEditor.Text = If(o.Text, "").Replace(vbCrLf, vbLf).Replace(vbLf, vbCrLf)
        Finally
            _loadingTextEditor = False
        End Try
        PositionTextEditor()
        _textEditor.Visible = True
        _textEditor.BringToFront()
        _textEditor.Focus()
        _textEditor.SelectAll()
        lblStatus.Text = TypingHint
    End Sub

    Private Sub SetTextEditorFont(o As DesignObject)
        Dim old = _textEditorFont
        _textEditorFont = EditorFontForDrawing(o)
        _textEditor.Font = _textEditorFont
        old?.Dispose()
    End Sub

    ''' <summary>A 14 pt font in the drawing's family and style for the editor (falls back to the UI font).</summary>
    Private Function EditorFontForDrawing(o As DesignObject) As Font
        Dim style = FontStyle.Regular
        If o.Bold Then style = style Or FontStyle.Bold
        If o.Italic Then style = style Or FontStyle.Italic
        Try
            Using fam As New FontFamily(o.FontName)
                If fam.IsStyleAvailable(style) Then Return New Font(fam, 14.0F, style, GraphicsUnit.Point)
                Return New Font(fam, 14.0F, FontStyle.Regular, GraphicsUnit.Point)
            End Using
        Catch
            Return New Font(Font.FontFamily, 14.0F, FontStyle.Regular, GraphicsUnit.Point)
        End Try
    End Function

    ''' <summary>Puts the editor just below the text drawing (above it when there is no room), sized to the lines.</summary>
    Private Sub PositionTextEditor()
        If _textEditor Is Nothing OrElse _editingObject Is Nothing Then Return
        Dim pts = _editingObject.Corners().Select(Function(c) _glView.WorldToScreen(c.X, c.Y)).ToList()
        Dim minX = pts.Min(Function(p) p.X), maxX = pts.Max(Function(p) p.X)
        Dim minY = pts.Min(Function(p) p.Y), maxY = pts.Max(Function(p) p.Y)
        Dim lineH = TextRenderer.MeasureText("Ag", _textEditor.Font).Height
        Dim lines = Math.Max(1, _textEditor.Lines.Length)
        Dim w = Math.Max(260, Math.Min(_glView.ClientSize.Width - 8, maxX - minX))
        Dim h = lineH * (lines + 1) + 6
        Dim x = Math.Max(4, Math.Min(_glView.ClientSize.Width - w - 4, minX))
        Dim y = maxY + 8
        If y + h > _glView.ClientSize.Height - 4 Then y = minY - h - 8
        ' Text panned out of sight: the editor still stays inside the view.
        y = Math.Max(4, Math.Min(_glView.ClientSize.Height - h - 4, y))
        _textEditor.Bounds = New Rectangle(_glView.Left + x, _glView.Top + y, w, h)
    End Sub

    Private Sub TextEditor_TextChanged(sender As Object, e As EventArgs)
        If _loadingTextEditor OrElse _editingObject Is Nothing Then Return
        _editingObject.Text = _textEditor.Text.Replace(vbCrLf, vbLf)
        _editingObject.FitTextBox(_settings.CurveTolerance)
        PositionTextEditor()          ' stays under the text as it grows (the height follows the lines)
        pgObject.Refresh()
        _glView.Objects = _objects
        _typingInEditor = True
        Try
            MarkDirty()
        Finally
            _typingInEditor = False
        End Try
        RequestRegenerate()
    End Sub

    Private Sub TextEditor_KeyDown(sender As Object, e As KeyEventArgs)
        If e.KeyCode = Keys.Escape Then
            EndTextEdit(commit:=False, focusView:=True)
            e.Handled = True : e.SuppressKeyPress = True
        ElseIf e.KeyCode = Keys.Enter AndAlso e.Control Then
            EndTextEdit(commit:=True, focusView:=True)
            e.Handled = True : e.SuppressKeyPress = True
        ElseIf e.KeyCode = Keys.A AndAlso e.Control Then
            _textEditor.SelectAll()
            e.Handled = True : e.SuppressKeyPress = True
        End If
    End Sub

    ''' <summary>
    ''' Closes the in-place editor. Cancel puts the old text back (and drops a text that
    ''' was just drawn); text left empty is removed either way.
    ''' </summary>
    Private Sub EndTextEdit(commit As Boolean, focusView As Boolean)
        If _editingObject Is Nothing OrElse _endingTextEdit Then Return
        _endingTextEdit = True
        Try
            Dim o = _editingObject
            _editingObject = Nothing
            If Not commit Then o.Text = _editingOldText
            Dim idx = _objects.IndexOf(o)
            ' Hiding the focused editor moves the focus: Leave comes back here and is ignored.
            If focusView Then _glView.Focus()
            _textEditor.Visible = False
            ' Cancel drops a new text; finishing with no text drops it too. Cancelling an edit of an
            ' existing text only puts the old text back.
            Dim removed = idx >= 0 AndAlso ((Not commit AndAlso _editingIsNew) OrElse (commit AndAlso If(o.Text, "").Trim().Length = 0))
            If removed Then
                _objects.RemoveAt(idx)
                RefreshObjectList(-1)
                _statusNote = If(commit, "Empty text removed.", "Text cancelled.")
                lblStatus.Text = _statusNote
                If commit Then MarkDirty()
                RequestRegenerate(immediate:=True)
            ElseIf idx >= 0 Then
                o.FitTextBox(_settings.CurveTolerance)
                RefreshObjectList(idx)
                If Not String.Equals(o.Text, _editingOldText, StringComparison.Ordinal) OrElse _editingIsNew Then MarkDirty()
                RequestRegenerate(immediate:=True)
            End If
            ' Cancelled, or finished without a change, and nothing else changed meanwhile: the
            ' project is as it was before.
            If Not _otherChangeDuringEdit AndAlso
               ((Not commit AndAlso (_editingIsNew OrElse Not removed)) OrElse
                (commit AndAlso Not removed AndAlso Not _editingIsNew AndAlso String.Equals(o.Text, _editingOldText, StringComparison.Ordinal))) Then
                _dirty = _dirtyBeforeEdit
                UpdateTitle()
            End If
        Finally
            _endingTextEdit = False
        End Try
    End Sub

    Private Sub mnuDrawEditText_Click(sender As Object, e As EventArgs) Handles mnuDrawEditText.Click
        Dim o = SelectedTextDrawing()
        If o IsNot Nothing Then BeginTextEdit(o, isNew:=False)
    End Sub

    ' ------------------------------------------------------------ text style

    ''' <summary>Changes the selected text drawing (if any) and remembers the style for new text.</summary>
    Private Sub ApplyTextStyle(change As Action(Of DesignObject))
        Dim probe As New DesignObject With {.Kind = DrawingKind.Text, .FontName = _newTextFont, .Bold = _newTextBold, .Italic = _newTextItalic, .Align = _newTextAlign}
        Dim o = SelectedTextDrawing()
        If o IsNot Nothing Then
            probe.FontName = o.FontName : probe.Bold = o.Bold : probe.Italic = o.Italic : probe.Align = o.Align
        End If
        change(probe)
        ' A font this PC lacks (from the selected text) is not made the default for new text.
        If IsInstalledFont(probe.FontName) Then _newTextFont = probe.FontName
        _newTextBold = probe.Bold : _newTextItalic = probe.Italic : _newTextAlign = probe.Align
        If o IsNot Nothing Then
            Dim before = String.Join("|", o.FontName, o.Bold, o.Italic, o.Align)
            change(o)
            If before = String.Join("|", o.FontName, o.Bold, o.Italic, o.Align) Then
                UpdateDrawUi()                ' nothing changed (e.g. Align Left on left-aligned text)
                Return
            End If
            AfterDrawingChanged(o)
            If _editingObject Is o Then SetTextEditorFont(o)
        Else
            UpdateDrawUi()
        End If
    End Sub

    Private Sub StyleBold_Click(sender As Object, e As EventArgs) Handles tsbDrawBold.Click, mnuStyleBold.Click
        ApplyTextStyle(Sub(o) o.Bold = Not o.Bold)
    End Sub

    Private Sub StyleItalic_Click(sender As Object, e As EventArgs) Handles tsbDrawItalic.Click, mnuStyleItalic.Click
        ApplyTextStyle(Sub(o) o.Italic = Not o.Italic)
    End Sub

    Private Sub StyleAlignLeft_Click(sender As Object, e As EventArgs) Handles tsbDrawAlignLeft.Click, mnuStyleAlignLeft.Click
        ApplyTextStyle(Sub(o) o.Align = TextAlign.Left)
    End Sub

    Private Sub StyleAlignCenter_Click(sender As Object, e As EventArgs) Handles tsbDrawAlignCenter.Click, mnuStyleAlignCenter.Click
        ApplyTextStyle(Sub(o) o.Align = TextAlign.Center)
    End Sub

    Private Sub StyleAlignRight_Click(sender As Object, e As EventArgs) Handles tsbDrawAlignRight.Click, mnuStyleAlignRight.Click
        ApplyTextStyle(Sub(o) o.Align = TextAlign.Right)
    End Sub

    Private Sub tscDrawFont_SelectedIndexChanged(sender As Object, e As EventArgs) Handles tscDrawFont.SelectedIndexChanged
        If _syncingDrawUi OrElse tscDrawFont.SelectedItem Is Nothing Then Return
        ApplyFontName(tscDrawFont.SelectedItem.ToString())
    End Sub

    Private Sub tscDrawFont_KeyDown(sender As Object, e As KeyEventArgs) Handles tscDrawFont.KeyDown
        If e.KeyCode <> Keys.Enter Then Return
        e.Handled = True : e.SuppressKeyPress = True
        ApplyFontName(tscDrawFont.Text)
    End Sub

    Private Function IsInstalledFont(name As String) As Boolean
        Return tscDrawFont.Items.Cast(Of Object)().Any(Function(i) String.Equals(i.ToString(), name, StringComparison.CurrentCultureIgnoreCase))
    End Function

    ''' <summary>Uses a typed or chosen family when it is installed (with its installed spelling).</summary>
    Private Sub ApplyFontName(name As String)
        Dim match = tscDrawFont.Items.Cast(Of Object)().Select(Function(i) i.ToString()).
                    FirstOrDefault(Function(n) String.Equals(n, If(name, "").Trim(), StringComparison.CurrentCultureIgnoreCase))
        If match Is Nothing Then
            lblStatus.Text = "Font '" & name & "' is not installed."
            UpdateDrawUi()
            Return
        End If
        ApplyTextStyle(Sub(o) o.FontName = match)
    End Sub

    Private Sub mnuStyleFont_Click(sender As Object, e As EventArgs) Handles mnuStyleFont.Click
        Dim o = SelectedTextDrawing()
        Dim fam = If(o IsNot Nothing, o.FontName, _newTextFont)
        Dim style = FontStyle.Regular
        If If(o IsNot Nothing, o.Bold, _newTextBold) Then style = style Or FontStyle.Bold
        If If(o IsNot Nothing, o.Italic, _newTextItalic) Then style = style Or FontStyle.Italic
        Dim old = _styleDialogFont
        Try
            _styleDialogFont = New Font(fam, 12.0F, style, GraphicsUnit.Point)
        Catch
            _styleDialogFont = New Font(Font.FontFamily, 12.0F)
        End Try
        dlgFont.Font = _styleDialogFont
        old?.Dispose()
        dlgFont.ShowEffects = False
        If dlgFont.ShowDialog(Me) <> DialogResult.OK Then Return
        Dim chosen = dlgFont.Font
        ApplyTextStyle(Sub(t)
                           t.FontName = chosen.FontFamily.Name
                           t.Bold = chosen.Bold
                           t.Italic = chosen.Italic
                       End Sub)
    End Sub

    ''' <summary>
    ''' New sides or points: a polygon or star that had its own proportions keeps them
    ''' (same height and centre), so it stays regular.
    ''' </summary>
    Private Sub KeepRegular(o As DesignObject, propertyName As String, oldValue As Object)
        If oldValue Is Nothing Then Return
        Dim before = o.Clone()
        Select Case propertyName
            Case NameOf(DesignObject.Sides) : before.Sides = Convert.ToInt32(oldValue, Globalization.CultureInfo.InvariantCulture)
            Case NameOf(DesignObject.StarPoints) : before.StarPoints = Convert.ToInt32(oldValue, Globalization.CultureInfo.InvariantCulture)
            Case Else : before.StarInnerPercent = Convert.ToDouble(oldValue, Globalization.CultureInfo.InvariantCulture)
        End Select
        before.Normalize()
        before.EnsureParsed(_settings.CurveTolerance)
        o.Normalize()
        o.EnsureParsed(_settings.CurveTolerance)
        If before.Shape Is Nothing OrElse o.Shape Is Nothing OrElse o.Height <= 0 Then Return
        If Math.Abs(o.Width / o.Height / before.NaturalAspect - 1) > 0.01 Then Return
        Dim centre = o.LocalToWorld(0, 0)
        o.Width = Math.Round(o.Height * o.NaturalAspect, 4)
        o.PlaceLocal(0, 0, centre)
    End Sub

    ''' <summary>Problems of visible drawings for the status bar: not buildable, or warnings of drawn shapes (fonts).</summary>
    Private Function DrawingNotes() As List(Of String)
        Dim notes As New List(Of String)
        For Each o In _objects
            If Not o.Visible Then Continue For
            o.EnsureParsed(_settings.CurveTolerance)
            If o.ParseError IsNot Nothing Then
                notes.Add(o.Name & ": " & o.ParseError)
            ElseIf o.IsDrawn AndAlso o.Shape IsNot Nothing Then
                notes.AddRange(o.Shape.Warnings.Select(Function(w) o.Name & ": " & w))
            End If
        Next
        Return notes
    End Function

    ' --------------------------------------------------------------- arrange

    ''' <summary>After a drawing changed outside the grid: refit text, refresh grid, list and view, regenerate.</summary>
    Private Sub AfterDrawingChanged(o As DesignObject)
        o.Normalize()
        If o.Kind = DrawingKind.Text Then o.FitTextBox(_settings.CurveTolerance)
        pgObject.Refresh()
        Dim i = _objects.IndexOf(o)
        If i >= 0 AndAlso i < lstObjects.Items.Count AndAlso lstObjects.Items(i).ToString() <> ObjectListText(o) Then
            _syncingSelection = True
            Try
                lstObjects.Items(i) = ObjectListText(o)
            Finally
                _syncingSelection = False
            End Try
        End If
        _glView.Objects = _objects
        If _editingObject IsNot Nothing Then PositionTextEditor()
        UpdateDrawUi()
        If o.ParseError IsNot Nothing Then lblStatus.Text = o.Name & ": " & o.ParseError
        MarkDirty()
        RequestRegenerate(immediate:=True)
    End Sub

    Private Sub mnuDrawRotateLeft_Click(sender As Object, e As EventArgs) Handles mnuDrawRotateLeft.Click
        RotateSelected(90)
    End Sub

    Private Sub mnuDrawRotateRight_Click(sender As Object, e As EventArgs) Handles mnuDrawRotateRight.Click
        RotateSelected(-90)
    End Sub

    Private Sub RotateSelected(degrees As Double)
        Dim o = SelectedDrawing()
        If o Is Nothing Then Return
        Dim a = (o.RotationDeg + degrees) Mod 360.0
        If a > 180 Then a -= 360
        If a <= -180 Then a += 360
        o.RotationDeg = Math.Round(a, 2)
        AfterDrawingChanged(o)
    End Sub

    Private Sub mnuDrawMirror_Click(sender As Object, e As EventArgs) Handles mnuDrawMirror.Click
        Dim o = SelectedDrawing()
        If o Is Nothing Then Return
        o.Mirror = Not o.Mirror
        AfterDrawingChanged(o)
    End Sub

    Private Sub mnuDrawFront_Click(sender As Object, e As EventArgs) Handles mnuDrawFront.Click
        MoveSelectedInOrder(toFront:=True)
    End Sub

    Private Sub mnuDrawBack_Click(sender As Object, e As EventArgs) Handles mnuDrawBack.Click
        MoveSelectedInOrder(toFront:=False)
    End Sub

    ''' <summary>Front = last in the list (drawn and picked on top).</summary>
    Private Sub MoveSelectedInOrder(toFront As Boolean)
        Dim i = _glView.SelectedObject
        If i < 0 OrElse i >= _objects.Count Then Return
        Dim o = _objects(i)
        _objects.RemoveAt(i)
        If toFront Then _objects.Add(o) Else _objects.Insert(0, o)
        RefreshObjectList(_objects.IndexOf(o))
        MarkDirty()
        RequestRegenerate(immediate:=True)
    End Sub

    ''' <summary>Hides the selected drawing: not shown, picked or carved until shown again.</summary>
    Private Sub HideSelected_Click(sender As Object, e As EventArgs) Handles mnuDrawHide.Click, tsbHide.Click
        Dim o = SelectedDrawing()
        If o Is Nothing OrElse Not o.Visible Then Return
        EndTextEdit(commit:=True, focusView:=False)
        o.Visible = False
        RefreshObjectList(-1)
        _statusNote = o.Name & " is hidden: Show All (Ctrl+Shift+H) shows it again."
        lblStatus.Text = _statusNote
        MarkDirty()
        RequestRegenerate(immediate:=True)
    End Sub

    ''' <summary>Shows every hidden drawing again.</summary>
    Private Sub ShowAll_Click(sender As Object, e As EventArgs) Handles mnuDrawShowAll.Click, tsbShowAll.Click
        Dim hidden = _objects.Where(Function(o) Not o.Visible).ToList()
        If hidden.Count = 0 Then Return
        For Each o In hidden
            o.Visible = True
        Next
        RefreshObjectList(_glView.SelectedObject)
        _statusNote = If(hidden.Count = 1, hidden(0).Name & " is shown again.", hidden.Count & " drawings are shown again.")
        lblStatus.Text = _statusNote
        MarkDirty()
        RequestRegenerate(immediate:=True)
    End Sub

    Private Sub mnuDrawDelete_Click(sender As Object, e As EventArgs) Handles mnuDrawDelete.Click
        RemoveDrawingAt(_glView.SelectedObject, selectNext:=False)
    End Sub

    ''' <summary>Removes a drawing (closing its text editor first).</summary>
    Private Sub RemoveDrawingAt(idx As Integer, selectNext As Boolean)
        If idx < 0 OrElse idx >= _objects.Count Then Return
        If _editingObject Is _objects(idx) Then
            _editingObject = Nothing
            _textEditor.Visible = False
        End If
        _objects.RemoveAt(idx)
        RefreshObjectList(If(selectNext, Math.Min(idx, _objects.Count - 1), -1))
        MarkDirty()
        RequestRegenerate(immediate:=True)
    End Sub

    ' ----------------------------------------------------------------- icons

    Private Shared Function NewIcon(paint As Action(Of Graphics, Pen)) As Bitmap
        Dim bmp As New Bitmap(16, 16)
        Using g = Graphics.FromImage(bmp)
            g.SmoothingMode = SmoothingMode.AntiAlias
            Using pen As New Pen(Color.FromArgb(40, 40, 48), 1.4F)
                paint(g, pen)
            End Using
        End Using
        Return bmp
    End Function

    ''' <summary>Small toolbar pictures of the draw tools.</summary>
    Private Shared Function DrawIcon(tool As DrawTool) As Bitmap
        Select Case tool
            Case DrawTool.Rectangle
                Return NewIcon(Sub(g, p) g.DrawRectangle(p, 2, 4, 12, 8))
            Case DrawTool.Ellipse
                Return NewIcon(Sub(g, p) g.DrawEllipse(p, 1.5F, 3.5F, 13, 9))
            Case DrawTool.Polygon
                Return NewIcon(Sub(g, p) g.DrawPolygon(p, Enumerable.Range(0, 6).Select(Function(i) New PointF(CSng(8 + 6.5 * Math.Cos(Math.PI / 6 + i * Math.PI / 3)), CSng(8 + 6.5 * Math.Sin(Math.PI / 6 + i * Math.PI / 3)))).ToArray()))
            Case DrawTool.Star
                Return NewIcon(Sub(g, p) g.DrawPolygon(p, Enumerable.Range(0, 10).Select(Function(i) New PointF(CSng(8 + If(i Mod 2 = 0, 7, 3) * Math.Cos(-Math.PI / 2 + i * Math.PI / 5)), CSng(8.5 + If(i Mod 2 = 0, 7, 3) * Math.Sin(-Math.PI / 2 + i * Math.PI / 5)))).ToArray()))
            Case DrawTool.Area
                Return NewIcon(Sub(g, p)
                                   Dim pts = {New PointF(2, 12), New PointF(5, 3), New PointF(10, 6), New PointF(14, 2), New PointF(13, 13)}
                                   g.DrawPolygon(p, pts)
                                   For Each q In pts
                                       g.FillRectangle(Brushes.SteelBlue, q.X - 1.5F, q.Y - 1.5F, 3, 3)
                                   Next
                               End Sub)
            Case DrawTool.Text
                Return NewIcon(Sub(g, p)
                                   g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit
                                   Using f As New Font("Georgia", 11.0F, FontStyle.Bold, GraphicsUnit.Pixel)
                                       Using b As New SolidBrush(p.Color)
                                           g.DrawString("T", f, b, 2.0F, 1.0F)
                                       End Using
                                   End Using
                               End Sub)
            Case Else
                ' Select: an arrow pointer.
                Return NewIcon(Sub(g, p)
                                   Dim arrow = {New PointF(4, 1), New PointF(4, 13), New PointF(7, 10), New PointF(9.5F, 15), New PointF(11.5F, 14), New PointF(9, 9), New PointF(13, 9)}
                                   g.FillPolygon(Brushes.White, arrow)
                                   g.DrawPolygon(p, arrow)
                               End Sub)
        End Select
    End Function

    ''' <summary>An eye; crossed out for Hide.</summary>
    Private Shared Function EyeIcon(crossed As Boolean) As Bitmap
        Return NewIcon(Sub(g, p)
                           Using path As New GraphicsPath()
                               path.AddBezier(1.5F, 8, 4.5F, 3, 11.5F, 3, 14.5F, 8)
                               path.AddBezier(14.5F, 8, 11.5F, 13, 4.5F, 13, 1.5F, 8)
                               g.DrawPath(p, path)
                           End Using
                           Using b As New SolidBrush(Color.FromArgb(40, 110, 210))
                               g.FillEllipse(b, 5.5F, 5.5F, 5, 5)
                           End Using
                           If crossed Then
                               Using red As New Pen(Color.FromArgb(200, 40, 40), 1.8F)
                                   g.DrawLine(red, 2, 14, 14, 2)
                               End Using
                           End If
                       End Sub)
    End Function

    ''' <summary>A grid of dots with a corner square sitting on one of them.</summary>
    Private Shared Function SnapIcon() As Bitmap
        Return NewIcon(Sub(g, p)
                           For gx = 0 To 3
                               For gy = 0 To 3
                                   g.FillRectangle(Brushes.Gray, 1.5F + gx * 4, 1.5F + gy * 4, 1.6F, 1.6F)
                               Next
                           Next
                           Using b As New SolidBrush(Color.FromArgb(40, 110, 210))
                               g.FillRectangle(b, 5.5F, 5.5F, 5, 5)
                           End Using
                           g.DrawRectangle(p, 5.5F, 5.5F, 5, 5)
                       End Sub)
    End Function

    ''' <summary>Four lines lined up left, centred or right.</summary>
    Private Shared Function AlignIcon(align As TextAlign) As Bitmap
        Return NewIcon(Sub(g, p)
                           Dim lengths = {12, 8, 11, 6}
                           For i = 0 To 3
                               Dim len = lengths(i)
                               Dim x0 As Single = If(align = TextAlign.Left, 2, If(align = TextAlign.Right, 14 - len, 8 - len / 2.0F))
                               g.DrawLine(p, x0, 3 + i * 3.3F, x0 + len, 3 + i * 3.3F)
                           Next
                       End Sub)
    End Function
End Class
