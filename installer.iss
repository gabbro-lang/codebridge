; CodeBridge installer (Inno Setup 6). Per user, no administrator rights needed.
; Build: ISCC installer.iss  (expects release\CodeBridge\ with the two exes and the README)
#define AppName "CodeBridge"
#define AppVersion "1.0.0"

[Setup]
AppId={{6B1D2C84-7F3A-4E5B-9C21-0A8E4D5F1B37}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=CodeBridge
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=release
OutputBaseFilename=CodeBridge-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
AppMutex=CodeBridge.Single
UninstallDisplayName={#AppName}
UninstallDisplayIcon={app}\codebridge.exe

[Files]
Source: "release\CodeBridge\codebridge.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "release\CodeBridge\codebridge-ble.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "release\CodeBridge\README.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\codebridge.exe"

[Tasks]
Name: "autostart"; Description: "Start CodeBridge when I sign in to Windows"; Flags: unchecked

[Registry]
; the app itself can switch this on and off from its tray menu; the installer only offers it
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "{#AppName}"; ValueData: """{app}\codebridge.exe"""; Tasks: autostart; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "{#AppName}"; Flags: uninsdeletevalue dontcreatekey
Root: HKCU; Subkey: "Software\{#AppName}"; ValueType: none; Flags: uninsdeletekey

[Run]
Filename: "{app}\codebridge.exe"; Description: "Start CodeBridge now"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{sys}\taskkill.exe"; Parameters: "/F /IM codebridge-ble.exe"; Flags: runhidden; RunOnceId: "KillHelper"
Filename: "{sys}\taskkill.exe"; Parameters: "/F /IM codebridge.exe"; Flags: runhidden; RunOnceId: "KillApp"

[UninstallDelete]
Type: filesandordirs; Name: "{localappdata}\CodeBridge"
