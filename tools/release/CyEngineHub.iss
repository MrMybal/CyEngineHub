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
Uninstallable=not IsAuditExtraction
CreateUninstallRegKey=not IsAuditExtraction
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
Name: "{autoprograms}\CyEngineHub"; Filename: "{app}\CyEngineHub.Desktop.exe"; Check: not IsAuditExtraction
Name: "{autodesktop}\CyEngineHub"; Filename: "{app}\CyEngineHub.Desktop.exe"; Tasks: desktopicon; Check: not IsAuditExtraction

[Run]
Filename: "{app}\CyEngineHub.Desktop.exe"; Description: "Ouvrir CyEngineHub"; Flags: nowait postinstall skipifsilent; Check: not IsAuditExtraction

; Never remove user catalogs, credentials, downloads, engine or project folders.
; No startup registration: it remains an explicit in-app preference.

[Code]
// Audit the actual installer payload without touching an installed application,
// registering an uninstaller, creating shortcuts or launching the hub.
function IsAuditExtraction: Boolean;
begin
  Result := ExpandConstant('{param:AUDITEXTRACT|0}') = '1';
end;

function HasArgument(Value: String): Boolean;
var
  I: Integer;
begin
  Result := False;
  for I := 1 to ParamCount do
    if CompareText(ParamStr(I), Value) = 0 then
      Result := True;
end;

function InitializeSetup: Boolean;
var
  Target: String;
begin
  Result := True;
  if IsAuditExtraction then
  begin
    Target := ExpandConstant('{param:DIR|}');
    // Require a NEW explicit directory. Refuse any existing destination.
    Result := (Length(Target) > 3) and (Copy(Target, 2, 2) = ':\') and
      not DirExists(Target) and not FileExists(Target) and WizardSilent and
      HasArgument('/NOCLOSEAPPLICATIONS') and
      HasArgument('/NORESTARTAPPLICATIONS');
    if not Result then
      MsgBox('Audit extraction requires a new absolute /DIR, silent mode and no application restart/close.', mbError, MB_OK);
  end;
end;
