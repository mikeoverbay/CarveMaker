' ============================================================================
'  AppInfo.vb
'  The program's name as people see it: title bar, About, dialogs, messages,
'  the G-code header. Since 2026-10-26 it is "CarveMakerGG Gcode Generator".
'  Behind the scenes the old name stays so nothing breaks: CarveMaker.exe, the
'  settings folder %AppData%\CarveMaker (tool library, preferences), the
'  installer's identity (in-place upgrades) and the project file marker.
' ============================================================================

Public Module AppInfo
    Public Const Name As String = "CarveMakerGG Gcode Generator"
    Public Const RepoUrl As String = "https://github.com/mikeoverbay/CarveMakerGG"
End Module
