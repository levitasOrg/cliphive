#define MyAppName "ClipHive"
#define MyAppVersion "1.3.1"
#define MyAppPublisher "ClipHive Contributors"
#define MyAppURL "https://github.com/levitasOrg/cliphive"
#define MyAppExeName "ClipHive.exe"
#define MyAppId "{{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}"

[Setup]
AppId={#MyAppId}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}/issues
AppUpdatesURL={#MyAppURL}/releases
DefaultDirName={autopf64}\{#MyAppName}
DefaultGroupName={#MyAppName}
OutputDir=..\dist
OutputBaseFilename=ClipHive-{#MyAppVersion}-Setup
SetupIconFile=..\assets\icon\ClipHive.ico
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
; Startup entry (HKCU) and user desktop shortcut are intentionally per-user
UsedUserAreasWarning=no
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\{#MyAppExeName}
LicenseFile=license.txt
MinVersion=10.0.17763

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon";   Description: "{cm:CreateDesktopIcon}";                   GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "startupentry";  Description: "Start ClipHive automatically with Windows"; GroupDescription: "Startup:";

[Files]
Source: "..\dist\release\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs

[Icons]
Name: "{group}\{#MyAppName}";                     Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{userdesktop}\{#MyAppName}";               Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "ClipHive"; ValueData: """{app}\{#MyAppExeName}"""; Flags: uninsdeletevalue; Tasks: startupentry

[Run]
; Refresh Windows icon cache so the ClipHive logo shows immediately
; without needing a reboot or manual Explorer restart
Filename: "ie4uinit.exe"; Parameters: "-show"; Flags: runhidden nowait
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{localappdata}\ClipHive"

[Code]

// ─── Already-installed check ────────────────────────────────────────────────

function IsAlreadyInstalled(): Boolean;
var
  UninstallKey: String;
  DisplayName: String;
begin
  UninstallKey := 'Software\Microsoft\Windows\CurrentVersion\Uninstall\' + '{#MyAppId}_is1';
  Result := RegQueryStringValue(HKCU, UninstallKey, 'DisplayName', DisplayName);
  if not Result then
    Result := RegQueryStringValue(HKLM, UninstallKey, 'DisplayName', DisplayName);
end;

function GetInstallPath(): String;
var
  UninstallKey: String;
begin
  UninstallKey := 'Software\Microsoft\Windows\CurrentVersion\Uninstall\' + '{#MyAppId}_is1';
  if not RegQueryStringValue(HKCU, UninstallKey, 'InstallLocation', Result) then
    RegQueryStringValue(HKLM, UninstallKey, 'InstallLocation', Result);
end;

function InitializeSetup(): Boolean;
var
  Answer: Integer;
  InstallPath: String;
  ExePath: String;
  MissingFiles: String;
  FileList: TArrayOfString;
  I: Integer;
  ResultCode: Integer;
begin
  Result := True;

  if not IsAlreadyInstalled() then
    Exit;

  // Already installed — offer Repair or Uninstall
  Answer := MsgBox(
    'ClipHive is already installed on this computer.' + #13#10 + #13#10 +
    'What would you like to do?' + #13#10 + #13#10 +
    'Yes  = Repair (reinstall missing or changed files)' + #13#10 +
    'No   = Uninstall ClipHive' + #13#10 +
    'Cancel = Exit this setup',
    mbConfirmation, MB_YESNOCANCEL);

  if Answer = IDCANCEL then
  begin
    Result := False;
    Exit;
  end;

  if Answer = IDNO then
  begin
    // Run the uninstaller
    InstallPath := GetInstallPath();
    ExePath := InstallPath + '\unins000.exe';
    if FileExists(ExePath) then
    begin
      Exec(ExePath, '/SILENT', '', SW_SHOW, ewWaitUntilTerminated, ResultCode);
    end else
      MsgBox('Uninstaller not found at: ' + ExePath, mbError, MB_OK);
    Result := False;
    Exit;
  end;

  // Answer = IDYES → Repair: check which key files are present
  InstallPath := GetInstallPath();
  MissingFiles := '';

  SetArrayLength(FileList, 3);
  FileList[0] := InstallPath + '\ClipHive.exe';
  FileList[1] := InstallPath + '\ClipHive.dll';
  FileList[2] := InstallPath + '\Microsoft.Data.Sqlite.dll';

  for I := 0 to GetArrayLength(FileList) - 1 do
  begin
    if not FileExists(FileList[I]) then
      MissingFiles := MissingFiles + #13#10 + '  • ' + ExtractFileName(FileList[I]);
  end;

  if MissingFiles = '' then
  begin
    MsgBox(
      'Repair scan complete.' + #13#10 + #13#10 +
      'All checked files are present — your installation looks good!' + #13#10 +
      'Click OK to run a full reinstall anyway, or cancel setup if not needed.',
      mbInformation, MB_OK);
  end else
  begin
    MsgBox(
      'The following files are missing and will be restored:' +
      MissingFiles + #13#10 + #13#10 +
      'Setup will now reinstall ClipHive.',
      mbInformation, MB_OK);
  end;

  // Continue with normal install (overwrites all files)
  Result := True;
end;

// ─── Uninstall: remove Run registry key ────────────────────────────────────

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
    RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'ClipHive');
end;
