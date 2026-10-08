; ============================================================================
;  Setup.iss - Inno Setup script for Text to CNC Path
;  Compiled by Setup\Setup.vbproj in Release builds: the app is published
;  self-contained (no .NET install needed on the target PC) and packed into a
;  single Setup.exe with Start Menu / desktop shortcuts, an uninstaller,
;  in-place upgrades and a .prj "open with" association.
;  Defines passed by the build: AppVersion, SourceDir (publish folder), OutputDir.
; ============================================================================

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "publish"
#endif
#ifndef OutputDir
  #define OutputDir "Output"
#endif
#define AppName "Text to CNC Path"
#define AppExe "Text_to_CNC_path.exe"
#define AppUrl "https://github.com/mikeoverbay/Text_to_CNC_path"

[Setup]
; Keep this GUID fixed forever so newer versions upgrade in place.
AppId={{7E3B2C94-5D0A-4C8F-9B61-2F0C4A7E6D11}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=Text to CNC Path
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}
AppUpdatesURL={#AppUrl}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
; Per-user by default (no admin prompt); the dialog lets the user choose all-users.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir={#OutputDir}
OutputBaseFilename=TextToCNCPath-Setup-{#AppVersion}
SetupIconFile=..\app.ico
UninstallDisplayIcon={app}\{#AppExe}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ChangesAssociations=yes
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{group}\{cm:UninstallProgram,{#AppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
; .prj projects: register our ProgId and add it to "Open with"; only become the
; default handler when .prj has no owner yet (other software uses .prj too).
Root: HKA; Subkey: "Software\Classes\TextToCNCPath.Project"; ValueType: string; ValueName: ""; ValueData: "Text to CNC Path Project"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\TextToCNCPath.Project\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\{#AppExe},0"
Root: HKA; Subkey: "Software\Classes\TextToCNCPath.Project\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExe}"" ""%1"""
Root: HKA; Subkey: "Software\Classes\.prj\OpenWithProgids"; ValueType: string; ValueName: "TextToCNCPath.Project"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\.prj"; ValueType: string; ValueName: ""; ValueData: "TextToCNCPath.Project"; Flags: createvalueifdoesntexist uninsclearvalue

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent
