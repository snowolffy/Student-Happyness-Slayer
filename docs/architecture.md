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

## รูปแบบการติดตั้ง (เหมือน RRRx) — 2 MSI แยกขาดกัน (2026-08-10)

เดิมเป็น MSI เดียวมี 3 mode เลือกตอน install (ผ่าน `ModeDlg` + `MODE` property) — เจอบั๊ก
feature-selection สองรอบจากดีไซน์นี้ (ADDLOCAL snapshot ผิดจังหวะตอน wizard, และ
`REMOVE="ALL"` หายจาก Level Condition ตอน uninstall) และเครื่อง lab นักเรียนก็ไม่ควรเห็น
ตัวเลือก Server/Console เลยด้วย — แยกเป็น 2 MSI ที่ build/ติดตั้ง/ถอนแยกขาดจากกันจริง
(คนละ ProductCode/UpgradeCode คนละ INSTALLFOLDER คนละ ARP entry ติดตั้งพร้อมกันบนเครื่อง
เดียวได้โดยไม่ชนกัน) ดู `installer/OnionProcOparetor.Agent.wxs` /
`installer/OnionProcOparetor.ServerConsole.wxs` header comment สำหรับรายละเอียด

### OnionProcOparetor-Server-Console-Setup.msi
- ติดตั้งบนเครื่อง server (เครื่องเดียวกับ RRRx Endpoint Manager) หรือเครื่องครู/แอดมิน
- มีทั้ง `MyLabGuard.Server` (Windows Service: API + SQLite) และ
  `MyLabGuard.Console` (GUI ที่ผูก localhost อัตโนมัติ แต่พิมพ์ IP อื่นก็ได้)
- **ไม่ยุ่งกับตัว RRRx เลย** แค่ใช้เครื่องเดียวกันเป็นที่ตั้ง (คนละ process, คนละ port)
  เพราะ RRRx ไม่เปิด API สาธารณะให้เขียนต่อ
- ไม่มี wizard page ให้กรอกอะไรเพิ่ม (ไม่มี uninstall-password gate ด้วย — เครื่อง
  ครู/admin ไม่ใช่ shared login แบบเครื่อง lab)

### OnionProcOparetor-Agent-Setup.msi
- ติดตั้งบนเครื่องนักเรียน x40
- `MyLabGuard.Client` (Windows Service, SYSTEM) — ตัวจริงที่ detect/บังคับใช้ ไม่มี UI
- `MyLabGuard.ClientTray` — **คนละ process จาก Service** รันตอน user login, โชว์ที่
  system tray, double-click ต้อง **login ด้วย password ก่อน** ถึงจะเห็น
  status/log/refresh ได้ ปิด tray.exe ได้จาก Task Manager แต่ **Service เบื้องหลัง
  ยังทำงานต่อปกติ** เพราะแยก process กันเด็ดขาด
- Wizard มีหน้า Server IP:Port (+ Test Connection) และหน้า uninstall password
  (optional, กันนักเรียนถอนโปรแกรมเองจาก shared login)

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

# Current Implementation Status (อัพเดตล่าสุด: 2026-08-11)

> เขียนใหม่ทั้งหมด (ไม่ใช่ copy จาก dated log เก่า) ตอน stabilization cleanup pass — สรุปสถานะจริง
> ของโค้ด ณ commit `d10ab06` ("VNC full-screen mouse/keyboard control (Step 4h)") ซึ่งเป็น baseline
> ที่เสถียรที่สุดตอนนี้ อ่านเพื่อเข้าใจภาพรวมระบบได้ในหน้าเดียว ไม่ต้องไล่อ่าน dated log ทั้งหมด
> (ย้ายไปไว้ที่ `docs/architecture-log.md` แล้ว — อ่านที่นั่นถ้าต้องการบริบท/เหตุผลของแต่ละรอบพัฒนา)
>
> **หมายเหตุ**: มีความพยายามทำ 3 ฟีเจอร์ใหญ่ต่อจาก baseline นี้ (Physical Input Block ระหว่างควบคุม VNC,
> ย้าย Console/Agent↔Server ทั้งหมดไปใช้ SignalR เป็นหลัก, Phase 5 Internet/website restriction) แต่เริ่ม
> บั๊กสะสมจนคุมยาก เลย **ถูก revert กลับมาที่ baseline นี้** (งานทั้งหมดยังไม่หายไปไหน เก็บไว้ใน `git
> stash` เผื่อกลับไปหยิบบางส่วนมาทำใหม่แบบระมัดระวังกว่าเดิมทีหลัง)
>
> **อัพเดต 2026-08-11 ("Stable 1" epic)**: เพิ่ม Agent Bypass Toggle, Rule Per-Agent Override (ดูหัวข้อ
> ที่เกี่ยวข้องด้านล่าง), แยก installer เดิม (1 .msi 3 โหมด) เป็น 2 .msi แยกตาม role, และแก้บั๊กจริงของ
> uninstall flow ทั้งชุด (ดูหัวข้อ Auth & install) — live-tested บนเครื่องนี้จริงทั้ง 2 installer แล้ว

## ภาพรวมสถาปัตยกรรม

4 โปรเจกต์หลัก + 1 ตัวช่วยติดตั้ง ทำงานร่วมกันแบบนี้:

- **`OnionProcOparetor.Server`** — Windows Service (Minimal API + SignalR hub, .NET 10, EF Core/SQLite)
  รันบนเครื่อง server เดียวกับ RRRx Endpoint Manager คือ single source of truth ของ rules/client
  state/logs ทั้งหมด
- **`OnionProcOparetor.Agent`** — Windows Service (LocalSystem) รันบนเครื่องนักเรียนแต่ละเครื่อง (x40)
  ตัวจริงที่ detect/บังคับใช้ rule ไม่มี UI
- **`OnionProcOparetor.AgentTray`** — process แยกจาก Agent รันในทุก interactive user session ทำงานที่
  ต้องอยู่ใน session จริง (ไม่ใช่ Session 0 ของ Windows Service) เช่น เปิด winvnc.exe, แสดง Lock Screen,
  broadcast popup message
- **`OnionProcOparetor.Console`** — WPF desktop app ที่ครู/แอดมินใช้ควบคุมทุกอย่าง
- **`OnionProcOparetor.UninstallHelper`** — ตัวช่วยเล็กๆ สำหรับ uninstall flow ที่ต้อง password gate
  (binary เดียวกัน bundle เข้าทั้ง 2 .msi — แยกพฤติกรรมด้วย argument ที่แต่ละ .msi ส่งมาเอง ไม่ใช่แยก
  binary คนละตัว)

**Transport**: HTTP REST (poll-based, client ดึงเองทุก ~30s) เป็นหลักสำหรับ Console↔Server CRUD และ
Agent↔Server config sync — SignalR เป็นชั้นเสริมคู่ขนาน (ไม่ใช่ตัวแทนหลัก ณ baseline นี้) สำหรับ live
client-status push, remote command delivery (dual-path กับ poll fallback), และ VNC session negotiation
(RegisterAgent/RegisterConsole/RelayVncSessionReady/RelayVncSessionFailed)

## Rule enforcement (publisher-based)

- Match ด้วย Authenticode digital signature เป็นหลัก, `FileVersionInfo.CompanyName` เป็น fallback
  (`RequireSignedMatch` เลือกได้)
- `ProcessNameContains` (optional) narrow เพิ่มจาก publisher เฉยๆ กันปัญหาจริงที่เคยเจอ (publisher
  เดียวกันหลายโปรแกรม เช่น Microsoft Corporation เซ็นทั้ง Notepad และ VS Code)
- Action: kill process และ/หรือรัน `ActionCommand` (script/exe อื่น) ตามที่ตั้งไว้ต่อ rule
- ตรวจจับผ่าน WMI `__InstanceCreationEvent` (event-driven, จับ process ใหม่ทันที) + periodic full-scan
  ทุก 10 วิ (จับ process ที่เปิดค้างอยู่ก่อน service เริ่ม/ก่อน rule ถูกเพิ่ม)
- Fail-secure: poll ไม่สำเร็จ = ใช้ rules/enabled ล่าสุดที่มีต่อไป ไม่ fallback ปิดระบบ
- **Rule Per-Agent Override** — override สถานะ enabled ของ rule ระดับ global ได้แยกต่อเครื่อง (ผ่าน
  "Config Rule" action ใน `ActionPanelWindow`) override ชนะ global เสมอ 100% ไม่มี merge logic
  คำนวณที่ `GET /api/poll/{clientGuid}` (ต่อ client ตรงๆ ไม่มี logic ฝั่ง Agent เพิ่ม — poll endpoint
  ส่ง final rule list ที่ resolve แล้วเหมือนเดิมทุกอย่าง)

## Remote commands (จาก Console ผ่าน `ActionPanelWindow`/header buttons)

`BroadcastMessage`, `LockWorkstation`/`UnlockWorkstation` (full-screen lock ปลดได้ทาง Console เท่านั้น
ไม่ใช่ Win+L ธรรมดา), `Shutdown`/`Restart` (เตือน user ล่วงหน้า 10 วิ), `UpdateSettings`,
`StartVncSession` — ทุกคำสั่งส่งผ่าน dual-path (SignalR ทันที + poll fallback ถ้า client ไม่ connect
SignalR อยู่) ประมวลผลที่ `CommandProcessor` เดียวกันไม่ว่าจะมาทางไหน (dedup ด้วย commandId)

## Agent Bypass Toggle

ปุ่มใน AgentTray's login-gated status panel ให้ตัด Agent ออกจาก Server ได้เต็มรูปแบบ (ทั้ง SignalR
และ HTTP poll) แบบเงียบๆ โดยไม่ต้อง uninstall — state เก็บ in-memory บน Agent process เท่านั้น (ไม่ใช่
AgentTray, ไม่ persist ข้าม restart — เปิดเครื่องใหม่ = กลับมา connected เสมอ) ไม่มีการแจ้ง Server ว่า
เครื่องถูก bypass อยู่

## VNC remote screen control

- **Live View grid** (DashboardWindow's LIVE VIEW tab) — multi-session pool + thumbnail mode ดูหลาย
  เครื่องพร้อมกันแบบ CCTV-wall, auto-stop session ที่ idle
- **Full-screen interactive control** (SCREEN VIEW tab) — mouse/keyboard forwarding เต็มรูปแบบ พร้อม
  coordinate scaling, scroll wheel, Alt+F4 handling (รู้ข้อจำกัด: Alt+Tab/ปุ่ม Windows เป็น global
  hotkey ระดับ shell กันไม่ได้จาก routed event ของ WPF)
- winvnc.exe (UltraVNC) รันจาก AgentTray เท่านั้น (Session 0 ของ Agent capture หน้าจอไม่ได้) firewall
  rule เปิด/ปิดแบบ dynamic ตาม port จริงที่เจอ (ไม่ใช่ port ตายตัว)

## Lock Screen

Full-screen overlay ปลดได้ทาง Console เท่านั้น ป้องกัน input-leak (mouse/keyboard ทะลุออกไปนอกหน้าจอ
lock ได้ในบางกรณี — แก้แล้ว) รองรับ multi-monitor มี failsafe กันค้าง

## Auth & install

- **Auth**: PBKDF2 + salt, opaque token เก็บใน `AuthTokens` table (อายุ 12 ชม.) built-in
  `"Administrator"` account บังคับเปลี่ยน password ตั้งแต่ login ครั้งแรก (`HasDefaultPassword` gate)
- **DB**: EF Core Migrations (ไม่ใช่ `EnsureCreated()` แล้ว) SQLite ที่
  `%ProgramData%\OnionProcOparetor\`
- **Installer**: WiX v5, ไม่มี .exe แล้ว (Inno Setup ตัวเก่าเลิกใช้ 2026-08-05 เพราะ AV false-positive
  กับ .exe wrapper ที่ไม่ได้ code-sign, ดู `installer/legacy/OnionProcOparetor.iss` header comment)
  **แยกเป็น 2 .msi ตาม role** (ไม่ใช่ 1 .msi 3 โหมดแบบเดิมแล้ว — เลิกใช้ ModeDlg/MODE property):
  - `OnionProcOparetor-Agent-Setup.msi` — Agent + AgentTray เท่านั้น (เครื่องนักเรียน)
  - `OnionProcOparetor-Server-Console-Setup.msi` — Server + Console เท่านั้น (เครื่องครู/แอดมิน)
  - `installer/publish-all.ps1` publish โปรเจกต์ที่เกี่ยวข้องแล้ว compile ทั้ง 2 `.msi` ด้วย `wix build`
    (คำนวณ `FileVersion` ที่ต่างกันทุก build ด้วย เพื่อให้ MSI upgrade ทับไฟล์เก่าได้จริง — เจอบั๊กจริงที่
    `FileVersion` static เดิมทำให้ `InstallFiles` ข้ามการ overwrite)
- **Uninstall flow**: `UninstallHelper.exe` (binary เดียว bundle ใน 2 .msi) — live-tested ทั้ง 2 product
  ผ่านจริงบนเครื่องนี้ 2026-08-11 (ไม่ใช่แค่โค้ดรีวิว): แสดงชื่อ product ที่ถูกต้อง (Agent vs
  Server+Console) จาก argument ที่ ARP `UninstallString` ส่งมาเอง, self-relaunch จาก `%TEMP%` ก่อนลบ
  ไฟล์ (กัน self-lock โฟลเดอร์ตัวเอง), ลบ service/ไฟล์/ARP entry ได้จริงครบ (ก่อนหน้านี้เงียบๆ ไม่ได้ลบ
  อะไรเลยเพราะ `UNINSTPWD=` ว่างไปชนกับ `REMOVE=ALL` ที่ msiexec ใส่เองภายใน — แก้แล้ว)

## Known limitations / ยังไม่ได้ทำ

- Windows Firewall/hosts-based website blocking, "ปิดเน็ตทั้งหมด" — ออกแบบ+implement ไปรอบหนึ่งแล้ว
  (Phase 5) แต่ถูก revert พร้อมงานอื่นตอน stabilization — ยังไม่มีในระบบตอนนี้
- `ResizeMode="NoResize"` + pixel-fixed `Canvas` layout ทุกหน้าจอ Console — ใช้งานได้ดีในสภาพแวดล้อมปกติ
  แต่ไม่รองรับ DPI/text-scaling ที่สูงกว่าปกติหรือเนื้อหาที่ยาวเกินพื้นที่ที่จัดไว้ (รู้ข้อจำกัดแล้ว ยังไม่
  ทำ เพราะเป็น rework ใหญ่)
- Console GUI ยังไม่มีการแยกสิทธิ์ระหว่าง admin account ต่างๆ (ทุกคนที่ login ได้ทำได้ทุกอย่างเหมือนกัน)

---

รายละเอียดการพัฒนาแบบ dated log ทั้งหมด (2026-07-16 ถึง 2026-08-02, ทุก session/ทุกฟีเจอร์ตามลำดับเวลา
ที่ทำจริง รวมบั๊กที่เจอและวิธีแก้) ย้ายไปไว้ที่ [`docs/architecture-log.md`](architecture-log.md) แล้ว —
อ่านที่นั่นถ้าต้องการ context เชิงประวัติศาสตร์หรือเหตุผลเบื้องหลังการตัดสินใจแต่ละรอบ
