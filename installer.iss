#define MyAppName "dapapa"
#define MyAppVersion "1.0.19"
#define MyAppPublisher "dapapa"
#define MyAppExeName "dapapa.exe"
#define MyAppURL "https://github.com/ouxan/dapapa"
#ifndef MyAppReleaseDir
  #define MyAppReleaseDir "artifacts\release"
#endif

[Setup]
AppId={{8B4B8765-8A1C-4B9F-8AE1-79A42F3507B9}}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}/issues
AppUpdatesURL={#MyAppURL}/releases
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
OutputDir=setup_output
OutputBaseFilename=dapapa_Setup
SetupIconFile=Resources\app.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
UsedUserAreasWarning=no
ArchitecturesInstallIn64BitMode=x64compatible
ArchitecturesAllowed=x64compatible
CloseApplications=yes
RestartApplications=no
DisableProgramGroupPage=yes

[Languages]
Name: "turkish"; MessagesFile: "compiler:Languages\Turkish.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "{#MyAppReleaseDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb"

[InstallDelete]
Type: files; Name: "{app}\warp\warp-cli.exe"
Type: dirifempty; Name: "{app}\warp"

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "dapapa"; Flags: dontcreatekey uninsdeletevalue
Root: HKCU; Subkey: "Software\dapapa"; ValueType: string; ValueName: "InstallerLanguage"; ValueData: "tr"; Languages: turkish; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\dapapa"; ValueType: string; ValueName: "InstallerLanguage"; ValueData: "en"; Languages: english; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\dapapa"; ValueType: string; ValueName: "Language"; ValueData: "tr"; Languages: turkish; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\dapapa"; ValueType: string; ValueName: "Language"; ValueData: "en"; Languages: english; Flags: uninsdeletekey

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent runascurrentuser

[UninstallRun]
Filename: "{cmd}"; Parameters: "/C taskkill /F /IM dapapa.exe /T 2>nul || exit 0"; Flags: runhidden; RunOnceId: "KillDapapa"
Filename: "{cmd}"; Parameters: "/C schtasks /delete /tn dapapa /f 2>nul || exit 0"; Flags: runhidden; RunOnceId: "DeleteDapapaTask"
Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -Command ""$h = [System.IO.Path]::Combine($env:windir, 'System32\drivers\etc\hosts'); if (Test-Path $h) {{$c = [System.IO.File]::ReadAllText($h); if ($c -match '(?s)# \[dapapa-start\].*?# \[dapapa-end\]\r?\n?') {{$c = $c -replace '(?s)# \[dapapa-start\].*?# \[dapapa-end\]\r?\n?', ''; [System.IO.File]::WriteAllText($h, $c, [System.Text.UTF8Encoding]::new($false))}}}}"""; Flags: runhidden; RunOnceId: "CleanHostsFile"
