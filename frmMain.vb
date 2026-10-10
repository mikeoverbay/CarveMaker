' ============================================================================
'  frmMain.vb
'  Main window: formatted text editor (three letter sizes, per-line alignment)
'  + settings on the left, OpenTK toolpath view on the right. Regenerates the
'  V-carve toolpath on a worker thread whenever the text or settings change.
'
'  Editor model: every line of the RichTextBox carries one of three size
'  "slots" (shown at 24 / 16 / 11 pt) and a paragraph alignment. The slot maps
'  to an inch size in the settings; the alignment is used as-is.
' ============================================================================

Imports System.Globalization
Imports System.IO
Imports System.Runtime.InteropServices
Imports System.Threading
Imports System.Threading.Tasks

Public Class frmMain

    ' Display point sizes of the three slots in the editor (not the carve sizes).
    Private Const PtLarge As Single = 24.0F
    Private Const PtMedium As Single = 16.0F
    Private Const PtSmall As Single = 11.0F

    Private _settings As New CarveSettings()
    Private _glView As ToolpathView
    Private _toolpath As Toolpath
    Private _lastLinesKey As String = ""
    Private _lastSettings As CarveSettings

    ' Project file state.
    Private _projectPath As String
    Private _dirty As Boolean

    ' Imported drawings placed on the blank (shared with the view for dragging).
    Private _objects As New List(Of DesignObject)
    Private _syncingSelection As Boolean

    ' Generation bookkeeping: only the newest request is allowed to publish a result.
    Private _genVersion As Integer
    Private _genCts As CancellationTokenSource
    Private _running As Boolean

    ' True while the code itself moves the editor selection / changes formatting.
    Private _suppressEditorEvents As Boolean

    <DllImport("user32.dll")>
    Private Shared Function SendMessage(hWnd As IntPtr, msg As Integer, wParam As IntPtr, lParam As IntPtr) As IntPtr
    End Function

    <StructLayout(LayoutKind.Sequential)>
    Private Structure NativePoint
        Public X As Integer
        Public Y As Integer
    End Structure

    <DllImport("user32.dll")>
    Private Shared Function SendMessage(hWnd As IntPtr, msg As Integer, wParam As IntPtr, ByRef lParam As NativePoint) As IntPtr
    End Function

    Private Const WM_SETREDRAW As Integer = &HB
    Private Const WM_USER As Integer = &H400
    Private Const EM_GETSCROLLPOS As Integer = WM_USER + 221
    Private Const EM_SETSCROLLPOS As Integer = WM_USER + 222

    ' ------------------------------------------------------------------ setup

    Private Sub frmMain_Load(sender As Object, e As EventArgs) Handles MyBase.Load
        ' Window icon (title bar and taskbar) from the embedded CarveMaker.ico.
        Try
            Using st = GetType(frmMain).Assembly.GetManifestResourceStream("app.ico")
                If st IsNot Nothing Then Icon = New Icon(st)
            End Using
        Catch
            ' Keep the default icon if the resource is missing or unreadable.
        End Try

        ' The GL surface is created in code (not in the designer) so the
        ' WinForms designer never has to instantiate an OpenGL context.
        _glView = New ToolpathView() With {
            .Dock = DockStyle.Fill,
            .ShowRapids = mnuViewRapids.Checked,
            .ShowOutline = mnuViewOutline.Checked,
            .ShowGrid = mnuViewGrid.Checked
        }
        pnlView.Controls.Add(_glView)
        _glView.BringToFront()
        If _glView.InitError IsNot Nothing Then lblStatus.Text = "OpenGL view unavailable: " & _glView.InitError
        AddHandler _glView.SimulationProgress, AddressOf OnSimulationProgress
        AddHandler _glView.GLReady, Sub(o, ev) pgSettings.Refresh()
        AddHandler _glView.SelectionChanged, AddressOf OnViewSelectionChanged
        AddHandler _glView.ObjectEdited, AddressOf OnViewObjectEdited
        AddHandler _glView.TextMoved, AddressOf OnViewTextMoved
        _glView.Objects = _objects
        _glView.CurveTolerance = _settings.CurveTolerance
        tscSimSpeed.SelectedIndex = 3      ' 10x

        pgSettings.SelectedObject = _settings
        UpdateSizeButtonCaptions()

        ' Sample text: one large centred line.
        _suppressEditorEvents = True
        rtbText.Font = EditorFontFor(SizeSlot.Large)
        rtbText.Text = "Hello"
        rtbText.SelectAll()
        rtbText.SelectionFont = EditorFontFor(SizeSlot.Large)
        rtbText.SelectionAlignment = HorizontalAlignment.Center
        rtbText.Select(rtbText.TextLength, 0)
        _suppressEditorEvents = False

        UpdateFormatButtons()
        UpdateStats(Nothing)
        _dirty = False
        UpdateTitle()

        ' Opened from Explorer / the installer's .prj association: load that project.
        Dim startupProject As String = Nothing
        For Each a In My.Application.CommandLineArgs
            If a.EndsWith(ProjectFile.Extension, StringComparison.OrdinalIgnoreCase) AndAlso File.Exists(a) Then
                startupProject = a
                Exit For
            End If
        Next
        If startupProject IsNot Nothing Then
            Try
                Dim pf = ProjectFile.Load(startupProject)
                _projectPath = startupProject
                LoadProjectIntoUi(pf)
                lblStatus.Text = "Opened " & startupProject
                Return
            Catch ex As Exception
                MessageBox.Show(Me, "Could not open the project:" & Environment.NewLine & ex.Message, "Open Project", MessageBoxButtons.OK, MessageBoxIcon.Error)
            End Try
        End If
        RequestRegenerate(immediate:=True)
    End Sub

    Private Sub frmMain_FormClosing(sender As Object, e As FormClosingEventArgs) Handles MyBase.FormClosing
        If Not ConfirmDiscardChanges() Then
            e.Cancel = True
            Return
        End If
        _genCts?.Cancel()
    End Sub

    ' ---------------------------------------------------------------- project

    Private Sub UpdateTitle()
        Dim name As String = If(String.IsNullOrEmpty(_projectPath), "Untitled", Path.GetFileNameWithoutExtension(_projectPath))
        Text = "CarveMaker - " & name & If(_dirty, "*", "")
    End Sub

    Private Sub MarkDirty()
        If _dirty Then Return
        _dirty = True
        UpdateTitle()
    End Sub

    ''' <summary>Asks to save unsaved changes. Returns False when the user cancels.</summary>
    Private Function ConfirmDiscardChanges() As Boolean
        If Not _dirty Then Return True
        Dim r = MessageBox.Show(Me, "Save changes to the project?", "CarveMaker",
                                MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question)
        If r = DialogResult.Cancel Then Return False
        If r = DialogResult.Yes Then Return SaveProject(saveAs:=False)
        Return True
    End Function

    ''' <summary>Builds the project object from the current settings and editor content.</summary>
    Private Function CollectProject() As ProjectFile
        Dim pf As New ProjectFile With {.Settings = _settings.Clone()}
        pf.Lines.AddRange(ReadEditorParagraphs())
        pf.Objects.AddRange(_objects.Select(Function(o) o.Clone()))
        Return pf
    End Function

    ' ---------------------------------------------------------- drawings

    ''' <summary>Placed polygons of every visible drawing (computed on the UI thread, cached parse).</summary>
    ''' <summary>Visible drawings with their machining (copies: the worker thread uses them).</summary>
    Private Function PlacedItems() As List(Of MachiningItem)
        Dim list As New List(Of MachiningItem)
        For Each o In _objects
            If o.Visible Then list.Add(New MachiningItem(o.Name, o.PlacedPaths(_settings.CurveTolerance), If(o.Machining?.Clone(), New Machining())))
        Next
        Return list
    End Function

    ''' <summary>Key describing all drawings, used to detect changes since the last generation.</summary>
    Private Function ObjectsKey() As String
        Dim sb As New Text.StringBuilder()
        Dim ci = CultureInfo.InvariantCulture
        For Each o In _objects
            sb.Append(o.Name).Append("|").Append(o.X.ToString("R", ci)).Append("|").Append(o.Y.ToString("R", ci)).Append("|").
               Append(o.Width.ToString("R", ci)).Append("|").Append(o.Height.ToString("R", ci)).Append("|").Append(o.RotationDeg.ToString("R", ci)).
               Append("|").Append(o.Mirror).Append("|").Append(o.Visible).Append("|").Append(If(o.SvgContent, "").Length).
               Append("|").Append(If(o.Machining?.Key(), "")).Append(ControlChars.Lf)
        Next
        Return sb.ToString()
    End Function

    Private Shared Function ObjectListText(o As DesignObject) As String
        Dim op = If(o.Machining Is Nothing OrElse o.Machining.Operation = CutOperation.VCarve, "", "   [" & If(o.Machining.Operation = CutOperation.Pocket, "pocket", "profile") & "]")
        Return If(o.Visible, "", "(hidden) ") & o.Name & op
    End Function

    ''' <summary>Opens an expandable row (the machining settings) so its options are in view.</summary>
    Private Shared Sub ExpandGridRow(pg As PropertyGrid, propertyName As String)
        Try
            If pg.IsDisposed OrElse pg.SelectedObject Is Nothing Then Return
            Dim root As GridItem = pg.SelectedGridItem
            If root Is Nothing Then Return
            While root.Parent IsNot Nothing
                root = root.Parent
            End While
            For Each cat As GridItem In root.GridItems
                For Each gi As GridItem In cat.GridItems
                    If gi.PropertyDescriptor IsNot Nothing AndAlso gi.PropertyDescriptor.Name = propertyName AndAlso gi.Expandable Then gi.Expanded = True
                Next
            Next
        Catch ex As ObjectDisposedException
            ' The grid was rebuilt again meanwhile; the next selection change expands it.
        End Try
    End Sub

    ' The grid rebuilds its rows after SelectedObjectsChanged: expand once it has settled.
    Private Sub pgObject_SelectedObjectsChanged(sender As Object, e As EventArgs) Handles pgObject.SelectedObjectsChanged
        If IsHandleCreated Then BeginInvoke(Sub() ExpandGridRow(pgObject, NameOf(DesignObject.Machining)))
    End Sub

    Private Sub pgSettings_SelectedObjectsChanged(sender As Object, e As EventArgs) Handles pgSettings.SelectedObjectsChanged
        If IsHandleCreated Then BeginInvoke(Sub() ExpandGridRow(pgSettings, NameOf(CarveSettings.TextMachining)))
    End Sub

    Private Sub RefreshObjectList(selectIndex As Integer)
        _syncingSelection = True
        Try
            lstObjects.BeginUpdate()
            lstObjects.Items.Clear()
            For Each o In _objects
                lstObjects.Items.Add(ObjectListText(o))
            Next
            lstObjects.EndUpdate()
            If selectIndex >= 0 AndAlso selectIndex < _objects.Count Then
                lstObjects.SelectedIndex = selectIndex
                pgObject.SelectedObject = _objects(selectIndex)
            Else
                lstObjects.SelectedIndex = -1
                pgObject.SelectedObject = Nothing
            End If
        Finally
            _syncingSelection = False
        End Try
        _glView.Objects = _objects
        _glView.SelectedObject = selectIndex
        Dim has = selectIndex >= 0 AndAlso selectIndex < _objects.Count
        tsbObjRemove.Enabled = has
        tsbObjFit.Enabled = has
        tsbObjCenter.Enabled = has
    End Sub

    ''' <summary>The drawings currently placed on the blank (read-only view for tests and tools).</summary>
    Public ReadOnly Property Drawings As IReadOnlyList(Of DesignObject)
        Get
            Return _objects
        End Get
    End Property

    ''' <summary>Imports an SVG file as a new drawing centred on the blank.</summary>
    Public Sub ImportSvgFile(path As String)
        Dim o As New DesignObject With {
            .Name = IO.Path.GetFileNameWithoutExtension(path),
            .SvgFileName = IO.Path.GetFileName(path),
            .SvgContent = File.ReadAllText(path)
        }
        o.EnsureParsed(_settings.CurveTolerance)
        If o.ParseError IsNot Nothing Then
            MessageBox.Show(Me, "Could not import the SVG:" & Environment.NewLine & o.ParseError, "Import SVG", MessageBoxButtons.OK, MessageBoxIcon.Error)
            Return
        End If
        If o.Shape Is Nothing OrElse o.Shape.Paths.Count = 0 Then
            MessageBox.Show(Me, "No carveable shapes were found in the SVG." & Environment.NewLine & String.Join(Environment.NewLine, o.Shape?.Warnings), "Import SVG", MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return
        End If
        ' Natural size, shrunk to fit inside the margins if needed, centred on the blank.
        Dim maxW = Math.Max(0.1, _settings.BlankWidthIn - 2 * _settings.MarginIn)
        Dim maxH = Math.Max(0.1, _settings.BlankHeightIn - 2 * _settings.MarginIn)
        If o.Shape.Width <= maxW AndAlso o.Shape.Height <= maxH Then
            o.Width = Math.Round(o.Shape.Width, 4) : o.Height = Math.Round(o.Shape.Height, 4)
        Else
            o.FitTo(maxW, maxH)
        End If
        CenterObjectOnBlank(o)
        _objects.Add(o)
        RefreshObjectList(_objects.Count - 1)
        tabLeft.SelectedTab = tabObjects
        If o.Shape.Warnings.Count > 0 Then lblStatus.Text = String.Join("  |  ", o.Shape.Warnings)
        MarkDirty()
        RequestRegenerate(immediate:=True)
    End Sub

    Private Sub CenterObjectOnBlank(o As DesignObject)
        o.X = Math.Round(_settings.BlankOriginX + (_settings.BlankWidthIn - o.Width) / 2, 4)
        o.Y = Math.Round(_settings.BlankOriginY + (_settings.BlankHeightIn - o.Height) / 2, 4)
    End Sub

    Private Sub OnViewSelectionChanged(sender As Object, e As EventArgs)
        If _syncingSelection Then Return
        _syncingSelection = True
        Try
            Dim idx = _glView.SelectedObject
            If idx >= 0 AndAlso idx < _objects.Count Then
                lstObjects.SelectedIndex = idx
                pgObject.SelectedObject = _objects(idx)
                tabLeft.SelectedTab = tabObjects
            Else
                lstObjects.SelectedIndex = -1
                pgObject.SelectedObject = Nothing
                If idx = ToolpathView.TextSelection Then tabLeft.SelectedTab = tabText
            End If
            Dim has = idx >= 0 AndAlso idx < _objects.Count
            tsbObjRemove.Enabled = has
            tsbObjFit.Enabled = has
            tsbObjCenter.Enabled = has
        Finally
            _syncingSelection = False
        End Try
    End Sub

    Private Sub OnViewObjectEdited(sender As Object, e As ToolpathView.ObjectEditedEventArgs)
        pgObject.Refresh()
        MarkDirty()
        RequestRegenerate(immediate:=True)
    End Sub

    Private Sub OnViewTextMoved(sender As Object, e As ToolpathView.TextMovedEventArgs)
        _settings.TextOffsetX = Math.Round(_settings.TextOffsetX + e.Dx, 4)
        _settings.TextOffsetY = Math.Round(_settings.TextOffsetY + e.Dy, 4)
        pgSettings.Refresh()
        MarkDirty()
        RequestRegenerate(immediate:=True)
    End Sub

    Private Sub lstObjects_SelectedIndexChanged(sender As Object, e As EventArgs) Handles lstObjects.SelectedIndexChanged
        If _syncingSelection Then Return
        _syncingSelection = True
        Try
            Dim idx = lstObjects.SelectedIndex
            pgObject.SelectedObject = If(idx >= 0, _objects(idx), Nothing)
            _glView.SelectedObject = idx
            tsbObjRemove.Enabled = idx >= 0
            tsbObjFit.Enabled = idx >= 0
            tsbObjCenter.Enabled = idx >= 0
        Finally
            _syncingSelection = False
        End Try
    End Sub

    Private Sub pgObject_PropertyValueChanged(s As Object, e As PropertyValueChangedEventArgs) Handles pgObject.PropertyValueChanged
        Dim o = TryCast(pgObject.SelectedObject, DesignObject)
        If o Is Nothing Then Return
        Dim name = If(e.ChangedItem?.PropertyDescriptor?.Name, "")
        If o.LockAspect Then
            If name = NameOf(DesignObject.Width) Then o.Height = Math.Round(o.Width / o.NaturalAspect, 4)
            If name = NameOf(DesignObject.Height) Then o.Width = Math.Round(o.Height * o.NaturalAspect, 4)
        End If
        o.Width = Math.Max(0.01, o.Width)
        o.Height = Math.Max(0.01, o.Height)
        pgObject.Refresh()
        ' Keep the list row (name, hidden, operation) in step with the drawing.
        Dim i = lstObjects.SelectedIndex
        If i >= 0 AndAlso i < _objects.Count AndAlso lstObjects.Items(i).ToString() <> ObjectListText(_objects(i)) Then
            _syncingSelection = True
            Try
                lstObjects.Items(i) = ObjectListText(_objects(i))
            Finally
                _syncingSelection = False
            End Try
        End If
        _glView.Objects = _objects
        MarkDirty()
        RequestRegenerate(immediate:=True)
    End Sub

    Private Sub tsbObjImport_Click(sender As Object, e As EventArgs) Handles tsbObjImport.Click
        ' First time: start in the Samples folder shipped next to the program.
        If String.IsNullOrEmpty(dlgSvg.FileName) AndAlso String.IsNullOrEmpty(dlgSvg.InitialDirectory) Then
            Dim samples = Path.Combine(AppContext.BaseDirectory, "Samples")
            If Directory.Exists(samples) Then dlgSvg.InitialDirectory = samples
        End If
        If dlgSvg.ShowDialog(Me) <> DialogResult.OK Then Return
        Try
            ImportSvgFile(dlgSvg.FileName)
        Catch ex As Exception
            MessageBox.Show(Me, ex.Message, "Import SVG", MessageBoxButtons.OK, MessageBoxIcon.Error)
        End Try
    End Sub

    Private Sub tsbObjRemove_Click(sender As Object, e As EventArgs) Handles tsbObjRemove.Click
        Dim idx = lstObjects.SelectedIndex
        If idx < 0 OrElse idx >= _objects.Count Then Return
        _objects.RemoveAt(idx)
        RefreshObjectList(Math.Min(idx, _objects.Count - 1))
        MarkDirty()
        RequestRegenerate(immediate:=True)
    End Sub

    Private Sub tsbObjFit_Click(sender As Object, e As EventArgs) Handles tsbObjFit.Click
        Dim idx = lstObjects.SelectedIndex
        If idx < 0 OrElse idx >= _objects.Count Then Return
        Dim o = _objects(idx)
        o.EnsureParsed(_settings.CurveTolerance)
        o.FitTo(Math.Max(0.1, _settings.BlankWidthIn - 2 * _settings.MarginIn), Math.Max(0.1, _settings.BlankHeightIn - 2 * _settings.MarginIn))
        CenterObjectOnBlank(o)
        pgObject.Refresh()
        _glView.Objects = _objects
        MarkDirty()
        RequestRegenerate(immediate:=True)
    End Sub

    Private Sub tsbObjCenter_Click(sender As Object, e As EventArgs) Handles tsbObjCenter.Click
        Dim idx = lstObjects.SelectedIndex
        If idx < 0 OrElse idx >= _objects.Count Then Return
        CenterObjectOnBlank(_objects(idx))
        pgObject.Refresh()
        _glView.Objects = _objects
        MarkDirty()
        RequestRegenerate(immediate:=True)
    End Sub

    ''' <summary>Saves to the current path, or prompts for one. Returns True when saved.</summary>
    Private Function SaveProject(saveAs As Boolean) As Boolean
        Dim target As String = _projectPath
        If saveAs OrElse String.IsNullOrEmpty(target) Then
            dlgSaveProject.FileName = If(String.IsNullOrEmpty(_projectPath), SuggestFileName(rtbText.Text) & ProjectFile.Extension, Path.GetFileName(_projectPath))
            If Not String.IsNullOrEmpty(_projectPath) Then dlgSaveProject.InitialDirectory = Path.GetDirectoryName(_projectPath)
            If dlgSaveProject.ShowDialog(Me) <> DialogResult.OK Then Return False
            target = dlgSaveProject.FileName
        End If
        Try
            CollectProject().Save(target)
            _projectPath = target
            _dirty = False
            UpdateTitle()
            lblStatus.Text = "Saved " & target
            Return True
        Catch ex As Exception
            MessageBox.Show(Me, ex.Message, "Save Project", MessageBoxButtons.OK, MessageBoxIcon.Error)
            Return False
        End Try
    End Function

    ''' <summary>Replaces settings and editor content with a loaded project.</summary>
    Private Sub LoadProjectIntoUi(pf As ProjectFile)
        _genCts?.Cancel()
        _genVersion += 1
        _settings = pf.Settings
        pgSettings.SelectedObject = _settings
        UpdateSizeButtonCaptions()
        _objects = New List(Of DesignObject)(pf.Objects.Select(Function(o) o.Clone()))
        For Each o In _objects
            o.InvalidateShape()
        Next
        _glView.CurveTolerance = _settings.CurveTolerance
        RefreshObjectList(-1)

        _suppressEditorEvents = True
        Try
            rtbText.Clear()
            rtbText.Font = EditorFontFor(SizeSlot.Large)
            Dim sb As New Text.StringBuilder()
            For i = 0 To pf.Lines.Count - 1
                If i > 0 Then sb.Append(vbLf)
                sb.Append(If(pf.Lines(i).Text, "").Replace(vbCr, "").Replace(vbLf, " "))
            Next
            rtbText.Text = sb.ToString()
            Dim paras = Paragraphs()
            For i = 0 To Math.Min(paras.Count, pf.Lines.Count) - 1
                SelectWholeParagraph(paras(i))
                rtbText.SelectionFont = EditorFontFor(pf.Lines(i).Slot)
                Select Case pf.Lines(i).Align
                    Case TextAlign.Center : rtbText.SelectionAlignment = HorizontalAlignment.Center
                    Case TextAlign.Right : rtbText.SelectionAlignment = HorizontalAlignment.Right
                    Case Else : rtbText.SelectionAlignment = HorizontalAlignment.Left
                End Select
            Next
            rtbText.Select(0, 0)
        Finally
            _suppressEditorEvents = False
        End Try
        UpdateFormatButtons()
        _dirty = False
        UpdateTitle()
        RequestRegenerate(immediate:=True)
    End Sub

    ' ------------------------------------------------------------ editor model

    Private Shared Function PointSizeOf(slot As SizeSlot) As Single
        Select Case slot
            Case SizeSlot.Medium : Return PtMedium
            Case SizeSlot.Small : Return PtSmall
            Case Else : Return PtLarge
        End Select
    End Function

    ''' <summary>Nearest slot for an editor font size (tolerates foreign sizes from pasted/loaded RTF).</summary>
    Private Shared Function SlotOfPointSize(pt As Single) As SizeSlot
        Dim best As SizeSlot = SizeSlot.Large
        Dim bestDiff As Single = Single.MaxValue
        For Each slot In New SizeSlot() {SizeSlot.Large, SizeSlot.Medium, SizeSlot.Small}
            Dim diff = Math.Abs(pt - PointSizeOf(slot))
            If diff < bestDiff Then
                bestDiff = diff
                best = slot
            End If
        Next
        Return best
    End Function

    ''' <summary>Editor font for a slot in that slot's family/style (with fallbacks).</summary>
    Private Function EditorFontFor(slot As SizeSlot) As Font
        Dim pt As Single = PointSizeOf(slot)
        Dim choice As FontChoice = _settings.FontFor(slot)
        Dim style = choice.Style()
        Try
            Using fam As New FontFamily(choice.Family)
                If Not fam.IsStyleAvailable(style) Then
                    For Each candidate In New FontStyle() {FontStyle.Regular, FontStyle.Bold, FontStyle.Italic, FontStyle.Bold Or FontStyle.Italic}
                        If fam.IsStyleAvailable(candidate) Then
                            style = candidate
                            Exit For
                        End If
                    Next
                End If
                Return New Font(fam, pt, style, GraphicsUnit.Point)
            End Using
        Catch
            Return New Font(FontFamily.GenericSansSerif, pt, FontStyle.Regular, GraphicsUnit.Point)
        End Try
    End Function

    Private Shared Function ToTextAlign(a As HorizontalAlignment) As TextAlign
        Select Case a
            Case HorizontalAlignment.Center : Return TextAlign.Center
            Case HorizontalAlignment.Right : Return TextAlign.Right
            Case Else : Return TextAlign.Left
        End Select
    End Function

    Private Sub LockRedraw(lock As Boolean)
        If Not rtbText.IsHandleCreated Then Return
        SendMessage(rtbText.Handle, WM_SETREDRAW, If(lock, IntPtr.Zero, New IntPtr(1)), IntPtr.Zero)
        If Not lock Then rtbText.Invalidate()
    End Sub

    ''' <summary>Runs an action that moves the selection around, then restores selection and scroll silently.</summary>
    Private Sub WithSilentSelection(action As Action)
        Dim saveStart = rtbText.SelectionStart
        Dim saveLen = rtbText.SelectionLength
        Dim scroll As NativePoint
        Dim haveScroll As Boolean = rtbText.IsHandleCreated
        If haveScroll Then SendMessage(rtbText.Handle, EM_GETSCROLLPOS, IntPtr.Zero, scroll)
        Dim wasSuppressed = _suppressEditorEvents
        _suppressEditorEvents = True
        LockRedraw(True)
        Try
            action()
        Finally
            rtbText.Select(saveStart, saveLen)
            If haveScroll Then SendMessage(rtbText.Handle, EM_SETSCROLLPOS, IntPtr.Zero, scroll)
            LockRedraw(False)
            _suppressEditorEvents = wasSuppressed
        End Try
    End Sub

    ''' <summary>One paragraph of the editor: character offset, length and text.</summary>
    Private Structure Paragraph
        Public Start As Integer
        Public Length As Integer
        Public Text As String
        Public IsLast As Boolean
    End Structure

    ''' <summary>
    ''' Paragraphs indexed from the Text itself (split on LF), so offsets stay consistent
    ''' even when RichEdit soft breaks make its own line numbering disagree with Lines().
    ''' </summary>
    Private Function Paragraphs() As List(Of Paragraph)
        Dim result As New List(Of Paragraph)
        Dim parts = rtbText.Text.Split(ControlChars.Lf)
        Dim offset As Integer = 0
        For i = 0 To parts.Length - 1
            result.Add(New Paragraph With {.Start = offset, .Length = parts(i).Length, .Text = parts(i), .IsLast = (i = parts.Length - 1)})
            offset += parts(i).Length + 1
        Next
        Return result
    End Function

    ''' <summary>Selects the characters whose format defines a paragraph's slot (its paragraph mark when empty).</summary>
    Private Sub SelectParagraphFormat(p As Paragraph)
        If p.Length > 0 Then
            rtbText.Select(p.Start, 1)
        ElseIf Not p.IsLast Then
            rtbText.Select(p.Start, 1)      ' the paragraph mark keeps its own format
        Else
            rtbText.Select(p.Start, 0)      ' final empty paragraph: insertion format
        End If
    End Sub

    ''' <summary>Selects the whole paragraph including its mark (except for the final one).</summary>
    Private Sub SelectWholeParagraph(p As Paragraph)
        rtbText.Select(p.Start, If(p.IsLast, p.Length, p.Length + 1))
    End Sub

    ''' <summary>Reads every editor paragraph with its size slot and alignment.</summary>
    Private Function ReadEditorParagraphs() As List(Of ProjectLine)
        Dim result As New List(Of ProjectLine)
        WithSilentSelection(
            Sub()
                For Each p In Paragraphs()
                    SelectParagraphFormat(p)
                    Dim f = rtbText.SelectionFont
                    Dim slot = If(f IsNot Nothing, SlotOfPointSize(f.Size), SizeSlot.Large)
                    result.Add(New ProjectLine(p.Text, slot, ToTextAlign(rtbText.SelectionAlignment)))
                Next
            End Sub)
        Return result
    End Function

    ''' <summary>Editor paragraphs resolved to inch sizes and fonts for the toolpath engine.</summary>
    Private Function ReadEditorLines() As List(Of TextLine)
        Dim result As New List(Of TextLine)
        For Each p In ReadEditorParagraphs()
            result.Add(New TextLine(p.Text, _settings.SizeOf(p.Slot), p.Align, _settings.FontFor(p.Slot).Clone()))
        Next
        Return result
    End Function

    ''' <summary>Re-applies each slot's family/style to its lines, keeping every line's slot and alignment.</summary>
    Private Sub NormalizeEditorFormatting()
        ' Never assign rtbText.Font while there is text: RichEdit would reformat the
        ' whole document and every line would read back as Large.
        If rtbText.TextLength = 0 Then rtbText.Font = EditorFontFor(SizeSlot.Large)
        WithSilentSelection(
            Sub()
                ' Read all slots first, then write, so a change never influences a later read.
                Dim paras = Paragraphs()
                Dim slots As New List(Of SizeSlot)
                For Each p In paras
                    SelectParagraphFormat(p)
                    Dim f = rtbText.SelectionFont
                    slots.Add(If(f IsNot Nothing, SlotOfPointSize(f.Size), SizeSlot.Large))
                Next
                For i = 0 To paras.Count - 1
                    SelectWholeParagraph(paras(i))
                    rtbText.SelectionFont = EditorFontFor(slots(i))
                Next
            End Sub)
        UpdateFormatButtons()
    End Sub

    ''' <summary>Formats whole paragraphs touched by the selection with a size slot.</summary>
    Private Sub ApplySizeToSelectedLines(slot As SizeSlot)
        Dim selStart = rtbText.SelectionStart
        Dim selEnd = selStart + Math.Max(0, rtbText.SelectionLength - 1)
        WithSilentSelection(
            Sub()
                For Each p In Paragraphs()
                    Dim pEnd = p.Start + p.Length
                    If pEnd < selStart OrElse p.Start > selEnd Then Continue For
                    SelectWholeParagraph(p)
                    rtbText.SelectionFont = EditorFontFor(slot)
                Next
            End Sub)
        UpdateFormatButtons()
        MarkDirty()
        RequestRegenerate()
    End Sub

    ''' <summary>RichEdit soft line breaks (Shift+Enter, RTF \line) become real paragraphs.</summary>
    Private Sub ReplaceSoftBreaks()
        Dim wasSuppressed = _suppressEditorEvents
        _suppressEditorEvents = True
        Try
            Dim idx As Integer = rtbText.Text.IndexOf(ChrW(11))
            While idx >= 0
                rtbText.Select(idx, 1)
                rtbText.SelectedText = vbLf
                idx = rtbText.Text.IndexOf(ChrW(11))
            End While
        Finally
            _suppressEditorEvents = wasSuppressed
        End Try
    End Sub

    Private Sub ApplyAlignmentToSelection(a As HorizontalAlignment)
        Dim wasSuppressed = _suppressEditorEvents
        _suppressEditorEvents = True
        rtbText.SelectionAlignment = a   ' applies to every paragraph in the selection
        _suppressEditorEvents = wasSuppressed
        UpdateFormatButtons()
        MarkDirty()
        RequestRegenerate()
    End Sub

    ''' <summary>Reflects the current line's slot and alignment in the toolbar.</summary>
    Private Sub UpdateFormatButtons()
        Dim slot As SizeSlot = SizeSlot.Large
        Dim f = rtbText.SelectionFont
        If f IsNot Nothing Then slot = SlotOfPointSize(f.Size)
        tsbSizeLarge.Checked = (slot = SizeSlot.Large)
        tsbSizeMedium.Checked = (slot = SizeSlot.Medium)
        tsbSizeSmall.Checked = (slot = SizeSlot.Small)
        Dim a = rtbText.SelectionAlignment
        tsbAlignLeft.Checked = (a = HorizontalAlignment.Left)
        tsbAlignCenter.Checked = (a = HorizontalAlignment.Center)
        tsbAlignRight.Checked = (a = HorizontalAlignment.Right)
    End Sub

    Private Sub UpdateSizeButtonCaptions()
        Dim ci = CultureInfo.InvariantCulture
        tsbSizeLarge.Text = String.Format(ci, "Large {0:0.##}""", _settings.SizeLargeIn)
        tsbSizeMedium.Text = String.Format(ci, "Medium {0:0.##}""", _settings.SizeMediumIn)
        tsbSizeSmall.Text = String.Format(ci, "Small {0:0.##}""", _settings.SizeSmallIn)
    End Sub

    ' ------------------------------------------------------------ regeneration

    ''' <summary>Schedules a regeneration (debounced) if auto mode is on.</summary>
    Private Sub RequestRegenerate(Optional immediate As Boolean = False)
        If Not mnuToolpathAuto.Checked AndAlso Not immediate Then
            lblStatus.Text = "Changed - press F5 to regenerate"
            Return
        End If
        tmrRegen.Stop()
        If immediate Then
            GenerateAsync()
        Else
            tmrRegen.Start()
        End If
    End Sub

    Private Sub tmrRegen_Tick(sender As Object, e As EventArgs) Handles tmrRegen.Tick
        tmrRegen.Stop()
        GenerateAsync()
    End Sub

    ''' <summary>Runs the glyph + V-carve pipeline on a worker thread and publishes the result.</summary>
    Private Sub GenerateAsync()
        Dim lines As List(Of TextLine) = ReadEditorLines()
        Dim snapshot As CarveSettings = _settings.Clone()
        Dim items As List(Of MachiningItem) = PlacedItems()
        Dim contentKey As String = TextLine.KeyOf(lines) & ObjectsKey()

        Dim problems = snapshot.Validate()
        If problems.Count > 0 Then
            ' Stop any in-flight run so it cannot publish a stale result over the message.
            _genCts?.Cancel()
            _genVersion += 1
            _running = False
            UseWaitCursor = False
            lblStatus.Text = "Settings: " & String.Join(" ", problems)
            Return
        End If

        _genCts?.Cancel()
        _genCts = New CancellationTokenSource()
        Dim token = _genCts.Token
        _genVersion += 1
        Dim myVersion = _genVersion
        _running = True
        lblStatus.Text = "Generating toolpath..."
        UseWaitCursor = True

        Task.Run(Function() JobBuilder.Generate(lines, items, snapshot, token), token).
            ContinueWith(
                Sub(t As Task(Of Toolpath))
                    If IsDisposed OrElse myVersion <> _genVersion Then Return
                    _running = False
                    UseWaitCursor = False
                    If t.IsCanceled Then Return
                    If t.IsFaulted Then
                        Dim ex As Exception = t.Exception
                        If TypeOf ex Is AggregateException Then ex = DirectCast(ex, AggregateException).GetBaseException()
                        lblStatus.Text = "Error: " & ex.Message
                        Return
                    End If
                    _toolpath = t.Result
                    _lastLinesKey = contentKey
                    ' The precision may have changed while the worker ran; it does not affect the toolpath.
                    CopySimulationOnlySettings(_settings, snapshot)
                    _lastSettings = snapshot
                    _glView.SetToolpath(_toolpath, snapshot)
                    UpdateStats(_toolpath)
                    If _toolpath.Warnings.Count > 0 Then
                        lblStatus.Text = String.Join("  |  ", _toolpath.Warnings)
                    ElseIf _toolpath.IsEmpty Then
                        lblStatus.Text = "Nothing to cut - type some text."
                    Else
                        lblStatus.Text = "Ready"
                    End If
                End Sub,
                CancellationToken.None,
                TaskContinuationOptions.None,
                TaskScheduler.FromCurrentSynchronizationContext())
    End Sub

    Private Sub UpdateStats(tp As Toolpath)
        If tp Is Nothing OrElse tp.IsEmpty Then
            lblStats.Text = "No toolpath"
            Return
        End If
        Dim ci = CultureInfo.InvariantCulture
        lblStats.Text = String.Format(ci,
            "{0} letters, {1} passes | {2:0.0}"" x {3:0.00}"" | depth {4:0.000}"" | cut {5:0.0}"" | ~{6:0.0} min",
            tp.RegionCount, tp.Contours.Count, tp.Width, tp.Height, -tp.MinZ, tp.CutLength, tp.EstimatedMinutes)
        Dim tools = GCodeWriter.ToolGroups(tp).Count
        If tools > 1 Then lblStats.Text &= " | " & tools & " tools, one program each"
    End Sub

    ' ---------------------------------------------------------- input events

    Private Sub rtbText_TextChanged(sender As Object, e As EventArgs) Handles rtbText.TextChanged
        If _suppressEditorEvents Then Return
        MarkDirty()
        RequestRegenerate()
    End Sub

    Private Sub rtbText_SelectionChanged(sender As Object, e As EventArgs) Handles rtbText.SelectionChanged
        If _suppressEditorEvents Then Return
        UpdateFormatButtons()
    End Sub

    Private Sub rtbText_KeyDown(sender As Object, e As KeyEventArgs) Handles rtbText.KeyDown
        ' Paste as plain text so foreign fonts/sizes never enter the layout
        ' (Ctrl+V and Shift+Insert; AltGr combinations are left alone for international layouts).
        Dim isCtrlV = e.Control AndAlso Not e.Alt AndAlso e.KeyCode = Keys.V
        Dim isShiftIns = e.Shift AndAlso Not e.Control AndAlso Not e.Alt AndAlso e.KeyCode = Keys.Insert
        If isCtrlV OrElse isShiftIns Then
            If Clipboard.ContainsText() Then
                rtbText.Paste(DataFormats.GetFormat(DataFormats.UnicodeText))
            End If
            e.Handled = True
            e.SuppressKeyPress = True
            Return
        End If
        ' Shift+Enter would insert a soft break inside the paragraph; make it a real line.
        If e.Shift AndAlso e.KeyCode = Keys.Enter Then
            rtbText.SelectedText = vbLf
            e.Handled = True
            e.SuppressKeyPress = True
        End If
    End Sub

    Private Sub tsbSizeLarge_Click(sender As Object, e As EventArgs) Handles tsbSizeLarge.Click
        ApplySizeToSelectedLines(SizeSlot.Large)
    End Sub

    Private Sub tsbSizeMedium_Click(sender As Object, e As EventArgs) Handles tsbSizeMedium.Click
        ApplySizeToSelectedLines(SizeSlot.Medium)
    End Sub

    Private Sub tsbSizeSmall_Click(sender As Object, e As EventArgs) Handles tsbSizeSmall.Click
        ApplySizeToSelectedLines(SizeSlot.Small)
    End Sub

    Private Sub tsbAlignLeft_Click(sender As Object, e As EventArgs) Handles tsbAlignLeft.Click
        ApplyAlignmentToSelection(HorizontalAlignment.Left)
    End Sub

    Private Sub tsbAlignCenter_Click(sender As Object, e As EventArgs) Handles tsbAlignCenter.Click
        ApplyAlignmentToSelection(HorizontalAlignment.Center)
    End Sub

    Private Sub tsbAlignRight_Click(sender As Object, e As EventArgs) Handles tsbAlignRight.Click
        ApplyAlignmentToSelection(HorizontalAlignment.Right)
    End Sub

    Private Sub pgSettings_PropertyValueChanged(s As Object, e As PropertyValueChangedEventArgs) Handles pgSettings.PropertyValueChanged
        MarkDirty()
        UpdateSizeButtonCaptions()
        If e.ChangedItem IsNot Nothing AndAlso e.ChangedItem.PropertyDescriptor IsNot Nothing Then
            Select Case e.ChangedItem.PropertyDescriptor.Name
                Case NameOf(FontChoice.Family), NameOf(FontChoice.Bold), NameOf(FontChoice.Italic),
                     NameOf(CarveSettings.FontLarge), NameOf(CarveSettings.FontMedium), NameOf(CarveSettings.FontSmall)
                    NormalizeEditorFormatting()
                Case NameOf(CarveSettings.CurveTolerance)
                    For Each o In _objects
                        o.InvalidateShape()
                    Next
                    _glView.CurveTolerance = _settings.CurveTolerance
                Case NameOf(CarveSettings.SimResolution), NameOf(CarveSettings.SimMeshDetail)
                    ' Simulation-only: rebuild the heightmap now, the toolpath is unchanged.
                    ' Simulate the toolpath with the settings it was generated from, so pending tool or
                    ' stock edits (Auto regenerate off) do not leak into the carved surface.
                    If _lastSettings IsNot Nothing Then CopySimulationOnlySettings(_settings, _lastSettings)
                    _glView.UpdateSimulationSettings(If(_lastSettings, _settings).Clone())
                    pgSettings.Refresh()
                    Return
            End Select
        End If
        pgSettings.Refresh() ' MaxToolDepthIn and other derived values
        RequestRegenerate()
    End Sub

    ' ------------------------------------------------------------- File menu

    Private Sub mnuFileNew_Click(sender As Object, e As EventArgs) Handles mnuFileNew.Click
        If Not ConfirmDiscardChanges() Then Return
        Dim pf As New ProjectFile()
        pf.Lines.Add(New ProjectLine("", SizeSlot.Large, TextAlign.Center))
        _projectPath = Nothing
        LoadProjectIntoUi(pf)
    End Sub

    Private Sub mnuFileImportSvg_Click(sender As Object, e As EventArgs) Handles mnuFileImportSvg.Click
        tsbObjImport_Click(sender, e)
    End Sub

    Private Sub mnuFileOpenProject_Click(sender As Object, e As EventArgs) Handles mnuFileOpenProject.Click
        If Not ConfirmDiscardChanges() Then Return
        If dlgOpenProject.ShowDialog(Me) <> DialogResult.OK Then Return
        Try
            Dim pf = ProjectFile.Load(dlgOpenProject.FileName)
            _projectPath = dlgOpenProject.FileName
            LoadProjectIntoUi(pf)
            lblStatus.Text = "Opened " & _projectPath
        Catch ex As Exception
            MessageBox.Show(Me, "Could not open the project:" & Environment.NewLine & ex.Message, "Open Project", MessageBoxButtons.OK, MessageBoxIcon.Error)
        End Try
    End Sub

    Private Sub mnuFileSaveProject_Click(sender As Object, e As EventArgs) Handles mnuFileSaveProject.Click
        SaveProject(saveAs:=False)
    End Sub

    Private Sub mnuFileSaveProjectAs_Click(sender As Object, e As EventArgs) Handles mnuFileSaveProjectAs.Click
        SaveProject(saveAs:=True)
    End Sub

    Private Sub mnuFileImportText_Click(sender As Object, e As EventArgs) Handles mnuFileImportText.Click
        If dlgOpen.ShowDialog(Me) <> DialogResult.OK Then Return
        Try
            _suppressEditorEvents = True
            If String.Equals(Path.GetExtension(dlgOpen.FileName), ".rtf", StringComparison.OrdinalIgnoreCase) Then
                rtbText.LoadFile(dlgOpen.FileName, RichTextBoxStreamType.RichText)
                ReplaceSoftBreaks()
            Else
                rtbText.Text = File.ReadAllText(dlgOpen.FileName)
                rtbText.SelectAll()
                rtbText.SelectionFont = EditorFontFor(SizeSlot.Large)
                rtbText.SelectionAlignment = HorizontalAlignment.Left
                rtbText.Select(0, 0)
            End If
            _suppressEditorEvents = False
            NormalizeEditorFormatting()
            MarkDirty()
            RequestRegenerate(immediate:=True)
        Catch ex As Exception
            _suppressEditorEvents = False
            MessageBox.Show(Me, ex.Message, "Import Text", MessageBoxButtons.OK, MessageBoxIcon.Error)
        End Try
    End Sub

    Private Sub mnuFileSaveGcode_Click(sender As Object, e As EventArgs) Handles mnuFileSaveGcode.Click
        ' Make sure the saved program matches what is on screen / in the editor.
        Dim lines = ReadEditorLines()
        Dim snapshot As CarveSettings = _settings.Clone()
        Dim problems = snapshot.Validate()
        If problems.Count > 0 Then
            MessageBox.Show(Me, String.Join(Environment.NewLine, problems), "Settings", MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return
        End If

        Dim tp As Toolpath = _toolpath
        Dim contentKey As String = TextLine.KeyOf(lines) & ObjectsKey()
        If tp Is Nothing OrElse _running OrElse contentKey <> _lastLinesKey OrElse Not SameSettings(snapshot, _lastSettings) Then
            UseWaitCursor = True
            Try
                tp = JobBuilder.Generate(lines, PlacedItems(), snapshot, CancellationToken.None)
                _toolpath = tp
                _lastLinesKey = contentKey
                _lastSettings = snapshot
                _glView.SetToolpath(tp, snapshot)
                UpdateStats(tp)
            Catch ex As Exception
                MessageBox.Show(Me, ex.Message, "Generate", MessageBoxButtons.OK, MessageBoxIcon.Error)
                Return
            Finally
                UseWaitCursor = False
            End Try
        End If

        If tp.IsEmpty Then
            MessageBox.Show(Me, "There is nothing to cut. Type some text first.", "Save G-code", MessageBoxButtons.OK, MessageBoxIcon.Information)
            Return
        End If

        If String.IsNullOrEmpty(dlgSave.FileName) Then
            dlgSave.FileName = SuggestFileName(rtbText.Text) & ".nc"
        End If
        If dlgSave.ShowDialog(Me) <> DialogResult.OK Then Return
        Try
            Dim groups = GCodeWriter.ToolGroups(tp)
            If groups.Count <= 1 Then
                File.WriteAllText(dlgSave.FileName, GCodeWriter.Write(tp, snapshot, rtbText.Text))
                lblStatus.Text = "Saved " & dlgSave.FileName
                Return
            End If
            ' Several tools: one program per tool, numbered in the order they must run.
            Dim dir = Path.GetDirectoryName(dlgSave.FileName)
            Dim stem = Path.GetFileNameWithoutExtension(dlgSave.FileName)
            Dim ext = Path.GetExtension(dlgSave.FileName)
            If String.IsNullOrEmpty(ext) Then ext = ".nc"
            Dim report As New Text.StringBuilder()
            report.AppendLine("This job uses " & groups.Count & " tools, so it was saved as " & groups.Count & " programs.")
            report.AppendLine("Run them in this order. Before each one, fit its tool and re-zero Z on the top of the stock:")
            report.AppendLine()
            For i = 0 To groups.Count - 1
                Dim g = groups(i)
                Dim fileName = stem & "-" & (i + 1).ToString(CultureInfo.InvariantCulture) & "-" & SafeFilePart(If(g.Tool?.AsciiName(), "tool")) & ext
                File.WriteAllText(Path.Combine(dir, fileName), GCodeWriter.Write(tp, snapshot, rtbText.Text, g, i + 1, groups.Count))
                report.AppendLine(String.Format(CultureInfo.InvariantCulture, "{0}. {1}{2}     {3}, about {4:0.0} min{2}     {5}",
                                                i + 1, fileName, Environment.NewLine, If(g.Tool?.DisplayName(), "tool"), g.EstimatedMinutes, g.Operations))
            Next
            lblStatus.Text = "Saved " & groups.Count & " programs (one per tool) in " & dir
            MessageBox.Show(Me, report.ToString(), "Save G-code", MessageBoxButtons.OK, MessageBoxIcon.Information)
        Catch ex As Exception
            MessageBox.Show(Me, ex.Message, "Save G-code", MessageBoxButtons.OK, MessageBoxIcon.Error)
        End Try
    End Sub

    ''' <summary>Settings that only drive the simulation display, never the toolpath.</summary>
    Private Shared Sub CopySimulationOnlySettings(source As CarveSettings, target As CarveSettings)
        target.SimResolution = source.SimResolution
        target.SimMeshDetail = source.SimMeshDetail
    End Sub

    ''' <summary>Toolpath > Tool Library: manage tools; follows edits to the project's own tool.</summary>
    Private Sub mnuToolLibrary_Click(sender As Object, e As EventArgs) Handles mnuToolLibrary.Click
        Using dlg As New frmToolLibrary(Nothing, _settings.CarveTool)
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
        End Using
        Dim current = _settings.CarveTool
        If current Is Nothing Then Return
        Dim inLibrary = ToolLibrary.Load().FindById(current.Id)
        If inLibrary IsNot Nothing AndAlso Not inLibrary.Equals(current) Then
            _settings.CarveTool = inLibrary.Clone()
            pgSettings.Refresh()
            MarkDirty()
            RequestRegenerate(immediate:=True)
            lblStatus.Text = "The V-carve tool was updated from the library: " & inLibrary.DisplayName()
        End If
    End Sub

    Private Shared Function SameSettings(a As CarveSettings, b As CarveSettings) As Boolean
        If a Is Nothing OrElse b Is Nothing Then Return False
        ' Compare every public property value; cheap and avoids a hand-written equality list.
        For Each p In GetType(CarveSettings).GetProperties()
            If Not p.CanRead OrElse p.GetIndexParameters().Length > 0 Then Continue For
            If Not Object.Equals(p.GetValue(a), p.GetValue(b)) Then Return False
        Next
        Return True
    End Function

    ''' <summary>Text usable in a file name (letters, digits, - and _).</summary>
    Private Shared Function SafeFilePart(text As String) As String
        Dim sb As New Text.StringBuilder()
        For Each ch In If(text, "")
            If Char.IsLetterOrDigit(ch) Then
                sb.Append(ch)
            ElseIf ch = "/"c Then
                sb.Append("-"c)
            ElseIf sb.Length > 0 AndAlso sb(sb.Length - 1) <> "_"c Then
                sb.Append("_"c)
            End If
        Next
        Return sb.ToString().Trim("_"c)
    End Function

    Private Shared Function SuggestFileName(text As String) As String
        Dim sb As New Text.StringBuilder()
        For Each ch In If(text, "")
            If Char.IsLetterOrDigit(ch) Then
                sb.Append(ch)
            ElseIf ch = " "c OrElse ch = "_"c OrElse ch = "-"c Then
                sb.Append("_"c)
            End If
            If sb.Length >= 24 Then Exit For
        Next
        Return If(sb.Length = 0, "text_vcarve", sb.ToString())
    End Function

    Private Sub mnuFileExit_Click(sender As Object, e As EventArgs) Handles mnuFileExit.Click
        Close()
    End Sub

    ' ------------------------------------------------------------- Font menu

    ''' <summary>Chooses the font for the size slot of the line the caret is on.</summary>
    Private Sub mnuFontChoose_Click(sender As Object, e As EventArgs) Handles mnuFontChoose.Click
        Dim slot As SizeSlot = SizeSlot.Large
        Dim cur = rtbText.SelectionFont
        If cur IsNot Nothing Then slot = SlotOfPointSize(cur.Size)
        Dim choice As FontChoice = _settings.FontFor(slot)
        Try
            dlgFont.Font = New Font(choice.Family, 24.0F, choice.Style(), GraphicsUnit.Point)
        Catch
            dlgFont.Font = New Font("Arial", 24.0F, FontStyle.Regular, GraphicsUnit.Point)
        End Try
        If dlgFont.ShowDialog(Me) <> DialogResult.OK Then Return
        choice.Family = dlgFont.Font.FontFamily.Name
        choice.Bold = dlgFont.Font.Bold
        choice.Italic = dlgFont.Font.Italic
        pgSettings.Refresh()
        NormalizeEditorFormatting()
        MarkDirty()
        lblStatus.Text = slot.ToString() & " font: " & choice.ToString()
        RequestRegenerate(immediate:=True)
    End Sub

    ' ------------------------------------------------------------- View menu

    Private Sub mnuViewTop_Click(sender As Object, e As EventArgs) Handles mnuViewTop.Click
        _glView.SetTopView()
    End Sub

    Private Sub mnuViewIso_Click(sender As Object, e As EventArgs) Handles mnuViewIso.Click
        _glView.SetIsoView()
    End Sub

    Private Sub mnuViewFit_Click(sender As Object, e As EventArgs) Handles mnuViewFit.Click
        _glView.ZoomToFit()
    End Sub

    Private Sub mnuViewRapids_CheckedChanged(sender As Object, e As EventArgs) Handles mnuViewRapids.CheckedChanged
        If _glView IsNot Nothing Then _glView.ShowRapids = mnuViewRapids.Checked
    End Sub

    Private Sub mnuViewOutline_CheckedChanged(sender As Object, e As EventArgs) Handles mnuViewOutline.CheckedChanged
        If _glView IsNot Nothing Then _glView.ShowOutline = mnuViewOutline.Checked
    End Sub

    Private Sub mnuViewGrid_CheckedChanged(sender As Object, e As EventArgs) Handles mnuViewGrid.CheckedChanged
        If _glView IsNot Nothing Then _glView.ShowGrid = mnuViewGrid.Checked
    End Sub

    ' ------------------------------------------------------------ Simulation

    Private Sub mnuViewSim_CheckedChanged(sender As Object, e As EventArgs) Handles mnuViewSim.CheckedChanged
        If _glView Is Nothing Then Return
        pnlSim.Visible = mnuViewSim.Checked
        _glView.ShowSimulation = mnuViewSim.Checked
    End Sub

    Private Shared Function FormatClock(seconds As Double) As String
        If Double.IsInfinity(seconds) OrElse Double.IsNaN(seconds) Then seconds = 0
        Dim t = TimeSpan.FromSeconds(Math.Max(0, seconds))
        If t.TotalHours >= 1 Then Return String.Format(CultureInfo.InvariantCulture, "{0}:{1:00}:{2:00}", CInt(Math.Floor(t.TotalHours)), t.Minutes, t.Seconds)
        Return String.Format(CultureInfo.InvariantCulture, "{0}:{1:00}", t.Minutes, t.Seconds)
    End Function

    Private _simUiUpdating As Boolean

    Private Sub OnSimulationProgress(sender As Object, e As EventArgs)
        If _glView Is Nothing Then Return
        _simUiUpdating = True
        Try
            Dim total = _glView.SimulationTotalSeconds
            Dim cur = _glView.SimulationSeconds
            tslSimTime.Text = FormatClock(cur) & " / " & FormatClock(total)
            tbSim.Value = If(total > 0, CInt(Math.Round(Math.Max(0, Math.Min(1, cur / total)) * tbSim.Maximum)), 0)
            tsbSimPlay.Text = If(_glView.SimulationPlaying, "Pause", "Play")
            Dim info = _glView.SimulationInfo
            tslSimInfo.Text = info
            tslSimInfo.ToolTipText = If(info.Length > 0, "Heightmap " & info & ". Set under 5. Simulation > Precision.", "")
        Finally
            _simUiUpdating = False
        End Try
    End Sub

    Private Sub tsbSimPlay_Click(sender As Object, e As EventArgs) Handles tsbSimPlay.Click
        If _glView.SimulationPlaying Then _glView.SimulationPause() Else _glView.SimulationPlay()
    End Sub

    Private Sub tsbSimReset_Click(sender As Object, e As EventArgs) Handles tsbSimReset.Click
        _glView.SimulationPause()
        _glView.SimulationSeek(0)
    End Sub

    Private Sub tscSimSpeed_SelectedIndexChanged(sender As Object, e As EventArgs) Handles tscSimSpeed.SelectedIndexChanged
        If _glView Is Nothing Then Return
        Dim text = If(tscSimSpeed.SelectedItem, "1x").ToString()
        If text = "Instant" Then
            _glView.SimulationSpeed = Double.PositiveInfinity
        Else
            Dim v As Double
            If Double.TryParse(text.TrimEnd("x"c), NumberStyles.Float, CultureInfo.InvariantCulture, v) Then _glView.SimulationSpeed = v
        End If
    End Sub

    Private Sub tbSim_Scroll(sender As Object, e As EventArgs) Handles tbSim.Scroll
        If _simUiUpdating OrElse _glView Is Nothing Then Return
        _glView.SimulationPause()
        _glView.SimulationSeek(_glView.SimulationTotalSeconds * tbSim.Value / tbSim.Maximum)
    End Sub

    ' --------------------------------------------------------- Toolpath menu

    Private Sub mnuToolpathGenerate_Click(sender As Object, e As EventArgs) Handles mnuToolpathGenerate.Click
        RequestRegenerate(immediate:=True)
    End Sub

    Private Sub mnuToolpathAuto_CheckedChanged(sender As Object, e As EventArgs) Handles mnuToolpathAuto.CheckedChanged
        ' Also raised from InitializeComponent, before the view exists.
        If _glView Is Nothing Then Return
        If mnuToolpathAuto.Checked Then RequestRegenerate(immediate:=True)
    End Sub

End Class
