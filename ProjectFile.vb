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
        File.WriteAllText(path, JsonSerializer.Serialize(Me, Options))
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
        MigrateTool(json, pf.Settings)
        If pf.Lines Is Nothing Then pf.Lines = New List(Of ProjectLine)
        If pf.Objects Is Nothing Then pf.Objects = New List(Of DesignObject)
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
