# Architecture Decisions

บันทึกการตัดสินใจทั้งหมดจากการวางแผน เพื่อไม่ให้ต้องย้อนคิดใหม่ตอนเริ่มโค้ดจริง

## บริบท / โจทย์

ใช้ในห้องคอมพิวเตอร์ที่ดูแลอยู่ (40 เครื่อง) ซึ่งมี Reboot Restore Rx (RRRx) Endpoint
Manager ควบคุม restore อยู่แล้ว ต้องการระบบเสริมที่:

1. ตรวจจับการรันโปรแกรมตาม **publisher** ที่กำหนด (ไม่ใช่แค่ชื่อไฟล์)
2. รัน task/สคริปต์ตามมาเมื่อ match (ไม่ใช่แค่ log — เช่นเปิดโปรแกรมคู่กัน)
3. มี GUI
4. รันเป็น service กัน end-task ทิ้ง (auto-restart พอ ไม่ต้องถึงระดับ protected process)
5. ควบคุม/toggle จากส่วนกลางผ่าน remote ได้ โดยมีเครื่อง server (เครื่องเดียวกับที่รัน
   RRRx Endpoint Manager) เป็น host

## การตรวจจับ process

- **วิธี**: WMI `__InstanceCreationEvent` (`WITHIN` query, `TargetInstance ISA
  'Win32_Process'`) — เลือกเพราะง่าย ยอมรับ delay เล็กน้อยได้ ไม่ต้องพึ่ง Sysmon
  หรือ kernel driver
- **การหา publisher**: ใช้ **ทั้งสองแบบควบคู่กัน**
  1. Authenticode digital signature (`WinVerifyTrust` / `X509Certificate2`) — น่าเชื่อถือ
     กว่า ใช้เป็นหลัก
  2. `FileVersionInfo.CompanyName` (metadata) — ใช้เป็น fallback/hint เพราะปลอมง่าย
     (แก้ resource ได้) **ต้องแสดงผลแยกให้ชัดว่า match มาจาก signature หรือ metadata
     เท่านั้น** เพื่อไม่ให้เข้าใจผิดว่าเชื่อถือได้เท่ากัน

## Task ที่รันเมื่อ match

- ไม่ใช่แค่ log/notify — ต้อง **รันคำสั่ง/สคริปต์อื่นตามมาได้** (เช่นเปิดโปรแกรมคู่กัน)
- ดังนั้น task runner ต้องรองรับ arbitrary process launch ตาม config ต่อ rule

## การกัน end-task

- ระดับที่ต้องการ: **แค่ auto-restart ผ่าน Windows Service Recovery ก็พอ**
  (ไม่ต้องทำ protected process / กัน admin ปิด)
- ตั้งค่า: `sc.exe failure` หรือ Services console > Recovery tab — restart ทันทีเมื่อ
  crash, reset failure counter ทุก 1 วัน

## รูปแบบการติดตั้ง (เหมือน RRRx) — 3 โหมดจาก installer เดียว

### Mode A: Server + Console
- ติดตั้งบนเครื่อง server (เครื่องเดียวกับ RRRx Endpoint Manager)
- มีทั้ง `MyLabGuard.Server` (Windows Service: API + SQLite) และ
  `MyLabGuard.Console` (GUI ที่ผูก localhost อัตโนมัติ แต่พิมพ์ IP อื่นก็ได้)
- **ไม่ยุ่งกับตัว RRRx เลย** แค่ใช้เครื่องเดียวกันเป็นที่ตั้ง (คนละ process, คนละ port)
  เพราะ RRRx ไม่เปิด API สาธารณะให้เขียนต่อ

### Mode B: Console only
- ติดตั้งบนเครื่องครู/แอดมิน/โน้ตบุ๊คใดๆ
- มีแค่ GUI ไม่มี service ไม่มี DB
- **ต้องพิมพ์ IP:Port ของ server ทุกครั้งที่เปิดโปรแกรม** (ตัดสินใจแล้ว — ไม่ทำ
  recent-server list หรือ auto-discovery)

### Mode C: Client (Agent)
- ติดตั้งบนเครื่องนักเรียน x40
- `MyLabGuard.Client` (Windows Service, SYSTEM) — ตัวจริงที่ detect/บังคับใช้ ไม่มี UI
- `MyLabGuard.ClientTray` — **คนละ process จาก Service** รันตอน user login, โชว์ที่
  system tray, double-click ต้อง **login ด้วย password ก่อน** ถึงจะเห็น
  status/log/refresh ได้ ปิด tray.exe ได้จาก Task Manager แต่ **Service เบื้องหลัง
  ยังทำงานต่อปกติ** เพราะแยก process กันเด็ดขาด

## Server ↔ Client / Console

- **Protocol**: REST/JSON, poll-based (client ดึงค่าเองทุก ~30s) ไม่ใช้ push
  — เหตุผล: client ไม่ต้องเปิด port, ไม่ต้องกังวล NAT/firewall, เหมาะกับ LAN โรงเรียน
- **State ทั้งหมดอยู่ที่ Server เท่านั้น** — client เป็น stateless client ของ config
  เหตุผล: เครื่อง client ถูก RRRx restore ทับอยู่แล้ว ถ้าเก็บ config ไว้บนเครื่อง
  client จะหายทุกรอบ restore เว้นแต่กันเป็น excluded folder ซึ่งไม่ควรพึ่งพา
- **Fail-safe**: เลือก **fail-secure** — ถ้า client ติดต่อ server ไม่ได้เกิน X นาที
  ให้ยังคง enforce กฎล่าสุดที่มีต่อไป (ไม่ fallback เป็นปิดระบบ)
- **Toggle**: มี 2 ระดับ — Global (ทั้งห้อง) และ Per-machine (เครื่องเดียว)

## Auth

- **Server auth (Console → Server)**: hashed password เก็บบน server
  - ใช้ one-way hash แบบมี salt (PBKDF2 ผ่าน `Rfc2898DeriveBytes` หรือ `BCrypt.Net`)
    ห้ามใช้ MD5/SHA1 เปล่าๆ
  - Client (GUI) hash/ส่งแบบที่กันดักฟังเบื้องต้นก่อนส่งผ่าน HTTP (เช่น
    challenge-response หรืออย่างน้อย hash ก่อนส่ง) เพราะเลือกเริ่มจาก HTTP ธรรมดา
    ก่อน (ดูหัวข้อ Protocol ด้านล่าง)
- **Client↔Server auth**: unique GUID ต่อเครื่อง generate ตอน install เก็บใน registry
  ใช้แยก log/toggle รายเครื่อง
- **Tray login**: login-gated ด้วย password (จะใช้ร่วมกับ admin password หรือแยกเป็น
  ชุดอื่นทีหลังก็ได้ — ยังไม่ fix)

## Protocol: HTTP vs HTTPS

- **เริ่มจาก HTTP ธรรมดาก่อน** (LAN ปิดของโรงเรียน ความเสี่ยงต่ำกว่าเปิดสาธารณะ)
- **ข้อควรระวัง**: ห้ามส่ง password แบบ plain text แม้บน HTTP — ต้อง hash/HMAC
  ฝั่ง client ก่อนส่งเป็นอย่างน้อย
- ค่อยอัพเป็น HTTPS + self-signed cert ทีหลังถ้าต้องการความปลอดภัยเต็มรูปแบบ

## Logging

- รวมศูนย์ที่ Server (SQLite พอสำหรับ scale 40 เครื่อง)
- Client push log กลับผ่าน API เดียวกับที่ poll config

## Tech stack

| ส่วน | เทคโนโลยี |
|---|---|
| Server | .NET 10 (LTS), Worker Service (`Microsoft.Extensions.Hosting`), Minimal API, SQLite |
| Client detection | `System.Management` (WMI), WinTrust/Authenticode API |
| GUI (Console + Tray) | WPF (.NET 8) |
| Auth | PBKDF2/BCrypt + salt |
| Config storage (server) | SQLite ที่ `%ProgramData%\MyLabGuard\` (service รันเป็น
  SYSTEM ต้องเขียนได้) |

## ยังไม่ได้ตัดสินใจ / ทิ้งไว้คิดต่อ

- Tray password ใช้ร่วมกับ admin password หรือแยกชุด (เช่น student PIN แบบดูอย่างเดียว)
- รายละเอียด API contract (endpoint paths, request/response schema) — ยังไม่ fix
- DB schema เต็มรูปแบบ — ยังไม่ได้ออกแบบ
- Rule matching syntax (wildcard? regex? exact match เท่านั้น?)

---

# Current Implementation Status (อัพเดตล่าสุด: 2026-07-16)

> ส่วนนี้สรุปสถานะจริงของโค้ดที่ implement ไปแล้ว เขียนไว้ให้ Claude session อื่น
> หรือคนอื่นอ่านแล้วเข้าใจ context ได้ทันที โดยไม่ต้องไล่อ่าน conversation history

## ภาพรวม

ทั้ง 4 ส่วนหลักเขียนเสร็จและทดสอบ end-to-end ผ่านแล้ว:
- `MyLabGuard.Server` — ✅ ทำงานสมบูรณ์
- `MyLabGuard.Client` — ✅ ทำงานสมบูรณ์ + ติดตั้งเป็น Windows Service ได้จริง (auto-restart ทดสอบผ่าน)
- `MyLabGuard.Console` — ✅ ทำงานสมบูรณ์ (dark theme, card-based UI)
- `MyLabGuard.ClientTray` — ✅ ทำงานสมบูรณ์ (tray icon + login-gated status window)

**Tech stack จริงที่ใช้**: .NET 10, EF Core + SQLite, WPF, `System.Management` (WMI),
WinTrust P/Invoke (Authenticode), `Hardcodet.NotifyIcon.Wpf` (ไม่ใช่ `.NetCore` — ตัวนั้นเจอ
`MissingMethodException` บน .NET 10 ใช้ไม่ได้)

## MyLabGuard.Server — รายละเอียด

**Models**: `Rule` (มี `KillProcess` field เพิ่มมาทีหลัง), `ClientMachine`, `LogEntry`,
`GlobalState`, `AdminUser`, `AuthToken`

**Endpoints ที่มีอยู่**:
- `POST /api/auth/setup` — สร้าง admin คนแรก (ใช้ได้ครั้งเดียว)
- `POST /api/auth/login` — คืน token (อายุ 12 ชม.)
- `GET /api/clients` — ต้อง token
- `POST /api/clients/{id}/toggle` — ต้อง token
- `POST /api/global/toggle` — ต้อง token
- `GET /api/rules` — ต้อง token
- `POST /api/rules` — ต้อง token
- `DELETE /api/rules/{id}` — ต้อง token (**เพิ่งเพิ่ม กำลังทำอยู่**)
- `POST /api/rules/{id}/toggle` — ต้อง token (**เพิ่งเพิ่ม กำลังทำอยู่**)
- `GET /api/poll/{clientGuid}?machineName=X` — ไม่ต้อง token (client ใช้ GUID ยืนยันตัวเอง)
- `POST /api/logs` — ไม่ต้อง token (client push log)
- `GET /api/logs?take=100` — ต้อง token

**Auth**: PBKDF2 (`Rfc2898DeriveBytes`, salt แยกต่อ user), token เก็บใน `AuthToken` table,
แนบผ่าน header `X-Auth-Token`

**DB**: ใช้ `EnsureCreated()` ไม่ใช่ EF Migrations — **ข้อควรระวังสำคัญ**: ทุกครั้งที่แก้ Model
(เพิ่ม field ใหม่) ต้องลบไฟล์ DB ทิ้งที่ `C:\ProgramData\MyLabGuard\mylabguard.db` แล้วสร้าง
admin/rules ใหม่หมด ไม่งั้นจะเจอ `SqliteException: no such column`
**TODO**: เปลี่ยนเป็น EF Migrations ก่อน deploy จริง

**Port**: 8787, ฟังทุก interface (`ListenAnyIP`)

## MyLabGuard.Client — รายละเอียด

**โครงสร้าง**: Web SDK (ไม่ใช่ Worker SDK) เพราะต้องมี local Minimal API ด้วย

**Services**:
- `PublisherChecker.cs` — เช็ค Authenticode (WinTrust P/Invoke) ก่อน, fallback ไป
  `FileVersionInfo.CompanyName` ถ้าไม่ signed
- `ClientIdentity.cs` — GUID เก็บที่ `HKLM\SOFTWARE\MyLabGuard\ClientGuid`
  (**ยังไม่ได้ทดสอบว่ารอด RRRx restore จริงไหม**)
- `ServerClient.cs` — poll + push log ไป Server กลาง, fail เงียบถ้า server ล่ม
- `ClientState.cs` — in-memory state (Singleton) สำหรับให้ local API อ่าน

**Local API** (port 8788, `127.0.0.1` เท่านั้น ไม่เปิดวง LAN):
- `GET /status` — enabled, rulesCount, lastPollAt, lastPollSucceeded, clientGuid
- `GET /logs/recent` — log 50 รายการล่าสุด (in-memory queue)

**Worker.cs หลัก**:
- WMI `__InstanceCreationEvent` watcher (`WITHIN 1`)
- Poll ทุก 30 วิ (ตั้งค่าใน `appsettings.json` → `ServerSettings:PollIntervalSeconds`)
- Rule matching: publisher name match (case-insensitive) + เช็ค `RequireSignedMatch`
- Action เมื่อ match: **kill process** (ถ้า `rule.KillProcess=true`) และ/หรือ **รัน
  ActionCommand** — รวม log เป็น string เดียวคั่นด้วย " + " เช่น "Killed process + Ran
  command: xxx.exe"
- Fail-secure: ถ้า poll ไม่สำเร็จ ใช้ค่า rules/enabled ล่าสุดที่มีต่อไป

**⚠️ บทเรียนสำคัญมาก**: Rule ที่ตั้ง publisher แบบกว้างเกินไป (เช่น "Microsoft
Corporation" เฉยๆ) จะ match โปรแกรมที่ไม่ตั้งใจด้วย เช่น **VS Code ก็ signed โดย Microsoft
Corporation** เคยตั้ง rule "Kill Notepad" match publisher นี้แล้วมัน kill VS Code ไปด้วย
จริงๆ **ต้องมีวิธี narrow ลงไปอีก** เช่น match ที่ path หรือชื่อไฟล์เฉพาะเจาะจงด้วย ไม่ใช่แค่
publisher — ยังไม่ได้แก้จุดนี้ เป็น TODO สำคัญ

**Windows Service**:
- Publish: `dotnet publish -c Release -r win-x64 --self-contained false -o publish`
- Install/uninstall script อยู่ที่ `installer/install-client-service.ps1` และ
  `uninstall-client-service.ps1` (ต้อง save เป็น **UTF-8 with BOM** ไม่งั้น PowerShell
  อ่านคอมเมนต์ภาษาไทยพังจนพาร์ส error)
- Service name: `MyLabGuardClient`
- Service Recovery ตั้งไว้แล้ว: restart ทันทีทุกครั้งที่ crash, reset counter ทุก 1 วัน
- ทดสอบ auto-restart ผ่านแล้ว (kill process แรงๆ แล้ว Windows restart ให้เองใน ~ไม่กี่วินาที)
- **สถานะปัจจุบัน (ล่าสุดในบทสนทนา)**: service ถูก**ลบทิ้งไปแล้ว**หลังจากเจอ rule ที่ kill
  VS Code ผิดพลาด ต้อง install ใหม่หลังแก้ rule ให้ปลอดภัยก่อน

## MyLabGuard.Console — รายละเอียด

**Auth**: login ผ่าน `/api/auth/login`, เก็บ token ใน `ApiClient` (ตัวแปรใน memory เท่านั้น
ไม่ persist)

**หน้าจอ**:
- `MainWindow.xaml` — Connect screen (กรอก IP:Port + username + password ทุกครั้ง ไม่เก็บ
  history)
- `DashboardWindow.xaml` — 3 tabs: Clients (list + toggle), Rules (add + list, **ยังไม่มี
  ปุ่ม delete/toggle ใน UI** แม้ endpoint เพิ่งเพิ่มใน Server แล้ว), Logs (list)

**Theme**: Dark theme แยกไว้ที่ `Themes/DarkTheme.xaml` ผูกผ่าน `App.xaml`
`MergedDictionaries` — สีหลัก: bg `#0F1115`, card `#1A1D23`, accent เขียว `#3DD68C` / แดง
`#F0555A` / น้ำเงิน `#4C8DFF`

**Rule creation form**: มีช่อง Name, Publisher, checkbox Kill Process, Action Command —
**ยังไม่มีช่องกรอก process path หรือ RequireSignedMatch** (ใช้ default `true` เสมอตอนนี้)

## MyLabGuard.ClientTray — รายละเอียด

**Flow**: tray icon โผล่ทันทีตอน start (ไม่ต้อง login) → double-click หรือ context menu
"Open Status..." → เปิด `LoginWindow` (username + password กรอกเอง, ยิงไปที่ Server
`/api/auth/login` ผ่าน `_serverBaseUrl` ที่อ่านจาก `appsettings.json`) → login สำเร็จ → เปิด
`StatusWindow` (อ่านจาก Client local API `localhost:8788`, ไม่ได้คุยกับ Server โดยตรง)

**Auto-start ตอน user login**: **ยังไม่ได้ทำ** (ต้องตั้ง registry Run key หรือ Startup
folder เอง — เป็น TODO)

**Icon**: ใช้ `SystemIcons.Shield` (default ของ Windows) ไปก่อน ยังไม่มี icon ของแอปเอง

## Installer (โฟลเดอร์ installer/)

มีแค่ `install-client-service.ps1` และ `uninstall-client-service.ps1` สำหรับ
`MyLabGuard.Client` เท่านั้น **ยังไม่มี installer สำหรับ Server, Console, หรือ ClientTray**
และยังไม่มี installer แบบรวม 3-mode ตามที่ออกแบบไว้ในหัวข้อ "รูปแบบการติดตั้ง" ด้านบน

## Known Issues / TODO ที่ยังไม่ได้ทำ (เรียงตามความสำคัญ)

1. **Rule ต้อง narrow กว่าแค่ publisher name** — เพิ่ง kill VS Code ผิดเพราะ publisher
   "Microsoft Corporation" กว้างเกินไป (ดูบทเรียนด้านบน) ควรเพิ่มการ match ด้วย path/ชื่อไฟล์
   ประกอบด้วย
2. **Console GUI ยังไม่มีปุ่ม delete/toggle rule** — endpoint ฝั่ง Server เพิ่งเพิ่ม
   (`DELETE /api/rules/{id}`, `POST /api/rules/{id}/toggle`) แต่ UI ยังไม่ได้ผูก
3. **DB migration** — เปลี่ยนจาก `EnsureCreated()` เป็น EF Core Migrations ก่อน deploy จริง
4. **Client Service ต้อง install ใหม่** — ถูกลบไปแล้วหลังปัญหา rule กว้างเกินไป
5. **Auto-start Tray ตอน user login** — ยังไม่ได้ทำ
6. **Installer รวม 3-mode** — ยังไม่ได้ทำเลย มีแค่ script ติดตั้ง Client service อย่างเดียว
7. **ClientGuid รอดจาก RRRx restore จริงไหม** — ยังไม่เคยทดสอบบนสภาพแวดล้อมจริงที่มี RRRx
8. **Console: offline indicator** — ยังไม่แสดงว่า client ไหน offline อยู่ (เทียบ
   `lastSeenAt`)
9. **Icon ของแอปเอง** — ทั้ง Console และ ClientTray ยังใช้ default icon

## Dev Environment Notes

- Path จริงบนเครื่อง dev: `F:\GitProject\StudentHappynessSlayer` (ชื่อ repo บน GitHub คือ
  `StudentHappynessSlayer` ไม่ใช่ `MyLabGuard` — ตั้งชื่อไปแล้วตอนแรกยังไม่ได้เปลี่ยน)
- ใช้ VS Code Portable (ไม่ใช่ Visual Studio) + Git for Windows Portable
- Client ต้องรันด้วยสิทธิ์ **Administrator** เสมอตอน dev (เขียน `HKEY_LOCAL_MACHINE`) ไม่งั้น
  เจอ `UnauthorizedAccessException`
- Server รันปกติไม่ต้อง admin
- ทดสอบ local API ต้องรอ poll รอบแรกเสร็จก่อน (`~1-2 วิ` หลัง start) ไม่งั้นจะได้ค่า default
  ว่างๆ กลับมา ไม่ใช่บั๊ก