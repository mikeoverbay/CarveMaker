' ============================================================================
'  frmMain.Help.vb
'  Help menu: the user guide (Help\CarveMakerGG Gcode Generator Help.html next to the exe,
'  opened in the default browser; F1), the releases page, and About.
' ============================================================================

Imports System.Diagnostics
Imports System.IO

Partial Class frmMain

    Private Const ReleasesUrl As String = AppInfo.RepoUrl & "/releases"
    Private Const ReadmeUrl As String = AppInfo.RepoUrl & "#readme"

    ''' <summary>Where the user guide is installed (next to the exe).</summary>
    Public Shared Function HelpGuidePath() As String
        Return Path.Combine(AppContext.BaseDirectory, "Help", AppInfo.Name & " Help.html")
    End Function

    ''' <summary>"1.3.0": the version without the build's commit id.</summary>
    Public Shared Function DisplayVersion() As String
        Dim v = Application.ProductVersion
        Dim plus = v.IndexOf("+"c)
        Return If(plus > 0, v.Substring(0, plus), v)
    End Function

    ''' <summary>Opens a file or web page with Windows' default program.</summary>
    Private Sub OpenWithShell(target As String)
        Try
            Process.Start(New ProcessStartInfo(target) With {.UseShellExecute = True})
        Catch ex As Exception
            MessageBox.Show(Me, "Could not open " & target & Environment.NewLine & ex.Message, AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Warning)
        End Try
    End Sub

    Private Sub mnuHelpGuide_Click(sender As Object, e As EventArgs) Handles mnuHelpGuide.Click
        Dim guide = HelpGuidePath()
        ' A copy run from somewhere without the Help folder still has the online description.
        OpenWithShell(If(File.Exists(guide), guide, ReadmeUrl))
    End Sub

    Private Sub mnuHelpReleases_Click(sender As Object, e As EventArgs) Handles mnuHelpReleases.Click
        OpenWithShell(ReleasesUrl)
    End Sub

    Private Sub mnuHelpAbout_Click(sender As Object, e As EventArgs) Handles mnuHelpAbout.Click
        MessageBox.Show(Me,
                        AppInfo.Name & " " & DisplayVersion() & Environment.NewLine & Environment.NewLine &
                        "V-carve, pocket, profile and 3D relief toolpaths and G-code for text, drawings and models, with a live preview and simulation." & Environment.NewLine & Environment.NewLine &
                        "User guide: Help > User Guide (F1)" & Environment.NewLine & ReleasesUrl,
                        "About " & AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Information)
    End Sub
End Class
