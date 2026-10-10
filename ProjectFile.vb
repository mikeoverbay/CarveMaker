' ============================================================================
'  ProjectFile.vb
'  A .prj project: all settings plus the formatted text (per line: text, size
'  slot and alignment). Stored as indented JSON so it is readable and easy to
'  edit or version. Sizes are stored as slot names (Large/Medium/Small) rather
'  than inches, so changing a slot's inch value later still applies to the text.
' ============================================================================

Imports System.IO
Imports System.Text.Json
Imports System.Text.Json.Serialization

Public Class ProjectLine
    Public Property Text As String = ""
    Public Property Slot As SizeSlot = SizeSlot.Large
    Public Property Align As TextAlign = TextAlign.Left

    Public Sub New()
    End Sub

    Public Sub New(text As String, slot As SizeSlot, align As TextAlign)
        Me.Text = text
        Me.Slot = slot
        Me.Align = align
    End Sub
End Class

Public Class ProjectFile
    Public Const CurrentVersion As Integer = 1
    Public Const Extension As String = ".prj"
    Public Const Filter As String = "CarveMaker project (*.prj)|*.prj|All files (*.*)|*.*"

    Public Property App As String = "CarveMaker"
    Public Property Version As Integer = CurrentVersion
    Public Property Settings As CarveSettings = New CarveSettings()
    Public Property Lines As New List(Of ProjectLine)
    ''' <summary>Imported drawings placed on the blank (SVG text embedded).</summary>
    Public Property Objects As New List(Of DesignObject)

    Private Shared ReadOnly Options As JsonSerializerOptions = CreateOptions()

    Private Shared Function CreateOptions() As JsonSerializerOptions
        Dim o As New JsonSerializerOptions With {
            .WriteIndented = True,
            .PropertyNameCaseInsensitive = True,
            .ReadCommentHandling = JsonCommentHandling.Skip,
            .AllowTrailingCommas = True
        }
        o.Converters.Add(New JsonStringEnumConverter())
        Return o
    End Function

    Public Sub Save(path As String)
        MirrorForOlderVersions()
        File.WriteAllText(path, JsonSerializer.Serialize(Me, Options))
    End Sub

    ''' <summary>
    ''' Older CarveMaker versions read one job-wide V-bit and feeds. Fill those from the first
    ''' V-carve toolpath (text first, then drawings) so such a version carves with the right bit.
    ''' </summary>
    Private Sub MirrorForOlderVersions()
        If Settings Is Nothing Then Return
        Dim all As New List(Of Machining)
        If Settings.TextMachining IsNot Nothing Then all.Add(Settings.TextMachining)
        If Objects IsNot Nothing Then all.AddRange(Objects.Where(Function(o) o?.Machining IsNot Nothing).Select(Function(o) o.Machining))
        Dim main = If(all.FirstOrDefault(Function(m) m.Operation = CutOperation.VCarve AndAlso m.Tool IsNot Nothing), all.FirstOrDefault())
        If main Is Nothing Then Return
        If main.Operation = CutOperation.VCarve AndAlso main.Tool IsNot Nothing Then Settings.CarveTool = main.Tool.Clone()
        Settings.FeedRate = main.FeedRate
        Settings.PlungeRate = main.PlungeRate
        Settings.SpindleRpm = CInt(Math.Round(Math.Max(0.0, Math.Min(100000.0, main.SpindleRpm))))
    End Sub

    ''' <summary>Loads a project, tolerating missing members (older files get defaults).</summary>
    Public Shared Function Load(path As String) As ProjectFile
        Dim json = File.ReadAllText(path)
        Dim pf As ProjectFile = JsonSerializer.Deserialize(Of ProjectFile)(json, Options)
        If pf Is Nothing Then Throw New InvalidDataException("The file is not a CarveMaker project.")
        If pf.Version > CurrentVersion Then
            Throw New InvalidDataException("This project was saved by a newer version of the program (file version " & pf.Version & ").")
        End If
        If pf.Settings Is Nothing Then pf.Settings = New CarveSettings()
        If pf.Settings.FontLarge Is Nothing Then pf.Settings.FontLarge = New FontChoice("Arial")
        If pf.Settings.FontMedium Is Nothing Then pf.Settings.FontMedium = New FontChoice("Arial")
        If pf.Settings.FontSmall Is Nothing Then pf.Settings.FontSmall = New FontChoice("Arial")
        If pf.Lines Is Nothing Then pf.Lines = New List(Of ProjectLine)
        If pf.Objects Is Nothing Then pf.Objects = New List(Of DesignObject)
        pf.Objects.RemoveAll(Function(o) o Is Nothing)
        MigrateTool(json, pf.Settings)
        MigrateToolpaths(json, pf)
        For Each o In pf.Objects
            If o.SvgContent Is Nothing Then o.SvgContent = ""
            If o.Name Is Nothing Then o.Name = "Drawing"
        Next
        For Each l In pf.Lines
            If l.Text Is Nothing Then l.Text = ""
        Next
        Return pf
    End Function

    ''' <summary>
    ''' Projects saved before the tool library stored the V-bit as ToolDiameterIn /
    ''' IncludedAngleDeg. Turn those into a V-bit (the library's 1/4" 90 degree bit when
    ''' they match it). A missing or broken tool falls back to that standard bit.
    ''' </summary>
    Private Shared Sub MigrateTool(json As String, s As CarveSettings)
        Dim hasTool As Boolean = False
        Dim dia As Double = Double.NaN, angle As Double = Double.NaN
        Try
            Using doc = JsonDocument.Parse(json, New JsonDocumentOptions With {.AllowTrailingCommas = True, .CommentHandling = JsonCommentHandling.Skip})
                Dim settingsEl As JsonElement
                If FindMember(doc.RootElement, "Settings", settingsEl) AndAlso settingsEl.ValueKind = JsonValueKind.Object Then
                    Dim el As JsonElement
                    hasTool = FindMember(settingsEl, "CarveTool", el) AndAlso el.ValueKind = JsonValueKind.Object
                    If FindMember(settingsEl, "ToolDiameterIn", el) AndAlso el.ValueKind = JsonValueKind.Number Then dia = el.GetDouble()
                    If FindMember(settingsEl, "IncludedAngleDeg", el) AndAlso el.ValueKind = JsonValueKind.Number Then angle = el.GetDouble()
                End If
            End Using
        Catch
            ' The main deserializer already accepted the file; keep whatever it produced.
        End Try
        If hasTool AndAlso s.CarveTool IsNot Nothing Then
            If String.IsNullOrWhiteSpace(s.CarveTool.Id) Then s.CarveTool.Id = ToolDefinition.NewId()
            If s.CarveTool.Name Is Nothing Then s.CarveTool.Name = ""
            s.CarveTool.Normalize()
            Return
        End If
        If dia > 0 AndAlso angle > 0 AndAlso angle < 180 Then
            Dim std = ToolDefinition.DefaultVBit()
            Dim t = ToolDefinition.MakeVBit(dia, angle)
            s.CarveTool = If(t.SameGeometry(std), std, t)
        Else
            s.CarveTool = ToolDefinition.DefaultVBit()
        End If
    End Sub

    ''' <summary>
    ''' Projects saved before toolpaths had their own tool, speed and feeds used one V-bit
    ''' (CarveTool) and one set of feeds for everything. Give each toolpath that lacks its own
    ''' values those job-wide ones: the feeds and speed for all, the V-bit for V-carving.
    ''' </summary>
    Private Shared Sub MigrateToolpaths(json As String, pf As ProjectFile)
        Dim s = pf.Settings
        If s.TextMachining Is Nothing Then s.TextMachining = New Machining()
        Dim textHasFeeds As Boolean = False
        Dim objectHasFeeds As New List(Of Boolean)
        Try
            Using doc = JsonDocument.Parse(json, New JsonDocumentOptions With {.AllowTrailingCommas = True, .CommentHandling = JsonCommentHandling.Skip})
                Dim settingsEl, el As JsonElement
                If FindMember(doc.RootElement, "Settings", settingsEl) AndAlso FindMember(settingsEl, "TextMachining", el) Then
                    textHasFeeds = HasMember(el, "FeedRate")
                End If
                Dim objectsEl As JsonElement
                If FindMember(doc.RootElement, "Objects", objectsEl) AndAlso objectsEl.ValueKind = JsonValueKind.Array Then
                    For Each o In objectsEl.EnumerateArray()
                        Dim mEl As JsonElement
                        objectHasFeeds.Add(FindMember(o, "Machining", mEl) AndAlso HasMember(mEl, "FeedRate"))
                    Next
                End If
            End Using
        Catch
        End Try
        If Not textHasFeeds Then AdoptJobWide(s.TextMachining, s)
        For i = 0 To pf.Objects.Count - 1
            Dim o = pf.Objects(i)
            If o.Machining Is Nothing Then o.Machining = New Machining()
            If i >= objectHasFeeds.Count OrElse Not objectHasFeeds(i) Then AdoptJobWide(o.Machining, s)
        Next
        For Each m In {s.TextMachining}.Concat(pf.Objects.Select(Function(o) o.Machining))
            If m.Tool Is Nothing Then m.Tool = Machining.DefaultToolFor(m.Operation)
            If String.IsNullOrWhiteSpace(m.Tool.Id) Then m.Tool.Id = ToolDefinition.NewId()
            If m.Tool.Name Is Nothing Then m.Tool.Name = ""
            m.Tool.Normalize()
        Next
    End Sub

    Private Shared Sub AdoptJobWide(m As Machining, s As CarveSettings)
        m.FeedRate = s.FeedRate
        m.PlungeRate = s.PlungeRate
        m.SpindleRpm = s.SpindleRpm
        If m.Operation = CutOperation.VCarve AndAlso s.CarveTool IsNot Nothing Then m.Tool = s.CarveTool.Clone()
    End Sub

    Private Shared Function HasMember(obj As JsonElement, name As String) As Boolean
        Dim v As JsonElement
        Return FindMember(obj, name, v)
    End Function

    Private Shared Function FindMember(obj As JsonElement, name As String, ByRef value As JsonElement) As Boolean
        If obj.ValueKind <> JsonValueKind.Object Then Return False
        For Each p In obj.EnumerateObject()
            If String.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase) Then
                value = p.Value
                Return True
            End If
        Next
        Return False
    End Function
End Class
