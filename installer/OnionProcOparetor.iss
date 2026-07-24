; OnionProcOparetor Installer - supports 3 installation modes
#define MyAppName "Onion ProcOparetor"
#define MyAppVersion "1.1"
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
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Code]
var
  ModePage: TWizardPage;
  RadioModeA, RadioModeB, RadioModeC: TRadioButton;
  SelectedMode: Integer;
  ServerConfigPage: TInputQueryWizardPage;
  UninstallPasswordPage: TInputQueryWizardPage;

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

  // ---- หน้าใหม่: ตั้ง uninstall password (optional) เฉพาะ Mode C (Client Agent) ----
  // เฉพาะ Mode C เท่านั้น เพราะ threat model ของฟีเจอร์นี้คือ "นักเรียนใช้ shared admin account
  // บนเครื่อง lab เปิด Control Panel ถอนโปรแกรมเอง" - Server/Console เป็นเครื่องของครู/IT ไม่ใช่
  // เป้าหมายของ threat นี้ ไม่จำเป็นต้องมี gate นี้
  UninstallPasswordPage := CreateInputQueryPage(ServerConfigPage.ID,
    'Uninstall Protection (Optional)',
    'Set an uninstall password for this client machine',
    'Lab machines share one admin account, so any student can normally remove this program ' +
    'from Control Panel. Setting a password here will require it before the program can be ' +
    'uninstalled. This is optional - leave both fields blank to skip.'#13#10#13#10 +
    'Note: this only blocks the normal uninstaller. A student with admin access can still stop ' +
    'the service directly (Task Manager / services.msc) - use the Console''s "Missing ' +
    'Unexpectedly" alert to catch that case.');
  UninstallPasswordPage.Add('Uninstall password (leave blank to skip):', True);
  UninstallPasswordPage.Add('Confirm password:', True);
end;

// ---- ซ่อนหน้า ServerConfigPage/UninstallPasswordPage ถ้าไม่ได้เลือก Mode C ----
function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := False;
  if PageID = ServerConfigPage.ID then
    Result := (SelectedMode <> 3)
  else if PageID = UninstallPasswordPage.ID then
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

  if CurPageID = UninstallPasswordPage.ID then
  begin
    if UninstallPasswordPage.Values[0] <> UninstallPasswordPage.Values[1] then
    begin
      MsgBox('Passwords do not match. Please re-enter, or leave both fields blank to skip.', mbError, MB_OK);
      Result := False;
      Exit;
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

// ---- สร้าง salt แบบสุ่มพอสมควร (ไม่ต้อง cryptographically secure - แค่ทำให้ hash ไม่ซ้ำกัน
// ต่อเครื่อง กัน rainbow table แบบง่ายๆ) แล้ว hash ด้วย SHA256 ผ่าน Inno Setup built-in
// GetSHA256OfUnicodeString (มีมาตั้งแต่ Inno Setup 6.1) เก็บ salt+hash ลงไฟล์ที่ {app} -
// ไม่ใช่ PBKDF2 แบบ PasswordHasher.cs ฝั่ง Server เพราะ Inno Setup Pascal Script ไม่มี PBKDF2
// built-in และไม่อยากให้ installer ต้อง shell out ไปพึ่ง Agent.exe แค่เพื่อ hash รหัสผ่าน
// (เพิ่มจุดที่ install จะ fail ได้โดยไม่จำเป็น) SHA256 + salt สุ่มต่อเครื่องเพียงพอสำหรับ
// threat model นี้ (กันนักเรียนเปิด Control Panel ถอนโปรแกรมเอง ไม่ใช่กัน nation-state attacker)
procedure SaveUninstallGuard(const Password: String);
var
  Salt, Hash, GuardJson, GuardPath: String;
begin
  Salt := GetDateTimeString('yyyymmddhhnnss', '-', '-') + '-' +
    IntToStr(Random(2147483647)) + '-' + IntToStr(Random(2147483647));
  Hash := GetSHA256OfUnicodeString(Salt + Password);

  GuardJson := '{"salt":"' + Salt + '","hash":"' + Hash + '"}';
  GuardPath := ExpandConstant('{app}\uninstall-guard.json');
  SaveStringToFile(GuardPath, GuardJson, False);
end;

// ---- อ่าน guard file กลับมาตอน uninstall - format คุมเองทั้งหมด (เขียนจาก SaveUninstallGuard
// ด้านบนเท่านั้น) เลย parse ด้วย Pos/Copy ธรรมดาได้ ไม่ต้องพึ่ง JSON parser จริง (salt/hash
// เป็น digit/hex/hyphen ล้วน ไม่มีอักขระที่ต้อง escape) ----
function TryLoadUninstallGuard(var Salt, Hash: String): Boolean;
var
  RawFile: AnsiString;
  Json: String;
  SaltStart, SaltEnd, HashStart, HashEnd: Integer;
begin
  Result := False;

  if not LoadStringFromFile(ExpandConstant('{app}\uninstall-guard.json'), RawFile) then
    Exit; // ไม่เคยตั้ง uninstall password ไว้ (ไฟล์ไม่มีเลย) - ถือว่าไม่ต้อง gate

  Json := String(RawFile);

  SaltStart := Pos('"salt":"', Json);
  HashStart := Pos('"hash":"', Json);
  if (SaltStart = 0) or (HashStart = 0) then
    Exit;

  SaltStart := SaltStart + Length('"salt":"');
  SaltEnd := Pos('"', Copy(Json, SaltStart, Length(Json) - SaltStart + 1));
  if SaltEnd = 0 then Exit;
  Salt := Copy(Json, SaltStart, SaltEnd - 1);

  HashStart := HashStart + Length('"hash":"');
  HashEnd := Pos('"', Copy(Json, HashStart, Length(Json) - HashStart + 1));
  if HashEnd = 0 then Exit;
  Hash := Copy(Json, HashStart, HashEnd - 1);

  Result := (Salt <> '') and (Hash <> '');
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

    // 4) ถ้าผู้ติดตั้งกรอก uninstall password ไว้ (ไม่ได้ skip) - hash แล้วเก็บลง guard file
    if Trim(UninstallPasswordPage.Values[0]) <> '' then
    begin
      SaveUninstallGuard(UninstallPasswordPage.Values[0]);
    end;
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

    // ลบ guard file ก่อนเสมอ (ไม่ผูกกับ DeleteAllData) - เป็นไฟล์ของตัวติดตั้งเอง ไม่ใช่ user
    // data แบบ database/logs/rules และไม่ได้อยู่ใน [Files] section ที่ Inno track อัตโนมัติ
    // ถ้าไม่ลบเอง จะค้างอยู่ใน {app} แล้วกัน Inno Setup ลบโฟลเดอร์ {app} ทิ้งตอนจบ (เพราะเห็นว่า
    // ยังไม่ว่างเปล่า)
    DeleteFile(ExpandConstant('{app}\uninstall-guard.json'));
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

// ถาม uninstall password จริง - เทียบกับ Salt/ExpectedHash ที่อ่านมาจาก guard file (เขียนไว้
// ตอน install โดย SaveUninstallGuard) ไม่มี hardcoded password ใดๆ ในซอร์สหรือ compiled
// installer อีกต่อไป (ของเดิมมี password constant ฝังตรงในซอร์ส .iss นี้ - เป็นสาเหตุหลักที่
// Microsoft Defender ยืนยันว่า installer นี้ meets criteria for malware)
function AskUninstallPassword(const Salt, ExpectedHash: String): Boolean;
var
  PasswordForm: TSetupForm;
  PasswordEdit: TPasswordEdit;
  PromptLabel: TNewStaticText;
  OKButton, CancelButton: TNewButton;
  ModalResultValue: Integer;
begin
  Result := False;

  // ตั้งแต่ Inno Setup 6.6.0 เป็นต้นมา CreateCustomForm ต้องระบุ ClientWidth/ClientHeight
  // (+ KeepSizeX/KeepSizeY) เป็น parameter ตอนสร้างเลย แก้เป็น property ทีหลังไม่ได้อีกต่อไป
  // (ของเดิมเขียนไว้ตาม API เก่าก่อน 6.6.0 ที่ยังเป็น property เขียนได้ - พังกับ compiler 6.7.3 ที่ใช้จริงตอนนี้)
  PasswordForm := CreateCustomForm(ScaleX(320), ScaleY(140), False, False);
  try
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
      if GetSHA256OfUnicodeString(Salt + PasswordEdit.Text) = ExpectedHash then
        Result := True
      else
        MsgBox('รหัสผ่านไม่ถูกต้อง ยกเลิกการถอนการติดตั้ง', mbError, MB_OK);
    end;
  finally
    PasswordForm.Free;
  end;
end;

function InitializeUninstall(): Boolean;
var
  Salt, Hash: String;
begin
  if TryLoadUninstallGuard(Salt, Hash) then
    Result := AskUninstallPassword(Salt, Hash)
  else
    Result := True; // ไม่เคยตั้ง uninstall password ไว้ตอน install (เลือก skip) - uninstall ได้ปกติไม่ต้องถาม
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
; เปิด inbound port 8787 ผ่าน Windows Firewall - จำเป็นเพราะ OnionCoreService รันเป็น Windows
; Service ไม่ใช่ interactive exe เลยไม่มี prompt "Allow access?" แบบที่ Windows โชว์ให้ตอน
; โปรแกรมทั่วไปเปิด port ครั้งแรก (ต่างจาก exe ที่ user ดับเบิลคลิกเปิดเอง) ถ้าไม่เปิด rule นี้
; ให้เอง ทั้ง HTTP poll เดิมและ SignalR ใหม่จาก Agent/Console เครื่องอื่นจะต่อเข้ามาไม่ได้เลย
; ไม่ throw error ให้ install ล้มเหลวถ้า netsh error (เช่น Firewall service ปิดอยู่) - Inno Setup
; ไม่เช็ค exit code ของ [Run] entry อยู่แล้วโดย default (ต่างจาก flags อื่นที่เช็ค เช่น file exists)
Filename: "netsh.exe"; Parameters: "advfirewall firewall add rule name=""OnionProcOparetor Server"" dir=in action=allow protocol=TCP localport=8787"; Flags: runhidden; Check: ShouldInstallServer
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
; ลบ firewall rule คู่กับตอนติดตั้ง - ไม่ gate ด้วย Check (เหมือน sc.exe entries ด้านบน) เพราะ
; wizard state (SelectedMode) ไม่ได้ set ระหว่าง uninstall flow อยู่แล้ว - netsh delete เป็น
; harmless no-op เองถ้า rule นี้ไม่เคยถูกสร้างไว้ตั้งแต่แรก (เช่น ติดตั้งโหมด Client/Console-only)
Filename: "netsh.exe"; Parameters: "advfirewall firewall delete rule name=""OnionProcOparetor Server"""; Flags: runhidden; RunOnceId: "DeleteFirewallRule"
