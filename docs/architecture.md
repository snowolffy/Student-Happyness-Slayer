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
  - ทั้งสองระดับต่อกันแบบ **AND** ไม่ใช่ OR: Agent บังคับใช้กฎก็ต่อเมื่อเปิดทั้งคู่
    (`enabled = globalEnabled && client.IsEnabled` ใน `GET /api/poll`) — Global จึงเป็น
    **kill switch คร่อมทุกเครื่อง** ไม่ใช่แค่ "ทางลัดสำหรับกดทีเดียวทั้งห้อง"
  - บั๊กจริงที่เจอ (2026-09-04, field test): ค่านี้ไม่มี endpoint ให้ "อ่าน" เลยตั้งแต่แรก มีแต่
    `POST /api/global/toggle` ที่พลิกค่าแบบมองไม่เห็น และปุ่มใน WPF Console ก็เคยถูกลบทิ้งด้วย
    ความเข้าใจผิดว่า "ACTIONS > ทุกเครื่อง > TOGGLE ENFORCEMENT ให้ผลเหมือนกัน" (ไม่จริง —
    อันนั้นแก้ `IsEnabled` ราย *เครื่อง* ไม่ได้แตะ gate ตัวนี้) ผลคือพอ gate ถูกปิดค้างไว้
    Console โชว์ทุกเครื่องเป็น ENABLED ตามปกติ ส่วน Agent ทุกตัวขึ้น Disabled ค้าง toggle
    รายเครื่องกี่รอบก็ไม่เปลี่ยน ส่วนคำสั่ง lock/shutdown ยังทำงานปกติ (ไม่ผ่าน gate นี้) =
    อาการที่หาสาเหตุไม่เจอ เพราะค่าเดียวที่อธิบายมันได้มองไม่เห็นจากทุก UI ที่มี
  - แก้แล้ว: `GET /api/global` (อ่านค่า) + `POST /api/global/set` (ตั้งค่าตรงๆ idempotent
    ไม่ใช่พลิกค่า — กัน lost-update ตอนครู 2 คนกดพร้อมกันด้วย) ส่วน `/toggle` เก็บไว้เพื่อ
    Console เวอร์ชันเก่าที่ deploy ไปแล้ว ฝั่ง UI ทั้ง WPF Console และ Online Console แสดง
    แถบเตือนตลอดเวลาที่ gate ปิดอยู่ พร้อมปุ่มเปิดกลับในแถบนั้นเอง และคอลัมน์ ENFORCEMENT
    ขึ้น `OFF (GLOBAL)` แทน `ENABLED` สำหรับเครื่องที่เปิดไว้เองแต่ถูก gate กดทับ

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

# Current Implementation Status (อัพเดตล่าสุด: 2026-09-02)

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
>
> **อัพเดต 2026-09-01 (audit + hardening pass)**: รอบใหญ่ที่สุดตั้งแต่ Stable 1 — audit ทั้งระบบ
> แล้วเพิ่ม 4 ระบบใหม่ (Tamper Watchdog, Agent Authentication, Program Control, Tailscale health)
> พร้อมแก้ช่องโหว่ 17 รายการและปรับ config ชุดใหญ่ **ยังไม่ผ่าน field test เลยแม้แต่เครื่องเดียว**
> รายละเอียดเต็มทุกข้ออยู่ที่ [`HARDENING-REPORT-2026-08-31.md`](../HARDENING-REPORT-2026-08-31.md)
> ที่ root ของโปรเจค — อ่านที่นั่นก่อนจะแตะอะไรในระบบเหล่านี้ โดยเฉพาะหัวข้อ 0 (disk freeze)
> ที่อธิบายข้อจำกัดซึ่งคุมการตัดสินใจเกือบทุกข้อในรอบนั้น
>
> **อัพเดต 2026-09-02**: กรองคิว Program Control ตามหมวดได้ (`e311ca0`) และแก้บั๊กที่คำสั่งซึ่ง
> ทำเสร็จแล้วค้างอยู่ในรายการ Pending ตลอดไป (`cf0e0b5` — ดูหัวข้อ Remote commands) สร้าง MSI ใหม่
> ทั้ง 2 ตัวที่ FileVersion `1.2.5.12310` **ต้องลงใหม่เฉพาะฝั่ง Server+Console เท่านั้น** เพราะ
> ทั้ง 2 อย่างแก้ที่ Server เป็นหลัก (ที่แก้ในฝั่ง Agent เป็นแค่ log เตือน ไม่ได้แก้อาการอะไร) —
> ไม่ต้องแตะเครื่อง lab ทั้ง 40 เครื่อง และไม่มี DB migration
>
> **อัพเดต 2026-09-11 (Website Control)**: เพิ่มฟีเจอร์ใหม่ "Website Control" — บล็อกเว็บไซต์ระดับ URL
> ผ่าน Chrome/Edge Enterprise Policy (`URLBlocklist` registry) ดูหัวข้อ Website Control ด้านล่าง
> **คนละกลไกกับ "Internet restriction (Phase 5)" ที่เคย stash ไว้และยังไม่ได้ทำต่อ** (Phase 5 คือ
> `NetShutdownManager` ตัดเน็ตทั้งเครื่องผ่าน Windows Firewall — อันนั้นยังไม่มีในระบบเหมือนเดิม
> ดูหัวข้อ Known limitations) build ผ่านทั้ง 4 โปรเจกต์ + สร้าง/ทดสอบ migration และ CRUD endpoint
> จริงบน DB แยกต่างหาก (isolated port/DB บนเครื่อง dev)
>
> **อัพเดต 2026-09-12**: field-test Website Control จริงบน Chrome/Edge จริง (ไม่ใช่แค่ curl) สำเร็จ
> ทั้งสองเบราว์เซอร์ — ดูรายละเอียดในหัวข้อ Website Control ด้านล่าง ตามด้วย **Bulk Add + Online
> Catalog Sync** (วางโดเมนทีละหลายบรรทัด + sync จาก hosts-file list ภายนอกอัตโนมัติทุก 24 ชม. พร้อม
> เพดานกันเหตุ list สาธารณะที่ใหญ่เกินจะใช้ได้จริง เช่น `gambling.txt` ที่มี 342,627 โดเมน) และ
> `GET /api/website-rules` เปลี่ยนเป็นแบ่งหน้า/ค้นหา/กรองแทนคืนทั้งหมดทีเดียว (breaking change ของ
> response shape) **ยังไม่ deploy ขึ้นเครื่อง lab**

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

**สถานะ 2 ชุดที่ห้ามสับสน** (2026-09-02 — เคยทำให้เกิดบั๊กจริง): `Status` = "ส่งมอบถึงเครื่องแล้ว
หรือยัง" เป็นตัวเดียวที่ `/api/poll` ใช้กรองว่าจะส่ง command ซ้ำไหม และเป็นตัวเดียวที่หน้า
`PENDING COMMANDS` อ่าน ส่วน `ExecutionStatus` = "ทำได้จริงไหม" ใช้รายงานผลให้ครูเท่านั้น
เดิมมีแค่ ack ที่ปิด `Status` ให้ พอ ack หลุด (Agent ก็ไม่เคยเช็ค status code ของมันด้วย) คำสั่งจะ
ค้าง Pending ตลอดไปทั้งที่ครูเห็นว่า "สำเร็จ" แล้ว และถูกส่งซ้ำทุกรอบ poll ไม่จบ — ตอนนี้การรายงาน
ผลจริงกลับมาปิด `Status` ให้ด้วย (ผลกลับมา = พิสูจน์แล้วว่าถึงเครื่อง) ดู `CommandResultRecorder`

### คำสั่งมีอายุ (2026-09-10) — บั๊กจริงที่อันตรายที่สุดที่เจอในหมวดนี้

**อาการ:** ครูสั่ง Shutdown ทั้งห้อง เครื่องที่เปิดอยู่ 30 เครื่องดับปกติ ส่วนอีก 10 เครื่องที่ปิดอยู่
ตอนนั้น พอถูกเปิดวันหลัง **ดับเองทันทีที่บูตเสร็จ** โดยไม่มีใครสั่งอะไรในตอนนั้นเลย

**สาเหตุ:** คำสั่งที่ยังไม่ถึงเครื่องค้างเป็น `Pending` ตลอดกาล และ `/api/poll` ส่งให้ทุกคำสั่งที่ยัง
Pending อยู่ โดยไม่สนว่าสั่งไว้เมื่อไหร่ — รากของปัญหาคือความหมายของคำสั่ง: "ปิดเครื่องทั้งห้อง"
หมายถึงเครื่องที่เปิดอยู่ *ตอนนี้* ไม่ใช่ "ปิดทันทีที่บูตครั้งหน้า"

**วิธีแก้:** `ClientCommand.ExpiresAt` + `CommandExpiry` (ดู `Models/ClientCommand.cs`)

- คำสั่งประเภท **เหตุการณ์** (Shutdown / Restart / Lock / Broadcast / VNC / ...) หมดอายุใน **3 นาที**
  (ตั้งได้ที่ `CommandTracking:EventCommandLifetimeSeconds`) — รอบ poll จริงคือ 30 วิ ดังนั้นเครื่องที่
  เปิดอยู่จริงได้รับภายในรอบเดียว 3 นาที = 6 รอบ เผื่อเน็ตกระตุกได้สบาย
- คำสั่งประเภท **สถานะ** (`UpdateSettings` / `SetTailscaleLockdown` / `RefreshRules`) **ไม่หมดอายุ** —
  ส่งถึงช้าก็ยังถูกต้องและควรถึง เป็น **allow-list ไม่ใช่ deny-list** โดยเจตนา: CommandType ใหม่ที่
  ยังไม่ขึ้นทะเบียนจะถูกถือว่าเป็นเหตุการณ์และหมดอายุ (เดาผิดทางนี้แค่คำสั่งไม่ถึงเครื่องที่ปิดอยู่
  เดาผิดอีกทางคือเครื่องดับเองตอนบูต)
- `/api/poll` เป็นด่านบังคับใช้จริง: mark เป็น `Expired` **แล้วค่อย** query (ถ้าแค่กรองออก แถวจะค้าง
  Pending ใน DB ตลอดไป แล้วหน้า PENDING COMMANDS จะโชว์คำสั่งที่ไม่มีวันทำงาน)
- **แก้ฝั่ง Server อย่างเดียว ไม่แตะ Agent เลย** — Agent 40 ตัวที่ deploy ไปแล้วได้รับการป้องกัน
  ทันทีที่ Server รีสตาร์ท ไม่ต้องไล่อัปเดต MSI ทีละเครื่อง
- migration `AddClientCommandExpiry` **ปลดชนวนคิวที่ค้างอยู่แล้วในฐานข้อมูลจริงด้วย** ไม่ใช่แค่เพิ่ม
  คอลัมน์ — ถ้าไม่ทำ แถวเก่าทุกแถวจะได้ `ExpiresAt = NULL` ซึ่งกติกาใหม่แปลว่า "ไม่มีวันหมดอายุ"
  = บั๊กเดิมทั้งดุ้น

ทดสอบแล้วแบบ end-to-end (Server จริง + DB จริง แยก port/ไฟล์ ไม่แตะ production): คำสั่ง Shutdown
ที่เลยอายุไม่ถูกส่งให้ Agent และหายจากรายการค้าง ส่วน `UpdateSettings` ยังถูกส่งตามปกติ

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

## Tamper Watchdog (2026-08-31)

ถ้า Agent ถูกลบ/ปิด/ทำให้ตายด้วยวิธีที่ไม่ได้ตั้งใจให้ใช้ เครื่องจะ **reboot ตัวเอง** — ซึ่งได้ผล
100% เพราะ Reboot Restore Rx restore ทุกครั้งที่เปิดเครื่องอยู่แล้ว

- ตัวตรวจเป็น **Scheduled Task รันเป็น SYSTEM ทุก 1 นาที + ตอนบูต** (`agent-watchdog.ps1`) ไม่ใช่
  โค้ดใน Agent เอง เพราะ Agent ตรวจ "ตัวเองถูกลบ" ไม่ได้
- **ค้ำกันสองทาง**: ลบ task → Agent สร้างคืนใน 1 นาที (`TamperProtectionService`) / ฆ่า Agent →
  task เห็น heartbeat ค้างแล้ว reboot ต้องทำทั้งคู่พร้อมกันในหน้าต่างแคบๆ ถึงจะรอด
- เงื่อนไขแบ่ง HARD (service/ไฟล์หาย → reboot รอบเดียว) กับ SOFT (หยุด/heartbeat ค้าง → ต้องเจอ 2 รอบ)
- **กัน reboot loop โดยไม่พึ่ง state ข้าม reboot** (ซึ่งบนเครื่อง freeze ทำไม่ได้เลย): จะ reboot
  ก็ต่อเมื่อเคยเห็น Agent ทำงานปกติต่อเนื่อง ≥ 120 วิ ในรอบบูตนั้น — ไม่เคยเห็นเลย = baseline พัง
  reboot ไปก็วนไม่จบ จึงแจ้ง Server แล้วลอง `sc start` แทน
- ทางออกที่ถูกกฎคือ **maintenance ticket ที่เซ็นด้วย HMAC-SHA256** จาก `wd.key` — MSI ออกให้ทุก
  transaction แบบไม่มีเงื่อนไข (major upgrade คือเคสที่จะโดนพอดีถ้า gate ด้วย `REMOVE`)
- สั่ง `shutdown` ก่อนแล้วใช้ช่วงนับถอยหลัง 20 วิยิงรายงานซ้ำ เพราะ log ในเครื่องและ Event Log
  ถูกล้างไปพร้อม reboot — `POST /api/tamper-report` คือหลักฐานชิ้นเดียวที่รอด
- **ไม่ได้ตั้งเป้ากัน admin** (ต้องใช้ kernel driver) ตั้งเป้ากันนักเรียนสิทธิ์ปกติ 100% และทำให้
  admin ที่ทำครึ่งๆ กลางๆ โดน reboot ทันที

## Agent Authentication (2026-08-31)

เดิม endpoint ฝั่ง agent ทั้งหมดไม่มี auth เลย ใช้แค่ "รู้ ClientGuid" ซึ่งเดินทางใน URL ของทุก poll
บน HTTP ธรรมดา — ดักดูรอบเดียวได้ครบทุกเครื่อง แล้วเอาไปดึงกฎ/ปลอม ack/ปลอมผลลัพธ์ได้

- แต่ละเครื่องมี **agent secret 32 ไบต์** สร้างตอนติดตั้ง (`install-watchdog.ps1`) เก็บใน
  โฟลเดอร์ที่ ACL ตัดกลุ่ม Users ออก — ต้องสร้างตอนติดตั้งเท่านั้น ไม่งั้นหายทุก reboot
- แนบ header `X-Agent-Guid` / `X-Agent-Secret` ทุก request และตอน negotiate ของ SignalR
- Server จำ secret แรกที่เห็นของแต่ละเครื่อง (trust-on-first-use) เก็บเป็น PBKDF2 hash แล้วล็อกไว้
- `/ack` และ `/result` ตรวจซ้ำว่า command เป็นของเครื่องที่เรียกมาจริง (กัน lateral movement)
- **`Security:RequireAgentSecret` default = `false`** โดยตั้งใจ — เปิดพร้อม deploy = เครื่องทั้ง 40
  ที่ยังรัน Agent เก่าหลุดพร้อมกันทันที ต้องเฝ้า Activity Log จนไม่มี `AgentSecretMismatch` ก่อน
- `POST /api/clients/{id}/reset-agent-secret` สำหรับเครื่องที่ key หายแต่ GUID เดิม
  — **ยังไม่ได้ต่อเข้า Console** (ไม่มีทั้ง ApiClient method และปุ่ม) ตอนนี้เรียกได้ทาง curl เท่านั้น
- **`AgentSecretMismatch` ยุบเหลือเครื่องละ 1 แถว/ชั่วโมง** (2026-09-02) — เจอจริงบน COM-31:
  เครื่องที่ `agent.key` ไม่ได้อยู่ใน baseline จะ mismatch ทุก poll = 120 แถว/ชั่วโมง จนกลบ
  Activity Log ทั้งหน้า แถวแรกยังขึ้นทันทีเสมอ และแถวถัดไปบอกจำนวนที่ยุบไว้ ข้อมูลจึงไม่หาย
- อาการ `Mismatch` ต่อเนื่องบนเครื่อง lab แปลว่า `agent.key` ถูกสร้างตอน runtime (อยู่นอก baseline)
  ลำดับแก้: thaw → รัน `install-watchdog.ps1` ใหม่ → **commit baseline ก่อน** → ค่อย reset secret
  ที่ Server (สลับลำดับแล้วจะ enroll กุญแจที่กำลังจะหายอยู่ดี) ระหว่างที่ยังไม่แก้
  **ไม่กระทบการทำงานใดๆ ตราบใดที่ `RequireAgentSecret` ยังเป็น `false`** — Agent ถูกปล่อยผ่านทุก
  endpoint ตามปกติ เสียแค่ log เท่านั้น

## Program Control (2026-08-31)

ตาข่ายรับโปรแกรมที่ระบบ rule เดิม **มองไม่เห็นเชิงโครงสร้าง** — เดิม `CheckAndActOnProcess`
return ทันทีถ้าไฟล์ไม่มี publisher ซึ่งครอบคลุมเกม portable/โหลดแทบทั้งหมด (ไม่มีแม้แต่ใน log)

- จำแนกที่ **Server ที่เดียว** key ด้วย **SHA-256** — ตัวระบุเดียวที่ rename/ย้ายที่แล้วไม่เปลี่ยน
- ให้คะแนนตามหลัก "สัญญาณที่ลบแล้วโปรแกรมพัง ได้น้ำหนักสูงกว่าสัญญาณที่แก้ด้วย text editor ได้":
  ไฟล์ SDK/engine → โครงสร้างโฟลเดอร์ของ launcher → DLL ที่โหลดจริง → publisher → ชื่อไฟล์
  พร้อมกลุ่ม **ร่องรอย crack** (Goldberg/OnlineFix/SmartSteamEmu) ที่ไม่มีทางอยู่ในของถูกลิขสิทธิ์
- Steam name lookup เป็นแค่ **1 ใน 7 กลุ่มสัญญาณ** (45 คะแนน) ไม่ใช่กระดูกสันหลัง — เกมนอก Steam
  จับได้จาก path/engine/DLL/crack marker ตามปกติ
- **3 ระดับ**: ≥70 Block · ≥40 Flag (เข้าคิวให้ครูตัดสิน) · ต่ำกว่านั้น Allow —
  คะแนนกลางๆ ตกไปที่คิวเพราะปิดโปรแกรมที่นักเรียนใช้ทำงานส่งแพงกว่าปล่อยเกมหลุดหนึ่งรอบ poll
- **คำตัดสินของครู (`IsManual`) ชนะอัลกอริทึมเสมอ** ไม่มีเงื่อนไข
- 2 ชั้นฝั่ง Agent: blocklist ในหน่วยความจำ (มากับ poll ส่งเฉพาะตอนเปลี่ยน) ปิดทันทีไม่มี round trip /
  ที่ยังไม่รู้จัก เข้าคิวส่งเป็นชุด — ชั้นหลัง **fail-open โดยตั้งใจ** (ห้ามทุกอย่างที่ไม่รู้จัก =
  Windows ปิดตัวเองตายตั้งแต่บูต)
- หน้า `PROGRAMS` ที่ header ของ Console = คิวตรวจสอบ แสดงเหตุผลทีละสัญญาณพร้อมน้ำหนัก
  **กรองตามหมวดได้** (2026-09-02) — ห้องจริงสะสมหลายร้อยแถวภายในไม่กี่วัน กล่องเลือกหมวดสร้างจาก
  จำนวนจริงตอน refresh (โชว์เฉพาะหมวดที่มีของ พร้อมตัวเลขกำกับ) กรองที่ Server เพราะ `take` ตัดแถว
  ตั้งแต่ก่อนถึง Console และตอนนี้บอกด้วยเมื่อรายการถูกตัด — เดิมเงียบสนิทจนเข้าใจผิดว่าเห็นครบแล้ว
- **⚠️ BYPASS และสวิตช์ปิด enforcement ปิด Program Control ไปด้วยทั้งชุด รวมถึง blocklist ที่ผูก hash**
  ทางเข้าเดียวคือ `CheckAndActOnProcess` ซึ่งถูกเรียกจาก `ScanRunningProcessesAsync` กับ
  `OnProcessCreated` เท่านั้น ทั้งคู่ return ทันทีเมื่อ `_bypassState.IsBypassed || !_lastKnownState.Enabled`
  (`Enabled` = `globalEnabled && client.IsEnabled` จาก poll) สำหรับปุ่ม BYPASS ถูกต้องตามเจตนา
  แต่สวิตช์ปิด enforcement เป็นกับดัก — ครูน่าจะคิดว่ากำลังปิดแค่กฎ publisher
  (คนละเรื่องกับ `Security:RequireAgentSecret` ซึ่ง**ไม่กระทบ Program Control เลย**)
- ปุ่ม `BLOCK` ในหน้า Running Processes เพิ่มโปรแกรมเข้าระบบด้วย hash ได้เลย ใช้ได้แม้ไม่มี publisher
  และ upsert ทับของที่ classifier เคยตัดสินไว้
- กฎเหล็ก: อะไรก็ตามใน `%SystemRoot%` คืน Allow ทันทีโดยไม่ผ่านการให้คะแนน
- ข้อจำกัดที่รู้ตัว: **เกมผ่าน browser มองไม่เห็นเลย** และไฟล์ที่ไม่มี marker อะไรเลยได้ 0 คะแนน

## Website Control (2026-09-11)

ปิดช่องที่ Program Control ประกาศไว้เป็นข้อจำกัดที่รู้ตัว ("เกมผ่าน browser มองไม่เห็นเลย") — เพิ่มชั้น
บล็อกระดับ URL ในเบราว์เซอร์ ตัดสินใจร่วมกับ user 2026-09-11 ว่าเป็นงาน step 2 ที่ควรทำก่อนระบบ
sign-in/tracking เพราะภัยจริงคือเด็กหนีไปเล่นเกม/emulator ผ่านเว็บ (threat model เดิมที่สรุปไว้ 2026-09-02
ว่า Server/Console-only fixes พอ ไม่ต้องแตะ agent 40 เครื่อง)

- **กลไก**: เขียน Chrome/Edge Enterprise Policy ผ่าน Registry — `HKLM\SOFTWARE\Policies\Google\
  Chrome\URLBlocklist` และ `...\Policies\Microsoft\Edge\URLBlocklist` เป็น subkey ที่มี REG_SZ
  เรียงเลข `1`, `2`, `3`... (list-type policy format มาตรฐานของ Chromium) เลือกวิธีนี้แทน
  hosts-file (หยาบเกิน บล็อกได้แค่ทั้งโดเมน) และแทน extension (เด็กถอนเองได้ถ้ามีสิทธิ์) —
  ผู้ใช้ปิดเองไม่ได้ผ่าน UI ปกติ (browser โชว์ "This setting is managed by your administrator")
  และ Chrome/Edge เองก็ watch key นี้แล้ว reload policy ให้เองภายในไม่กี่วินาทีโดยไม่ต้อง restart
- **คนละเรื่องกับ "Internet restriction (Phase 5)"** ที่เคยออกแบบไว้ในปี 2026-08-02 แล้วถูก stash
  พร้อมงานอื่นตอน stabilization (ดูหัวข้อ Known limitations) — Phase 5 คือสวิตช์ตัดเน็ตทั้งเครื่อง
  (`NetShutdownManager`, Windows Firewall rule) ส่วน Website Control คือบล็อกทีละเว็บไซต์แบบเลือกได้
  คนละ scope และคนละกลไกกันโดยสิ้นเชิง ไม่ได้ต่อยอดจากโค้ดที่ stash ไว้เลย
- **Model**: `WebsiteRule` (Id/Name/UrlPattern/IsEnabled) — table แยกจาก `Rule` ของ Program Control
  ไม่มี `RuleType` discriminator ร่วมกัน (เดินตาม pattern เดิมที่ `Rule` เองก็เป็น dedicated table)
  **ยังไม่มี per-machine override** (`WebsiteRuleOverride`) แบบที่ `RuleOverride` มีให้ `Rule` — ตัดสินใจ
  เว้นไว้ก่อนเพราะไม่มีใครขอ (YAGNI) เพิ่มทีหลังได้ถ้าจำเป็นจริง
- **Endpoint**: `/api/website-rules[/{id}]` + `/toggle` (mirror `/api/rules` ทุกอย่าง auth/error shape
  เดียวกัน) `/api/poll/{clientGuid}` ส่ง `websiteRules` (เฉพาะ `IsEnabled`) มาคู่กับ `rules` เดิม
- **⚠️ Gate ต่างจาก Program Control โดยตั้งใจ**: `Worker.ScanRunningProcessesAsync` เรียก
  `WebsiteControlService.Apply()` **ทุกรอบ scan เสมอ ไม่มี early-return** ตามด้วยเงื่อนไข
  `_bypassState.IsBypassed || !_lastKnownState.Enabled` เหมือน process rules — ส่ง `Array.Empty
  <string>()` เข้าไปเองแทนตอน bypass/ปิด enforcement เหตุผล: registry key ที่เคยเขียนไว้จะ
  **"ค้างอยู่ต่อ"** ในเซสชันเดียวกันจนกว่าจะมีคนลบจริง (ต่างจาก process kill ที่แค่ไม่ทำอะไรต่อก็พอ)
  ถ้า early-return เหมือน process rules เว็บที่เคยบล็อกไว้จะยังบล็อกค้างอยู่หลังปิด enforcement/bypass
  จนกว่าจะ reboot (เครื่อง lab ทุกเครื่องรัน Reboot Restore Rx ซึ่งล้าง registry ทุก reboot อยู่แล้ว)
  ซึ่งขัดกับสิ่งที่ครูคาดหวังตอนกด BYPASS/ปิดสวิตช์ทันที ใช้ global enforcement toggle ตัวเดียวกับ
  Program Control (`GlobalState.IsEnabled` AND-gate) ไม่มี toggle แยก
- **Console UI**: แท็บแยกใน sidebar (`WebsiteControlPage`) ไม่ใช่ tab ของ Rules — เหมือน
  `ProgramReviewPage` ตั้งใจไม่ให้กฎบล็อกแอปกับบล็อกเว็บปนกันจนงง (เดิมทั้งสองเป็นหน้าต่างลอย
  `WebsiteControlWindow`/`ProgramReviewWindow` แยกจาก Dashboard แต่ WP6, 2026-10-30 แปลงเป็นหน้าใน
  shell เดียวกันแล้ว - เช่นเดียวกับ `SettingsWindow` เดิม → `SettingsPage` ดู `docs/console-ui-notes.md`
  §3.2 สำหรับรายละเอียดการแปลง) ฝั่ง OnlineConsole เป็นหน้าแยก `/website-rules` เข้า group เดียวกับ Rules
- **ข้อจำกัดที่รู้ตัว**: ครอบคลุมแค่ Chrome/Edge เท่านั้น (เครื่อง lab ใช้แค่สองตัวนี้จริง) ไม่มี Firefox/
  browser อื่น และ registry policy เป็นระดับ `HKLM` — นักเรียนที่ยังมีสิทธิ์ local admin (ก่อนแผนเปลี่ยน
  นักเรียนเป็น standard user เสร็จสมบูรณ์ — ดูหัวข้อ "AgentTray กับ user account ใหม่" ด้านบน) ลบ key นี้
  เองได้โดยตรงถ้ารู้ตำแหน่ง เหมือนกับที่ Program Control ก็มีข้อจำกัดเดียวกันเรื่องสิทธิ์ ไม่ใช่ปัญหาใหม่
  ของฟีเจอร์นี้
- **Field-test 2026-09-12**: ทดสอบจริงบนเครื่อง dev นี้ทั้ง Chrome และ Edge จริง (ไม่ใช่แค่ curl) —
  เห็น `URLBlocklist` ขึ้นใน `chrome://policy`/`edge://policy` เองอัตโนมัติ, เข้าเว็บที่บล็อกไว้เจอ
  หน้า "This page is blocked" จริง, ปิดกฎแล้ว registry เคลียร์เองภายใน ~15 วิ ยืนยันครบทั้งสอง
  เบราว์เซอร์ (รายละเอียดขั้นตอนเทสอยู่ใน session log 2026-09-12) **ยังไม่ deploy ขึ้นเครื่อง lab**
  (รอ MSI rollout รอบที่มีงานค้างอื่นๆ ฝั่ง Agent/AgentTray อยู่แล้ว เช่น pipe-deafness fix จาก
  2026-09-10)
- **Bulk Add + Online Catalog Sync (2026-09-12)** — ปิดช่องที่ต้องพิมพ์ทีละโดเมน:
  - `POST /api/website-rules/bulk` วางรายชื่อโดเมนทีละหลายบรรทัดได้ทีเดียว (cap 200 รายการ/ครั้ง,
    dedup case-insensitive ทั้งกับของเดิมในระบบและภายในชุดที่วางมาเอง)
  - `WebsiteCatalogSource` (model ใหม่) + `WebsiteCatalogSyncer`/`WebsiteCatalogSyncService` (Server) —
    ให้ admin ชี้ไป URL ของ hosts-file/domain-list ภายนอกอะไรก็ได้ แล้ว sync เข้า `WebsiteRule` ให้เอง
    ทุก 24 ชม. (เลือกแบบอัตโนมัติ ไม่ใช่ manual-only) **ต้องมีเพดานจริงจัง** เพราะเช็คจริงแล้วพบว่า list
    สาธารณะมีขนาดต่างกันมหาศาล — ตรวจสอบจริงกับ `blocklistproject/Lists` บน GitHub วันที่ 2026-09-12:
    `fortnite.txt` มี 5 โดเมน, `smart-tv.txt` มี 90, แต่ `gambling.txt` มี **342,627** และ `youtube.txt`
    มี 24,295 — สอง list หลังนี้ไม่มีทางใช้กับกลไก registry ของเราได้จริง ทดสอบแล้วว่าระบบปฏิเสธ
    `gambling.txt` ถูกต้อง (`SkippedTooLarge`, ไม่ import อะไรเลย, เห็นตัวเลขจริง 342,623 ใน error
    message) ในขณะที่ `fortnite.txt` sync ผ่านได้ปกติ (สร้าง `WebsiteRule` 5 แถวพอดี, ลบ source ก็
    cascade ลบทั้ง 5 แถวถูกต้อง, re-sync ซ้ำก็ idempotent ไม่สร้างซ้ำ) — ทดสอบจริงกับ endpoint ตรงๆ
    ผ่าน curl แล้ว (isolated DB) ไม่ใช่แค่ทฤษฎี
  - `AbsoluteMaxEntries = 5000` (เดิมตั้งไว้ 500 ตอนออกแบบแรก แต่ user ชี้ว่าปัญหา UI แก้ด้วยการแบ่ง
    หน้า/ค้นหา/กรองได้อยู่แล้ว ไม่ต้องพึ่ง cap ต่ำๆ — เพดานที่เหลืออยู่จึงป้องกันแค่ "ใช้กับ registry
    policy ไม่ได้จริง" ไม่ใช่ "ตารางค้าง")
  - `GET /api/website-rules` เปลี่ยนจากคืน array ตรงๆ เป็น `{ items, totalCount, page, pageSize }`
    พร้อม `search`/`sourceCatalogId`/`manualOnly` — เดินตาม pattern เดียวกับ `GET /api/activitylog`
    (page/pageSize + totalCount) ที่มีอยู่แล้ว **นี่คือ breaking change ของ response shape** ถ้ามีที่
    อื่นเรียก endpoint นี้แบบเก่าไว้ต้องแก้ด้วย

## Online Console — bundled into the Server+Console installer (2026-09-20)

`OnionProcOparetor.OnlineConsole` เดิม deploy ด้วยมือล้วนๆ (`dotnet publish` + copy ไฟล์เอง, ดู
`deploy/onlineconsole/install-service.ps1`) แม้โค้ดจะมี `UseWindowsService()` ต่อไว้ให้แล้วตั้งแต่แรก
ตอนนี้ผูกเข้า `installer/OnionProcOparetor.ServerConsole.wxs` เป็น Feature ที่ 3 (`OnlineConsoleFeature`)
แล้ว — อัปเดตทีเดียวไม่ต้อง copy ไฟล์เองอีกต่อไป

- **default ไม่ติ๊ก** (`INSTALLONLINECONSOLE` default `"0"`) ต่างจาก Server/Console ที่ default ติ๊กทั้งคู่
  — เป็นความสามารถใหม่ที่มี external dependency (ต้องมี Tailscale เชื่อมต่ออยู่แล้ว) เครื่องที่อัปเกรดมา
  ไม่ควรได้ service ใหม่ที่ไม่ได้ขอ
- **บังคับว่าต้องติ๊ก Server ด้วยเสมอถ้าจะติ๊ก Online Console** (validation ใหม่ใน `ComponentsDlg`) เพราะ
  OnlineConsole proxy เข้า `/api/auth/login` ของ Server ที่ `127.0.0.1:8787` ตรงๆ ต้องรันเครื่องเดียวกัน
- **ความเสี่ยงจริงที่แก้ก่อนทำ packaging**: `Program.cs` resolve Tailscale IP ตอน startup แล้ว throw ทันที
  ถ้าไม่สำเร็จ (เดิมลองแค่ครั้งเดียว) — เป็น Windows Service แบบ `Start="auto"` แปลว่าเครื่องรีสตาร์ท
  ตอนที่ tailscaled ยังเชื่อมต่อไม่เสร็จจะทำให้ service ตายตั้งแต่เริ่ม แก้เป็น 2 ชั้น: (1) retry ใน
  `Program.cs` เอง 6 ครั้ง (~60 วิ) ก่อน throw จริง (2) `sc.exe failure OnionOnlineConsole reset=86400
  actions=restart/30000/restart/60000/restart/120000` ผ่าน deferred CustomAction (`ConfigureOnlineConsoleRecovery`,
  `After="StartServices"`) — ค่าเดียวกับที่ `deploy/onlineconsole/install-service.ps1` ใช้มาก่อนแล้วจริง
  ไม่ใช้ `util:ServiceConfig` เพราะ schema ไม่ชัดว่า `RestartServiceDelayInSeconds` รองรับ delay แบบขั้นบันได
  เหมือน `sc.exe` หรือเปล่า — เรียก `sc.exe` ตรงๆ ปลอดภัยกว่า **ลอง `DelayedAutoStart="yes"` บน
  `<ServiceInstall>` แล้วด้วยแต่ WiX v5 ปฏิเสธ (`WIX0004: unexpected attribute`) ทั้งที่ string นี้มีอยู่จริง
  ใน `WixToolset.Data.dll` — ไม่ได้ถูก expose ผ่าน attribute นี้ ไม่ได้ตามหา syntax อื่นต่อเพราะไม่ใช่
  เกราะหลัก
- **ยืนยันด้วย `wix build` จริง**: compile ผ่าน 0 error หลังแก้ปัญหาข้างบน ตรวจ table ในตัว .msi ที่ compile
  ได้ตรงๆ ผ่าน `WindowsInstaller.Installer` COM object (ไม่ต้องติดตั้งจริง) ยืนยัน Feature/Component/
  ServiceInstall (`StartType=2` = auto ถูกต้อง)/CustomAction/sequencing/Dialog controls ครบถ้วนตรงตามที่
  ตั้งใจ **ยังไม่เคยติดตั้งจริงบนเครื่องไหนเลย** (ต้องมีคนอยู่หน้าเครื่องตอนทดสอบ service recovery จริง
  ด้วยการถอด Tailscale ออกก่อนแล้วลองรีสตาร์ท - เป็นการทดสอบที่กระทบเครื่องจริง ไม่ทำแบบไม่มีคนดูอยู่)
- **บทเรียนเรื่อง tooling**: `wix build` รันผ่าน Bash (MSYS) บนเครื่องนี้ล้มเหลวเงียบๆ/เขียนไฟล์ผิดที่ —
  backslash ใน `-o output\file.msi` โดน bash กลืนทิ้ง (backslash+ตัวอักษรอื่นในตัวมันเองไม่มีความหมายพิเศษ
  ใน bash เลยหายไปเฉยๆ) กลายเป็นชื่อไฟล์ `outputfile.msi` แบนๆ ในโฟลเดอร์ผิด ต้องรันผ่าน PowerShell เท่านั้น
  สำหรับคำสั่งที่มี Windows-style path แบบนี้

## Tailscale fallback health (2026-08-31)

`tailscaled.state` (node key) อยู่บนไดรฟ์ที่ freeze เหมือนกัน → **หลุด tailnet ทุก reboot** ถ้าไม่ได้
enroll ตอน freeze ปิดแล้ว commit baseline และ `SwitchToFallback` ก็ไม่ได้ตรวจว่าเส้นทางสำรองใช้ได้จริง
ก่อนสลับ — เป็น failover ที่พังเงียบที่สุด `TailscaleHealthService` ตรวจทุก 15 นาทีแล้วรายงานเข้า
log กลางเฉพาะตอนสถานะเปลี่ยน ส่วน AlwaysOn policy ย้ายไปตั้งตอนติดตั้งเพื่อให้ติด baseline

## AgentTray กับ user account ใหม่ (สำคัญต่อแผนเปลี่ยนนักเรียนเป็น standard user)

AgentTray ตั้ง autostart ให้ตัวเองด้วย **HKCU Run key** ที่มันเขียนตอนรัน (`RegistryStartup`) —
เป็น per-user ทั้งหมด ไม่มี HKLM Run และไม่มี shortcut ใน Startup folder มีแต่ shortcut ใน Start Menu
ที่ MSI ลงไว้ **user account ที่สร้างใหม่จึงมี HKCU ว่างเปล่า → tray ไม่สตาร์ท → lock screen /
broadcast / VNC ตายเงียบทั้งหมด** โดย Console ยังเห็นเครื่องเป็นปกติ (Agent service ไม่เกี่ยวกัน)

เป็นไก่กับไข่: tray เขียน key ได้ก็ต่อเมื่อมันรัน แต่รันเองได้ก็ต่อเมื่อมี key
`AgentTrayRecoveryService` กู้ให้ได้ (Agent เป็น SYSTEM สตาร์ท tray เข้า user session) แต่เป็น
**reactive เท่านั้น** — ถูกเรียกตอนส่ง pipe ไม่สำเร็จ ไม่ใช่ health check เป็นระยะ ครูจึงต้องสั่ง
อะไรสักอย่างก่อน tray ถึงกลับมา

**ลำดับที่ต้องทำตอนสร้าง account ใหม่** (account อยู่ใน SAM = ไฟล์บนไดรฟ์ที่ freeze คุมอยู่
สร้างตอน freeze ทำงาน = หายทุก reboot):
thaw → สร้าง user → **login เป็น user นั้น** → **เปิด tray 1 ครั้งจาก Start Menu** → ตรวจว่าขึ้นถาดจริง
→ logout → commit baseline → freeze

ส่วนที่รองรับ standard user อยู่แล้ว: named pipe ให้สิทธิ์ LocalSystem + เจ้าของ process เอง
(ไม่ได้ผูกกับ Administrators — ดู `BroadcastPipeListener`) และ `ultravnc.ini` มี ACL ระดับไฟล์ให้
`BUILTIN\Users` เขียนเนื้อไฟล์ได้แต่ลบไม่ได้ (ตั้งใน `setup-client.ps1` ตอนติดตั้ง)
**ยังไม่เคยรัน tray ภายใต้ standard user จริง** — จุดที่ยังไม่ยืนยัน: `PhysicalInputBlocker`
(low-level keyboard hook) และ `createpassword.exe` ของ UltraVNC

## AgentTray หูหนวกได้จาก connection เดียว (2026-09-10)

**อาการที่ผู้ใช้รายงาน:** เครื่องเข้า sleep ทั้งที่ tray ทำงานอยู่ พอตื่นขึ้นมา tray "ค้างไปเลย
ไม่ตอบสนอง" — กด tray icon ยังเปิดหน้าต่างได้ แต่คำสั่งจาก Console ไม่เข้าเลยสักคำสั่ง

**สิ่งที่พิสูจน์ได้จริง (ทำซ้ำสำเร็จบนเครื่อง dev กับ build เก่า):** accept-loop ของ
`BroadcastPipeListener` ถูกทำให้หูหนวกได้ด้วย client เพียงตัวเดียวที่ "ต่อติดแล้วไม่ส่งอะไรเลย"
เพราะ `ReadLineAsync` เดิมไม่มี timeout (ใช้ token อายุเท่าโปรแกรม) และ pipe เปิดด้วย
`maxNumberOfServerInstances=1` — ระหว่างที่มันรออยู่ ไม่มีทางสร้าง server instance ใหม่มารับใครได้เลย
ทุกคำสั่ง (lock/unlock/broadcast/shutdown notify/VNC/process scan) เงียบหมด ทั้งที่ process ยังรันปกติ

การทดสอบ: ต่อ client แล้วไม่ส่งอะไร ค้างไว้ → ยิง Broadcast จริงด้วย timeout 3 วิเท่า
`AgentTrayNotifier` ของจริง

| | t+1.5s | t+12.5s |
|---|---|---|
| build เก่า | CONNECT FAILED | CONNECT FAILED (หูหนวกตราบเท่าที่ connection นั้นยังเปิดอยู่) |
| build ใหม่ | CONNECT FAILED (server instance เดียวยังถูกจองอยู่ ปกติ) | **SENT ok** — popup เด้งจริง |

**ที่แย่กว่าคือกู้เองไม่ได้:** Agent ส่งไม่สำเร็จ → `AgentTrayRecoveryService` สตาร์ท AgentTray ใหม่
→ ตัวใหม่เจอ `SingleInstanceGuard` ว่ามีตัวเก่าถือ mutex อยู่ → ปิดตัวเองทันที safety net ฝั่ง Agent
แก้ได้เฉพาะเคส **"tray ตาย"** ไม่ใช่ **"tray ค้าง"** — ต้องไป kill process เองที่เครื่องเท่านั้น
(ยังเป็นแบบนี้อยู่ ไม่ได้แก้ — ดู Known limitations)

**แก้ไปแล้ว:**
1. `BroadcastPipeListener.ReadTimeout` = 10 วิ ต่อ connection ผ่าน `Task.WhenAny` (ไม่ใช่แค่ส่ง token
   เข้า `ReadLineAsync` เฉยๆ — ถ้าการยกเลิก overlapped read ไม่เป็นผล เส้นทางนี้ยังหลุดออกมาแล้วปล่อยให้
   `using` dispose ตัว server ทิ้ง ซึ่งเป็นสิ่งที่ทำให้ read ที่ค้างอยู่ตายจริง) → loop ค้างถาวรไม่ได้อีก
2. `BroadcastPipeListener.RestartAccept()` + `App.OnPowerModeChanged` — ตอนเครื่องตื่นจาก sleep
   เปิด pipe server ใหม่ทั้งชุด ไม่ว่าของเดิมจะพังหรือไม่ ราคาตอนไม่มีอะไรพังคือศูนย์
3. `App.EnsureOnScreen()` — resume เปลี่ยน display topology ได้ (จอหาย/ความละเอียดเปลี่ยน)
   หน้าต่างที่เหลือพิกัดบนจอที่ไม่มีแล้วจะ `Show()`+`Activate()` สำเร็จทั้งคู่แต่ผู้ใช้ไม่เห็นอะไรเลย
4. `TrayDiagnostics` → `%LocalAppData%\OnionProcOparetor\AgentTray\logs\tray.log` — เดิม AgentTray
   ใช้ `Debug.WriteLine` ซึ่งมองไม่เห็นถ้าไม่มี debugger attach ทำให้ทุกอย่างที่เกิดใน field
   หายไปหมด ตอนนี้มี log ของ pipe / power event / การเปิดหน้าต่าง

**ยังไม่ยืนยัน:** ยังพิสูจน์ไม่ได้ว่า "การหลับ" ทำให้เกิดสภาพนั้นด้วยกลไกไหนกันแน่ (ที่นี่ไม่มีเครื่อง
แล็บให้ทำ sleep/resume จริง) ที่ทำไปคือปิดช่องให้มันค้างถาวรไม่ได้ + ทิ้ง log ไว้ให้ครั้งหน้ามีหลักฐาน
**เครื่องแล็บทั้ง 40 เครื่องใช้ Reboot Restore Rx — `tray.log` หายทุก reboot ต้องเก็บ log ตอนอาการ
ยังค้างอยู่ก่อนรีสตาร์ทเสมอ** และการแก้นี้ไม่มีผลจนกว่าจะ rollout MSI ตัวใหม่ลงเครื่องจริง

## UI-3a: DialogHost / SidePanelHost (2026-11-03)

WP UI-3a สร้างโครง 2 ตัวใน `src/OnionProcOparetor.UI/Controls/` สำหรับย้ายหน้าต่างย่อยของ Console
30 บานเข้ามาอยู่ในหน้าต่างหลัก (MainWindow) เดียวตามที่ roadmap UI-3 สั่ง — UI-3b/3c ทำต่อจากนี้ ควร
อ่านหัวข้อนี้ก่อนย้ายหน้าต่างไหนก็ตาม

### `DialogHost` (dialog ลอย + พื้นมืดทับ)

- โหนดเดียวใน `MainWindow.xaml` (`AppDialogHost`) ประกาศเป็นลูกตัวสุดท้ายของ root `Grid` เสมอ (z-order
  สูงสุด = ทับทุกอย่าง, ดักคลิกทุกอันที่ตกบนพื้นมืดเอง เพราะเป็น `Border` ที่มี `Background`)
- **เนื้อหา (dialog content) ไม่รู้จัก `DialogHost` เลย** — เป็น `UserControl` ธรรมดาที่ raise event
  ผลลัพธ์ของตัวเอง (เช่น `event Action<bool>? ResultChosen`) แล้วให้ **ผู้เปิด** เป็นคนเรียก
  `DialogHost.Push(content, onClosed)` หรือ `await DialogHost.ShowAsync<TResult>(content)` และเป็นคน
  subscribe `ResultChosen` เพื่อเรียก `DialogHost.CloseTop(result)` เอง — ดูตัวอย่างจริงสองแบบ:
  - **Async ตรงๆ** (ง่ายที่สุด ใช้เมื่อจุดเรียกเป็น `async` method อยู่แล้ว): `ForceChangePasswordWindow`
    ใน `MainWindow.ConnectButton_Click`
  - **Bridge แบบ "ดูเหมือน synchronous"** (ใช้เมื่อ call site เดิมเรียกแบบ
    `if (ConfirmWindow.Show(...)) { ... }` ไม่ await และมี caller เดิมหลายสิบจุดที่แก้ทุกจุดไม่คุ้ม):
    `ConfirmWindow.ShowViaHost` ใช้ `DispatcherFrame` แบบเดียวกับที่ `Window.ShowDialog()` ใช้ภายใน —
    สูบ message loop ต่อจนกว่า dialog จะถูกปิด แล้ว `return` ค่าจริง ไม่ใช่การ block thread เฉยๆ
- Esc = ปิดแบบยกเลิก (ส่งผล `null`/`default(TResult)` กลับ — สำหรับ `bool` คือ `false` พอดีอยู่แล้ว)
- Enter = กดปุ่มที่ติด `controls:DialogHost.IsDefaultButton="True"` (attached property เขียนเอง ไม่ใช้
  `Button.IsDefault`/`IsCancel` ของ WPF ตรงๆ เพราะตัวนั้นผูกกับกลไกภายในของ `Window` ซึ่งอาจมีผลข้างเคียง
  ไม่คาดคิดถ้า content ไม่ได้เป็น `Window` เอง)
- โฟกัสไม่หลุดออกนอก dialog ผ่าน `KeyboardNavigation.TabNavigation="Cycle"` บน slot เนื้อหา (มาตรฐานของ
  WPF สำหรับ trap focus ใน subtree เดียว ไม่ต้องเขียน handler ดัก Tab เอง)
- Stack ได้ถ้า dialog หนึ่งเปิด dialog อีกใบซ้อน (ใบเดิมไม่หายไป แค่ไม่โชว์จนกว่าใบบนจะปิด)
- **Owner ที่ยังไม่มี `DialogHost` ของตัวเอง** (ตอนนี้คือหน้าต่างแยกทั้งหมดที่ UI-3c จะย้าย:
  `ActionPanelWindow`, `LaunchableProgramsWindow`, `RunningProcessesWindow`,
  `WebsiteCatalogSourcesWindow`) **ยัง fallback ไปเปิดเป็น `Window` ลอยแบบเดิม** — `ConfirmWindow.Show`/
  `ShowWithTypeToConfirm` เช็คแค่ `owner is MainWindow` แล้วเลือกเส้นทาง ไม่ต้องแก้ 18 call site เดิมเลย
  แม้แต่จุดเดียว เมื่อ UI-3c ย้ายหน้าต่างพวกนี้เป็น `UserControl` ที่มี `DialogHost` ของตัวเอง (หรือยืมของ
  `MainWindow` ผ่าน property เดียวกัน) ต้องแก้แค่เงื่อนไขนี้ให้ครอบคลุม owner type ใหม่ด้วย

### `SidePanelHost` (แผงสไลด์ขวา)

- โหนดเดียวใน `MainWindow.xaml` (`AppSidePanel`) ต่อยอดจากลาย `ClientInspector`/`InspectorSlide` เดิมใน
  `DashboardWindow.xaml` (Border + `TranslateTransform` + ปุ่มกาง/ย่อ/ปิด) — ยกลอจิก slide-in/out ออกมา
  เป็น control แยกให้ใช้ซ้ำได้ **ยังไม่ได้ย้าย `ClientInspector` เดิมมาใช้ control นี้จริง** (นอกขอบเขต
  ไฟล์ของ UI-3a — `ClientInspector` อยู่ใน `DashboardWindow.xaml` ซึ่งจะถูกแก้โดย UI-3c ตอนย้ายแผงอื่นๆ
  เข้ามา เพื่อลดความเสี่ยงชนไฟล์กับ UI-3b ที่ทำงานขนานกันอยู่)
- `Show(content)` / `Close()` + event `Closed` — ไม่มีพื้นมืดทับ (ต่างจาก `DialogHost` — ครูยังเห็น/คลิก
  รายการเครื่องข้างหลังได้ระหว่างแผงเปิดอยู่ เหมือน inspector เดิม) Esc ปิดแผงให้เองเสมอ

### 401 → เด้งกลับหน้าล็อกอิน

- `ApiClient` (Console) ครอบ `HttpClientHandler` เดิม (cert pinning) ด้วย `SessionAwareHandler`
  (`DelegatingHandler`) อีกชั้น — ดัก `401` ของทุก request ที่จุดเดียวใน `SendAsync` ไม่ต้องแก้ 60+
  method ของ `ApiClient.cs` ทีละตัว ยกเว้น `/api/auth/login` เอง (endpoint นั้นตอบ 401 ปกติเวลา
  username/password ผิด — ดู `AuthEndpoints.cs`) และยกเว้นตอนที่ยังไม่ login (`IsLoggedIn==false`)
- ยิง `event Action? SessionExpired` ครั้งเดียวต่อตอนที่หลุด (กันหลาย request พร้อมกันยิงซ้ำ) —
  `MainWindow.ShowDashboardAsync` subscribe แล้วเรียก `ReturnToLoginAsync("เซสชันหมดอายุ กรุณาเข้าสู่
  ระบบใหม่")` เส้นทางเดียวกับ logout ปกติ ต่างกันแค่ข้อความสีแดงที่ StatusText ของหน้าล็อกอิน

## Known limitations / ยังไม่ได้ทำ

- `AgentTrayRecoveryService` กู้ได้เฉพาะ tray ที่ **ตาย** ไม่ใช่ tray ที่ **ค้าง** — `SingleInstanceGuard`
  ทำให้ instance ใหม่ปิดตัวเองทันทีถ้าตัวเก่ายังถือ mutex อยู่ ไม่ได้แก้เพราะการยอมให้มี tray สองตัว
  แปลว่ามี pipe listener สองตัวแย่งกันรับ message และ lock screen ซ้อนกัน — อันตรายกว่าปัญหาที่จะแก้
  ทางที่ถูกคือให้ instance ใหม่ probe pipe ของตัวเก่าก่อนแล้วค่อยตัดสินใจ ซึ่งยังไม่ได้ทำ
- **"ปิดเน็ตทั้งหมด" แบบสวิตช์เดียว (Phase 5 internet kill-switch)** — ออกแบบ+implement ไปรอบหนึ่งแล้ว
  (`NetShutdownManager`, Windows Firewall rule + watchdog กันเครื่องค้างในสถานะนี้ถาวร) แต่ถูก stash
  พร้อมงานอื่น (SignalR migration, Physical Input Block) ตอน stabilization เพราะบั๊กสะสมจนคุมยากรวมกัน
  — ยังไม่มีในระบบตอนนี้ โค้ดยังอยู่ใน `git stash`/commit `2f733be` เผื่อกลับไปหยิบมาทำใหม่ทีหลัง
  **ต่างจาก Website Control** (เพิ่มแล้ว 2026-09-11 ดูหัวข้อด้านบน) ซึ่งบล็อกเป็นรายเว็บไซต์ผ่าน
  Chrome/Edge policy ไม่ใช่ตัดเน็ตทั้งเครื่อง — สองเรื่องนี้แก้ปัญหาคนละแบบ ไม่ใช่ฟีเจอร์เดียวกัน
- `ResizeMode="NoResize"` + pixel-fixed `Canvas` layout ทุกหน้าจอ Console — ใช้งานได้ดีในสภาพแวดล้อมปกติ
  แต่ไม่รองรับ DPI/text-scaling ที่สูงกว่าปกติหรือเนื้อหาที่ยาวเกินพื้นที่ที่จัดไว้ (รู้ข้อจำกัดแล้ว ยังไม่
  ทำ เพราะเป็น rework ใหญ่)
- Console GUI ยังไม่มีการแยกสิทธิ์ระหว่าง admin account ต่างๆ (ทุกคนที่ login ได้ทำได้ทุกอย่างเหมือนกัน)
- **`POST 127.0.0.1:8788/bypass/set` ยังไม่มี auth** — login gate ของ AgentTray อยู่แค่ในหน้าต่าง WPF
  ไม่ได้อยู่ในเส้นทางของ endpoint เลย นักเรียนสิทธิ์ปกติยิงบรรทัดเดียวปิด enforcement เครื่องตัวเองได้
  (ช่องที่คุ้มแก้ที่สุดที่เหลืออยู่ — ทางแก้คือให้ tray ส่ง token ที่ได้จาก Server มาด้วยแล้วให้ Agent verify)
  **และเนื่องจาก bypass ปิด Program Control ไปด้วย (ดูหัวข้อ Program Control) request เดียวนี้จึงปิดทั้ง
  กฎ publisher และ blocklist เกมทั้งหมดพร้อมกัน** — เป็น loopback HTTP ล้วนๆ ไม่เกี่ยวกับสิทธิ์ผู้ใช้
  การเปลี่ยนนักเรียนเป็น standard user จึง**ไม่ได้ปิดช่องนี้เลย** ตั้งแต่ Website Control (2026-09-11)
  เพิ่มเข้ามา ช่องเดียวกันนี้**เคลียร์ URLBlocklist ทิ้งด้วย** (ตั้งใจให้ gate เดียวกับ Program Control —
  ดูหัวข้อ Website Control) เพราะฉะนั้น exploit เดิมตัวนี้ตอนนี้ปลดบล็อกเว็บที่เคยบล็อกไว้ได้ด้วย ไม่ใช่
  แค่โปรแกรม — ยิ่งเพิ่มน้ำหนักว่าช่องนี้ควรแก้เป็นลำดับแรกๆ
- `/vnc/session-result` และ `/processes/list-result` บน loopback ก็ยังไม่มี auth เช่นกัน — ยิง PID ปลอม
  เข้ามาให้ Agent (SYSTEM) เปิด firewall rule ให้ port ที่เลือกเองได้
- **ทั้งระบบยังเป็น HTTP ล้วน** — agent secret ที่เพิ่มมาก็ยังเดินทางเป็น plaintext
- Tailscale ไม่ enroll ซ้ำเองหลังบูต (ต้องทำตอน freeze ปิดแล้ว commit baseline)
- ลบ `uninstall-guard.json` ไฟล์เดียว = ปิด uninstall password gate ถาวรและเงียบ

---

รายละเอียดการพัฒนาแบบ dated log ทั้งหมด (2026-07-16 ถึง 2026-08-02, ทุก session/ทุกฟีเจอร์ตามลำดับเวลา
ที่ทำจริง รวมบั๊กที่เจอและวิธีแก้) ย้ายไปไว้ที่ [`docs/architecture-log.md`](architecture-log.md) แล้ว —
อ่านที่นั่นถ้าต้องการ context เชิงประวัติศาสตร์หรือเหตุผลเบื้องหลังการตัดสินใจแต่ละรอบ

### สถานะหลัง UI-3b/3c (4 พ.ย. 2569)

- **ใช้ DialogHost แล้ว:** ฟอร์มสั้น 16 หน้า (UI-3b) และ dialog ใหญ่ ได้แก่ ActionReport, Files, LaunchablePrograms, WebsiteCatalogSources (UI-3c)
  - ทุกหน้าเปลี่ยนจาก `Window` เป็น `UserControl` แล้ว แต่ยังใช้ชื่อไฟล์ `*Window` เดิม
  - ผลลัพธ์ส่งผ่าน event `ResultChosen` แทน `DialogResult`
- **ใช้ SidePanel แล้ว:** ClientSettings, RunningProcesses, PendingCommands, RuleOverrides, ActionPanel
  - เปิดผ่าน `DashboardWindow.ShowInSidePanel` ซึ่งซ่อน inspector ระหว่างที่แผงเปิด
  - ถ้าเลือกเครื่องอื่น แผงจะเปิดหน้าเดิมให้เครื่องใหม่
  - การรอให้การเลือกเครื่องนิ่งก่อน (`ReconcileSidePanelAfterSelectionSettled`) กันไม่ให้ auto-refresh ปิดแผงเอง
- **หน้าที่เปิดได้จากหลายที่:** AddEditRule, AddEditWebsiteCatalogSource, LockScreenMessage มี `ShowViaHost(Window.GetWindow(this), ...)`
  - ถ้าหน้าที่เปิดอยู่ใน MainWindow จะใช้ DialogHost
  - ถ้าไม่ได้อยู่ใน MainWindow จะ fallback ไปห่อด้วย Window
- **หน้าต่างแยกที่คงไว้:** About, TrustCertificate, TailscaleLogin, BroadcastWarningBar, TeacherBroadcastBorder, TeacherDrawCanvas, TeacherDrawToolbar และ Screen View/VNC
  - ConfirmWindow ยังเป็นหน้าต่างแยกเฉพาะตอน fallback
- **Debug harness:** `App.xaml.cs --dialog=<ชื่อ>` ห่อ UserControl ด้วย Window ชั่วคราวผ่าน `WrapAsDialog`/`WrapAsPreviewWindow` เพื่อถ่ายภาพ
