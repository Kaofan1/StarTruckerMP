; Star Trucker Multiplayer launcher installer (Inno Setup 6)
;
; Build with:
;   "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer\StarTruckerMP.iss
; or simply run build-installer-winx64.cmd from the repository root.
;
; The output file intentionally carries no version number: StarTruckerMP-Setup.exe

#define MyAppName "Star Trucker Multiplayer"
#define MyAppShortName "StarTruckerMP"
#define MyAppPublisher "Kaofan"
#define MyAppUrl "https://github.com/Kaofan1/StarTruckerMP"
#define MyAppExeName "StarTruckerMP.Launcher.exe"

#ifndef MyAppVersion
  #define MyAppVersion "1.0.0"
#endif

#ifndef SourceDir
  #define SourceDir "..\artifacts\launcher"
#endif

[Setup]
AppId={{B2C4E7A1-5F3D-4A8B-9E62-1D7C0A9F3B54}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppUrl}
AppSupportURL={#MyAppUrl}/issues
AppUpdatesURL={#MyAppUrl}/releases
DefaultDirName={localappdata}\{#MyAppShortName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
DisableWelcomePage=no
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\artifacts\installer
OutputBaseFilename=StarTruckerMP-Setup
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
SetupLogging=yes
UninstallDisplayName={#MyAppName}
UninstallDisplayIcon={app}\{#MyAppExeName}
AllowNoIcons=yes
CloseApplications=no
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "polish"; MessagesFile: "compiler:Languages\Polish.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; {app} only ever contains the launcher itself, so removing the whole folder keeps the
; uninstall complete even if a file was locked while Setup ran.
Type: filesandordirs; Name: "{app}"
; The launcher stores its settings and logs in %APPDATA%\StarTruckMP; they are removed here
; so that a reinstall starts clean. The mod files inside the game folder are left alone.
Type: filesandordirs; Name: "{userappdata}\StarTruckMP"
