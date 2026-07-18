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
  ServerConfigPage: TInputQueryWizardPage;

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

  // ---- หน้าใหม่: ถาม Server IP:Port เฉพาะ Mode C (Client Agent) ----
  ServerConfigPage := CreateInputQueryPage(ModePage.ID,
    'Server Connection', 'Configure the Onion Core Service address',
    'Enter the IP address and port of the Onion Core Service (the Server machine). ' +
    'Default port is 8787 unless changed.');
  ServerConfigPage.Add('Server address (IP:Port):', False);
  ServerConfigPage.Values[0] := 'localhost:8787';
end;

// ---- ซ่อนหน้า ServerConfigPage ถ้าไม่ได้เลือก Mode C ----
function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := False;
  if PageID = ServerConfigPage.ID then
    Result := (SelectedMode <> 3);
end;

// ---- แยก host กับ port จาก string "IP:Port" ----
procedure SplitHostPort(const Input: String; var HostOut: String; var PortOut: String);
var
  ColonPos: Integer;
begin
  ColonPos := Pos(':', Input);
  if ColonPos > 0 then
  begin
    HostOut := Copy(Input, 1, ColonPos - 1);
    PortOut := Copy(Input, ColonPos + 1, Length(Input) - ColonPos);
  end
  else
  begin
    HostOut := Input;
    PortOut := '8787'; // ไม่ใส่ port มา ใช้ default
  end;
end;

// ---- ปุ่ม Test Connection: เช็คว่า port เปิดรับ connection ไหม (ผ่าน PowerShell Test-NetConnection) ----
function TestServerConnection(const HostPort: String): Boolean;
var
  HostPart, PortPart: String;
  ResultCode: Integer;
  Command: String;
begin
  SplitHostPort(HostPort, HostPart, PortPart);

  Command := '-NoProfile -Command "$r = Test-NetConnection -ComputerName ''' + HostPart +
    ''' -Port ' + PortPart +
    ' -InformationLevel Quiet -WarningAction SilentlyContinue; if ($r) { exit 0 } else { exit 1 }"';

  Result := Exec('powershell.exe', Command, '', SW_HIDE, ewWaitUntilTerminated, ResultCode)
            and (ResultCode = 0);
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  TestOk: Boolean;
  Proceed: Integer;
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

  if CurPageID = ServerConfigPage.ID then
  begin
    if Trim(ServerConfigPage.Values[0]) = '' then
    begin
      MsgBox('Please enter the Server address (IP:Port).', mbError, MB_OK);
      Result := False;
      Exit;
    end;

    TestOk := TestServerConnection(ServerConfigPage.Values[0]);

    if TestOk then
      MsgBox('Connection successful! The Server is reachable.', mbInformation, MB_OK)
    else
    begin
      Proceed := MsgBox(
        'Could not reach the Server at this address.' + #13#10 +
        'This may be normal if the Server is not running yet, or firewall is blocking it.' + #13#10#13#10 +
        'Continue installation anyway with this address?',
        mbConfirmation, MB_YESNO);
      if Proceed = IDNO then
      begin
        Result := False;
        Exit;
      end;
    end;
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

// ---- Helper: แก้ "BaseUrl": "http://localhost:8787" ในไฟล์ appsettings.json ที่ path ที่กำหนด
// ให้เป็น NewBaseUrl - ใช้ร่วมกันทั้ง Client (Agent Service) และ ClientTray (login window)
// เพราะทั้งสองมี appsettings.json แยกไฟล์กันคนละชุด ไม่ใช้ค่าเดียวกันอัตโนมัติ
procedure PatchServerBaseUrl(const AppSettingsPath, NewBaseUrl: String);
var
  FileContentAnsi: AnsiString;
  FileContentUnicode: String;
begin
  if LoadStringFromFile(AppSettingsPath, FileContentAnsi) then
  begin
    FileContentUnicode := String(FileContentAnsi);
    StringChangeEx(FileContentUnicode, '"BaseUrl": "http://localhost:8787"',
      '"BaseUrl": "' + NewBaseUrl + '"', True);
    FileContentAnsi := AnsiString(FileContentUnicode);
    SaveStringToFile(AppSettingsPath, FileContentAnsi, False);
  end;
end;

// ---- หลัง install เสร็จ (เฉพาะ Mode C): เขียน Server address ที่กรอกไว้ลง appsettings.json ----
// สำคัญ: create/patch config/start service ทั้งหมดต้องทำ "ในนี้" ตามลำดับนี้เท่านั้น ห้ามแยก
// "sc.exe start OnionAgent" ไปเป็น [Run] entry ต่างหากอีก เพราะเคยเจอ race condition จริงตอน
// deploy: [Run] กับ CurStepChanged(ssPostInstall) ไม่การันตีลำดับก่อนหลังกัน ทำให้ service
// เคย start ไปก่อนไฟล์ appsettings.json จะถูกแก้ BaseUrl เสร็จ - agent เลยอ่านค่า default
// (localhost) ไปใช้รอบแรก กว่าจะได้ค่าที่ถูกต้องต้อง restart service เองอีกรอบ
procedure CurStepChanged(CurStep: TSetupStep);
var
  NewBaseUrl: String;
  ResultCode: Integer;
begin
  if (CurStep = ssPostInstall) and ShouldInstallClient then
  begin
    NewBaseUrl := 'http://' + ServerConfigPage.Values[0];

    // 1) สร้าง service ก่อน (ยังไม่ start)
    Exec('sc.exe',
      'create OnionAgent binPath= "' + ExpandConstant('{app}\Client\OnionProcOparetor.Agent.exe') + '" start= auto',
      '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

    // 2) แก้ appsettings.json ของทั้ง Client (Agent Service) และ ClientTray (login window) -
    // สองไฟล์แยกกันคนละชุด ลืมแก้ตัวใดตัวหนึ่งจะทำให้ Tray login เข้า Server ไม่ได้ทั้งที่ Service
    // ทำงานถูกต้องแล้ว (เจอปัญหานี้จริง - Tray appsettings.json ไม่เคยถูกแก้เลยตั้งแต่แรก)
    PatchServerBaseUrl(ExpandConstant('{app}\Client\appsettings.json'), NewBaseUrl);
    PatchServerBaseUrl(ExpandConstant('{app}\ClientTray\appsettings.json'), NewBaseUrl);

    // 3) ตั้งค่า recovery แล้วค่อย start เป็นลำดับสุดท้าย - รับประกันว่า config ถูกแล้วแน่นอน
    Exec('sc.exe', 'failure OnionAgent reset= 86400 actions= restart/0/restart/0/restart/0',
      '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Exec('sc.exe', 'start OnionAgent', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  end;
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

const
  UninstallPassword = '036439339';  // เปลี่ยนเป็นรหัสที่ต้องการก่อน build จริง

function AskUninstallPassword(): Boolean;
var
  PasswordForm: TSetupForm;
  PasswordEdit: TPasswordEdit;
  PromptLabel: TNewStaticText;
  OKButton, CancelButton: TNewButton;
  ModalResultValue: Integer;
begin
  Result := False;

  PasswordForm := CreateCustomForm();
  try
    PasswordForm.ClientWidth := ScaleX(320);
    PasswordForm.ClientHeight := ScaleY(140);
    PasswordForm.Caption := 'ยืนยันการถอนการติดตั้ง';
    PasswordForm.Position := poScreenCenter;
    PasswordForm.BorderStyle := bsDialog;

    PromptLabel := TNewStaticText.Create(PasswordForm);
    PromptLabel.Parent := PasswordForm;
    PromptLabel.Left := ScaleX(16);
    PromptLabel.Top := ScaleY(16);
    PromptLabel.Width := PasswordForm.ClientWidth - ScaleX(32);
    PromptLabel.AutoSize := True;
    PromptLabel.WordWrap := True;
    PromptLabel.Caption := 'กรุณากรอกรหัสผ่านของผู้ดูแลระบบเพื่อถอนการติดตั้ง Onion ProcOparetor:';

    PasswordEdit := TPasswordEdit.Create(PasswordForm);
    PasswordEdit.Parent := PasswordForm;
    PasswordEdit.Left := ScaleX(16);
    PasswordEdit.Top := PromptLabel.Top + PromptLabel.Height + ScaleY(12);
    PasswordEdit.Width := PasswordForm.ClientWidth - ScaleX(32);
    //PasswordEdit.PasswordChar := '*';

    OKButton := TNewButton.Create(PasswordForm);
    OKButton.Parent := PasswordForm;
    OKButton.Width := ScaleX(75);
    OKButton.Height := ScaleY(23);
    OKButton.Top := PasswordEdit.Top + PasswordEdit.Height + ScaleY(16);
    OKButton.Left := PasswordForm.ClientWidth - ScaleX(16) - ScaleX(75) - ScaleX(8) - ScaleX(75);
    OKButton.Caption := 'ตกลง';
    OKButton.ModalResult := mrOk;
    OKButton.Default := True;

    CancelButton := TNewButton.Create(PasswordForm);
    CancelButton.Parent := PasswordForm;
    CancelButton.Width := ScaleX(75);
    CancelButton.Height := ScaleY(23);
    CancelButton.Top := OKButton.Top;
    CancelButton.Left := PasswordForm.ClientWidth - ScaleX(16) - ScaleX(75);
    CancelButton.Caption := 'ยกเลิก';
    CancelButton.ModalResult := mrCancel;
    CancelButton.Cancel := True;

    PasswordForm.ActiveControl := PasswordEdit;

    ModalResultValue := PasswordForm.ShowModal();

    if ModalResultValue = mrOk then
    begin
      if PasswordEdit.Text = UninstallPassword then
        Result := True
      else
        MsgBox('รหัสผ่านไม่ถูกต้อง ยกเลิกการถอนการติดตั้ง', mbError, MB_OK);
    end;
  finally
    PasswordForm.Free;
  end;
end;

function InitializeUninstall(): Boolean;
begin
  Result := AskUninstallPassword();
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
; หมายเหตุ: OnionAgent create/patch-config/start/failure ทั้งหมดถูกย้ายไปทำใน CurStepChanged
; (ssPostInstall) ใน [Code] ด้านบนแล้ว เพื่อรับประกันลำดับ create -> patch config -> start
; ไม่ให้เกิด race condition ที่ service start ไปก่อน appsettings.json จะถูกแก้ค่า BaseUrl เสร็จ
Filename: "{app}\ClientTray\OnionProcOparetor.AgentTray.exe"; Description: "Launch Onion ProcOparetor Tray"; Flags: postinstall nowait skipifsilent; Check: ShouldInstallClient
Filename: "{app}\Console\OnionProcOparetor.Console.exe"; Description: "Launch Onion ProcOparetor Console"; Flags: postinstall nowait skipifsilent; Check: ShouldInstallConsole

[UninstallRun]
Filename: "sc.exe"; Parameters: "stop OnionCoreService"; Flags: runhidden; RunOnceId: "StopServerService"
Filename: "sc.exe"; Parameters: "delete OnionCoreService"; Flags: runhidden; RunOnceId: "DeleteServerService"
Filename: "sc.exe"; Parameters: "stop OnionAgent"; Flags: runhidden; RunOnceId: "StopClientService"
Filename: "sc.exe"; Parameters: "delete OnionAgent"; Flags: runhidden; RunOnceId: "DeleteClientService"
