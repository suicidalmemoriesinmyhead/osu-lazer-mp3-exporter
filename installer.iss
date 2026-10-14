#define MyAppName "osu!lazer MP3 Exporter"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "Akz0n1"
#define MyAppURL "https://github.com/suicidalmemoriesinmyhead/osu-lazer-mp3-exporter"
#define MyAppExeName "OsuLazerAudioExporter.exe"

[Setup]
AppId={{B88E91A2-4E3F-48B2-B671-A3C2F7A5E911}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DisableProgramGroupPage=yes
LicenseFile=LICENSE
PrivilegesRequired=lowest
OutputDir=installer_output
OutputBaseFilename=OsuLazerAudioExporter_Setup_v1.0.0
Compression=lzma
SolidCompression=yes
WizardStyle=modern

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "dist\win-x64\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "dist\win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent