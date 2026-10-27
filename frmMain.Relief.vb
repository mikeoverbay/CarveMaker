' ============================================================================
'  frmMain.Relief.vb
'  3D models on the blank: File > Import 3D Model / Depth Image (read on a
'  worker thread: a big STL takes seconds), edits of a model's own rows
'  (carved side, units) and Draw > Scale for any drawing.
' ============================================================================

Imports System.Globalization
Imports System.IO
Imports System.Threading
Imports System.Threading.Tasks

Partial Class frmMain

    ' One import at a time; the form stays usable while the file is read.
    Private _importing As Boolean

    ' While a toolpath is being computed: a busy bar and Abort in the status bar, and Toolpath > Stop Generating.
    Private _tsbAbort As ToolStripStatusLabel
    Private _tspGenerating As ToolStripProgressBar
    Private _mnuToolpathAbort As ToolStripMenuItem

    ''' <summary>The generation controls (called from Load).</summary>
    Private Sub SetupGenerationUi()
        _tspGenerating = New ToolStripProgressBar With {.Name = "tspGenerating", .Style = ProgressBarStyle.Marquee, .MarqueeAnimationSpeed = 30,
                                                        .Size = New Size(90, 16), .Visible = False, .ToolTipText = "Computing the toolpath"}
        _tsbAbort = New ToolStripStatusLabel With {.Name = "tsbAbort", .Text = "Abort", .IsLink = True, .Visible = False,
                                                   .ToolTipText = "Stop computing the toolpath (the last finished toolpath stays). Shift+F5"}
        AddHandler _tsbAbort.Click, Sub(o, ev) AbortGeneration()
        stsMain.Items.Insert(1, _tspGenerating)
        stsMain.Items.Insert(2, _tsbAbort)
        _mnuToolpathAbort = New ToolStripMenuItem("&Stop Generating") With {.Name = "mnuToolpathAbort", .ShortcutKeys = Keys.Shift Or Keys.F5, .Enabled = False}
        AddHandler _mnuToolpathAbort.Click, Sub(o, ev) AbortGeneration()
        mnuToolpath.DropDownItems.Insert(mnuToolpath.DropDownItems.IndexOf(mnuToolpathGenerate) + 1, _mnuToolpathAbort)
    End Sub

    Private Async Sub mnuFileImportModel_Click(sender As Object, e As EventArgs) Handles mnuFileImportModel.Click, tsbObjImportModel.Click
        If _importing Then
            lblStatus.Text = "A model is still being read."
            Return
        End If
        dlgModel.Filter = ModelImport.FileFilter
        If dlgModel.ShowDialog(Me) <> DialogResult.OK Then Return
        Await ImportModelFile(dlgModel.FileName)
    End Sub

    ''' <summary>
    ''' Reads a 3D model or depth image on a worker thread and adds it at its natural size, centred
    ''' on the blank. The returned task completes (on the UI thread) once the model is placed.
    ''' </summary>
    Public Async Function ImportModelFile(path As String, Optional options As ModelImportOptions = Nothing) As Task
        EndTextEdit(commit:=True, focusView:=False)
        _importing = True
        UseWaitCursor = True
        Dim name = IO.Path.GetFileName(path)
        Dim size As Long = 0
        Try
            size = New FileInfo(path).Length
        Catch
        End Try
        lblStatus.Text = String.Format(CultureInfo.InvariantCulture, "Reading {0} ({1:0.#} MB)...", name, size / 1048576.0)
        Dim opts = If(options, New ModelImportOptions())
        ' Await (not a task continuation): the rest runs as an ordinary UI callback, so the toolpath
        ' generation it starts can post its own result back to this thread.
        Dim o As DesignObject = Nothing
        Dim failure As Exception = Nothing
        Try
            o = Await Task.Run(Function() DesignObject.FromModel(ModelImport.Load(path, opts, CancellationToken.None), path))
        Catch ex As Exception
            failure = ex
        End Try
        ' Without a UI synchronization context (a caller that pumps messages itself) the await resumes on
        ' a pool thread: the controls may only be touched on their own thread.
        If InvokeRequired Then
            Dim done As New TaskCompletionSource(Of Boolean)()
            BeginInvoke(New Action(Sub()
                                       FinishImport(o, failure, name)
                                       done.SetResult(True)
                                   End Sub))
            Await done.Task
        Else
            FinishImport(o, failure, name)
        End If
    End Function

    ''' <summary>The end of an import, on the UI thread: place the model or report why it failed.</summary>
    Private Sub FinishImport(o As DesignObject, failure As Exception, name As String)
        _importing = False
        UseWaitCursor = False
        If IsDisposed Then Return
        If failure IsNot Nothing Then
            lblStatus.Text = "Could not import " & name
            MessageBox.Show(Me, "Could not import the model:" & Environment.NewLine & failure.GetBaseException().Message, "Import 3D Model", MessageBoxButtons.OK, MessageBoxIcon.Error)
            Return
        End If
        AddImportedModel(o)
    End Sub

    ''' <summary>Places a freshly imported model: natural size, centred on the blank, selected.</summary>
    Private Sub AddImportedModel(o As DesignObject)
        o.EnsureParsed(_settings.CurveTolerance)
        If o.ParseError IsNot Nothing Then
            MessageBox.Show(Me, "Could not import the model:" & Environment.NewLine & o.ParseError, "Import 3D Model", MessageBoxButtons.OK, MessageBoxIcon.Error)
            Return
        End If
        CenterObjectOnBlank(o)
        ' Show the Drawings tab first, so its list and grid are created inside the visible page.
        tabLeft.SelectedTab = tabObjects
        _objects.Add(o)
        RefreshObjectList(_objects.Count - 1)
        Dim ci = CultureInfo.InvariantCulture
        Dim note = String.Format(ci, "{0}: {1:0.###} x {2:0.###} in, {3:0.###} in deep ({4}).", o.Name, o.Width, o.Height, o.Depth, o.Relief.Description)
        If o.Width > _settings.BlankWidthIn + 0.0001 OrElse o.Height > _settings.BlankHeightIn + 0.0001 Then
            note &= String.Format(ci, " It is larger than the {0:0.###} x {1:0.###} in blank: type its Width (or Scale %) in the Relief rows, or set the blank under Machine > Machine Settings.", _settings.BlankWidthIn, _settings.BlankHeightIn)
        End If
        If o.Relief.Warnings.Count > 0 Then note &= "  |  " & String.Join("  |  ", o.Relief.Warnings)
        lblStatus.Text = note
        _statusNote = note
        _glView.ZoomToFit()
        MarkDirty()
        RequestRegenerate(immediate:=True)
    End Sub

    ''' <summary>
    ''' A 3D model's own rows changed in the grid. Returns False when the change was undone
    ''' (the model file could not be read again).
    ''' </summary>
    Private Function ModelPropertyChanged(o As DesignObject, name As String, oldValue As Object) As Boolean
        Select Case name
            Case NameOf(DesignObject.ModelUp)
                ' Another side up is another heightmap: read the file again.
                Dim path = o.ModelPath
                If String.IsNullOrEmpty(path) OrElse Not File.Exists(path) Then
                    If TypeOf oldValue Is ModelUpAxis Then o.ModelUp = DirectCast(oldValue, ModelUpAxis)
                    MessageBox.Show(Me, "The model file is needed to carve another side, but it was not found:" & Environment.NewLine & If(path, ""),
                                    "Carved Side", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                    Return False
                End If
                UseWaitCursor = True
                Try
                    o.ReplaceModel(ModelImport.Load(path, New ModelImportOptions With {.Up = o.ModelUp, .Units = o.ModelUnits}, CancellationToken.None))
                Catch ex As Exception
                    If TypeOf oldValue Is ModelUpAxis Then o.ModelUp = DirectCast(oldValue, ModelUpAxis)
                    MessageBox.Show(Me, "Could not read the model again:" & Environment.NewLine & ex.Message, "Carved Side", MessageBoxButtons.OK, MessageBoxIcon.Error)
                    Return False
                Finally
                    UseWaitCursor = False
                End Try
            Case NameOf(DesignObject.ModelUnits)
                ' Same scale percentage in the new units: the size follows.
                If TypeOf oldValue Is ModelUnits Then
                    Dim oldUnits = DirectCast(oldValue, ModelUnits)
                    Dim oldNatural = o.NaturalWidthIn * DesignObject.UnitInches(oldUnits) / DesignObject.UnitInches(o.ModelUnits)
                    If oldNatural > 0 Then
                        Dim pct = 100.0 * o.Width / oldNatural
                        o.InvalidateShape()
                        o.ScalePercent = pct
                    End If
                End If
        End Select
        Return True
    End Function

    ' ------------------------------------------------------------------ scale

    Private Sub mnuDrawScale_Click(sender As Object, e As EventArgs) Handles mnuDrawScale.Click
        Dim o = SelectedDrawing()
        If o Is Nothing Then Return
        EndTextEdit(commit:=True, focusView:=False)
        o.EnsureParsed(_settings.CurveTolerance)
        Using dlg As New frmScale(o)
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
            ApplyScale(o, dlg.NewWidth, dlg.NewHeight, dlg.NewDepth)
        End Using
    End Sub

    ''' <summary>Resizes a drawing about its centre (text by its letter height; a model's depth when given).</summary>
    Friend Sub ApplyScale(o As DesignObject, newWidth As Double, newHeight As Double, newDepth As Double)
        If Not (newWidth > 0 AndAlso newHeight > 0) Then Return
        Dim centre = o.LocalToWorld(0, 0)
        If o.Kind = DrawingKind.Text Then
            Dim k = newWidth / Math.Max(0.0001, o.Width)
            o.LetterHeight = Math.Round(Math.Max(0.02, Math.Min(100.0, o.LetterHeight * k)), 4)
            o.Normalize()
            o.FitTextBox(_settings.CurveTolerance)
        Else
            o.Width = Math.Round(newWidth, 4)
            o.Height = Math.Round(newHeight, 4)
        End If
        o.PlaceLocal(0, 0, centre)
        If o.Kind = DrawingKind.Model AndAlso newDepth > 0 AndAlso Math.Abs(newDepth - o.Depth) > 0.00005 Then o.Depth = newDepth
        AfterDrawingChanged(o)
    End Sub
End Class

''' <summary>Draw > Scale: a new size for the selected drawing, by percentage or in inches.</summary>
Friend Class frmScale
    Inherits Form

    Private ReadOnly _o As DesignObject
    Private ReadOnly _aspect As Double
    Private ReadOnly _txtPercent As New TextBox()
    Private ReadOnly _txtWidth As New TextBox()
    Private ReadOnly _txtHeight As New TextBox()
    Private ReadOnly _txtDepth As New TextBox()
    Private ReadOnly _chkKeep As New CheckBox()
    Private _updating As Boolean

    Public Property NewWidth As Double
    Public Property NewHeight As Double
    ''' <summary>Depth for a 3D model (0 = unchanged).</summary>
    Public Property NewDepth As Double

    Public Sub New(o As DesignObject)
        _o = o
        _aspect = If(o.Height > 0, o.Width / o.Height, 1.0)
        NewWidth = o.Width
        NewHeight = o.Height
        Text = "Scale " & o.Name
        FormBorderStyle = FormBorderStyle.FixedDialog
        MaximizeBox = False
        MinimizeBox = False
        ShowInTaskbar = False
        StartPosition = FormStartPosition.CenterParent
        AutoScaleMode = AutoScaleMode.Font
        Font = New Font("Segoe UI", 9.0F)

        Dim isModel = o.Kind = DrawingKind.Model
        Dim grid As New TableLayoutPanel With {.ColumnCount = 2, .Dock = DockStyle.Fill, .Padding = New Padding(12), .AutoSize = True}
        grid.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
        grid.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 120))
        Dim addRow = Sub(label As String, ctl As Control)
                         grid.Controls.Add(New Label With {.Text = label, .AutoSize = True, .Anchor = AnchorStyles.Left, .Margin = New Padding(0, 6, 8, 0)})
                         ctl.Dock = DockStyle.Fill
                         grid.Controls.Add(ctl)
                     End Sub
        addRow("Scale (%):", _txtPercent)
        addRow("Width (in):", _txtWidth)
        addRow("Height (in):", _txtHeight)
        _chkKeep.Text = "Keep proportions"
        _chkKeep.Checked = o.LockAspect OrElse o.Kind = DrawingKind.Text
        _chkKeep.Enabled = o.Kind <> DrawingKind.Text
        _chkKeep.AutoSize = True
        grid.Controls.Add(New Label())
        grid.Controls.Add(_chkKeep)
        If isModel Then addRow("Depth (in):", _txtDepth)
        Dim buttons As New FlowLayoutPanel With {.FlowDirection = FlowDirection.RightToLeft, .Dock = DockStyle.Bottom, .AutoSize = True, .Padding = New Padding(8)}
        Dim ok As New Button With {.Text = "OK", .DialogResult = DialogResult.OK, .Width = 80}
        Dim cancel As New Button With {.Text = "Cancel", .DialogResult = DialogResult.Cancel, .Width = 80}
        buttons.Controls.Add(cancel)
        buttons.Controls.Add(ok)
        AcceptButton = ok
        CancelButton = cancel
        Controls.Add(grid)
        Controls.Add(buttons)
        AutoSize = True
        AutoSizeMode = AutoSizeMode.GrowAndShrink

        _txtPercent.Text = "100"
        _txtWidth.Text = InchFormat.EditText(o.Width)
        _txtHeight.Text = InchFormat.EditText(o.Height)
        If isModel Then _txtDepth.Text = InchFormat.EditText(o.Depth)
        AddHandler _txtPercent.TextChanged, Sub(s, e) FromPercent()
        AddHandler _txtWidth.TextChanged, Sub(s, e) FromSide(True)
        AddHandler _txtHeight.TextChanged, Sub(s, e) FromSide(False)
        AddHandler ok.Click, AddressOf Ok_Click
    End Sub

    Private Sub FromPercent()
        If _updating Then Return
        Dim pct As Double
        If Not Double.TryParse(_txtPercent.Text, NumberStyles.Float, CultureInfo.CurrentCulture, pct) OrElse pct <= 0 Then Return
        _updating = True
        _txtWidth.Text = InchFormat.EditText(_o.Width * pct / 100)
        _txtHeight.Text = InchFormat.EditText(_o.Height * pct / 100)
        If _o.Kind = DrawingKind.Model AndAlso _o.DepthFollowsSize Then _txtDepth.Text = InchFormat.EditText(_o.Depth * pct / 100)
        _updating = False
    End Sub

    Private Sub FromSide(isWidth As Boolean)
        If _updating Then Return
        Dim v As Double
        If Not InchFormat.TryParse(If(isWidth, _txtWidth, _txtHeight).Text, v) OrElse v <= 0 Then Return
        _updating = True
        Dim k = If(isWidth, v / _o.Width, v / _o.Height)
        If _chkKeep.Checked Then
            If isWidth Then _txtHeight.Text = InchFormat.EditText(v / _aspect) Else _txtWidth.Text = InchFormat.EditText(v * _aspect)
            _txtPercent.Text = (k * 100).ToString("0.###", CultureInfo.CurrentCulture)
            If _o.Kind = DrawingKind.Model AndAlso _o.DepthFollowsSize Then _txtDepth.Text = InchFormat.EditText(_o.Depth * k)
        End If
        _updating = False
    End Sub

    Private Sub Ok_Click(sender As Object, e As EventArgs)
        Dim w, h As Double
        If Not InchFormat.TryParse(_txtWidth.Text, w) OrElse Not InchFormat.TryParse(_txtHeight.Text, h) OrElse w <= 0 OrElse h <= 0 Then
            MessageBox.Show(Me, "Width and height must be positive numbers (inches).", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning)
            DialogResult = DialogResult.None
            Return
        End If
        Dim d As Double = 0
        If _o.Kind = DrawingKind.Model AndAlso Not InchFormat.TryParse(_txtDepth.Text, d) Then d = 0
        NewWidth = w
        NewHeight = h
        NewDepth = d
    End Sub
End Class
