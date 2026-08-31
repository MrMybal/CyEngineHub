#ifndef AppVersion
  #error AppVersion must be provided by build-windows.ps1
#endif
#ifndef AppSource
  #error AppSource must be provided by build-windows.ps1
#endif
#ifndef ReleaseOutput
  #error ReleaseOutput must be provided by build-windows.ps1
#endif

[Setup]
AppId={{082080E4-0B96-4ABD-ADE7-347C932F01BC}
AppName=CyEngineHub
AppVersion={#AppVersion}
AppVerName=CyEngineHub {#AppVersion}
AppPublisher=CyberAlien
AppPublisherURL=https://github.com/MrMybal/CyEngineHub
AppSupportURL=https://github.com/MrMybal/CyEngineHub/issues
AppUpdatesURL=https://github.com/MrMybal/CyEngineHub/releases
DefaultDirName={localappdata}\Programs\CyEngineHub
DefaultGroupName=CyEngineHub
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
OutputDir={#ReleaseOutput}
OutputBaseFilename=CyEngineHub-{#AppVersion}-Setup-win-x64
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\CyEngineHub.Desktop.exe
CloseApplications=yes
CloseApplicationsFilter=CyEngineHub.Desktop.exe,CyEngineHub.AssetBridge.Tool.exe
RestartApplications=no

[Languages]
Name: "french"; MessagesFile: "compiler:Languages\French.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Créer un raccourci sur le bureau"; Flags: unchecked

[Files]
Source: "{#AppSource}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\CyEngineHub"; Filename: "{app}\CyEngineHub.Desktop.exe"
Name: "{autodesktop}\CyEngineHub"; Filename: "{app}\CyEngineHub.Desktop.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\CyEngineHub.Desktop.exe"; Description: "Ouvrir CyEngineHub"; Flags: nowait postinstall skipifsilent

; Never remove user catalogs, credentials, downloads, engine or project folders.
; No startup registration: it remains an explicit in-app preference.
