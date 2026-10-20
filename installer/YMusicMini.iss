#define MyAppName "YMusic Mini"
#define MyAppVersion "0.6.12"
#define MyAppExeName "YMusicMini.exe"

; The AppId must stay IDENTICAL in future installer versions for upgrades.
[Setup]
AppId=YMusicMini.Desktop.9c8d04a9-8538-4c49-bcc1-93aab56f816e
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
DefaultDirName={autopf}\YMusicMini
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
SetupIconFile=..\Assets\Icons\midnight-player.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
OutputDir=..\artifacts\installers
OutputBaseFilename=YMusicMini-Setup-{#MyAppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; IMPORTANT: publish the app from Visual Studio FIRST, using Win-x64-Installer.
Source: "..\publish\win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent
