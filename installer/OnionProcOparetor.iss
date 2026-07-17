; OnionProcOparetor Installer - supports 3 installation modes
#define MyAppName "Onion ProcOparetor"
#define MyAppVersion "1.0"
#define MyAppPublisher "OnionProcOparetor"

[Setup]
AppId={{8C2D41E6-5A6D-4D1A-95E2-FB74C37A6E5A}}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=.\output
OutputBaseFilename=OnionProcOparetor-Setup
Compression=lzma
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesInstallIn64BitMode=x64

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Code]
var
  ModePage: TWizardPage;
  RadioModeA, RadioModeB, RadioModeC: TRadioButton;
  SelectedMode: Integer;

function IsDotNet10Installed: Boolean;
var
  DotNetInstallPath: String;
begin
  Result := False;

  if FileExists(ExpandConstant('{sys}\dotnet.exe')) then
    Result := True
  else if RegQueryStringValue(HKEY_LOCAL_MACHINE, 'SOFTWARE\dotnet\Setup\InstalledVersions\x64\dotnet', 'InstallLocation', DotNetInstallPath) then
    Result := True
  else if RegQueryStringValue(HKEY_LOCAL_MACHINE, 'SOFTWARE\dotnet\Setup\InstalledVersions\x86\dotnet', 'InstallLocation', DotNetInstallPath) then
    Result := True;
end;

function InitializeSetup: Boolean;
begin
  Result := True;
end;

procedure InitializeWizard;
begin
  ModePage := CreateCustomPage(wpSelectDir, 'Select Installation Mode',
    'Onion ProcOparetor supports 3 modes. Choose the one that matches this machine.');

  RadioModeA := TRadioButton.Create(ModePage);
  RadioModeA.Parent := ModePage.Surface;
  RadioModeA.Caption := 'Server + Console (main server machine - Windows Service + GUI)';
  RadioModeA.Left := 0;
  RadioModeA.Top := 0;
  RadioModeA.Width := ModePage.SurfaceWidth;
  RadioModeA.Checked := True;

  RadioModeB := TRadioButton.Create(ModePage);
  RadioModeB.Parent := ModePage.Surface;
  RadioModeB.Caption := 'Console only (teacher/admin machine - GUI only, enter IP:Port each time)';
  RadioModeB.Left := 0;
  RadioModeB.Top := RadioModeA.Top + RadioModeA.Height + 16;
  RadioModeB.Width := ModePage.SurfaceWidth;

  RadioModeC := TRadioButton.Create(ModePage);
  RadioModeC.Parent := ModePage.Surface;
  RadioModeC.Caption := 'Client Agent (student machine - Windows Service + Tray icon)';
  RadioModeC.Left := 0;
  RadioModeC.Top := RadioModeB.Top + RadioModeB.Height + 16;
  RadioModeC.Width := ModePage.SurfaceWidth;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if CurPageID = ModePage.ID then
  begin
    if RadioModeA.Checked then
      SelectedMode := 1
    else if RadioModeB.Checked then
      SelectedMode := 2
    else
      SelectedMode := 3;
  end;
end;

function ShouldInstallServer: Boolean;
begin
  Result := (SelectedMode = 1);
end;

function ShouldInstallConsole: Boolean;
begin
  Result := (SelectedMode = 1) or (SelectedMode = 2);
end;

function ShouldInstallClient: Boolean;
begin
  Result := (SelectedMode = 3);
end;

var
  DeleteAllData: Boolean;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ProgramDataPath: String;
begin
  if CurUninstallStep = usUninstall then
  begin
    DeleteAllData := (MsgBox(
      'Do you also want to delete all Onion ProcOparetor data (database, logs, rules)?' + #13#10 +
      'Choose No if you plan to reinstall later and want to keep your existing data.',
      mbConfirmation, MB_YESNO) = IDYES);
  end;

  if CurUninstallStep = usPostUninstall then
  begin
    if DeleteAllData then
    begin
      ProgramDataPath := ExpandConstant('{commonappdata}\OnionProcOparetor');
      if DirExists(ProgramDataPath) then
        DelTree(ProgramDataPath, True, True, True);

      RegDeleteValue(HKEY_CURRENT_USER, 'SOFTWARE\Microsoft\Windows\CurrentVersion\Run', 'OnionProcOparetorAgentTray');
    end;
  end;
end;

[Files]
Source: "..\publish\OnionProcOparetor.Server\*"; DestDir: "{app}\Server"; Flags: ignoreversion recursesubdirs createallsubdirs; Check: ShouldInstallServer
Source: "..\publish\OnionProcOparetor.Console\*"; DestDir: "{app}\Console"; Flags: ignoreversion recursesubdirs createallsubdirs; Check: ShouldInstallConsole
Source: "..\publish\OnionProcOparetor.Agent\*"; DestDir: "{app}\Client"; Flags: ignoreversion recursesubdirs createallsubdirs; Check: ShouldInstallClient
Source: "..\publish\OnionProcOparetor.AgentTray\*"; DestDir: "{app}\ClientTray"; Flags: ignoreversion recursesubdirs createallsubdirs; Check: ShouldInstallClient
Source: "run-setup.ps1"; DestDir: "{app}"; Flags: ignoreversion; Check: ShouldInstallServer

[Icons]
Name: "{group}\Onion ProcOparetor Console"; Filename: "{app}\Console\OnionProcOparetor.Console.exe"; Check: ShouldInstallConsole
Name: "{group}\Onion ProcOparetor Tray"; Filename: "{app}\ClientTray\OnionProcOparetor.AgentTray.exe"; Check: ShouldInstallClient

[Run]
Filename: "sc.exe"; Parameters: "create OnionCoreService binPath= ""{app}\Server\OnionProcOparetor.Server.exe"" start= auto"; Flags: runhidden; Check: ShouldInstallServer
Filename: "sc.exe"; Parameters: "start OnionCoreService"; Flags: runhidden; Check: ShouldInstallServer
Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\run-setup.ps1"""; Flags: runhidden; Check: ShouldInstallServer
Filename: "sc.exe"; Parameters: "create OnionAgent binPath= ""{app}\Client\OnionProcOparetor.Agent.exe"" start= auto"; Flags: runhidden; Check: ShouldInstallClient
Filename: "sc.exe"; Parameters: "start OnionAgent"; Flags: runhidden; Check: ShouldInstallClient
Filename: "sc.exe"; Parameters: "failure OnionAgent reset= 86400 actions= restart/0/restart/0/restart/0"; Flags: runhidden; Check: ShouldInstallClient
Filename: "{app}\ClientTray\OnionProcOparetor.AgentTray.exe"; Description: "Launch Onion ProcOparetor Tray"; Flags: postinstall nowait skipifsilent; Check: ShouldInstallClient
Filename: "{app}\Console\OnionProcOparetor.Console.exe"; Description: "Launch Onion ProcOparetor Console"; Flags: postinstall nowait skipifsilent; Check: ShouldInstallConsole

[UninstallRun]
Filename: "sc.exe"; Parameters: "stop OnionCoreService"; Flags: runhidden; RunOnceId: "StopServerService"
Filename: "sc.exe"; Parameters: "delete OnionCoreService"; Flags: runhidden; RunOnceId: "DeleteServerService"
Filename: "sc.exe"; Parameters: "stop OnionAgent"; Flags: runhidden; RunOnceId: "StopClientService"
Filename: "sc.exe"; Parameters: "delete OnionAgent"; Flags: runhidden; RunOnceId: "DeleteClientService"
