# Full Rename Plan: MyLabGuard → Onion ProcOparetor
> ✅ **สถานะ: เสร็จสมบูรณ์แล้วเมื่อ 17 ก.ค. 2026** — เก็บไฟล์นี้ไว้เป็น reference ประวัติการ rename เท่านั้น

> สร้างไว้เป็น reference ให้ทำตามบนเครื่อง dev จริง (`F:\GitProject\StudentHappynessSlayer`)
> ใช้ IDE "Rename Symbol" เป็นหลัก ไม่ใช้ find-replace ข้อความตรงๆ (ตามที่บันทึกไว้ใน architecture.md)
> ทำเป็น**เซสชันแยกต่างหาก** ไม่ปนกับ feature work อื่น

## การตัดสินใจที่ fix แล้ว (จากบทสนทนา)

| จุด | การตัดสินใจ |
|---|---|
| Registry key `ClientGuid` | เปลี่ยนเป็น `HKLM\SOFTWARE\OnionProcOparetor\ClientGuid` (ไม่มีเครื่องจริงติดตั้ง ไม่ต้อง backward-compat) |
| DB path | เปลี่ยนเป็น `%ProgramData%\OnionProcOparetor\onionprocoparetor.db` (DB ปัจจุบันมีแค่ test data ลบทิ้งได้) |
| Windows Service names | `MyLabGuardServer` → `OnionCoreService`, `MyLabGuardClient` → `OnionAgent` |
| Backward compat กับเครื่องเก่า | **ไม่ต้อง** — ยังไม่เคยติดตั้งจริง ไม่มี data ที่ต้อง migrate |

## Mapping ชื่อเต็ม

| เดิม | ใหม่ |
|---|---|
| `MyLabGuard.Server` (.csproj/namespace/โฟลเดอร์) | `OnionProcOparetor.Server` |
| `MyLabGuard.Console` | `OnionProcOparetor.Console` |
| `MyLabGuard.Client` | `OnionProcOparetor.Agent` |
| `MyLabGuard.ClientTray` | `OnionProcOparetor.AgentTray` |
| `MyLabGuard.Shared` | `OnionProcOparetor.Shared` |
| Service: `MyLabGuardServer` | `OnionCoreService` |
| Service: `MyLabGuardClient` | `OnionAgent` |
| Registry: `HKLM\SOFTWARE\MyLabGuard\ClientGuid` | `HKLM\SOFTWARE\OnionProcOparetor\ClientGuid` |
| Registry: `HKCU\...\Run\MyLabGuardClientTray` (value name) | `HKCU\...\Run\OnionProcOparetorAgentTray` |
| DB: `%ProgramData%\MyLabGuard\mylabguard.db` | `%ProgramData%\OnionProcOparetor\onionprocoparetor.db` |
| Solution file `MyLabGuard.sln` | `OnionProcOparetor.sln` |
| Installer output: `MyLabGuard-Setup.exe`, AppName | `OnionProcOparetor-Setup.exe`, `Onion ProcOparetor` |
| GitHub repo name | **ไม่เปลี่ยน** (`Student-Happyness-Slayer` ตามเดิม ตาม README ปัจจุบัน) |

---

## ขั้นตอนแนะนำ (ทำตามลำดับ — แต่ละขั้น build ให้ผ่านก่อนไปขั้นถัดไป)

### 0. เตรียมตัว
- [ ] commit/push โค้ดปัจจุบันก่อนเริ่ม (checkpoint กันพลาด)
- [ ] ปิด Visual Studio/Rider ที่เปิด solution ค้างไว้ก่อนย้ายโฟลเดอร์ (กัน lock file)
- [ ] ลบ `mylabguard.db` เดิมทิ้งได้เลย (ตัดสินใจแล้วว่าไม่ migrate data)

### 1. Rename โฟลเดอร์โปรเจกต์ + .csproj + .sln
- [ ] `src/MyLabGuard.Server/` → `src/OnionProcOparetor.Server/`
- [ ] `src/MyLabGuard.Console/` → `src/OnionProcOparetor.Console/`
- [ ] `src/MyLabGuard.Client/` → `src/OnionProcOparetor.Agent/`
- [ ] `src/MyLabGuard.ClientTray/` → `src/OnionProcOparetor.AgentTray/`
- [ ] `src/MyLabGuard.Shared/` → `src/OnionProcOparetor.Shared/`
- [ ] ไฟล์ `.csproj` แต่ละตัว rename ตามโปรเจกต์ (เช่น `MyLabGuard.Server.csproj` → `OnionProcOparetor.Server.csproj`)
- [ ] `MyLabGuard.sln` → `OnionProcOparetor.sln` — แก้ project references ข้างในให้ตรง path ใหม่
- [ ] แก้ `UserSecretsId` ใน `.csproj` (Server, Agent) — สร้าง GUID ใหม่หรือคงเดิมก็ได้ (ไม่กระทบ prod)

### 2. Rename Namespace (ใช้ IDE Rename Symbol เท่านั้น)
- [ ] เปิด solution ใหม่ (path ใหม่) ใน Visual Studio/Rider
- [ ] ใช้ "Rename Symbol"/"Rename Namespace" ไล่ทีละโปรเจกต์:
  - `MyLabGuard.Server.*` → `OnionProcOparetor.Server.*`
  - `MyLabGuard.Console.*` → `OnionProcOparetor.Console.*`
  - `MyLabGuard.Client.*` → `OnionProcOparetor.Agent.*`
  - `MyLabGuard.ClientTray.*` → `OnionProcOparetor.AgentTray.*`
- [ ] เช็ค `.xaml` files ด้วยมือ — namespace ใน `x:Class` และ `xmlns:local` ไม่โดน IDE rename อัตโนมัติเสมอไป ต้องแก้เอง:
  - `DashboardWindow.xaml`, `MainWindow.xaml` (ทั้ง Console และ ClientTray), `ForceChangePasswordWindow.xaml`, `LoginWindow.xaml`, `StatusWindow.xaml`, `App.xaml` (ทั้งสองโปรเจกต์)
- [ ] build ทั้ง solution ให้ผ่าน ไม่มี error ค้าง

### 3. Windows Service names
- [ ] `src/OnionProcOparetor.Server/Program.cs` — ไม่มีการ hardcode ชื่อ service ในโค้ด (ชื่อ service มาจาก `sc.exe create` ตอน install) — ไม่ต้องแก้จุดนี้
- [ ] `installer/MyLabGuard.iss` (จะ rename เป็น `OnionProcOparetor.iss` ด้วย — ดูขั้นตอน 6):
  - `sc.exe create MyLabGuardServer` → `sc.exe create OnionCoreService`
  - `sc.exe create MyLabGuardClient` → `sc.exe create OnionAgent`
  - ทุกจุดที่อ้าง `MyLabGuardServer`/`MyLabGuardClient` ใน `[Run]` และ `[UninstallRun]`

### 4. Registry keys
- [ ] `src/OnionProcOparetor.Agent/Services/ClientIdentity.cs`:
  ```csharp
  private const string RegistryPath = @"SOFTWARE\OnionProcOparetor";
  ```
- [ ] `src/OnionProcOparetor.AgentTray/Services/RegistryStartup.cs`:
  ```csharp
  private const string ValueName = "OnionProcOparetorAgentTray";
  ```
- [ ] `installer/OnionProcOparetor.iss` — จุดที่ `RegDeleteValue` ตอน uninstall ต้องใช้ value name ใหม่ด้วย:
  ```
  RegDeleteValue(HKEY_CURRENT_USER, 'SOFTWARE\Microsoft\Windows\CurrentVersion\Run', 'OnionProcOparetorAgentTray');
  ```

### 5. DB path + connection string
- [ ] `src/OnionProcOparetor.Server/appsettings.json`:
  ```json
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=C:\\ProgramData\\OnionProcOparetor\\onionprocoparetor.db"
  }
  ```
- [ ] `src/OnionProcOparetor.Server/Data/AppDbContext.cs` — comment อ้างถึง path เดิม ต้องอัพเดต comment ด้วย (ไม่กระทบ logic)
- [ ] **ลบ migration เดิมทิ้งแล้วสร้างใหม่** — เพราะ namespace ของ `AppDbContext`/Models เปลี่ยน, migration snapshot จะอ้างชื่อเก่า:
  ```powershell
  # ลบโฟลเดอร์ Migrations/ ทิ้งทั้งหมด
  Remove-Item src/OnionProcOparetor.Server/Migrations -Recurse -Force
  # ลบ DB เดิมทิ้ง (ไม่ migrate data ตามที่ตกลง)
  Remove-Item "C:\ProgramData\MyLabGuard" -Recurse -Force -ErrorAction SilentlyContinue
  cd src/OnionProcOparetor.Server
  dotnet ef migrations add InitialCreate
  ```
- [ ] เช็คใน `Program.cs` ว่า `Directory.CreateDirectory(dbDir)` ยังทำงานถูกกับ path ใหม่ (โค้ด logic เดิมไม่ต้องแก้ แค่ path ใน appsettings.json เปลี่ยนพอ)

### 6. Installer (Inno Setup)
- [ ] rename `installer/MyLabGuard.iss` → `installer/OnionProcOparetor.iss`
- [ ] แก้ `#define MyAppName "MyLabGuard"` → `"Onion ProcOparetor"`
- [ ] แก้ `AppId={{...}}` — **สร้าง GUID ใหม่** (เพราะเป็น product ใหม่ในทางเทคนิค ไม่ควรใช้ AppId เดิมซ้ำ ถ้า user เคยลง MyLabGuard ไว้จะได้ไม่ conflict — แม้จะยังไม่มีเครื่องจริงที่ลงก็ตาม เป็น best practice)
- [ ] `OutputBaseFilename=MyLabGuard-Setup` → `OnionProcOparetor-Setup`
- [ ] `[Files]` section — path `..\publish\Server\*` ฯลฯ อ้างอิงโฟลเดอร์ publish (ดูขั้นตอน 7 — ชื่อโฟลเดอร์ publish จะยังใช้ `Server/Console/Client/ClientTray` เป็น label ภายในได้ ไม่จำเป็นต้องเปลี่ยนตาม เพราะเป็นแค่ output folder name)
- [ ] `sc.exe create` parameters — ชื่อ service (ดูขั้นตอน 3) และ path ไปยัง `.exe` ใหม่:
  ```
  binPath= ""{app}\Server\OnionProcOparetor.Server.exe""
  binPath= ""{app}\Client\OnionProcOparetor.Agent.exe""
  ```
- [ ] `[Icons]` — path ไปยัง `.exe` ใหม่ทั้งหมด (`OnionProcOparetor.Console.exe`, `OnionProcOparetor.AgentTray.exe`)
- [ ] `[Code]` wizard page text — เปลี่ยนคำว่า "MyLabGuard" ในข้อความ UI เป็น "Onion ProcOparetor"
- [ ] `installer/run-setup.ps1` — comment อ้างชื่อเก่า อัพเดตตามสมควร (ไม่กระทบ logic เพราะยิง localhost:8787 เหมือนเดิม)

### 7. UI Text ที่ user เห็นตรงๆ
- [ ] Title ทุก `.xaml` Window: `MainWindow.xaml` (Console), `DashboardWindow.xaml`, `ForceChangePasswordWindow.xaml`, `MainWindow.xaml` (ClientTray), `LoginWindow.xaml`, `StatusWindow.xaml`
- [ ] `src/OnionProcOparetor.AgentTray/App.xaml.cs` — `ToolTipText = "MyLabGuard - กำลังทำงาน"` → `"Onion ProcOparetor - กำลังทำงาน"`
- [ ] `DashboardWindow.xaml` — `TextBlock Text="MyLabGuard"` → `"Onion ProcOparetor"`
- [ ] เพิ่มหน้า **About** ใหม่ (ยังไม่มี) — ใส่ tagline `"Peeling processes since 2026."` ตามที่ตกลงไว้ก่อนหน้า (ถ้าจะทำในรอบนี้ด้วย — ไม่บังคับ อาจแยกเป็น task ถัดไป)
- [ ] `MessageBox.Show` ข้อความ error ต่างๆ ที่มีคำว่า "MyLabGuard Tray Error" ฯลฯ

### 8. Publish commands (อัพเดต reference ในเอกสาร/scripts)
```powershell
dotnet publish src/OnionProcOparetor.Server -c Release -r win-x64 --self-contained false -o publish/Server
dotnet publish src/OnionProcOparetor.Console -c Release -r win-x64 --self-contained false -o publish/Console
dotnet publish src/OnionProcOparetor.Agent -c Release -r win-x64 --self-contained false -o publish/Client
dotnet publish src/OnionProcOparetor.AgentTray -c Release -r win-x64 --self-contained false -o publish/ClientTray
```
(โฟลเดอร์ publish ปลายทางคง `Server/Console/Client/ClientTray` เดิมได้ เพราะ installer .iss อ้างอิงแค่ path ภายใน repo ไม่กระทบ user)

### 9. อัพเดตเอกสาร
- [ ] `README.md` — เปลี่ยน mapping table ให้บอกว่า **rename เสร็จแล้ว** (ไม่ใช่ TODO อีกต่อไป), อัพเดตชื่อโปรเจกต์ในโครงสร้าง repo
- [ ] `docs/architecture.md` — เพิ่ม Update Log entry ใหม่บันทึกว่า rename เสร็จแล้ว วันที่ทำ, ตัดข้อ TODO "Full Rename" ออกจากลิสต์ค้าง

### 10. ทดสอบ regression เต็มรูปแบบ (บังคับ — ความเสี่ยงสูงจากขอบเขตงานนี้)
- [ ] Server: `dotnet run` เริ่มได้ปกติ, DB migrate สร้างตารางใหม่ครบ
- [ ] `/api/auth/setup` สร้าง built-in Administrator ได้
- [ ] Console: login, เปลี่ยน password บังคับ, เข้าหน้า Dashboard, ทุก tab (Clients/Rules/Logs/Users) โหลดข้อมูลได้
- [ ] Agent (Client): รันแบบ console (ไม่ต้อง service ก่อน) เช็คว่า publisher check + poll + WMI watcher ทำงาน, registry key ใหม่ถูกสร้างที่ `HKLM\SOFTWARE\OnionProcOparetor\ClientGuid`
- [ ] AgentTray: เปิดแอป, auto-start registry key ใหม่ถูกเขียนที่ `HKCU\...\Run\OnionProcOparetorAgentTray`, login ผ่าน Server ได้, เห็น status
- [ ] Installer ทั้ง 3 โหมด (A/B/C) — install จริงอย่างน้อย 1 รอบต่อโหมด, เช็คชื่อ service ใหม่ถูกสร้าง (`OnionCoreService`, `OnionAgent`), uninstall ทำงานถูกต้อง (ลบ service + ถาม-ลบ data + ลบ registry Run key ใหม่)

---

## ความเสี่ยงที่ควรระวังเป็นพิเศษ

1. **XAML `x:Class` ไม่โดน rename อัตโนมัติเสมอ** — เช็คทุกไฟล้ `.xaml`/`.xaml.cs` คู่กันด้วยมือหลัง IDE rename
2. **Migration ต้องลบของเก่าทิ้งทั้งหมดแล้วสร้างใหม่** — ถ้าลืมลบ `Migrations/` โฟลเดอร์เดิม จะได้ migration ที่อ้าง namespace ผสมกันทั้งเก่าใหม่ พังแน่นอน
3. **AppId ใหม่ใน .iss** — ถ้าลืมเปลี่ยน และมีคนเคยลง MyLabGuard ไปแล้วจริง (ไม่ใช่กรณีนี้ แต่เผื่ออนาคต) installer ใหม่จะเข้าใจผิดว่าเป็นการ upgrade
4. **Service ชื่อเก่าที่เคย `sc.exe create` ไว้บนเครื่อง dev แล้วลบไปตอน incident** — เช็คด้วย `sc query MyLabGuardClient` ว่าไม่มีเศษ service ค้างอยู่ก่อนติดตั้งชื่อใหม่ (ป้องกัน conflict เวลาทดสอบ)
5. **อย่าทำปนกับ feature work อื่น** — ตามที่บันทึกไว้ใน architecture.md เดิม ควรแยกเป็น commit/PR เดียวโฟกัส rename อย่างเดียว
