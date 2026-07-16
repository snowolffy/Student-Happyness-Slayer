; MyLabGuard Installer - รองรับ 3 โหมดติดตั้งจาก installer เดียว
; ตามที่ออกแบบไว้ใน docs/architecture.md:
;   Mode A: Server + Console (เครื่อง server)
;   Mode B: Console only (เครื่องครู/แอดมิน)
;   Mode C: Client Agent (เครื่องนักเรียน)
;
; ก่อน compile ต้อง publish ทั้ง 4 โปรเจกต์ไว้ที่ publish/Server, publish/Console,
; publish/Client, publish/ClientTray ก่อน (ดูคำสั่ง dotnet publish ที่คุยกันไว้)
;
; วางไฟล์นี้ไว้ที่ installer/MyLabGuard.iss (จะได้ path สัมพัทธ์ ..\publish\... ถูกต้อง)

#define MyAppName "MyLabGuard"
#define MyAppVersion "1.0"
#define MyAppPublisher "MyLabGuard Project"

[Setup]
AppId={{A1B2C3D4-5E6F-47A8-9B0C-1D2E3F4A5B6C}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=.\output
OutputBaseFilename=MyLabGuard-Setup
Compression=lzma
SolidCompression=yes
WizardStyle=modern
; ต้อง Administrator เสมอ เพราะ Mode A/C ต้อง install Windows Service
PrivilegesRequired=admin
ArchitecturesInstallIn64BitMode=x64

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

; ---- ตัวแปรเก็บโหมดที่ user เลือกจากหน้า custom wizard page ----
[Code]
var
  ModePage: TWizardPage;
  RadioModeA, RadioModeB, RadioModeC: TRadioButton;
  SelectedMode: Integer; // 1 = Mode A, 2 = Mode B, 3 = Mode C

procedure InitializeWizard;
begin
  ModePage := CreateCustomPage(wpSelectDir, 'Select Installation Mode',
    'MyLabGuard supports 3 modes. Choose the one that matches this machine.');

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

// ---- ฟังก์ชันเช็คว่าควร install ไฟล์กลุ่มไหน อ้างอิงจาก SelectedMode ----
// ใช้ผูกกับ Check: parameter ใน [Files] section ด้านล่าง

function ShouldInstallServer: Boolean;
begin
  Result := (SelectedMode = 1); // เฉพาะ Mode A
end;

function ShouldInstallConsole: Boolean;
begin
  Result := (SelectedMode = 1) or (SelectedMode = 2); // Mode A หรือ B
end;

function ShouldInstallClient: Boolean;
begin
  Result := (SelectedMode = 3); // เฉพาะ Mode C
end;

// ---- Uninstall: ถามก่อนว่าจะลบข้อมูล (DB, logs) ด้วยไหม หรือแค่ลบโปรแกรมเก็บข้อมูลไว้ ----
// เก็บผลไว้ใน global variable นี้ ใช้ใน CurUninstallStepChanged ด้านล่าง
var
  DeleteAllData: Boolean;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ProgramDataPath: String;
begin
  if CurUninstallStep = usUninstall then
  begin
    // ถามตอนเริ่ม uninstall (ก่อนลบไฟล์ใดๆ) - MsgBox คืนค่า IDYES/IDNO
    DeleteAllData := (MsgBox(
      'Do you also want to delete all MyLabGuard data (database, logs, rules)?' + #13#10 +
      'Choose No if you plan to reinstall later and want to keep your existing data.',
      mbConfirmation, MB_YESNO) = IDYES);
  end;

  if CurUninstallStep = usPostUninstall then
  begin
    if DeleteAllData then
    begin
      // ลบข้อมูลทั้งหมดที่ %ProgramData%\MyLabGuard\ (DB, logs) - ทำหลัง uninstall โปรแกรมเสร็จแล้ว
      ProgramDataPath := ExpandConstant('{commonappdata}\MyLabGuard');
      if DirExists(ProgramDataPath) then
        DelTree(ProgramDataPath, True, True, True);

      // ลบ Registry Run key ของ Tray ด้วย (HKCU) - กัน key ค้างชี้ไปยัง exe ที่ถูกลบไปแล้ว
      RegDeleteValue(HKEY_CURRENT_USER, 'SOFTWARE\Microsoft\Windows\CurrentVersion\Run', 'MyLabGuardClientTray');
    end;
  end;
end;

[Files]
; ---- Server (Mode A เท่านั้น) ----
Source: "..\publish\Server\*"; DestDir: "{app}\Server"; Flags: ignoreversion recursesubdirs createallsubdirs; Check: ShouldInstallServer

; ---- Console (Mode A หรือ B) ----
Source: "..\publish\Console\*"; DestDir: "{app}\Console"; Flags: ignoreversion recursesubdirs createallsubdirs; Check: ShouldInstallConsole

; ---- Client + ClientTray (Mode C เท่านั้น) ----
Source: "..\publish\Client\*"; DestDir: "{app}\Client"; Flags: ignoreversion recursesubdirs createallsubdirs; Check: ShouldInstallClient
Source: "..\publish\ClientTray\*"; DestDir: "{app}\ClientTray"; Flags: ignoreversion recursesubdirs createallsubdirs; Check: ShouldInstallClient

; ---- Helper script (Mode A เท่านั้น) - เรียก /api/auth/setup ให้อัตโนมัติหลัง Server start ----
Source: "run-setup.ps1"; DestDir: "{app}"; Flags: ignoreversion; Check: ShouldInstallServer

[Icons]
; ---- Shortcut ของ Console (Mode A หรือ B) ----
Name: "{group}\MyLabGuard Console"; Filename: "{app}\Console\MyLabGuard.Console.exe"; Check: ShouldInstallConsole

; ---- Shortcut ของ ClientTray ใน Startup folder (Mode C) ----
; หมายเหตุ: Tray เองมี RegistryStartup.EnsureAutoStartEnabled() เขียน HKCU Run key
; ให้อัตโนมัติอยู่แล้วตอนเปิดครั้งแรก แต่ยังใส่ shortcut ใน Start Menu ไว้ด้วย
; เผื่อ user อยากเปิดเองตอนไหนก็ได้ (ไม่ได้ผูกกับ auto-start logic ที่โค้ดทำอยู่)
Name: "{group}\MyLabGuard Tray"; Filename: "{app}\ClientTray\MyLabGuard.ClientTray.exe"; Check: ShouldInstallClient

[Run]
; ---- Mode A: install Server เป็น Windows Service หลัง copy ไฟล์เสร็จ ----
Filename: "sc.exe"; Parameters: "create MyLabGuardServer binPath= ""{app}\Server\MyLabGuard.Server.exe"" start= auto"; Flags: runhidden; Check: ShouldInstallServer
Filename: "sc.exe"; Parameters: "start MyLabGuardServer"; Flags: runhidden; Check: ShouldInstallServer

; ---- Mode A: เรียก /api/auth/setup ให้อัตโนมัติหลัง Server start เสร็จ ----
; สำคัญ: ต้องรอ service พร้อมรับ request ก่อน ใช้ retry loop สั้นๆ ผ่าน PowerShell แทนยิงครั้งเดียว
; เพราะ sc.exe start คืนค่าทันทีที่สั่ง start ไม่ได้รอจน server เปิด port รับ connection จริง
; แยกเป็นไฟล์ run-setup.ps1 ต่างหาก (ดู [Files] ด้านล่าง) แทนที่จะ inline ใน Parameters ตรงนี้
; เพราะ inline PowerShell ยาวๆ เสี่ยงเรื่อง escape อักขระพิเศษกับ Inno Setup syntax สูงมาก
Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\run-setup.ps1"""; Flags: runhidden; Check: ShouldInstallServer

; ---- Mode C: install Client เป็น Windows Service หลัง copy ไฟล์เสร็จ ----
Filename: "sc.exe"; Parameters: "create MyLabGuardClient binPath= ""{app}\Client\MyLabGuard.Client.exe"" start= auto"; Flags: runhidden; Check: ShouldInstallClient
Filename: "sc.exe"; Parameters: "start MyLabGuardClient"; Flags: runhidden; Check: ShouldInstallClient
; ตั้ง Service Recovery: restart อัตโนมัติทันทีเมื่อ crash (เหมือนที่ install-client-service.ps1 เดิมทำ)
Filename: "sc.exe"; Parameters: "failure MyLabGuardClient reset= 86400 actions= restart/0/restart/0/restart/0"; Flags: runhidden; Check: ShouldInstallClient

; ---- เปิด Tray ทันทีหลัง install เสร็จ (Mode C) ----
Filename: "{app}\ClientTray\MyLabGuard.ClientTray.exe"; Description: "Launch MyLabGuard Tray"; Flags: postinstall nowait skipifsilent; Check: ShouldInstallClient

; ---- เปิด Console ทันทีหลัง install เสร็จ (Mode A หรือ B) ----
Filename: "{app}\Console\MyLabGuard.Console.exe"; Description: "Launch MyLabGuard Console"; Flags: postinstall nowait skipifsilent; Check: ShouldInstallConsole

[UninstallRun]
; ---- ถอน Windows Service ตอน uninstall (ต้องหยุด + ลบ service ก่อนลบไฟล์) ----
Filename: "sc.exe"; Parameters: "stop MyLabGuardServer"; Flags: runhidden; RunOnceId: "StopServerService"
Filename: "sc.exe"; Parameters: "delete MyLabGuardServer"; Flags: runhidden; RunOnceId: "DeleteServerService"
Filename: "sc.exe"; Parameters: "stop MyLabGuardClient"; Flags: runhidden; RunOnceId: "StopClientService"
Filename: "sc.exe"; Parameters: "delete MyLabGuardClient"; Flags: runhidden; RunOnceId: "DeleteClientService"

; No [UninstallDelete] entries needed - registry and data cleanup are handled
; directly in [Code]'s CurUninstallStepChanged, gated on the DeleteAllData choice.
