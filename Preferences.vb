' ============================================================================
'  Preferences.vb
'  Per-user settings that belong to the person, not to a project: snapping
'  to the grid and the rotation snap step. Stored as JSON next to the tool
'  library in %AppData%\CarveMaker\Preferences.json; a missing or unreadable
'  file just gives the defaults.
' ============================================================================

Imports System.IO
Imports System.Text.Json

Public Class UserPreferences
    ''' <summary>Grid spacings offered for snapping (inches).</summary>
    Public Shared ReadOnly GridSpacings As Double() = {1.0, 0.5, 0.25, 0.125, 0.0625, 0.03125, 0.1, 0.05}

    ''' <summary>Rotation snap steps offered (degrees; 0 = off).</summary>
    Public Shared ReadOnly RotationSteps As Double() = {0, 1, 5, 15, 30, 45, 90}

    ''' <summary>Moving, resizing and drawing snap to the grid.</summary>
    Public Property SnapToGrid As Boolean = False

    ''' <summary>Grid spacing for snapping, inches.</summary>
    Public Property GridSpacing As Double = 0.125

    ''' <summary>Rotation with the knob snaps to multiples of this (degrees; 0 = free).</summary>
    Public Property RotationSnapDeg As Double = 15

    ''' <summary>Where the preferences live (tests point it elsewhere).</summary>
    Public Shared Property FilePath As String =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CarveMaker", "Preferences.json")

    ''' <summary>Reads the preferences; anything missing or out of range gets its default.</summary>
    Public Shared Function Load() As UserPreferences
        Dim p As UserPreferences = Nothing
        Try
            If File.Exists(FilePath) Then
                p = JsonSerializer.Deserialize(Of UserPreferences)(File.ReadAllText(FilePath), New JsonSerializerOptions With {.PropertyNameCaseInsensitive = True})
            End If
        Catch
            ' Unreadable: start from the defaults.
        End Try
        If p Is Nothing Then p = New UserPreferences()
        If Not GridSpacings.Contains(p.GridSpacing) Then p.GridSpacing = 0.125
        If Not RotationSteps.Contains(p.RotationSnapDeg) Then p.RotationSnapDeg = 15
        Return p
    End Function

    ''' <summary>Writes the preferences (a failure only means they are not remembered).</summary>
    Public Sub Save()
        Try
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath))
            Dim tmp = FilePath & ".tmp"
            File.WriteAllText(tmp, JsonSerializer.Serialize(Me, New JsonSerializerOptions With {.WriteIndented = True}))
            File.Move(tmp, FilePath, overwrite:=True)
        Catch
        End Try
    End Sub

    ''' <summary>"1/8""", "0.05""": how a spacing is shown in menus.</summary>
    Public Shared Function SpacingText(inches As Double) As String
        Select Case inches
            Case 1.0 : Return "1"""
            Case 0.5 : Return "1/2"""
            Case 0.25 : Return "1/4"""
            Case 0.125 : Return "1/8"""
            Case 0.0625 : Return "1/16"""
            Case 0.03125 : Return "1/32"""
            Case Else : Return inches.ToString("0.###", Globalization.CultureInfo.InvariantCulture) & """"
        End Select
    End Function

    ''' <summary>"15°" or "Off".</summary>
    Public Shared Function StepText(degrees As Double) As String
        Return If(degrees <= 0, "Off", degrees.ToString("0", Globalization.CultureInfo.InvariantCulture) & "°")
    End Function
End Class
