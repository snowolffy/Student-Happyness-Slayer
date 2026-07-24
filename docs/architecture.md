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

# Update Log

- 2026-07-17 — Rename โครงการจาก MyLabGuard เป็น Onion ProcOparetor แล้วตามแผน
  - โฟลเดอร์/ไฟล์ .csproj ใหม่: OnionProcOparetor.Server, OnionProcOparetor.Console,
    OnionProcOparetor.Agent, OnionProcOparetor.AgentTray
  - Registry path, service names, DB path, installer name, UI text ถูกเปลี่ยนเป็นชื่อใหม่
  - Migrations เดิมถูกเตรียมให้ลบทิ้งและสร้างใหม่เพื่อให้ snapshot สอดคล้องกับ namespace ใหม่

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

  ---

# Update Log (2026-07-16, ต่อจาก Current Implementation Status ด้านบน)

## สิ่งที่ทำเสร็จเพิ่มเติมหลังจากบันทึกด้านบน

### Rule Delete/Toggle (เสร็จแล้ว)
- Server: เพิ่ม `DELETE /api/rules/{id}` และ `POST /api/rules/{id}/toggle` (ต้อง token
  ทั้งคู่) — build ผ่านแล้ว
- Console: เพิ่มปุ่ม TOGGLE/DELETE ในตาราง Rules tab, ผูกกับ `ApiClient.DeleteRuleAsync()`
  และ `ToggleRuleAsync()` — มี confirm dialog ก่อนลบ — build ผ่านแล้ว
- **ยังไม่ได้ทดสอบใช้งานจริงหลัง build** (ติดปัญหา DB readonly ก่อนจะทดสอบ)

### Incident: Rule กว้างเกินไป kill VS Code
- Rule "Kill Notepad" ตั้ง publisher = "Microsoft Corporation" (ไม่ narrow พอ) ทำให้ไป kill
  VS Code ด้วย เพราะ VS Code ก็ signed โดย Microsoft Corporation เหมือนกัน
- แก้เฉพาะหน้า: ลบ Client Service ทิ้งไปก่อน (ยังไม่ได้ install ใหม่ ณ จุดนี้)
- **ยังไม่ได้แก้ root cause** (ต้องเพิ่มการ match แบบ narrow กว่านี้ เช่น path/filename
  ประกอบ — ยังเป็น TODO อยู่)

### DB Permission Issue
- หลัง build เสร็จแล้วรัน Server เจอ `SQLite Error 8: attempt to write a readonly
  database` ที่ `C:\ProgramData\MyLabGuard\mylabguard.db`
- สาเหตุคาดว่า: ไฟล์ DB ถูกสร้าง/แก้ตอนที่ Client Service (รันเป็น SYSTEM) ยังทำงานอยู่
  ทำให้ ACL ของไฟล์เปลี่ยนเป็นของ SYSTEM/Admin จนกลาย user ปกติเขียนไม่ได้
- **แก้ไขแล้วโดยลบ DB ทิ้งทั้งก้อน** (`Remove-Item mylabguard.db -Force`) — ข้อมูลเดิม
  (rules, admin, logs) หายหมด ต้อง setup admin ใหม่
- **TODO ในอนาคต**: ถ้าเจอปัญหานี้อีก ให้เช็ค `icacls` ของโฟลเดอร์
  `C:\ProgramData\MyLabGuard` ก่อนลบทิ้ง อาจจะแก้ permission แทนได้โดยไม่ต้องเสียข้อมูล

## กำลังทำอยู่ตอนนี้: User Management System

**เหตุผล**: อยากให้ Console GUI จัดการ admin account ได้เอง (สร้าง/ลบ/list/เปลี่ยน
password) แทนที่จะต้อง hardcode หรือยิง API เองตอน setup

**การตัดสินใจที่ fix แล้ว**:
1. มี built-in account ชื่อ `"Administrator"` เสมอ — **ลบไม่ได้** (ป้องกัน lockout)
2. Setup ครั้งแรก **ไม่ถาม username/password จาก request แล้ว** — สร้าง
   `"Administrator"` ให้อัตโนมัติพร้อม **password ว่างเปล่า** (empty string)
3. Login ด้วย password ว่างต้องผ่านได้ (ต้องเช็ค validation logic ที่มีอยู่ไม่บล็อกก่อน)
4. Response ตอน login ต้องมี field `hasDefaultPassword: true/false` เพื่อให้ Console
   บังคับเปลี่ยน password ก่อนเข้าหน้า Dashboard ปกติ (ป้องกันความเสี่ยงจาก password ว่าง
   ที่ไม่ได้เปลี่ยน)
5. ต้องทำ Console GUI **เต็มรูปแบบ** (ไม่ใช่แค่กันลบ account สุดท้าย) — list users, สร้าง
   user ใหม่, ลบ user (ยกเว้น built-in), เปลี่ยน password

**ความคืบหน้าจริงในโค้ด ณ จุดนี้**:
- ✅ แก้ `Models/AdminUser.cs` แล้ว — เพิ่ม `IsBuiltIn` (bool, default false) และ
  `HasDefaultPassword` (bool, default false)
- ⏳ ยังไม่ได้แก้: `AppDbContext.cs` (อาจต้อง seed ข้อมูลหรือปรับ index), setup endpoint,
  login endpoint (เพิ่ม field response), endpoints ใหม่สำหรับ user management
  (`GET/POST/DELETE /api/admin/users`, `POST /api/admin/users/{id}/change-password`),
  Console GUI ทั้งหมด (tab ใหม่ "Users", หน้าบังคับเปลี่ยน password ตอน login ครั้งแรก)

**สิ่งที่ต้องระวังตอนทำต่อ**:
- ทุกครั้งที่แก้ Model ต้องลบ DB ทิ้งอีกครั้ง (`EnsureCreated()` ไม่ auto-migrate) — คราวนี้
  DB เพิ่งถูกลบไปสดๆ ร้อนๆ เลยยังไม่มีข้อมูลอะไรให้เสียดาย เป็นจังหวะดีที่จะแก้ schema รอบนี้
  ให้ครบก่อนสร้างข้อมูลใหม่
- Password ว่างเปล่าต้องเช็คว่า `PasswordHasher.HashPassword("", salt)` ทำงานได้ปกติไม่
  throw exception (ยังไม่ได้ทดสอบ)
- Client Service ที่ถูกลบไปตอน incident ก่อนหน้า **ยังไม่ได้ install กลับ** ต้องรอ rule
  narrow-matching fix เสร็จก่อนค่อย install ใหม่ (กันเหตุการณ์ kill โปรแกรมผิดซ้ำ)


  ---

# Update Log (2026-07-16, ต่อจาก Update Log ก่อนหน้า — User Management System เสร็จสมบูรณ์ + งานเพิ่มเติม)

## สรุปสถานะหลังจบเซสชันนี้

ทำ 6 เรื่องหลักเสร็จเรียบร้อย เรียงตามลำดับที่ทำ:

### 1. User Management System (เสร็จสมบูรณ์)
- Server: `/api/auth/setup` สร้าง built-in `"Administrator"` อัตโนมัติ (password ว่างเปล่า, `IsBuiltIn=true`,
  `HasDefaultPassword=true`) แทนการรับ username/password จาก request
- `/api/auth/login` เพิ่ม field `hasDefaultPassword` และ `adminId` ใน response
- `GetAuthorizedAdmin` (คืน `AdminUser?`) แทนที่ `IsAuthorized` (bool) เดิม — ทุก endpoint ที่ต้อง auth
  เช็ค 2 ชั้น: token valid + `HasDefaultPassword` ต้องเป็น false (ยกเว้น endpoint change-password เอง)
- Endpoints ใหม่: `GET/POST /api/admin/users`, `DELETE /api/admin/users/{id}`,
  `POST /api/admin/users/{id}/change-password`
- Console: `ForceChangePasswordWindow` บังคับเปลี่ยน password ก่อนเข้า Dashboard ถ้า `HasDefaultPassword=true`,
  tab **USERS** ใหม่ (list/create/delete, built-in account ลบไม่ได้ - ปุ่ม DELETE disable อัตโนมัติ)
- **บั๊กที่เจอระหว่างทำ**: `Results.Forbid()` throw exception เพราะไม่มี `AddAuthentication()` ใน
  DI container (เราไม่ได้ใช้ ASP.NET Authentication scheme จริง เช็ค token เองผ่าน header) แก้เป็น
  `Results.Json(..., statusCode: 403)` แทน

### 2. Rule Narrowing - ProcessNameContains (เสร็จสมบูรณ์)
- เพิ่ม field `ProcessNameContains` (nullable string) ใน `Rule.cs`/`RuleDto.cs` ทั้ง 3 โปรเจกต์
- Optional filter: ถ้ากรอกไว้ ต้อง publisher ตรง **และ** ชื่อไฟล์ contains คำนี้ (case-insensitive) ถึงจะ
  match ถ้าเว้นว่างไว้ fallback เป็น publisher-only เหมือนเดิม (backward compatible)
- แก้ปัญหาจริงที่เคย kill VS Code ผิดพลาดเพราะ publisher เดียวกับ Notepad (`Microsoft Corporation`)
- Console: เพิ่มช่องกรอกในฟอร์ม rule + คอลัมน์ในตาราง RULES

### 3. Periodic Process Scan (เสร็จสมบูรณ์)
- **ปัญหาเดิม**: WMI `__InstanceCreationEvent` จับได้แค่ process ที่เพิ่ง "สร้างใหม่" เท่านั้น process ที่
  เปิดค้างอยู่ก่อน service เริ่ม หรือก่อน rule ถูกเพิ่ม/เปิดใช้งาน จะไม่โดนตรวจจับเลย
- เพิ่ม loop ใหม่ `RunPeriodicProcessScanAsync` ทำงานคู่ขนานกับ poll loop เดิม สแกน
  `Process.GetProcesses()` ทั้งหมดทุก **10 วิ** (`ProcessScanIntervalSeconds`, แยกอิสระจาก
  `PollIntervalSeconds`)
- **บั๊กที่เจอระหว่างทำ**: ใช้ PID เดี่ยวๆ เก็บ cache "เคย action แล้ว" ตอนแรก มีความเสี่ยง PID-reuse
  (Windows recycle เลข PID ได้หลัง process เดิมตายไป) แก้เป็นเก็บคู่ `(PID, StartTime)` แทน
- `CheckAndActOnProcess` เปลี่ยน return type เป็น `bool` (บอกว่า action ไปหรือยัง) ใช้ร่วมกันทั้ง
  WMI event handler และ periodic scan

### 4. DB Migration (เสร็จสมบูรณ์ - สำคัญมาก)
- เปลี่ยนจาก `db.Database.EnsureCreated()` เป็น `db.Database.Migrate()` ใน `Program.cs`
- เพิ่ม `Microsoft.EntityFrameworkCore.Tools` package + ติดตั้ง `dotnet-ef` CLI tool (global)
- **บั๊กที่เจอระหว่างทำ**: seed data ของ `GlobalState` ใช้ `DateTime.UtcNow` (non-deterministic) ทำให้
  เจอ `PendingModelChangesWarning` ตอน `Migrate()` ทำงาน (EF มองว่า model "ไม่นิ่ง" เปลี่ยนค่าทุกครั้งที่
  build) แก้เป็นใช้ค่า `DateTime` แบบ fixed/static (`new DateTime(2026, 1, 1, ...)`) แทน
- **ผลลัพธ์สำคัญที่สุด**: ตั้งแต่นี้ไป **แก้ Model (เพิ่ม field ใหม่) ไม่ต้องลบ `mylabguard.db` ทิ้งอีกแล้ว**
  แค่รัน `dotnet ef migrations add <ชื่อ>` แล้ว restart server ข้อมูลเดิมยังอยู่ครบ (ต่างจากที่ผ่านมาที่
  ต้องลบ DB ทิ้งทุกรอบที่แก้ schema)

### 5. Auto-start Tray ตอน user login (เสร็จสมบูรณ์ในเชิงโค้ด - รอทดสอบหลัง publish)
- เพิ่ม `RegistryStartup.cs` (ClientTray/Services/) จัดการ Registry Run key ที่
  `HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Run\MyLabGuardClientTray`
- ใช้ `Environment.ProcessPath` ดึง path ของ .exe ที่กำลังรันอยู่จริง **ห้าม hardcode path** เพราะต่างกัน
  ระหว่างเครื่อง dev กับเครื่องที่ install จริง
- ทำงานแบบ idempotent (เรียกซ้ำได้ไม่มีผลเสีย) เรียกจาก `App.xaml.cs` ตอน `OnStartup`
- มี `DisableAutoStart()` เตรียมไว้เผื่อใช้ตอน uninstall (installer เรียกใช้แล้ว - ดูข้อ 6)
- **ข้อควรระวัง**: ทดสอบผ่าน `dotnet run` (dev mode) จะไม่เห็นผลถูกต้อง เพราะ `Environment.ProcessPath`
  จะได้ path เป็น `dotnet.exe` ไม่ใช่ตัว .exe จริง **ต้องทดสอบหลัง publish เป็น standalone .exe เท่านั้น**

### 6. Installer รวม 3-mode ด้วย Inno Setup (compile ผ่านแล้ว - รอทดสอบรันจริง)
- ใช้ Inno Setup Portable (จาก portableapps.com) เพราะ dev environment ทั้งหมดอยู่บน external drive
- ไฟล์ `installer/MyLabGuard.iss` - custom wizard page ให้เลือกโหมด (radio button):
  - Mode A: Server + Console (install `MyLabGuardServer` service)
  - Mode B: Console only (แค่ copy ไฟล์ + shortcut)
  - Mode C: Client Agent (install `MyLabGuardClient` service + ClientTray)
- `[Files]`/`[Icons]`/`[Run]` ทุกตัวผูกกับ `Check:` parameter อ้างอิง `SelectedMode` จากหน้า wizard
- Uninstall: ถาม `MsgBox` ว่าจะลบข้อมูล (DB, logs ที่ `%ProgramData%\MyLabGuard\`) ด้วยหรือเก็บไว้
  เลือก Yes จะลบทั้งโฟลเดอร์ data + ลบ Registry Run key ของ Tray ด้วย (`RegDeleteValue`)
- **บั๊กที่เจอระหว่าง compile**:
  1. `ArchitecturesInstallIn64BitMode=x64compatible` ไม่รู้จักใน Inno Setup 6.2.0 (syntax ใหม่กว่า)
     แก้เป็น `x64` เฉยๆ
  2. `Thai.isl` ไม่ได้ bundle มากับเวอร์ชัน Portable ทำให้ compile fail ตอน include language file
     แก้โดยตัด `[Languages]` เหลือแค่ English (ข้อความในหน้า wizard ที่เขียนเองใน `[Code]` เปลี่ยนเป็น
     อังกฤษด้วย เพราะภาษาไทยเจอปัญหา encoding เพี้ยนตอนแสดงผลใน custom wizard page)
- **Publish command ที่ใช้** (ต้องรันก่อน compile installer ทุกครั้งที่มีการเปลี่ยนแปลงโค้ด):
  ```powershell
  dotnet publish src/MyLabGuard.Server -c Release -r win-x64 --self-contained false -o publish/Server
  dotnet publish src/MyLabGuard.Console -c Release -r win-x64 --self-contained false -o publish/Console
  dotnet publish src/MyLabGuard.Client -c Release -r win-x64 --self-contained false -o publish/Client
  dotnet publish src/MyLabGuard.ClientTray -c Release -r win-x64 --self-contained false -o publish/ClientTray
  ```
  **สำคัญ**: ใช้ `--self-contained false` แปลว่าเครื่องปลายทางต้องมี .NET 10 Runtime ติดตั้งไว้ก่อน ถ้า
  service ไม่ start (แม้ install สำเร็จ) ให้เช็คจุดนี้ก่อนเป็นอันดับแรก

## Known Issues / TODO ที่ยังไม่ได้ทำ (อัพเดตลำดับความสำคัญ)

1. **ทดสอบ installer จริง** - ยังไม่เคย install/uninstall จริงสักครั้ง ต้องทดสอบทั้ง 3 โหมดแยกกัน
   ก่อนเอาไปใช้กับเครื่องนักเรียน 40 เครื่องจริง (แนะนำเริ่มจาก Mode B ก่อน เสี่ยงน้อยสุด)
2. **Console: offline indicator** - ยังไม่แสดงว่า client ไหน offline อยู่ (เทียบ `lastSeenAt`)
3. **Icon ของแอปเอง** - ทั้ง Console และ ClientTray ยังใช้ default icon
4. **Catalog signing support** - พักไว้ตามการตัดสินใจร่วมกัน เพราะ P/Invoke เสี่ยงสูง (พึ่ง
   `CryptQueryObject`/`CryptMsgGetParam`/`CertFindCertificateInStore` ที่ยังไม่เคย compile/ทดสอบจริง)
   ปัญหาที่ยังค้าง: ไฟล์ system ของ Windows (เช่น `notepad.exe`) ใช้ Catalog Signing ไม่ใช่ embedded
   signature ทำให้ `X509Certificate.CreateFromSignedFile()` หา cert ไม่เจอ (แม้ `WinVerifyTrust` จะ
   verify ผ่าน) ทำให้ rule ที่ require signed match ใช้กับไฟล์ระบบไม่ได้ในบางกรณี
5. **Debug log ค้างใน Worker.cs** - มี log ขึ้นต้น `[DEBUG]` 3 จุดใน `ScanRunningProcessesAsync` (จากตอน
   debug ปัญหา rule ไม่ match ที่แท้จริงคือพิมพ์ publisher name ผิด ไม่ใช่บั๊ก) ยังไม่ได้เอาออก ไม่มีผลเสีย
   แค่ log จะยาวขึ้น
6. **ClientGuid รอดจาก RRRx restore จริงไหม** - ยังไม่เคยทดสอบบนสภาพแวดล้อมจริงที่มี RRRx
7. **Auto-start Tray** - เขียนโค้ดเสร็จแล้วแต่ยังไม่เคยทดสอบหลัง publish จริง (ดูข้อควรระวังด้านบน)
8. **Client Service ต้อง install ใหม่** - ยังค้างจาก incident ก่อนหน้า (rule กว้างเกินไป) ตอนนี้ปัญหาแก้
   แล้วทั้งคู่ (ProcessNameContains + publisher พิมพ์ผิด) แต่ต้องยืนยันว่า install ผ่าน installer ใหม่
   หรือยังใช้ script เดิม (`install-client-service.ps1`) อยู่

## Dev Environment Notes (อัพเดต)

- Inno Setup ใช้เวอร์ชัน **Portable** จาก portableapps.com (ไม่ใช่ installer แบบเต็ม) เพราะ dev
  environment ทั้งหมด (VS Code, Git, ตอนนี้รวม Inno Setup) อยู่บน external drive เผื่อเปลี่ยนเครื่อง
- Inno Setup Portable **ไม่มี Thai.isl bundle มาด้วย** - ถ้าต้องการ UI ภาษาไทยจริงๆ ต้องดาวน์โหลด
  language file แยกมาวางเองที่ `Languages\` ของตัว portable app (ยังไม่ได้ทำ - ตอนนี้ใช้ English ทั้งหมด)

  ---

# Update Log (2026-07-16, ต่อจาก Update Log ก่อนหน้า — ตั้งชื่อแบรนด์ใหม่)


## แบรนด์ใหม่: Onion ProcOparetor

ตัดสินใจแล้ว (2026-07-16): โปรแกรมนี้จะใช้ชื่อแบรนด์ **"Onion ProcOparetor"** แทนที่ "MyLabGuard" ในทุก
เอกสาร/UI ที่ user เห็น โดยมีที่มาของชื่อ (เก็บไว้เป็นบันทึกสนุกๆ): **"Onion" = Oh No, It's Observing Now**

### Mapping ชื่อแบรนด์ ↔ ชื่อโค้ดปัจจุบัน

| ส่วนประกอบ | ชื่อแบรนด์ | ชื่อโค้ด (`.csproj`/namespace ปัจจุบัน) |
|---|---|---|
| Server (Windows Service + API + DB) | **Onion Core Service (OCS)** | `MyLabGuard.Server` |
| Admin GUI | **Onion Console** | `MyLabGuard.Console` |
| Client Service + ClientTray รวมกัน | **Onion Agent** | `MyLabGuard.Client` + `MyLabGuard.ClientTray` |

- ชื่อ repo บน GitHub **ยังคงเป็น** `Student-Happyness-Slayer` ตามเดิม (ไม่เปลี่ยน)
- Tagline หน้า About ของโปรแกรม: **"Peeling processes since 2026."**

### สถานะการเปลี่ยนชื่อ

- ✅ `README.md` อัพเดตแล้ว ใช้ชื่อแบรนด์ใหม่ในเอกสารทั้งหมด
- ✅ **โค้ดจริง rename เสร็จสมบูรณ์แล้ว (17 ก.ค. 2026)** — namespace, ชื่อ Windows Service
  (`OnionCoreService`, `OnionAgent`), ชื่อไฟล์ `.exe`, ชื่อ `.csproj`/โฟลเดอร์ทั้งหมดเป็น
  `OnionProcOparetor.*` แล้ว, registry path และ DB path ย้ายไปที่ `OnionProcOparetor` แล้ว
- ✅ Regression test หลัง rename ผ่านแล้ว (เช็คด้วยมือ)
- รายละเอียดขั้นตอนที่ทำตอน rename ดูย้อนหลังได้ที่ `docs/RENAME_PLAN.md` (ใช้เป็น reference
  ประวัติ ไม่ใช่ TODO ค้างแล้ว)

> จากนี้งานที่เหลือของโปรเจกต์จะเป็นฟีเจอร์ใหม่และการปรับแต่งจุดต่างๆ (ไม่ใช่งาน rename อีกต่อไป)

### คำแนะนำสำหรับตอนลงมือจริง (ยังไม่ได้ทำ แค่เตือนไว้ล่วงหน้า)

- ควรทำเป็น**เซสชันแยกต่างหาก** ไม่ปนกับ feature work อื่น เพราะเป็นการเปลี่ยนแปลงวงกว้างที่กระทบ
  แทบทุกไฟล์ ถ้าทำปนกับงานอื่นจะ debug ยากมากว่าอะไรพังเพราะ rename หรือเพราะ feature ใหม่
- แนะนำใช้ฟีเจอร์ rename ของ IDE (Visual Studio/Rider "Rename Symbol") แทนการ find-replace ข้อความ
  ตรงๆ เพื่อกัน rename ผิดจุด (เช่น string literal ที่ไม่ควรเปลี่ยนแต่ดันมีคำว่า "MyLabGuard" ปนอยู่)
- ต้องทดสอบทุก flow ใหม่ทั้งหมดหลัง rename (setup, login, rule matching, installer ทั้ง 3 โหมด) เพราะ
  ความเสี่ยง regression สูงมากจากการเปลี่ยนชื่อวงกว้างขนาดนี้
- ตัดสินใจเรื่อง Registry key ของ `ClientGuid` (ข้อ 7 ด้านบน) **ก่อน** เริ่มลงมือ เพราะถ้าตัดสินใจผิด
  จะทำให้ client ที่มีอยู่แล้วสูญเสีย identity เดิมไป กลายเป็น register ซ้ำที่ server

  ---

# QoL Roadmap (บันทึกเมื่อ 2026-07-17, หลังจบงาน rename)

> สถานะ: **แค่ spec/แผน ยังไม่ได้ลงมือทำ** — รอรวบรวมให้ครบทุกฟีเจอร์ก่อน แล้วค่อยออกแบบ UI
> รวดเดียวใน xaml.io (ตัดสินใจแล้วว่าสะดวกกว่าทำทีละอัน)

## Server / Console

| ฟีเจอร์ | รายละเอียด |
|---|---|
| Remote client setting | Console แก้ config ของเครื่องใดเครื่องหนึ่งจากศูนย์กลางได้ |
| View client setting | ดูค่า config ปัจจุบันของเครื่องนั้นๆ ได้ |
| Rule config เพิ่มเติม | ต่อยอดจากที่มีอยู่ (Name/Publisher/KillProcess/ActionCommand/ProcessNameContains) |
| Offline indicator | ไฟสถานะเทียบ `lastSeenAt` |
| Search/filter | ค้นหาในตาราง Clients/Rules/Logs |
| Bulk action | เลือกหลายแถวแบบ ctrl (เลือกเพิ่มทีละแถว) / shift (เลือกเป็นช่วง) แล้ว apply พร้อมกัน |
| Sort ตามคอลัมน์ | คลิก header จัดเรียง asc/desc |
| Client immediately pull | Console สั่งแล้ว client **ลด poll interval ชั่วคราวเหลือ 0.5 วิ นาน 2 วิ** แทนรอรอบปกติ (ไม่ทำ push จริงแบบ SignalR/WebSocket — ตัดสินใจแล้วว่าเกินความจำเป็น) |
| Account config | จัดการ user/account ของ Console (ต่อยอดจาก User Management System ที่มีอยู่) |

## Agent

| ฟีเจอร์ | รายละเอียด |
|---|---|
| Client config | หน้า/ขั้นตอนตั้งค่า Server IP:Port ตอน install (Mode C) + ปุ่ม Test Connection |
| Uninstall password-gate | ป้องกันระดับเบา — Inno Setup uninstaller ถามรหัสผ่านที่ hardcode ไว้ตอน build ก่อนถอนได้ (เป้าหมาย: กันเด็กเล่น ไม่ใช่กัน IT ตัวจริงที่ตั้งใจเลี่ยง — ไม่ต้องเช็คกับ Server, ไม่ต้อง online) |
| Output log UI แบบ terminal | หน้าต่าง scroll log สด ต่อยอดจาก local API `/logs/recent` ที่มีอยู่แล้ว (auto-scroll/refresh ต่อเนื่อง) |

## ตัดสินใจแล้วว่า "ไม่ต้องทำ" (กันย้อนคิดซ้ำ)

- ไม่ต้องมี installer reminder เรื่อง update RRRx baseline หลัง config — ผู้ดูแลทำเป็น routine อยู่แล้วทุกครั้งที่ลงของใหม่
- Config ฝั่ง Agent เก็บใน `appsettings.json` ปกติได้ ไม่ต้องย้ายไป registry ตอนนี้ — เหตุผล: workflow "update baseline หลัง install" ทำให้ค่าที่ตั้งกลายเป็นค่า default ของ baseline เอง ไม่ต้องพึ่ง registry-exclusion
  - **ทางเลือกเปิดไว้** (ยังไม่ตัดสินใจตายตัว): ถ้าภายหลังอยากได้ความชัวร์กว่านี้โดยไม่พึ่ง discipline อย่างเดียว ค่อยพิจารณาเก็บ config เป็น JSON string ใน registry value เดียว + RRRx Registry Exclusion
- ไม่ทำ protected process หรือการป้องกัน uninstall ระดับ IT-proof — เกินความจำเป็นของ use case (เป้าหมายแค่กันเด็กถอนเล่นๆ ไม่ใช่กัน admin ตัวจริง)
- ไม่ทำ push-based communication (SignalR/WebSocket) — คงสถาปัตยกรรม poll-based เดิมไว้ตามที่ตัดสินใจแต่แรก (client ไม่เปิด port, ไม่ต้องกังวล NAT/firewall)

## ขั้นตอนถัดไป

1. รวบรวมไอเดียเพิ่มเติมให้ครบ (ยังเปิดรับ — ผู้ดูแลบอกว่า "อาจมีอย่างอื่นอีกแต่ยังนึกไม่ออก")
2. เมื่อ spec ครบแล้ว ค่อยออกแบบ UI ทั้งหมดพร้อมกันใน xaml.io (ตัดสินใจแล้วว่าสะดวกกว่าทำทีละฟีเจอร์)
3. เริ่ม implement ตามลำดับที่ตกลงกันตอนนั้น

---

# QoL Implementation Log (2026-07-17, สรุปงานที่ทำเสร็จจริงจาก QoL Roadmap)

> อัปเดตสถานะ: งานส่วนใหญ่ใน QoL Roadmap เสร็จสมบูรณ์แล้ว รายละเอียดตามนี้

## Server / Console — เสร็จแล้ว

- **Sort ตามคอลัมน์** — เพิ่ม `SortMemberPath` ให้ template columns (STATUS, SIGNED) ที่เหลือ sort ได้เองอัตโนมัติจาก WPF DataGrid
- **Search/filter** — ทั้ง 3 ตาราง (Clients/Rules/Logs) มี TextBox ค้นหาแยกต่อตาราง, filter แบบ client-side จาก master list ที่เก็บไว้ (`_allClients`/`_allRules`/`_allLogs`)
- **Bulk action** — `ClientsGrid` เปิด `SelectionMode="Extended"` รองรับ ctrl/shift multi-select, ปุ่ม TOGGLE SELECTED ยิง toggle ทีละเครื่องตามลำดับ (ไม่ parallel กันปัญหา SQLite lock)
- **Offline indicator** — เพิ่ม computed property `IsOffline` ใน `ClientMachineDto` (เทียบ `LastSeenAt` เกิน 90 วิ = offline), STATUS column มี 3 สี (เขียว/แดง/เทา)
- **Remote/View client setting** — เพิ่ม `PollIntervalOverrideSeconds` (nullable) ใน `ClientMachine` model + migration `AddPollIntervalOverride`, endpoint `PUT /api/clients/{id}/settings`, Console มีปุ่ม SETTINGS เปิด `ClientSettingsWindow` แยกต่างหาก
- **Account management เพิ่มเติม** — endpoint ใหม่ `POST /api/admin/users/{id}/reset-password` (ต่างจาก `change-password` เดิมที่จำกัดแค่ตัวเอง) ให้ admin คนหนึ่ง reset password ให้ user อื่นได้ โดยตั้ง `HasDefaultPassword = true` บังคับให้เจ้าของ account เปลี่ยนเองอีกครั้งตอน login ถัดไป, Console มีปุ่ม RESET PW คู่กับ DELETE ในตาราง USERS

## Agent — เสร็จแล้ว

- **Client config ตอน install** — Inno Setup installer (Mode C) มีหน้า wizard ใหม่ถาม Server IP:Port พร้อมปุ่ม Test Connection (เช็คผ่าน `Test-NetConnection` ทาง PowerShell), เขียนค่าลง `appsettings.json` ของ Agent อัตโนมัติหลัง install เสร็จ (`CurStepChanged` ที่ `ssPostInstall`)
- **Uninstall password-gate** — Inno Setup uninstaller มี custom form ถามรหัสผ่านก่อนถอนการติดตั้งได้ (ป้องกันระดับเบา กันเด็กเล่น ไม่ใช่กัน IT ตัวจริง)
- **Output log UI แบบ terminal** — เพิ่มหน้าต่างใหม่ `LogTerminalWindow` (เปิดจากปุ่ม "OPEN LOG TERMINAL" ใน `StatusWindow`) แสดง log แบบ scroll ต่อเนื่อง auto-refresh ทุก 3 วิ พร้อมปุ่ม PAUSE/RESUME

## ยังไม่ได้ทำ (ข้ามไปตามที่ตกลงกัน)

- **Client immediately pull** — แนวคิดคือลด poll interval ชั่วคราวเหลือ 0.5 วิ นาน 2 วิ ผ่าน `PollIntervalOverrideSeconds` ที่มีอยู่แล้ว แต่ยังไม่ได้ตัดสินใจเรื่องกลไก auto-reset ค่ากลับ (Server ใช้ timer เอง vs. เก็บ `PollIntervalOverrideExpiresAt` ใน DB แล้วให้ `/api/poll` เช็ค expiry เอง) — พักไว้เป็น TODO ในอนาคต

## บั๊กที่เจอระหว่างทำรอบนี้ (กันเจอซ้ำ)

1. **Inno Setup ไม่มี `Format()`/array literal แบบ Delphi เต็มรูปแบบ** — ต้องต่อ string ด้วย `+` แทน
2. **ปีกกา `{ }` ใน Pascal string literal ธรรมดาไม่ต้อง escape เป็น `{{ }}`** — กฎ escape ใช้เฉพาะ string ที่ผ่าน `ExpandConstant()` เท่านั้น ถ้า escape ผิดที่ (เช่นใน command string ที่ยิง PowerShell) จะทำให้ logic ข้างในไม่ทำงานเงียบๆ (กรณีนี้ทำให้ `exit 0`/`exit 1` ไม่ถูกเรียกจริง ส่งผลให้ Test Connection คืนค่า "สำเร็จ" เสมอไม่ว่าจะ reachable จริงหรือไม่)
3. **`LoadStringFromFile`/`SaveStringToFile` ใช้ `AnsiString`, `StringChangeEx` ใช้ `String` (Unicode)** — เป็นคนละ type กัน ต้อง cast ไปมาเมื่อใช้ร่วมกัน
4. **EF Core migration ordering ผูกกับ timestamp ในชื่อไฟล์** — ถ้านาฬิกาเครื่อง dev คลาดเคลื่อนระหว่างสร้าง migration 2 ตัว อาจได้ migration ใหม่ที่ timestamp เก่ากว่าตัวก่อนหน้า ทำให้ EF สับสนเรื่องลำดับ (แก้ด้วยการลบ DB + migrations ทั้งหมดแล้วสร้างใหม่ในเซสชันนี้)
5. **ไฟล์ `.iss` ต้อง save เป็น UTF-8 with BOM** ถ้ามีข้อความภาษาไทยปนอยู่ ไม่งั้น encoding จะเพี้ยน (เหมือนปัญหาที่เคยเจอกับ `.ps1` มาก่อน)

---

# Update Log (2026-07-18, Agent poll bug — เจอ root cause + แก้เสร็จ)

> รายละเอียดเต็มของการไล่ debug อยู่ที่ `docs/debug-notes-agent-poll.md` — ส่วนนี้สรุปแค่ผลลัพธ์
> สุดท้ายสำหรับภาพรวมสถานะโปรเจกต์

## บริบท

หลังจากพบว่า `OnionProcOparetor.Agent` (Windows Service `OnionAgent`) poll ไป Server ไม่สำเร็จ
(`lastPollSucceeded` ค้างเป็น `false` ตลอด) เมื่อ deploy คนละเครื่องกับ Server จึง uninstall
Agent/Server ออกจากทุกเครื่องเพื่อไล่ debug ใหม่ตั้งแต่ต้น

## Root cause: Windows Service content root ผิด ทำให้หา `appsettings.json` ไม่เจอ

`Program.cs` เดิมอ่าน `appsettings.json` ตอน `WebApplication.CreateBuilder(args)` โดยอิง
`Directory.GetCurrentDirectory()` ณ ตอนนั้น แต่ Windows SCM จะสั่ง start service ด้วย working
directory เป็น `%SystemRoot%\System32` เสมอ (ไม่ใช่ install path — `sc.exe create` **ไม่มี option
ตั้ง working directory ได้เลย**) ทำให้ config หาไฟล์ไม่เจอ (โหลดแบบ `optional: true` เงียบๆ ไม่
throw) แล้ว `ServerSettings` fallback ไปใช้ default ในโค้ด (`BaseUrl = "http://localhost:8787"`)
แทนค่าจริงที่ installer เขียนไว้ให้ตอน install เสมอ — ไฟล์ `appsettings.json` เองมีค่าถูกต้องอยู่
แล้ว (installer patch ให้ถูกตั้งแต่ต้น) แต่ process ไม่เคยไปเปิดไฟล์ตำแหน่งนั้นจริง

**Verify แล้วจริง** (ไม่ใช่แค่เดา): รัน `.exe` ที่ build แล้วโดยตั้ง `-WorkingDirectory "C:\Windows"`
(จำลอง SCM) พร้อมตั้งค่าทดสอบใน `appsettings.json` → `startup-debug.log` ยืนยันว่า resolved
BaseUrl อ่านค่าถูกต้องจาก config ไม่ fallback ไป localhost

## แก้แล้วใน `OnionProcOparetor.Agent`

- `Program.cs` — ล็อค `WebApplicationOptions.ContentRootPath = AppContext.BaseDirectory` ตอนสร้าง
  builder (ก่อน config จะถูกอ่านเลย) บังคับให้หา `appsettings.json` จากตำแหน่ง .exe จริงเสมอ ไม่ว่า
  SCM จะตั้ง current directory เป็นอะไร
- `Services/StartupDiagnostics.cs` (ใหม่) — log ตรงไฟล์ ไม่พึ่ง ILogger/DI เลย เขียนที่
  `%ProgramData%\OnionProcOparetor\Agent\logs\startup-debug.log` (CurrentDirectory,
  ContentRootPath, appsettings.json เจอไหม, BaseUrl ที่อ่านได้แต่ละขั้น)
- `Services/FileLoggerProvider.cs` (ใหม่) — ILoggerProvider เขียนไฟล์ เสริมจาก console/debug/eventlog
  เดิม เขียนที่ `%ProgramData%\OnionProcOparetor\Agent\logs\agent.log` — จำเป็นเพราะรันเป็น Windows
  Service ไม่มี console ให้ดู log
- `/status` local API endpoint เพิ่ม field `serverBaseUrl` เช็คได้ตรงๆ ผ่าน `GET
  http://localhost:8788/status` ไม่ต้องเดาจากไฟล์ config

**เช็คแล้วว่าไม่ใช่ปัญหา**: publish process/`.iss` — `dotnet publish` copy `appsettings.json` ไปที่
output ถูกต้อง (verify ด้วยการ publish จริง), `.iss` copy ทั้งโฟลเดอร์ถูกต้องอยู่แล้ว ไม่ต้องแก้

## ✅ สถานะสุดท้าย: ยืนยันแล้วว่าแก้จบจริง (2026-07-18 บ่าย)

Publish ทั้ง 4 โปรเจกต์ + compile `.iss` (ผ่าน `ISCC.exe` ที่ `F:\innosetup-portable\app\ISCC.exe`
เพื่อความชัวร์ว่า build ล่าสุดจริง) + install บนเครื่อง client จริง (COM-22, ข้ามเครื่องกับ Server
ที่ `192.168.200.106:8787` ผ่าน Ethernet) แล้ว:

- `startup-debug.log` ยืนยันว่า `ContentRootPath` resolve ถูกไปที่ install path จริงแม้ SCM จะตั้ง
  `CurrentDirectory` เป็น `C:\Windows\system32` ก็ตาม และ `resolvedBaseUrl` อ่านค่าได้ถูกต้อง
- `GET http://localhost:8788/status` → `lastPollSucceeded: true` — **poll สำเร็จจริงข้ามเครื่อง**

**บทเรียนเพิ่มเติมที่เจอระหว่าง verify แล้วแก้ต่อจนจบ** (รายละเอียดเต็มอยู่ที่
`docs/debug-notes-agent-poll.md`):

- **Race condition ใน `.iss` (เจอ + แก้แล้ว)**: `CurStepChanged(ssPostInstall)` (patch
  `appsettings.json` ให้ตรงกับ IP ที่กรอกตอน install) กับ `[Run]` entry `sc.exe start OnionAgent`
  ไม่มีอะไรการันตีลำดับก่อนหลังกัน ทำให้บาง run service start ไปอ่านค่า default (`localhost`) ก่อน
  patch จะเสร็จ (ดูคล้ายบั๊ก config ตอนแรก แต่จริงๆ เป็น race condition) — แก้โดยย้าย
  create/patch config/start ทั้งหมดเข้าไปเรียงลำดับชัดเจนใน Pascal เดียวกัน
- **`OnionProcOparetor.AgentTray` มี `appsettings.json` แยกจาก `Client` (เจอ + แก้แล้ว)**: `.iss`
  เดิม patch แค่ไฟล์ของ Agent Service เท่านั้น ไม่เคย patch ของ Tray เลย ทำให้ Tray login window
  ต่อ Server ไม่ได้เสมอ (ยังชี้ localhost อยู่) เพิ่ม helper `PatchServerBaseUrl()` แล้วเรียกกับทั้ง
  สองไฟล์ + แก้ `ClientApiClient.LoginToServerAsync` ไม่ให้โชว์ raw exception message ยาวเกินไป
  จนล้นกรอบ `LoginWindow` (fixed-size, ไม่มี scroll)
- Inno Setup ไม่ touch timestamp ตอน extract ไฟล์ ทำให้เทียบ `LastWriteTime` เพื่อดูว่า
  installer/exe ตัวไหนใหม่กว่ากันไม่ชัวร์ 100% — ใช้ `Get-FileHash` เทียบ หรือ compile installer
  ใหม่สดๆ ผ่าน `ISCC.exe` (พบที่ `F:\innosetup-portable\app\ISCC.exe`) ก่อนทดสอบทุกครั้งแทนจะแม่นกว่า

**ผู้ใช้ยืนยันแล้วว่า deploy จริงใช้งานได้ครบทุกส่วน** (Agent poll + Tray login) ณ 2026-07-18 บ่าย

---

# Update Log (2026-07-19, Console UI rebuild — design system ใหม่ + Login/Dashboard restyle)

> เริ่ม rebuild UI ของ `OnionProcOparetor.Console` ใหม่ทั้งหมด แยก design system ออกจาก
> `Themes/AppTheme.xaml` เดิม (ยังไม่ลบของเดิม อยู่คู่กันไปก่อนจน migration ครบทุกหน้า)

## Theme/ — design system ใหม่ (light + amber accent)

3 ไฟล์ resource dictionary ใหม่ใน `src/OnionProcOparetor.Console/Theme/` (คนละโฟลเดอร์กับ
`Themes/` เดิม), merge เพิ่มใน `App.xaml` ต่อจาก `Themes/AppTheme.xaml`:

- `Theme/Colors.xaml` — สี BEM-style key (`Brush.Background.Primary/Card`, `Brush.Border.Default`,
  `Brush.Accent.Default/Light/Dark`, `Brush.Status.Online/Offline/Warning`, `Brush.Danger.*`,
  `Brush.Text.Primary/Secondary`)
- `Theme/Typography.xaml` — `Heading1`, `Heading2`, `Body`, `Caption`, `Label` (Segoe UI)
- `Theme/Controls.xaml` — `RoundedTextBox`, `RoundedPasswordBox`, `ButtonPrimary`, `ButtonSecondary`,
  `ButtonDanger`, `PillTabButton`, `StatusBadge` (Tag-driven: `Online`/`Offline`/`Warning`/`Danger`)

**เปลี่ยนใจกลางทาง**: เริ่มต้นขอ dark theme (`#0F1115` ฯลฯ) แต่ยกเลิกระหว่างทาง เปลี่ยนเป็น light
theme + amber accent (`#F5A623`) แทน — ค่าสีทั้งหมดใน `Theme/Colors.xaml` คือค่าที่ confirm แล้ว

## MainWindow.xaml (หน้า Login) — restyle เต็มหน้า ไม่แตะ logic

โลโก้+หัวข้อจัดกึ่งกลางเหนือการ์ดโค้งมน, ใช้ `RoundedTextBox`/`RoundedPasswordBox`/`ButtonPrimary`
ทั้งหมด — ภายหลังแยกช่อง `SERVER (IP:PORT)` เดิมเป็น `ServerIpBox` + `PortBox` สองช่องจริงตาม
mockup (รวม `ip:port` ก่อนส่งเข้า `ApiClient.SetServer()` ใน code-behind เหมือนเดิม)

## DashboardWindow.xaml (Server Console) — restyle toolbar/footer + Client tab

Rule/Logs/User tab **ยังไม่ได้ restyle เนื้อหา** (ยังใช้ style เก่าจาก `AppTheme.xaml` อยู่) — แก้
เฉพาะ toolbar บนสุด, footer, และ tab "CLIENTS" ทั้งหมด ตาม `docs/Ref-page-1.png`:

- Tab header ทั้ง 4 tab กลายเป็น pill style ไปด้วย (ผูก scope ที่ `TabControl.Resources` เลย
  กระทบทุก tab แม้เนื้อหาข้างในยังไม่ได้แก้)
- Client status badge (`ClientStatusBadge`, local style ใน `DataGrid.Resources`) จำลอง logic 3
  สถานะเดิมของ status ellipse (`IsEnabled`/`IsOffline`, ไม่มี property `Status` จริงใน
  `ClientMachineDto`) ผ่าน `StatusBadge`'s `Tag`: Online (เขียว) / Offline (เทา) / Danger (แดง — เพิ่ม
  Tag `"Danger"` ใหม่ใน `Theme/Controls.xaml` เพื่อแทนสถานะ "reachable แต่ถูก disable" เดิม)

**เพิ่มฟีเจอร์ Delete client จริง** (เดิมไม่มีเลย ไม่ใช่แค่ restyle):

- `OnionProcOparetor.Server/Program.cs` — เพิ่ม `DELETE /api/clients/{id}` (pattern เดียวกับ
  `DeleteRule`/`DeleteUser`) ปลอดภัยเพราะ `LogEntry.ClientGuid` ไม่มี FK ผูกกับ `ClientMachine`
  โดยตั้งใจ (comment เดิมในโค้ดบอกไว้ตรงๆ ว่าเผื่อ client ถูกลบ) ลบแล้ว log เก่ายังอยู่ครบ
- `ApiClient.DeleteClientAsync()` + `DashboardWindow.DeleteClientButton_Click` (confirm dialog
  แบบเดียวกับปุ่ม Delete อื่น) + ปุ่ม DELETE กลับเข้า Action column ของ Clients tab

**Auto-refresh + selection fix**: `DashboardWindow` มี `DispatcherTimer` เรียก `LoadAllDataAsync()`
ทุก 10 วิ (หยุดเองตอนปิดหน้าต่าง) ตามคำขอ ไม่ต้องกด REFRESH เอง — ผลข้างเคียงที่เจอคือ multi-select
ใน Clients grid (ใช้กับ "TOGGLE SELECTED") หลุดทุกครั้งที่ reload ข้อมูล แก้ใน
`ApplyClientsFilter()` ให้จำ `Id` ของแถวที่เลือกไว้ก่อน reload แล้วเลือกคืนให้หลัง `ItemsSource`
เปลี่ยน (ครอบคลุมทั้ง auto-refresh, กด REFRESH เอง, และตอนพิมพ์ค้นหา)

## สถานะ ณ ตอนนี้ (2026-07-19)

Login page + Dashboard toolbar/footer/Client tab ใช้ design system ใหม่ครบแล้ว, build ผ่านทั้ง
solution 0 error. **ยังไม่ได้ทำ**: restyle เนื้อหา Rule/Logs/User tab (รอบถัดไป), small button
style variant (`ButtonSecondarySmall`/`ButtonDangerSmall` — ตอนนี้ override Padding/FontSize
ตรงจุดใช้งานแทน), ลบ `Themes/AppTheme.xaml` เดิมออก (รอ migration ครบทุกหน้าก่อน)

---

# Update Log (2026-07-22, SignalR real-time layer + Broadcast message)

## SignalR เสริมคู่ขนานกับ HTTP polling เดิม (commit `ba4b255`)

ไม่ได้แทนที่ poll-based เดิม — ตัดสินใจเดิมเรื่อง "poll เท่านั้น กัน client ต้องเปิด port" ยังอยู่
SignalR เป็นแค่ **ชั้นเสริม** ให้ command ไปถึง Agent เร็วขึ้นถ้าต่อติดอยู่ ถ้า SignalR ใช้ไม่ได้
poll-based เดิมยัง fallback ให้ครบทุก flow

- **Server**: `LabHub` (agent-guid groups + console group), model ใหม่ `ClientCommand` + migration,
  `POST /api/commands/{clientGuid}` (สร้าง command แล้ว push ทันทีถ้า Agent connected),
  `POST /api/commands/{commandId}/ack` (**idempotent โดยตั้งใจ** — Agent อาจ ack ซ้ำจากทั้ง
  SignalR path และ poll fallback path พร้อมกัน), `pendingCommands` เพิ่มเข้า
  `GET /api/poll/{clientGuid}` เป็น fallback field (backward-compatible)
- **Agent**: `LabHubConnection` (exponential backoff reconnect, `RegisterAgent`),
  `CommandProcessor` เป็น **จุดเดียว** ที่ dedup + ack ทั้ง command จาก SignalR (`ReceiveCommand`
  handler) และจาก poll fallback loop — key ด้วย `commandId` การันตี exactly-once execution
  ไม่ว่า path ไหนมาถึงก่อน
- **Console**: `LabHubClient` subscribe `ClientStatusChanged` สำหรับ live dashboard update,
  `RegisterConsole`, `ApiClient.SendCommandAsync` (infrastructure พร้อมใช้ ยังไม่มีปุ่ม UI ตอนนั้น)

## BroadcastMessage command (commit `2547e0b`, **NOT YET INTEGRATION TESTED ตอน commit**)

- Named pipe ใหม่ (`AgentTray.Broadcast`) สำหรับ Agent → AgentTray push โดยเฉพาะ เพราะ
  `ClientApiClient` (port 8788) เดิมเป็น pull-only ไม่รองรับ push
- Console: broadcast dialog + ปุ่มใน Client Management
- Agent: `CommandProcessor` handle `BroadcastMessage`, ส่งต่อผ่าน `AgentTrayNotifier`
  (**fail-secure**: ไม่ throw แม้ Tray ไม่ได้รันอยู่)
- ตอน commit ระบุไว้ชัดว่ายังไม่ verify: named pipe ACL ข้าม service/user session boundary,
  end-to-end popup delivery ทั้ง SignalR path และ poll fallback path

---

# Update Log (2026-07-23, Remote power control + missing-client detection — ยังไม่ commit)

> งานกลุ่มนี้อยู่ใน working tree ตอนที่บันทึกนี้เขียน (`git status` แสดงเป็น modified/untracked)
> ยังไม่ได้ commit — สรุปไว้กันหลงทางว่าทำอะไรไปแล้วบ้าง

## Remote command ใหม่ใน `CommandProcessor` (Agent)

- **`Shutdown`/`Restart`** — เรียกผ่าน `WindowsPowerControl.cs` (ใหม่, P/Invoke `ExitWindowsEx`
  ตรงจาก Agent เพราะเป็น system-level operation รันได้จาก Session 0) มี delay เตือน user ก่อน
  (`PowerActionWarningDelaySeconds`) แบบ fire-and-forget (ไม่ await กัน command อื่นที่เข้ามา
  ระหว่างรอถูก block) — **ข้อควรระวังที่ comment ไว้ในโค้ด**: ต้องเปิด `SE_SHUTDOWN_NAME`
  privilege ก่อนเรียก `ExitWindowsEx` เสมอ ไม่งั้น fail เงียบๆ (`ERROR_PRIVILEGE_NOT_HELD`) แม้
  Windows Service จะรันเป็น LocalSystem อยู่แล้วก็ตาม (ไม่ได้ enable privilege ให้อัตโนมัติ)
- **`LockWorkstation`/`UnlockWorkstation`** — ต่างจาก Shutdown/Restart ตรงที่เป็น **per-session
  operation** ต้องส่งต่อผ่าน AgentTray (ดู `AgentTrayNotifier.SendLockWorkstationAsync`) ไม่เรียก
  ตรงจาก Agent (Session 0) ได้ — AgentTray ใหม่มี `LockScreenWindow` (full-screen, ปลดได้ทางเดียว
  คือ `UnlockWorkstation` command จาก Console เท่านั้น)
- **`UpdateSettings`** — ตั้ง `PollIntervalOverrideSeconds` มีผล**ทันที** ไม่ต้องรอ poll รอบถัดไป

## `MissingClientMonitorService` ใหม่ (Server)

Background scan ทุก 30 วิ หาเครื่องที่ `IsEnabled=true` (กำลังถูก enforce rules อยู่) แต่ Agent
หายไปนานผิดปกติ (`ClientMachine.IsMissingUnexpectedly` / `MissingUnexpectedlyThresholdSeconds`
ฟิลด์ใหม่) — จำเป็นเพราะเครื่องที่หายจริงจะไม่ poll เข้ามาแจ้งตัวเองอีกเลย เก็บ
`_lastKnownMissingState` ต่อเครื่องเพื่อ broadcast แค่ตอน**เปลี่ยนสถานะ**เท่านั้น (เพิ่งหาย/กลับมา)
ไม่ spam ทุกรอบสแกน — ชั้นเสริมเท่านั้น ถ้า service นี้หยุด Console ยังเห็นค่าล่าสุดผ่าน
`GET /api/clients` (auto-refresh 10 วิ) อยู่ดี แค่ไม่มี push แจ้งเตือนทันที

## Console/Server ฝั่งอื่นที่แก้ไปพร้อมกัน

`DashboardWindow`/`ClientMachineDto`/`ClientSettingsWindow` ปรับรองรับฟิลด์ใหม่จาก
`MissingClientMonitorService` และปุ่ม/dialog สำหรับ command ใหม่ทั้ง 4 ตัวข้างต้น

---

# Update Log (2026-07-23–24, Installer overhaul — WiX migration + 3 บั๊กร้ายแรง + Server logging)

> ทำในเซสชัน Claude Code แยกต่างหาก เริ่มจาก "OnionAgent service ไม่ขึ้น/crash" แล้วขุดลึกจนเจอ
> ต้นตอจริง 3 เรื่องที่ independent กัน ทุกจุดยืนยันด้วย log จริงก่อนแก้ (ไม่เดา) รายละเอียดเต็ม
> อยู่ใน conversation history — ที่นี่สรุปเฉพาะผลลัพธ์และวิธีแก้

## เปลี่ยน installer จาก Inno Setup → WiX v5

ไฟล์ใหม่ `installer/OnionProcOparetor.wxs` (Inno Setup เดิม `installer/OnionProcOparetor.iss`
ยังเก็บไว้เป็น fallback คู่ขนาน, `installer/MyLabGuard.iss` เก่าถูกลบทิ้ง) — ยังคง 3-mode เดิม
(Server+Console / Console-only / Client Agent) ผ่าน custom wizard dialog เดียวกัน

**เหตุผลที่เปลี่ยน**: ไม่ได้ระบุไว้ชัดในบทสนทนา (เป็นการตัดสินใจที่มาก่อนเซสชันนี้) — แต่พบว่า
WiX v5 ต้อง pin เวอร์ชันไว้ที่ 5.0.2 อย่างเคร่งครัด (`installer/.wix/extensions/`) เพราะ v6/v7
ต้องยอมรับ paid Open Source Maintenance Fee EULA ก่อนถึงจะ build ได้

## บั๊กที่ 1: ADDLOCAL/CostFinalize — Client Agent ไม่เคยถูกติดตั้งจริงเมื่อเลือกผ่าน wizard

**อาการ**: `sc.exe create OnionAgent` สำเร็จ แต่ `.exe` ไม่มีอยู่จริง → SCM error 7000/1053
"the system cannot find the file specified" ทุกครั้ง

**Root cause** (ยืนยันจาก MSI verbose log `/l*vx` จริง): WiX's `InstallUISequence` รัน
`CostFinalize` อัตโนมัติทันทีหลัง WelcomeDlg **ก่อน** ModeDlg จะแสดงด้วยซ้ำ ตอนนั้น `MODE`
property ยังเป็นค่า default `'1'` (Server+Console) — CostFinalize snapshot ผลการเลือก feature
ตอนนั้นลง `ADDLOCAL` property และ**เมื่อ `ADDLOCAL` ไม่ว่างแล้ว Windows Installer จะเลิกคำนวณ
Feature selection จาก Level/Condition ใหม่ไปตลอด transaction** (พฤติกรรมมาตรฐานของ MSI, ไม่ใช่
WiX bug) — ดังนั้นต่อให้ผู้ใช้เลือก Client Agent (`MODE=3`) ทีหลังใน wizard จริง ก็ไม่มีผลอะไรกับ
feature selection ที่ snapshot ไปแล้ว

**ทางเลือกที่พิจารณาแล้วไม่ใช้**: `AddLocal`/`Remove` ControlEvent ตรงๆ — เอกสาร Microsoft
ยืนยันว่า `AddLocal` **override Feature ที่ `Level=0` ไม่ได้เลย** (ซึ่ง Feature ทุกตัวเป็นแบบนั้น
ตอน CostFinalize คำนวณผิดไปแล้ว)

**วิธีแก้ที่ใช้จริง** (documented Microsoft fix สำหรับสถานการณ์นี้โดยเฉพาะ): เพิ่ม
`<Publish Event="DoAction" Value="CostFinalize"/>` เป็นตัวแรกบนปุ่ม Next ของ `ModeDlg` — บังคับ
ให้ CostFinalize คำนวณใหม่ตอนที่ `MODE` มีค่าถูกต้องแล้วจริง **ไม่แตะ** `Level`/`Condition`
authoring เดิมเลย เพราะกลไกเดิมถูกต้องอยู่แล้วสำหรับ silent install (`msiexec /qn MODE=3` ไม่มี
ปัญหานี้ เพราะ property ถูก set ก่อน CostFinalize รอบเดียวเสมอ)

**Verify**: ติดตั้งจริงทั้ง 3 mode, เช็ค MSI log ยืนยัน `Feature: ClientFeature; Request: Local`
(MODE=3), `ServerFeature/ConsoleFeature: Local, ClientFeature: Null` (MODE=1), เฉพาะ
`ConsoleFeature: Local` (MODE=2) — ตรงทุก mode, ไฟล์ถูก copy จริง service รันได้จริงและ poll
สำเร็จ (`lastPollSucceeded: true`)

## บั๊กที่ 2: ทุก build มี FileVersion เท่ากันหมด (`1.0.0.0`) — install ทับแล้วได้ไฟล์เก่า

**อาการที่ user รายงาน**: install สำเร็จ แต่ `Agent.exe` ที่ได้เป็นเวอร์ชันเก่าก่อน mockup ใหม่

**Root cause** (ยืนยันจาก MSI File table โดยตรง): 4 โปรเจกต์ไม่มี `<FileVersion>`/
`<AssemblyVersion>`/`<Version>` ใน `.csproj` เลย — .NET SDK เลย default เป็น `1.0.0.0` ทุกครั้ง
ไม่ว่าจะแก้ source ไปแค่ไหน **Windows Installer's `InstallFiles` action ไม่ overwrite ไฟล์ที่มี
อยู่แล้วถ้า version ใหม่ไม่ได้ "ใหม่กว่า" ไฟล์เดิมอย่างชัดเจน** — พอทุก build เท่ากันหมด (1.0.0.0
ตลอด) รวมกับไฟล์เก่าที่ค้างจาก product ที่ uninstall ไม่สมบูรณ์ (ดูบั๊กที่ 3) ทำให้ไฟล์ใหม่ไม่เคย
ได้ copy ทับจริง

**วิธีแก้**: เพิ่ม `src/Directory.Build.props` ใหม่ ตั้ง `<Version>`/`<AssemblyVersion>`/
`<FileVersion>` ร่วมกันทั้ง 4 โปรเจกต์ (ปัจจุบัน `1.1.2.0`, sync กับ `installer/OnionProcOparetor.wxs`'s
`Package Version` เสมอ — **ต้อง bump คู่กันทุกครั้งที่ release**) — verify แล้วว่า MSI File table
แสดง version ที่ถูกต้องในทุก .exe/.dll หลังแก้

## บั๊กที่ 3: uninstall-password gate marker file ทำให้ `msiexec /x` fail ด้วย MSI error 1324

**Root cause**: marker file `.uninstall-auth-ok` (ขึ้นต้นด้วยจุด) ทำให้ MSI's `Signature`/
`DrLocator` table (ที่ `FileSearch` compile ลงไป) validate ไม่ผ่าน (ยังใช้ legacy 8.3-shortname
validation) — ยืนยันจาก [WiX GitHub issue #9114](https://github.com/wixtoolset/issues/issues/9114)
ที่ยืนยันปัญหาการ validate ชื่อไฟล์แบบนี้ตรงๆ

**วิธีแก้**: เปลี่ยนชื่อเป็น `uninstall-auth-ok.flag` (ไม่ขึ้นต้นด้วยจุด) ทั้งใน `FileSearch Name`
และ `-FlagPath` argument ของ `CheckUninstallPasswordHeadless` CA — verify แล้วว่า `msiexec /x`
ผ่าน `LaunchConditions` ปกติและ "Removal completed successfully" จริง

## เครื่องทดสอบสะสมมลพิษ MSI registration หนักมาก (แยกจาก 3 บั๊กข้างบน — ไม่ใช่บั๊กในโค้ด)

ระหว่างทดสอบซ้ำหลายรอบ (version bump ต่อเนื่องเพื่อ trigger major-upgrade cascade) เจอว่าเครื่อง
dev มี **orphaned product registration ค้างจากการทดสอบก่อนหน้าบทสนทนานี้ด้วย** (ProductCode บาง
ตัวเก่ากว่าทั้งเซสชัน) ทำให้ shared-component reference count ใน
`HKLM:\...\Installer\UserData\S-1-5-18\Components\*` ค้าง ส่งผลให้ `[#FileKey]` resolution ของ
CustomAction บางตัว (`SetupClientPs1`, `RunSetupPs1`) fail เป็น path ว่างแม้ product ที่อ้างอิงจะ
ตายไปแล้วจริง (ไม่มี ARP entry เหลือ) — แก้ด้วยการลบ stale value เฉพาะที่ยืนยันแล้วว่า
ProductCode เจ้าของตายจริง (104 component ที่เช็ค, ลบไป 208 stale value) ไม่ใช่ปัญหาที่จะเจอบน
เครื่องที่ไม่เคยผ่านการทดสอบซ้ำๆ แบบนี้มาก่อน

## เพิ่ม file logging ให้ Server (`OnionProcOparetor.Server`)

ระหว่างแก้บั๊กพบว่า **Server ไม่มี file logging เลย** (ต่างจาก Agent ที่มี pattern นี้อยู่แล้วตั้งแต่
บั๊ก content-root เมื่อ 18 ก.ค.) — เพิ่ม `Services/StartupDiagnostics.cs` +
`Services/FileLoggerProvider.cs` (pattern เดียวกับ Agent เป๊ะ) เขียนที่
`%ProgramData%\OnionProcOparetor\Server\logs\` (`startup-debug.log`, `server.log`) พร้อมบังคับ
`ContentRootPath = AppContext.BaseDirectory` เหมือน Agent (กัน `appsettings.json` หาไม่เจอถ้ารัน
เป็น Windows Service) และครอบ `db.Database.Migrate()` ด้วย log ก่อน-หลังโดยเฉพาะ (จุดที่พบว่าเป็น
สาเหตุ SCM error 1053 ตอนทดสอบบนเครื่องที่มี disk-freeze/restore-on-reboot software — ซอฟต์แวร์
ประเภทนี้แทรกแซง OS-level file locking ของ SQLite ทำให้ `Migrate()` ค้างแบบไม่มี exception เลย —
**ยืนยันแล้วว่าไม่ใช่บั๊กใน installer/code, เกิดจากทดสอบผิดประเภทเครื่อง** เพราะ Server ไม่ได้
ตั้งใจให้รันบนเครื่องที่มี freeze software)

## สถานะสุดท้าย

Build `installer\output\OnionProcOparetor-Setup.msi` เวอร์ชัน **1.1.2.0** — ยืนยันถูกต้องแล้วทั้ง
package-level (File table version, ADDLOCAL/CostFinalize ControlEvent, uninstall marker filename)
และ live install/uninstall cycle จริงหลายรอบ **ยังไม่ commit เข้า git** (`git status` ยังแสดง
`installer/OnionProcOparetor.wxs`, `src/Directory.Build.props`,
`src/OnionProcOparetor.Server/Services/{StartupDiagnostics,FileLoggerProvider}.cs` เป็น
untracked/modified)

## Known Issues / TODO ใหม่จากรอบนี้

1. **`installer/MyLabGuard.iss` ถูกลบแล้ว, `installer/OnionProcOparetor.iss` (Inno Setup) ยังอยู่
   คู่กับ WiX** — ยังไม่ตัดสินใจว่าจะเลิกใช้ตัวไหนถาวร หรือเก็บ Inno ไว้เป็น fallback ต่อไป
2. **`Directory.Build.props`'s `Version` กับ `.wxs`'s `Package Version` ต้อง sync มือทุกครั้ง** —
   ยังไม่มีกลไกอัตโนมัติกันลืม bump คู่กัน (เสี่ยงเจอบั๊กที่ 2 ซ้ำถ้าลืม)
3. **BroadcastMessage (2547e0b) ยังไม่เคย integration test บนเครื่องจริง** ตามที่ commit message
   ระบุไว้เอง — ค้างมาตั้งแต่ 22 ก.ค.
4. ~~**Remote power control (`Shutdown`/`Restart`/`LockWorkstation`) ยังไม่ commit และยังไม่เคย
   ทดสอบยืนยันผลจริง**~~ — **แก้แล้ว**: ตรวจโค้ดจริงยืนยันครบทุกจุดแล้ว (ดู Update Log ถัดไปด้านล่าง)
   ยังเหลือแค่ evidence ว่า `ExitWindowsEx`/`SE_SHUTDOWN_NAME` ทำงานจริงบนเครื่อง lab จริง (ยังไม่มี
   ใครกด Shutdown/Restart ผ่าน Console จริงๆ สักครั้ง — โค้ดถูกต้องตาม static review แต่ runtime
   ยังไม่ verify)

---

# Update Log (2026-07-24, Lock Screen + Bulk Actions + Settings-via-command + Custom Command fix — verify แล้วว่าถูกต้องครบ)

> งาน 5 ส่วนนี้ (Lock Screen จริง, รวม bulk action เข้า panel เดียว, Settings force ผ่าน command,
> แก้บั๊ก Custom Command, UI cleanup) ทำเสร็จในอีก session หนึ่งก่อนหน้านี้ — เข้าใจผิดว่า MSI ไม่ได้
> แพ็คไฟล์ใหม่เข้าไป (ที่จริงคือบั๊ก FileVersion=1.0.0.0 ที่แก้ไปแล้วใน Update Log ก่อนหน้า) เซสชันนี้
> ตรวจโค้ดจริงทุกไฟล์เทียบกับ spec เดิมทีละส่วน **ยืนยันว่าถูกต้องครบทั้ง 5 ส่วน ไม่มีอะไรต้องแก้เพิ่ม**

## ส่วนที่ 1: Lock Screen จริง (แทนที่ `LockWorkStation()` เดิม) — ยืนยันถูกต้อง

- `CommandProcessor.cs`: `LockWorkstation`/`UnlockWorkstation` ส่งผ่าน `AgentTrayNotifier` (
  `ShowLockScreen`/`HideLockScreen`) ไม่เรียก `user32.dll LockWorkStation()` ตรงๆ อีกแล้ว
- `LockScreenWindow.xaml(.cs)` ใหม่ (AgentTray): เต็มจอสีดำ (`WindowStyle="None"`,
  `WindowState="Maximized"`, `Topmost="True"`), ข้อความเป็น `const string LockMessage` แก้ง่ายจุด
  เดียว, `WH_KEYBOARD_LL` hook block Alt-Tab/Win/Ctrl-Esc/Alt-F4 (Ctrl-Alt-Del ยอมรับว่ากันไม่ได้
  ตามดีไซน์ Windows), delegate เก็บเป็น field กัน GC, unhook ทันทีใน `ForceClose()`, `OnClosing`
  cancel ทุกทางที่ไม่ใช่ `ForceClose()` — ปลดล็อกได้ทางเดียวจาก Console เท่านั้นจริง
- `WorkstationLocker.cs` เก่าถูกลบทิ้งจริง (verify แล้วว่าไม่เหลือในโปรเจกต์)
- Console: ปุ่ม Lock/Unlock แยก 2 ปุ่มจริง (ไม่ใช่ toggle เดียว) อยู่ใน `ActionPanelWindow`

## ส่วนที่ 2: รวม bulk actions เข้า `ActionPanelWindow` เดียว — ยืนยันถูกต้อง

`ActionPanelWindow` มี 2 constructor (single-client / bulk) ใช้ panel เดียวกัน bulk mode มี radio
"เครื่องที่เลือกไว้ (N)" vs "ทุกเครื่อง (N)" header แสดง `"Applying to N selected machines"` ตรงคำ
ที่ตกลงไว้เป๊ะ ปุ่มแยกเดิม (Toggle Selected, bulk Shutdown/Restart) ถูกลบออกจริง (grep หาไม่เจอ
เหลือแล้ว) เหลือปุ่ม ACTIONS เดียวบน header เรียก `BulkActionsButton_Click`

## ส่วนที่ 3: Settings force ผ่าน command ทันที — ยืนยันถูกต้อง

`ClientSettingsWindow.ApplySettingAsync`: `SendCommandAsync` (SignalR) เป็นทางหลัก ไม่รอ ack ก่อน
ปิด dialog, เขียน DB แบบ fire-and-forget คู่ขนานไปด้วย, fallback ไปรอ DB write จริงถ้า
`SendCommandAsync` ล้มเหลว — poll ยังเป็น safety-net เดิมสำหรับเครื่องที่ไม่ได้ connect SignalR อยู่

## ส่วนที่ 4: บั๊ก Custom Command ใน Rule — ยืนยัน root cause + fix ถูกต้อง

Root cause ที่รายงานไว้ตรงกับโค้ดจริง: `Process.Start` เดิมใช้ `UseShellExecute=false` เสมอ เรียก
`CreateProcess` ตรง รันได้แค่ native `.exe` เท่านั้น สคริปต์ `.bat/.cmd/.ps1` โดน `Win32Exception`
เงียบๆ (catch ไว้ log แค่ฝั่ง Agent เอง ไม่มีอะไรแจ้งครูที่ Console เลย) — `Rule.KillProcess` (bool)
กับ `Rule.ActionCommand` (string) เป็น 2 field อิสระทำงานคู่ขนานกันมาตั้งแต่แรก ไม่ใช่ mutually
exclusive ไม่มี fallthrough แปลกๆ อย่างที่สงสัยไว้ตอนแรก — `BuildActionCommandProcessStartInfo`
ใหม่ dispatch ตาม extension ถูกต้อง (`.bat/.cmd` → `cmd.exe /c`, `.ps1` → `powershell.exe -NoProfile
-ExecutionPolicy Bypass -File`, อื่นๆ รันตรงเหมือนเดิม) พร้อม log ก่อนรันเสมอเพื่อ audit trail

## ส่วนที่ 5: UI cleanup — ยืนยันถูกต้อง

Clients tab DataGrid เหลือ STATUS/MACHINE NAME/CLIENT GUID/LAST SEEN + ACTIONS column เดียวตรง
spec เป๊ะ header เหลือ search box + ปุ่ม ACTIONS + BROADCAST MESSAGE (global toolbar บนสุด — Toggle
Global/Refresh — ไม่ถูกแตะ เพราะเป็นคนละส่วนกับที่ spec ขอให้ตัด)

## สถานะ

`dotnet build` ทั้ง solution ผ่าน 0 error (warning เดิมเท่านั้น) — rebuild installer แล้ว
(`installer\output\OnionProcOparetor-Setup.msi`, v1.1.2.0 เดิม เพราะ `Directory.Build.props`'s
FileVersion fix ครอบคลุมไฟล์เหล่านี้อยู่แล้ว ไม่ต้อง bump version ซ้ำ)
