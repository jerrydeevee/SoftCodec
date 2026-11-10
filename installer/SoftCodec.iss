#define MyAppVersion "0.1.0"
[Setup]
AppId={{A401C6D2-C175-4B45-AF90-75190451D22A}
AppName=SoftCodec
AppVersion={#MyAppVersion}
AppPublisher=DVC-it
DefaultDirName={autopf}\DVC-it\SoftCodec
DefaultGroupName=SoftCodec
OutputDir=..\artifacts\installer
OutputBaseFilename=SoftCodec-{#MyAppVersion}-win-x64-setup
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
UninstallDisplayIcon={app}\SoftCodec.App.exe
CloseApplications=yes
[Languages]
Name: "dutch"; MessagesFile: "compiler:Languages\Dutch.isl"
[Tasks]
Name: "desktopicon"; Description: "Snelkoppeling op het bureaublad"; Flags: unchecked
Name: "firewall"; Description: "Firewallregels voor SIP 5060 en RTP/RTCP 5004–5005 (privé- en domeinnetwerk)"; Flags: unchecked
[Dirs]
Name: "{commonappdata}\DVC-it\SoftCodec"; Permissions: users-modify
[Files]
Source: "..\artifacts\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "firewall.ps1"; DestDir: "{app}"; Flags: ignoreversion
Source: "autostart.ps1"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\README.md"; DestDir: "{app}"
Source: "..\THIRD-PARTY-NOTICES.md"; DestDir: "{app}"
[Icons]
Name: "{group}\SoftCodec"; Filename: "{app}\SoftCodec.App.exe"
Name: "{autodesktop}\SoftCodec"; Filename: "{app}\SoftCodec.App.exe"; Tasks: desktopicon
[Run]
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\firewall.ps1"" -AppPath ""{app}\SoftCodec.App.exe"""; Flags: runhidden waituntilterminated; Tasks: firewall
Filename: "{app}\SoftCodec.App.exe"; Description: "SoftCodec starten"; Flags: nowait postinstall skipifsilent runasoriginaluser
[UninstallRun]
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -Command ""Get-NetFirewallRule -Group 'SoftCodec' -ErrorAction SilentlyContinue | Remove-NetFirewallRule"""; Flags: runhidden
