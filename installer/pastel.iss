; Pastel installer — per-user (no admin required), built with Inno Setup 7
#define MyAppName "Pastel"
#define MyAppVersion "1.2.0"
#define MyAppExeName "Pastel.exe"

[Setup]
AppId={{C1A7F3D9-2E64-4B8A-9F15-D0B72E6C8A41}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher=Pastel
DefaultDirName={autopf}\{#MyAppName}
DisableProgramGroupPage=yes
DisableDirPage=auto
PrivilegesRequired=lowest
OutputDir=..\dist
OutputBaseFilename=PastelSetup-{#MyAppVersion}
SetupIconFile=..\src\pastel.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; Flags: unchecked
Name: "autostart"; Description: "Start {#MyAppName} automatically when you sign in"

[Files]
Source: "..\Pastel.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion
; Inter UI font (SIL Open Font License) — installed per-user, left in place on uninstall
Source: "fonts\Inter-Regular.ttf"; DestDir: "{autofonts}"; FontInstall: "Inter Regular"; Flags: onlyifdoesntexist uninsneveruninstall
Source: "fonts\Inter-Medium.ttf"; DestDir: "{autofonts}"; FontInstall: "Inter Medium"; Flags: onlyifdoesntexist uninsneveruninstall
Source: "fonts\Inter-SemiBold.ttf"; DestDir: "{autofonts}"; FontInstall: "Inter SemiBold"; Flags: onlyifdoesntexist uninsneveruninstall
Source: "fonts\Inter-Bold.ttf"; DestDir: "{autofonts}"; FontInstall: "Inter Bold"; Flags: onlyifdoesntexist uninsneveruninstall
Source: "fonts\LICENSE.txt"; DestDir: "{app}"; DestName: "Inter-LICENSE.txt"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "{#MyAppName}"; ValueData: """{app}\{#MyAppExeName}"" --startup"; Flags: uninsdeletevalue; Tasks: autostart

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{sys}\taskkill.exe"; Parameters: "/f /im {#MyAppExeName}"; Flags: runhidden; RunOnceId: "KillPastel"

[Code]
// stop a running instance so files can be replaced on install/upgrade
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  R: Integer;
begin
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/f /im {#MyAppExeName}', '',
    SW_HIDE, ewWaitUntilTerminated, R);
  Result := '';
end;
