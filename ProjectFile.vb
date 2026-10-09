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
        Dim pf As ProjectFile = JsonSerializer.Deserialize(Of ProjectFile)(File.ReadAllText(path), Options)
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
        For Each o In pf.Objects
            If o.SvgContent Is Nothing Then o.SvgContent = ""
            If o.Name Is Nothing Then o.Name = "Drawing"
        Next
        For Each l In pf.Lines
            If l.Text Is Nothing Then l.Text = ""
        Next
        Return pf
    End Function
End Class
