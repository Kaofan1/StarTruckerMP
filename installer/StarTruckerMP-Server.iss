; Star Trucker Multiplayer dedicated server installer (Inno Setup 6)
;
; This is the server on its own, separate from the launcher Setup. It installs the server into
; Program Files, writes server.json on the first install, opens the firewall and registers the
; executable as a manual Windows service that only runs when the operator starts it.
;
; Build with:
;   build-server-installer-winx64.cmd <build number>
; or:
;   "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" /DMyAppVersion=1.0.71 installer\StarTruckerMP-Server.iss
;
; Silent install: StarTruckerMP-Server-Setup.exe /VERYSILENT /PORT=7778 /MAXPLAYERS=16

#define MyAppName "StarTruckerMP Dedicated Server"
#define MyAppShortName "StarTruckerMP Server"
#define MyAppPublisher "Kaofan"
#define MyAppUrl "https://github.com/Kaofan1/StarTruckerMP"
#define MyAppExeName "StarTruckerMP.Server.exe"
#define ServiceName "StarTruckerMPServer"
#define MyAppIcon "..\StarTruckerMP.Launcher\Assets\StarTruckerMP.ico"

#ifndef MyAppVersion
  #define MyAppVersion "1.0.0"
#endif

#ifndef SourceDir
  #define SourceDir "..\artifacts\server-win"
#endif

[Setup]
AppId={{6D3F0C2A-9B44-4E7A-8C51-2F8A9D0B7E31}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppUrl}
AppSupportURL={#MyAppUrl}/issues
AppUpdatesURL={#MyAppUrl}/releases
DefaultDirName={autopf}\{#MyAppShortName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
DisableWelcomePage=no
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\artifacts\server-installer
OutputBaseFilename=StarTruckerMP-Server-Setup
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
SetupLogging=yes
UninstallDisplayName={#MyAppName}
UninstallDisplayIcon={app}\{#MyAppExeName}
SetupIconFile={#MyAppIcon}
CloseApplications=no
AllowNoIcons=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "polish"; MessagesFile: "compiler:Languages\Polish.isl"

[Tasks]
Name: "firewall"; Description: "Open TCP and UDP port {code:GetPortText} in Windows Firewall"; GroupDescription: "Network:"
Name: "service"; Description: "Register and start the server as a Windows service"; GroupDescription: "Service:"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Excludes: "*.pdb,server.json"; Flags: ignoreversion recursesubdirs createallsubdirs

[Dirs]
; Settings live outside Program Files so editing server.json needs no administrator rights.
Name: "{commonappdata}\StarTruckerMP"; Permissions: users-modify
Name: "{commonappdata}\StarTruckerMP\certs"; Permissions: users-modify

[Registry]
Root: HKLM; Subkey: "Software\StarTruckerMP Server"; ValueType: string; ValueName: "InstallDir"; ValueData: "{app}"; Flags: uninsdeletekey
Root: HKLM; Subkey: "Software\StarTruckerMP Server"; ValueType: dword; ValueName: "Port"; ValueData: "{code:GetPortText}"; Flags: uninsdeletekey

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "https://localhost:{code:GetPortText}/"; Comment: "Status page of the local server"
Name: "{group}\Server settings (server.json)"; Filename: "notepad.exe"; Parameters: """{commonappdata}\StarTruckerMP\server.json"""; Comment: "No administrator rights needed, the settings live in ProgramData"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"

[UninstallRun]
Filename: "{sys}\cmd.exe"; Parameters: "/c sc.exe stop {#ServiceName} & timeout /t 5 /nobreak >nul & sc.exe delete {#ServiceName}"; Flags: runhidden; RunOnceId: "StopService"
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""StarTruckerMP Dedicated Server (TCP)"""; Flags: runhidden; RunOnceId: "DeleteTcpRule"
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""StarTruckerMP Dedicated Server (UDP)"""; Flags: runhidden; RunOnceId: "DeleteUdpRule"

[Code]
var
  ServerPort: Integer;
  MaxPlayers: Integer;
  GeneratedPassword: String;
  ConfigCreated: Boolean;
  ServiceWasInstalled: Boolean;
  ServiceWasActive: Boolean;

function GetParamValue(const Name, Default: String): String;
var
  Index: Integer;
  Param: String;
begin
  Result := Default;

  for Index := 1 to ParamCount do
  begin
    Param := ParamStr(Index);
    if CompareText(Copy(Param, 1, Length(Name) + 1), Name + '=') = 0 then
    begin
      Result := Copy(Param, Length(Name) + 2, MaxInt);
      Exit;
    end;
  end;
end;

function GetPortText(Param: String): String;
begin
  Result := IntToStr(ServerPort);
end;

function ValidPort(const Value: String): Boolean;
var
  Number: Integer;
begin
  Number := StrToIntDef(Value, -1);
  Result := (Number >= 1) and (Number <= 65535);
end;

function ValidPlayers(const Value: String): Boolean;
var
  Number: Integer;
begin
  Number := StrToIntDef(Value, -1);
  Result := (Number >= 1) and (Number <= 256);
end;

function InitializeSetup: Boolean;
var
  PortText, PlayersText: String;
begin
  PortText := GetParamValue('PORT', '7777');
  PlayersText := GetParamValue('MAXPLAYERS', '100');

  if not ValidPort(PortText) then
  begin
    MsgBox('The port must be between 1 and 65535.', mbError, MB_OK);
    Result := False;
    Exit;
  end;

  if not ValidPlayers(PlayersText) then
  begin
    MsgBox('Max players must be between 1 and 256.', mbError, MB_OK);
    Result := False;
    Exit;
  end;

  ServerPort := StrToInt(PortText);
  MaxPlayers := StrToInt(PlayersText);
  Result := True;
end;

function ServiceQueryCode(const Arguments: String): Integer;
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant('{sys}\sc.exe'), Arguments, '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Result := ResultCode;
end;

/// <summary>True when sc reports a running or starting service, so an upgrade can restore it.</summary>
function ServiceReportsActive: Boolean;
var
  ResultCode: Integer;
  TempFile: String;
  Content: AnsiString;
  Upper: String;
begin
  TempFile := ExpandConstant('{tmp}\stmp-service-state.txt');
  Exec(ExpandConstant('{sys}\cmd.exe'),
       '/c sc.exe query {#ServiceName} > "' + TempFile + '" 2>&1',
       '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

  Result := False;
  if LoadStringFromFile(TempFile, Content) then
  begin
    Upper := Uppercase(Content);
    Result := (Pos('RUNNING', Upper) > 0) or (Pos('START_PENDING', Upper) > 0);
  end;

  DeleteFile(TempFile);
end;

function ServerProcessRunning: Boolean;
var
  ResultCode: Integer;
begin
  // tasklist piped into find: a zero exit code means the server process is still alive.
  Exec(ExpandConstant('{sys}\cmd.exe'),
       '/c tasklist /fi "imagename eq StarTruckerMP.Server.exe" | find /i "StarTruckerMP.Server.exe" >nul',
       '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Result := ResultCode = 0;
end;

procedure StopAndDeleteService;
var
  Attempt, ResultCode: Integer;
begin
  if ServiceQueryCode('query {#ServiceName}') = 0 then
  begin
    // Stop first so the server shuts down cleanly, then delete it so Windows cannot restart it.
    ServiceQueryCode('stop {#ServiceName}');
    ServiceQueryCode('delete {#ServiceName}');
  end;

  // The service object can disappear while its process is still shutting down, and the executable
  // stays locked until that process exits, so wait for the process itself.
  for Attempt := 1 to 20 do
  begin
    if not ServerProcessRunning then
      Exit;

    Sleep(1000);
  end;

  // Something kept the server alive; the files cannot be replaced while it runs.
  Exec(ExpandConstant('{sys}\cmd.exe'),
       '/c taskkill /f /im StarTruckerMP.Server.exe',
       '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Sleep(3000);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  // An upgrade replaces a running executable, so the service has to go before the files are copied.
  // The service existed before the upgrade, so it is put back even when the task is not selected,
  // and it is only started again when it was running before: a stopped server stays stopped.
  ServiceWasInstalled := ServiceQueryCode('query {#ServiceName}') = 0;
  ServiceWasActive := ServiceWasInstalled and ServiceReportsActive;
  StopAndDeleteService;
  Result := '';
end;

function GeneratePassword: String;
var
  TempFile: String;
  Content: AnsiString;
  ResultCode: Integer;
begin
  TempFile := ExpandConstant('{tmp}\stmp-password.txt');

  Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),
       '-NoProfile -Command "[guid]::NewGuid().ToString(''N'') | Set-Content -NoNewline ''' + TempFile + '''"',
       '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

  if LoadStringFromFile(TempFile, Content) and (Length(Content) >= 16) then
    Result := String(Content)
  else
    Result := 'changeit';

  DeleteFile(TempFile);
end;

procedure MigrateServerConfig;
var
  Source, Target, SourceCerts, TargetCerts: String;
  Content: AnsiString;
  ResultCode: Integer;
begin
  // An installation from an older release keeps server.json next to the executable. Copy it to
  // the shared folder so the operator can edit it without administrator rights.
  Source := ExpandConstant('{app}\server.json');
  Target := ExpandConstant('{commonappdata}\StarTruckerMP\server.json');

  if FileExists(Source) and not FileExists(Target) then
    FileCopy(Source, Target, False);

  // The certificate is only copied when the shared folder has none, so a custom certificate in
  // the shared folder is never overwritten by the old one from Program Files.
  SourceCerts := ExpandConstant('{app}\certs');
  TargetCerts := ExpandConstant('{commonappdata}\StarTruckerMP\certs');
  if DirExists(SourceCerts) and not DirExists(TargetCerts) then
    Exec(ExpandConstant('{sys}\cmd.exe'),
         '/c xcopy /E /I /Q /Y "' + SourceCerts + '" "' + TargetCerts + '"',
         '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

  // Once the shared copy exists, the old one goes: two server.json files that drift apart are
  // worse than none, and the shortcut and the docs point at ProgramData.
  if FileExists(Source) and FileExists(Target) then
  begin
    // A custom absolute CertificatePath can still point into the install folder, so the
    // certificate folder stays in that case.
    if not (LoadStringFromFile(Target, Content) and (Pos(ExpandConstant('{app}'), String(Content)) > 0)) then
      if DirExists(SourceCerts) then
        DelTree(SourceCerts, True, True, True);

    DeleteFile(Source);
  end;
end;

procedure WriteServerConfig;
var
  Path, Json: String;
begin
  Path := ExpandConstant('{commonappdata}\StarTruckerMP\server.json');
  if FileExists(Path) then
    Exit;

  GeneratedPassword := GeneratePassword;

  Json := '{' + #13#10 +
          '  "IpAddress": "0.0.0.0",' + #13#10 +
          '  "Port": ' + IntToStr(ServerPort) + ',' + #13#10 +
          '  "MaxPlayers": ' + IntToStr(MaxPlayers) + ',' + #13#10 +
          '  "ApiAdminUsername": "admin",' + #13#10 +
          '  "ApiAdminPassword": "' + GeneratedPassword + '",' + #13#10 +
          '  "DirectoryUrl": "",' + #13#10 +
          '  "DirectoryHost": "",' + #13#10 +
          '  "DirectoryName": "",' + #13#10 +
          '  "DirectoryDescription": ""' + #13#10 +
          '}';

  ConfigCreated := SaveStringToFile(Path, Json, False);
end;

procedure CreateFirewallRules;
var
  ResultCode: Integer;
  Netsh: String;
begin
  Netsh := ExpandConstant('{sys}\netsh.exe');

  Exec(Netsh, 'advfirewall firewall delete rule name="StarTruckerMP Dedicated Server (TCP)"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec(Netsh, 'advfirewall firewall delete rule name="StarTruckerMP Dedicated Server (UDP)"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec(Netsh, 'advfirewall firewall add rule name="StarTruckerMP Dedicated Server (TCP)" dir=in action=allow protocol=TCP localport=' + IntToStr(ServerPort), '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec(Netsh, 'advfirewall firewall add rule name="StarTruckerMP Dedicated Server (UDP)" dir=in action=allow protocol=UDP localport=' + IntToStr(ServerPort), '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

procedure CreateAndStartService;
var
  ResultCode: Integer;
  ExePath: String;
begin
  ExePath := ExpandConstant('{app}\{#MyAppExeName}');

  if ServiceQueryCode('query {#ServiceName}') = 0 then
  begin
    ServiceQueryCode('stop {#ServiceName}');
    Sleep(3000);
    ServiceQueryCode('delete {#ServiceName}');
    Sleep(2000);
  end;

  if Exec(ExpandConstant('{sys}\sc.exe'),
          'create {#ServiceName} binPath= "' + ExePath + '" obj= "NT AUTHORITY\LocalService" start= demand DisplayName= "{#MyAppName}"',
          '', SW_HIDE, ewWaitUntilTerminated, ResultCode) and (ResultCode = 0) then
  begin
    Exec(ExpandConstant('{sys}\sc.exe'),
         'description {#ServiceName} "StarTruckerMP dedicated server. Settings: ' + ExpandConstant('{commonappdata}\StarTruckerMP\server.json') + '"',
         '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    // The service is manual on purpose: it only runs when the operator starts it, so a stopped
    // server stays stopped after a reboot.
    Exec(ExpandConstant('{sys}\sc.exe'), 'config {#ServiceName} start= demand', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

    // A fresh install starts the server once so the operator sees it working; an upgrade keeps
    // whatever state the service had, so updating a stopped server does not start it.
    if (not ServiceWasInstalled) or ServiceWasActive then
      Exec(ExpandConstant('{sys}\sc.exe'), 'start {#ServiceName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  Message: String;
begin
  if CurStep = ssPostInstall then
  begin
    MigrateServerConfig;
    WriteServerConfig;

    // A silent install cannot tick the boxes, and that is exactly how a host updates the
    // server, so an unattended run always gets the firewall rule and the service.
    if WizardIsTaskSelected('firewall') or WizardSilent then
      CreateFirewallRules;

    if WizardIsTaskSelected('service') or ServiceWasInstalled or WizardSilent then
      CreateAndStartService;

    Message := 'StarTruckerMP dedicated server is installed.' + #13#10#13#10 +
               'Settings: ' + ExpandConstant('{commonappdata}\StarTruckerMP\server.json') + #13#10 +
               'Status page: https://localhost:' + IntToStr(ServerPort) + '/' + #13#10 +
               'Players join: <public ip>:' + IntToStr(ServerPort) + ' (UDP and TCP)';

    if ConfigCreated then
      Message := Message + #13#10#13#10 +
                 'API admin: admin / ' + GeneratedPassword + #13#10 +
                 'Save that password, it is written in server.json and not shown again.';

    if not WizardSilent then
      MsgBox(Message, mbInformation, MB_OK);
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if (CurUninstallStep = usPostUninstall) and not UninstallSilent then
    MsgBox('server.json and the certs folder were kept in ' + ExpandConstant('{commonappdata}\StarTruckerMP') + ', so a reinstall continues with the same settings.' + #13#10 +
           'Remove that folder yourself when the server is no longer needed.',
           mbInformation, MB_OK);
end;
