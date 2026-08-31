# รายงานสรุปงาน Hardening / Audit — 2026-08-31

รอบงานนี้ทำ 5 เรื่องตามที่สั่ง: หาเกณฑ์ว่า "ดี" ในบริบทนี้คืออะไร, audit หาบั๊ก/ช่องโหว่ให้ครบ,
ทำระบบสั่ง restart เมื่อ Agent ถูกลบผิดวิธี, ทำระบบคุมโปรแกรมให้รัดกุมกว่าเดิม, และตรวจ/ปรับ config

**Baseline ก่อนเริ่ม:** `438ae1f` — commit ทั้งหมดของรอบนี้ต่อจากจุดนั้น

| commit | เรื่อง |
|---|---|
| `438ae1f` | checkpoint ก่อนเริ่ม (snapshot งาน Console ที่ค้างอยู่) |
| `00de95f` | Task 3 — tamper watchdog |
| `d475d11` | Task 2 (ส่วนแรก) — แก้ช่องโหว่ระดับสูงสุด |
| `87764ff` | Task 2 (ส่วนสอง) — agent authentication |
| `3e42977` | Task 4 — Program Control |
| `ca86d71` | Task 5 — config review |

> **ยังไม่ได้ทดสอบบนเครื่องจริง** ทั้งหมดนี้ผ่าน build + ทดสอบเท่าที่ทำได้บนเครื่องนี้เท่านั้น
> (รายละเอียดว่าอะไรทดสอบแล้ว/ยังอยู่ท้ายรายงาน) การติดตั้งจริงต้องทำตามลำดับในหัวข้อ "แผน rollout"

---

## 1. เกณฑ์ "ระดับที่ดี" ในบริบทของโปรแกรมเรา

ค้นข้อมูลจากผลิตภัณฑ์ระดับใช้งานจริง (Huntress, Sophos, ThreatLocker, Symantec DLP) และแนวทาง
application control ของ Microsoft เอง แล้วเทียบกับบริบทของเรา (ห้อง lab 40 เครื่อง, shared login,
มี Reboot Restore Rx, LAN ปิด, ไม่มีงบ/ไม่มี domain)

### สิ่งที่ระบบระดับดีในหมวดนี้มีเหมือนกันหมด

| เกณฑ์ | ระบบเราก่อนรอบนี้ | หลังรอบนี้ |
|---|---|---|
| **Tamper protection + watchdog** — ตัว agent เองต้องกลับมาเองได้ และ "การถูกลบ" ต้องเป็นเหตุการณ์ที่ระบบรู้ | มีแค่ `sc failure` (auto-restart ตอน crash) — ลบ service ทิ้ง = จบ ไม่มีใครรู้ | มี watchdog แยก process + ค้ำกันสองทาง + reboot + แจ้ง server |
| **Agent มีตัวตนที่พิสูจน์ได้** ไม่ใช่แค่ "รู้ ID ก็พอ" | ClientGuid ใน URL ทุก poll บน HTTP ธรรมดา = ใครดักดูก็ปลอมเป็นเครื่องอื่นได้ | per-agent secret + enroll ครั้งแรก + ตรวจทุก endpoint และ SignalR hub |
| **Allowlist-first ไม่ใช่ blocklist ชื่อ publisher** | blocklist ตาม publisher ที่ครูพิมพ์เอง — ของที่ไม่เซ็นและไม่มี metadata "มองไม่เห็นแม้แต่ใน log" | ยังไม่ใช่ allowlist เต็มรูปแบบ แต่มีตาข่ายรับ + คิวให้คนตัดสิน (ดูข้อ 4) |
| **Audit trail ที่ปิดไม่ได้** | มี Activity Log อยู่แล้ว (ดีอยู่) แต่ path ที่ bypass ระบบไม่ถูกบันทึกเลย | tamper / secret mismatch / คำตัดสินโปรแกรม เข้า Activity Log หมด |
| **Retention + ขอบเขตทรัพยากร** | Activity Log มี 7 วัน แต่ LogEntry/AuthToken/ไฟล์ log ไม่มีเลย | มี retention ทั้งสามอย่าง |
| **Fail-secure ที่นิยามชัด** | ทำถูกอยู่แล้ว (`_lastKnownState`) — เป็นจุดแข็งเดิมของโปรเจกต์ | คงไว้ + นิยามเพิ่มว่าส่วนไหน fail-open และทำไม |

### เรื่องที่ตัดสินใจ "ไม่ทำ" พร้อมเหตุผล

- **WDAC / AppLocker** — เป็นวิธีที่แข็งที่สุดจริง (WDAC อยู่ใน kernel code-integrity, admin ก็ข้าม
  ไม่ได้) แต่ **AppLocker ต้องใช้ Windows Enterprise/Education** เครื่อง lab เป็น Pro จึงใช้ไม่ได้
  ตามสัญญาการรองรับ ส่วน WDAC ทำได้บน Pro แต่เป็น allowlist ทั้งเครื่อง ซึ่งแปลว่าทุกครั้งที่ครู
  ต้องลงโปรแกรมใหม่ต้องแก้ policy ก่อน — ภาระสูงเกินไปสำหรับคนดูแลคนเดียว **เก็บเป็น plan**
  (ดูหัวข้อ 7) ถ้าอนาคตอัปเกรดเป็น Education license
- **Protected Process Light / kernel driver** — กัน admin ได้จริงแต่ต้องเซ็น driver ด้วย EV cert
  ราคาหลักหมื่นต่อปี ไม่คุ้มกับ threat model นี้ (นักเรียน ไม่ใช่ผู้โจมตีมืออาชีพ)
- **TLS** — ควรทำ และเป็นข้อจำกัดที่ยังเหลืออยู่จริง (ดูข้อ 7) ไม่ทำในรอบนี้เพราะเป็นการเปลี่ยน
  ที่กระทบทั้ง Agent/Console/installer พร้อมกัน ควรเป็นรอบของตัวเอง

---

## 2. บั๊กและช่องโหว่ที่เจอ (audit 3 ชุด: Server / Agent+Tray / Console+Installer)

### แก้แล้วในรอบนี้

| # | ระดับ | เรื่อง | จุด |
|---|---|---|---|
| 1 | **CRITICAL** | MSI ให้สิทธิ์ `GenericAll` (= NTFS Full Control รวม WRITE_DAC/WRITE_OWNER) แก่กลุ่ม `Users` ทั้งโฟลเดอร์ `ClientTray` — **นักเรียนสิทธิ์ปกติเขียนทับ `AgentTray.exe` และ `winvnc.exe` ได้เลย** comment ในโค้ดเขียนว่าตั้งใจให้แค่ "Modify เพื่อเขียน ultravnc.ini" | `OnionProcOparetor.Agent.wxs` |
| 2 | **CRITICAL** | named pipe ของ AgentTray ตั้ง ACL เป็น `Everyone` และประมวลผลทุกบรรทัดที่ได้รับโดยไม่ตรวจผู้ส่ง — นักเรียนยิง `{"Type":"HideLockScreen"}` เข้ามาปลดล็อกหน้าจอที่ครูสั่งล็อกได้เอง (รวมถึง `UnblockTargetInput`, `StopVncSession`) | `BroadcastPipeListener.cs` |
| 3 | **CRITICAL** | endpoint ฝั่ง agent ทั้งหมดไม่มี auth เลย + `commandId` เป็นเลขรันนิ่ง → ยิง `POST /api/commands/{n}/ack` รัวๆ ทำให้คำสั่งของครูถูก mark ว่าส่งแล้วโดยไม่เคยถูกรัน / ปลอมผลลัพธ์ / อ่านกฎทั้งชุด / ปลอม log | `Program.cs` (Server) |
| 4 | **CRITICAL** | SignalR hub ไม่มี auth — ใครก็ต่อเข้ามาเรียก `RegisterAgent(guid ของเครื่องอื่น)` แล้วรับคำสั่งแทนเครื่องนั้นได้ | `LabHub.cs` |
| 5 | **HIGH** | `/api/auth/setup` ไม่ต้อง auth + สร้าง Administrator รหัสผ่านว่าง + `change-password` ไม่ต้องรู้รหัสเดิม + built-in account reset จากทางอื่นไม่ได้ = ชิงยึด Console ถาวรได้ | `Program.cs` |
| 6 | **HIGH** | login ไม่มี rate limit / lockout เลย | `Program.cs` |
| 7 | **HIGH** | token ไม่เคยถูกเพิกถอน แม้เปลี่ยนรหัสผ่านแล้ว (อายุ 12 ชม.) | `Program.cs` |
| 8 | **HIGH** | `POST /api/logs` ไม่ต้อง auth + ไม่จำกัดความยาว field + `LogEntry` ไม่มี retention = ถมดิสก์ server จนล่มได้ | `Program.cs` |
| 9 | **HIGH** | `RfbClient` จองหน่วยความจำตามค่าที่มาจาก wire ก่อนตรวจ — string length ~2GB และ rectangle ใหญ่กว่า framebuffer ที่ตกลงกันไว้ | `RfbClient.cs` |
| 10 | **MEDIUM** | kill เฉพาะ PID ที่ match ไม่ใช่ทั้ง tree — launcher ที่ spawn ตัวเกมจริงไปแล้วรอด | `Worker.cs` |
| 11 | **MEDIUM** | SQLite ใช้ rollback journal + busy timeout 0 ภายใต้ผู้เขียนพร้อมกัน ~45 ราย | `Program.cs` |
| 12 | **MEDIUM** | `remove-installfolder-extras.ps1` ลบ `INSTALLFOLDER` แบบ recursive force โดยไม่ตรวจอะไรเลย ทั้งที่ path นั้นผู้ติดตั้งเลือกเองได้ | installer |
| 13 | **MEDIUM** | `delete-programdata.ps1` **parse ไม่ผ่านเลย** เพราะไม่มี UTF-8 BOM (ยังไม่กระทบของจริงเพราะตัวที่รันคือ blob แยก แต่เป็นกับดักตอน regenerate) + อีก 6 ไฟล์เสี่ยงเดียวกัน | installer |
| 14 | **MEDIUM** | `SQLitePCLRaw.lib.e_sqlite3 2.1.11` — CVE-2025-6965 (memory corruption ใน SQLite ก่อน 3.50.2) | csproj |
| 15 | **LOW** | ไฟล์ log ทั้ง Agent และ Server ไม่มี rotation เลย | `FileLoggerProvider.cs` |
| 16 | **LOW** | `AuthToken` ไม่เคยถูกลบแม้หมดอายุ | Server |
| 17 | — | **บั๊กที่เจอเองระหว่างอ่านโค้ด (ไม่ได้อยู่ใน audit):** `PollIntervalOverrideSeconds` ถูกเขียนลง `ClientState` ทั้งจาก poll response และ `UpdateSettings` command มาตลอด แต่ **ไม่มีโค้ดตรงไหนอ่านกลับมาใช้เลย** — loop หลักใช้ค่าจาก `appsettings.json` ตายตัวเสมอ ฟีเจอร์ "Remote client setting" ทั้งอัน (UI, DB column, command type, และ comment ที่เขียนว่า "มีผลทันที") ไม่เคยมีผลจริงสักครั้ง | `Worker.cs` |

### รู้แล้วแต่ยังไม่แก้ในรอบนี้ (เก็บเป็น plan — ดูหัวข้อ 7)

| ระดับ | เรื่อง | ทำไมยังไม่แก้ |
|---|---|---|
| CRITICAL | `POST /bypass/set` บน loopback ไม่มี auth — process ของนักเรียนยิง `Invoke-RestMethod http://127.0.0.1:8788/bypass/set` ปิด enforcement ของเครื่องตัวเองได้เลย ไม่ต้องมีสิทธิ์อะไร | ทางแก้ที่ถูกคือให้ Server เป็นคนอนุญาต (bypass เป็น action ของครู) ซึ่งเปลี่ยน flow ของ AgentTray ด้วย — ควรทำเป็นรอบของตัวเอง |
| HIGH | `/vnc/session-result` และ `/processes/list-result` บน loopback ก็ไม่มี auth — ยิง PID ปลอมเข้ามาให้ Agent (SYSTEM) เปิด firewall rule ให้ port ที่นักเรียนเลือกเองได้ | ทางแก้: nonce ต่อ request ที่ Agent ออกให้ตอนสั่ง แล้ว tray ส่งกลับมา (pipe ที่แก้ ACL แล้วในรอบนี้เป็นเงื่อนไขที่ทำให้วิธีนี้ปลอดภัยพอ) |
| HIGH | ทั้งระบบยังเป็น **HTTP ล้วน** — agent secret ที่เพิ่มในรอบนี้ก็ยังเดินทางเป็น plaintext | เปลี่ยนพร้อมกันทั้ง Agent/Console/installer ควรเป็นรอบของตัวเอง |
| MEDIUM | ลบ `uninstall-guard.json` ไฟล์เดียว = ปิด uninstall password gate ถาวรและเงียบ | ต้องมี marker แยกที่บอกว่า "เครื่องนี้ควรมี gate" ซึ่งควรออกแบบคู่กับ watchdog |
| MEDIUM | UninstallHelper ส่งรหัสผ่านเป็น command-line argument ของ `msiexec.exe` (คนอื่นบนเครื่องอ่าน command line ได้ระหว่างที่รันอยู่) | ไม่มีทางแก้ที่ไม่ต้อง redesign flow |
| MEDIUM | ฆ่า Agent = LockScreen ปลดตัวเองภายใน ~3 วิ (fail-open โดยตั้งใจ กันเครื่องค้างถาวร) | ตอนนี้ watchdog ทำให้ราคาของการฆ่า Agent คือ "เครื่อง reboot" ซึ่งลดผลของช่องนี้ไปมากแล้ว |
| MEDIUM | `WinVerifyTrust` ตั้ง `WTD_REVOKE_NONE` — cert ที่ถูก revoke ยังนับว่า signed | ถ้าเครื่อง lab ต่อเน็ตได้ ควรเปลี่ยนเป็น `WTD_REVOKE_WHOLECHAIN` |
| LOW | `RunningProcessesWindow` / `PendingCommandsWindow` เปิดซ้อนหลายบานต่อเครื่องเดียวได้ | UX ล้วน (หน้าต่างใหม่ในรอบนี้กันไว้แล้ว) |

---

## 3. ตรวจจับการถูกลบผิดวิธี → สั่ง restart เครื่อง

### หลักการ

ตัว Agent ตรวจ "ตัวเองถูกลบ" ไม่ได้ (ลบไปแล้วก็ไม่มีใครรัน) จึงต้องมี process อิสระ
เลือก **Scheduled Task รันเป็น SYSTEM** แทน service ตัวที่ 2 เพราะ (ก) ไม่โผล่ใน `services.msc`
ให้เห็นเป็นเป้าถัดไป (ข) มี trigger `AtStartup` ในตัว (ค) Agent สร้าง task คืนได้เอง

**ค้ำกันสองทาง** — นี่คือหัวใจ:
- ลบ task อย่างเดียว → Agent สร้างคืนภายใน 1 นาที (`TamperProtectionService`)
- ฆ่า Agent อย่างเดียว → task เห็น heartbeat ค้าง แล้ว reboot ภายใน ~2 นาที
- ต้องทำทั้งสองอย่างสำเร็จ **พร้อมกัน** ในหน้าต่างเวลาแคบๆ ถึงจะรอด

### เงื่อนไขที่นับว่าผิดปกติ

| ระดับ | เงื่อนไข | reboot เมื่อ |
|---|---|---|
| HARD | ไม่มี service `OnionAgent` ใน SCM | ครั้งแรกทันที |
| HARD | StartMode ถูกเปลี่ยนจาก Auto | ครั้งแรกทันที |
| HARD | ไม่มีไฟล์ `OnionProcOparetor.Agent.exe` | ครั้งแรกทันที |
| SOFT | service ไม่ได้ Running | เจอติดกัน 2 รอบ (~2 นาที) |
| SOFT | heartbeat เก่าเกิน 240 วิ (จับ process ถูก suspend/แขวน) | เจอติดกัน 2 รอบ |

แยก HARD/SOFT เพราะ **reboot กลางคาบเรียนเพราะ false-positive แพงกว่ารอเพิ่มอีก 1 นาที**

### กันไม่ให้พังเอง (สำคัญกว่าตัวฟีเจอร์)

- **maintenance ticket** — เอกสาร JSON เซ็นด้วย HMAC-SHA256 จาก `wd.key` ที่ ACL ตัด `Users` ออก
  MSI ออก ticket ให้ **ทุก transaction แบบไม่มีเงื่อนไข** ที่ `InstallInitialize`
  จงใจไม่ gate ด้วย `REMOVE` — **major upgrade** คือเคสที่จะโดนพอดี (ของเก่าถูกถอนโดยมี
  `UPGRADINGPRODUCTCODE` ตั้งอยู่ ถ้า gate ด้วย REMOVE จะถูกข้าม แล้วช่วงระหว่าง `RemoveFiles`
  กับ `InstallFiles` ใหม่จะหน้าตาเหมือนการถูกลบทุกประการ)
- **decommission marker** — uninstall จริงเขียน marker ถาวร (เซ็นเหมือนกัน) + ถอน task
  marker ยังจำเป็นแม้ถอน task สำเร็จ เผื่อการถอนล้มเหลว จะได้ไม่มี watchdog ค้างอยู่บนเครื่องที่
  ไม่มี Agent แล้ว reboot วนไปเรื่อยๆ
- **เพดาน 3 reboot ต่อ 24 ชม.** — เกินแล้วหยุด reboot เขียน Event Log + แจ้ง server แทน
- **ไม่ประเมินอะไรเลยใน 180 วินาทีแรกหลังบูต** — ให้ service มีเวลาสตาร์ท
- **`cleanup-legacy-agent.ps1`** (สคริปต์ push ทั้งห้องผ่าน endpoint manager) ออก decommission
  marker ก่อนแตะอะไร — ไม่งั้นมันจะกลายเป็นตัวสั่ง reboot ทั้งห้องพร้อมกันกลางคาบ

### ข้อจำกัดที่พูดตรงๆ

**ไม่ได้กัน admin** คนที่มีสิทธิ์ admin จริงลบทั้ง service + task + ไฟล์พร้อมกันได้เสมอ
(จะกันจริงต้องใช้ kernel driver) เป้าที่ตั้งไว้คือ: กันนักเรียนสิทธิ์ปกติ 100%, ทำให้ admin ที่ทำ
ครึ่งๆ กลางๆ โดน reboot ทันที, และเหลือหลักฐานเสมอ

### ไฟล์ที่เกี่ยวข้อง

```
installer/CustomActions/agent-watchdog.ps1     ตัวตรวจ (Scheduled Task รันทุก 1 นาที + ตอนบูต)
installer/CustomActions/install-watchdog.ps1   ลงทะเบียน task + สร้าง wd.key + ตั้ง ACL
installer/CustomActions/maintenance-ticket.ps1 ออก/ยกเลิกใบอนุญาตยุ่งกับ Agent
src/OnionProcOparetor.Agent/Services/TamperProtectionService.cs   heartbeat + self-heal
%ProgramData%\OnionProcOparetor\Agent\watchdog-config.json        ปรับ threshold ได้ที่นี่
%ProgramData%\OnionProcOparetor\Agent\watchdog.log                log ของ watchdog
Event Log > Application > OnionProcOparetorWatchdog (9001-9004)
```

---

## 4. Program Control — คุมโปรแกรมให้รัดกุมกว่าเดิม

### ปัญหาของระบบเดิม

ระบบ rule เดิมจับได้เฉพาะโปรแกรมที่ครูรู้จักชื่อ publisher ล่วงหน้าและพิมพ์กฎไว้แล้ว และ
`Worker.CheckAndActOnProcess` **return ทันทีถ้าไฟล์ไม่มี publisher เลย** แปลว่าโปรแกรมที่ไม่ได้เซ็น
และไม่มี `CompanyName` (เกม portable / โหลดมา ส่วนใหญ่) **ไม่เคยปรากฏแม้แต่ใน log**
และการเลี่ยงกฎที่ match ก็แค่แก้ `CompanyName` ด้วย resource editor (ไม่ต้อง re-sign อะไรเลยถ้า
ไฟล์ไม่ได้เซ็นมาแต่แรก) พูดสั้นๆ: **ระบบเดิมจับได้เฉพาะเกมที่ยอมบอกชื่อตัวเอง**

### วิธีที่เลือก

จำแนกที่ **Server** ที่เดียว key ด้วย **SHA-256 ของไฟล์** — ตัวระบุเดียวที่นักเรียนเปลี่ยนไม่ได้ด้วย
การ rename หรือย้ายที่ (เปลี่ยนได้ก็ต่อเมื่อไป patch binary จริง)

ทำไม Server ไม่ใช่ Agent: (ก) เกณฑ์เปลี่ยนได้โดยไม่ต้อง deploy Agent ใหม่ 40 เครื่อง
(ข) ผลที่คำนวณแล้วแชร์กันทั้งห้อง — เครื่องแรกที่เจอเกมหนึ่งทำให้อีก 39 เครื่องปิดมันได้ทันที
(ค) การเรียกข้อมูลออนไลน์ควรออกจากจุดเดียว ไม่ใช่ 40 จุดพร้อมกัน

### น้ำหนักของสัญญาณ — เรียงตาม "ปลอมยากแค่ไหน"

| กลุ่ม | ตัวอย่าง | น้ำหนัก | เหตุผล |
|---|---|---|---|
| ไฟล์/DLL ที่ลบแล้วเกมพัง | `steam_appid.txt`, `steam_api64.dll`, `EOSSDK`, `UnityPlayer.dll` | 25–45 | แพงที่สุดที่จะปลอม |
| โครงสร้างโฟลเดอร์ที่ launcher สร้าง | `\steamapps\common\`, `\Epic Games\`, `\Roblox\` | 35–50 | ย้ายได้ แต่เสียการอัปเดต/รันจาก launcher |
| DLL ที่ process โหลดจริง | XInput, D3D12, FMOD, Wwise | 8–22 | XInput = API จอยเกม โปรแกรมเรียนแทบไม่โหลด |
| publisher | Valve, Riot, miHoYo, Garena ฯลฯ | 40–50 (จาก signature) / ครึ่งเดียว (จาก metadata) | ตรงกับหลักการ `RequireSignedMatch` เดิม |
| ข้อมูลออนไลน์ | ชื่อตรงกับเกมใน Steam app list | 45 | เป็นสัญญาณหนึ่งตัว ไม่ใช่คำตัดสิน |
| ชื่อ/คำสำคัญ | `-Win64-Shipping`, ชื่ออีมูเลเตอร์ | 10–40 | ต่ำสุดโดยตั้งใจ — แก้ด้วย text editor ได้ |
| **หักคะแนน** | เซ็นโดย Microsoft/Adobe/Python/Intel ฯลฯ | −25 ถึง −35 | **ให้ต่อเมื่อเซ็นจริงเท่านั้น** — ไม่งั้นพิมพ์ "Microsoft Corporation" ลง CompanyName ก็ได้ส่วนลดฟรี |

**กฎเหล็ก:** อะไรก็ตามใน `%SystemRoot%` → Allow ทันที ไม่ผ่านการให้คะแนนเลย
(`dwm.exe`/`explorer.exe` โหลด d3d11 + xinput อยู่แล้วตามปกติ — ถ้าปิดไป Windows พังทั้งเครื่อง)

### สามระดับ ไม่ใช่สอง

`>= 70` Block · `>= 40` Flag (log + เข้าคิวให้ครู) · ต่ำกว่านั้น Allow

คะแนนกลางๆ ตกไปที่ Flag เพราะ **ต้นทุนของการปิดโปรแกรมที่นักเรียนกำลังใช้ทำงานส่งกลางคาบ
แพงกว่าการปล่อยเกมหลุดหนึ่งรอบ poll** และเมื่อครูตัดสินครั้งเดียว ผลผูกกับ hash ตลอดไปทั้ง 40 เครื่อง

**คำตัดสินของครู (`IsManual`) ชนะอัลกอริทึมเสมอ ไม่มีเงื่อนไข** — อัลกอริทึมที่แก้คำตัดสินของคนได้เอง
คือระบบที่ครูเลิกเชื่อถือในสัปดาห์แรกที่มันย้อนกลับมาบล็อกของที่เพิ่งปลดไป

### สองชั้นของการบังคับใช้

1. **blocklist ในหน่วยความจำ** — hash ที่ตัดสินแล้วมาพร้อม poll response (ส่งเฉพาะตอนเปลี่ยนจริง
   ผ่าน version tag ไม่งั้น 40 เครื่องจะลากรายการเดิมข้ามเน็ตทุก 30 วิ) เจอแล้วปิดทันที ไม่มี round trip
2. **ยังไม่รู้จัก** — เข้าคิว ส่งเป็นชุดหลัง poll แล้วบังคับใช้ผลที่ได้กลับมา

**ชั้นที่ 2 fail-open โดยตั้งใจ** — ทางเลือกคือ "ห้ามทุกอย่างที่ยังไม่รู้จัก" ซึ่งบน Windows จริง
แปลว่าปิดตัวเองตายตั้งแต่บูต ราคาที่จ่าย: ครั้งแรกที่มีคนเปิดเกมใหม่ในห้อง เกมนั้นได้รันประมาณ
หนึ่งรอบ poll ก่อนถูกปิด **หลังจากนั้นทั้งห้องปิดได้ทันที**
(ต่างจากระบบ rule เดิมที่ fail-secure ได้เพราะมีรายการที่แน่นอนอยู่แล้ว — ตรงนั้นไม่เปลี่ยน)

### ผลทดสอบจริง (ยิงเข้า server แยก port/DB — production ไม่ถูกแตะ)

| อินพุต | ผล |
|---|---|
| `explorer.exe` (ระบบ, โหลด d3d11 + xinput) | Allow |
| VS Code (เซ็นโดย Microsoft) | Allow |
| `python.exe` (เซ็นโดย PSF) | Allow |
| Steam game (`\steamapps\common\` + `steam_api64.dll`) | **Block** |
| Unreal shipping exe ในโฟลเดอร์ Epic Games | **Block** |
| Roblox | **Block** |
| **Steam game ที่ปลอม CompanyName เป็น "Microsoft Corporation"** | **Block** ← ส่วนลด trusted-publisher ให้เฉพาะที่เซ็นจริง |
| Unity build เปล่าๆ ใน Downloads (ไม่เซ็น) | Flag → ถูกแล้ว อาจเป็นงาน Unity ของนักเรียนเอง |
| LDPlayer (อีมูเลเตอร์) | Flag |
| ไฟล์ rename เป็น `chrome.exe` ไม่มี marker อะไรเลย | Allow ← ข้อจำกัดจริง ดูด้านล่าง |

### ข้อจำกัดที่ยอมรับ (ไม่ใช่บั๊กที่จะแก้ทีหลัง)

- **เกมผ่าน browser มองไม่เห็นเลย** ด้วยวิธีนี้ — ต้องคุมที่ระดับ DNS/เว็บ ซึ่งเป็นคนละงาน
- **ไฟล์ที่ไม่มี marker อะไรเลย** ได้ 0 คะแนน = Allow ถ้ามันเป็นเกมจริงต้องพึ่งครูเห็นเองแล้วบล็อก
  ด้วยมือ (ซึ่งจะผูกกับ hash แล้วมีผลทั้งห้องทันที)
- ไฟล์ใหญ่กว่า 250 MB ไม่ hash (ประหยัด I/O) → จะถูกจำแนกใหม่ทุกครั้ง ยังทำงานถูก แค่ไม่ประหยัด

### ปรับจูนได้ที่

`src/OnionProcOparetor.Server/appsettings.json` → `ProgramControl`
(`BlockThreshold`, `FlagThreshold`, `EnableOnlineLookup`)
ตารางสัญญาณและน้ำหนักอยู่ที่ `Services/ProgramClassifier.cs` (บนสุดของ class อ่านง่ายเป็นตาราง)

**หน้าใช้งาน:** Console → ปุ่ม `PROGRAMS` ที่ header → เลือกรายการ → เห็น **เหตุผลทีละสัญญาณ**
พร้อมน้ำหนัก → กด "อนุญาตถาวร" / "บล็อกถาวร (ทุกเครื่อง)"

---

## 5. Config ที่ปรับ

| เดิม | ใหม่ | เหตุผล |
|---|---|---|
| `sc failure reset= 86400 actions= restart/0 x3` | `reset= 3600 actions= restart/5000/15000/60000` + `failureflag 1` | เดิม: ล้ม 3 ครั้งในวันเดียว = ไม่ restart อีกเลยทั้งวัน นักเรียนที่ทำให้ล้มซ้ำได้ 3 ครั้งจะได้เครื่องที่ไม่มีการควบคุมฟรีๆ / restart/0 ติดกัน 3 รอบไม่ได้เพิ่มโอกาสสำเร็จ |
| log ไฟล์โตไม่จำกัด | rotate ที่ 8MB เก็บ 1 รุ่น | Information level + poll ทุก 30 วิ = โตทั้งเทอมโดยไม่มีใครลบ บนเครื่องที่มี RRRx กินพื้นที่ snapshot อยู่แล้ว |
| SQLite rollback journal, busy timeout 0 | `journal_mode=WAL` + `Default Timeout=30` | 40 agent + Console + 3 background service เขียนไฟล์เดียวกัน — ยืนยัน WAL ทำงานจริงจากไฟล์ `-wal`/`-shm` ที่เกิดขึ้น |
| Kestrel body limit 30MB (default) | 4MB | endpoint ที่ไม่ต้อง auth ไม่ควรรับ 30MB ต่อ request |
| ไม่มี retention ของ `LogEntry`/`AuthToken` | 90 วัน / ลบ token หมดอายุ | ปรับได้ที่ `Retention:LogEntryDays` |
| `SQLitePCLRaw.lib.e_sqlite3 2.1.11` (CVE-2025-6965) | pin `3.50.3` | EF Core 10.0.10 ยัง pin 2.1.11 — override เฉพาะ native binary (แพ็กเกจ `lib.*` มีแต่ไฟล์ .dll ไม่มี managed API ให้ชนกับ core/provider) **ยืนยัน runtime แล้ว** ไม่ใช่แค่ build ผ่าน |
| `Microsoft.AspNetCore.SignalR 1.2.0` | ถอดออก | แพ็กเกจยุค ASP.NET Core 2.x — SignalR อยู่ใน shared framework ของ net10.0 อยู่แล้ว |
| `.ps1` 7 ไฟล์มีข้อความไทยแต่ไม่มี BOM | เติม BOM ครบ | `delete-programdata.ps1` parse ไม่ผ่านเลย และ `setup-client.ps1` พังทันทีที่เติม comment ไทยเข้าไป |
| ลบ INSTALLFOLDER แบบไม่มีเงื่อนไข | ตรวจชื่อ leaf + กัน drive root | + **regenerate EncodedCommand blob ทั้ง 2 .wxs** (ไฟล์ .ps1 เป็นแค่ source ไม่ได้ถูกรันจริง) |

---

## 6. สถานะการทดสอบ — อะไรทดสอบแล้ว อะไรยัง

### ทดสอบแล้วจริงบนเครื่องนี้

- build ทั้ง solution ผ่าน 0 error
- `ProgramClassifier` — 10 เคสตามตารางข้อ 4 ยิงผ่าน HTTP เข้า server จริง (port 18787, DB แยก)
- blocklist versioning — รอบแรกส่ง 4 hash, รอบสองส่ง `null` (ใช้ของเดิม) ถูกต้อง
- login lockout — ผิด 5 ครั้งได้ 401, ครั้งที่ 6 ได้ 429
- `/api/program-verdicts` ไม่มี token → 401
- `/api/auth/setup` จาก loopback → ยังทำงานได้ (ไม่ได้พังของเดิม)
- migration `AddAgentSecret` + `AddProgramVerdicts` รันผ่านบน DB เปล่า
- SQLite ตัวใหม่ (3.50.3) — migration + read/write ผ่านจริง
- WAL — ยืนยันจากไฟล์ `-wal`/`-shm` ที่เกิดขึ้น
- `.ps1` ทุกไฟล์ใน installer parse ผ่าน + มี BOM ครบ
- EncodedCommand blob ทั้ง 2 .wxs — decode กลับ, parse ผ่าน, มี guard ใหม่จริง
- `.wxs` ทั้ง 2 ไฟล์ยังเป็น XML ที่ถูกต้อง
- **production server บน port 8787 ไม่ถูกแตะตลอดการทดสอบ**

### ยังไม่ได้ทดสอบ (ต้องทำก่อน rollout จริง)

- ❗ **watchdog ทั้งระบบ** — ยังไม่เคยรันบนเครื่องที่ติดตั้งจริงเลย รวมถึงการ reboot จริง
- ❗ **maintenance ticket ตอน MSI upgrade/uninstall** — จุดที่พลาดแล้วเจ็บที่สุด (เครื่อง reboot กลาง upgrade)
- ❗ **agent secret enroll** กับ Agent จริง (ทดสอบแค่ฝั่ง Server)
- ❗ **ACL ของ `ultravnc.ini`** — VNC ยังเปิดได้จริงไหมหลังถอน Full Control
- ❗ **named pipe ACL ใหม่** — broadcast/lock screen ยังทำงานไหม (Agent เป็น SYSTEM ควรผ่าน แต่ต้องยืนยัน)
- ❗ `wix build` ทั้ง 2 MSI (ไม่ได้ build installer ในรอบนี้)
- Program Control end-to-end จาก Agent จริง (ทดสอบแค่ฝั่ง Server ด้วย fingerprint ที่ประกอบเอง)
- หน้า `ProgramReviewWindow` (build ผ่าน แต่ยังไม่เคยเปิดดูจริง)

---

## 7. แผน rollout ที่แนะนำ

> ลำดับนี้สำคัญ — ทำสลับแล้วมีโอกาสทำให้ทั้งห้องหลุดจากระบบพร้อมกัน

1. **ทดสอบบนเครื่องทดลอง 1 เครื่องก่อนเสมอ** ห้าม push ทั้งห้องเลย
   - ติดตั้ง Agent MSI ใหม่ → ยืนยันว่า Scheduled Task `OnionProcOparetor Agent Watchdog` ถูกสร้าง
   - ดู `%ProgramData%\OnionProcOparetor\Agent\watchdog.log` ว่าเดินทุกนาทีและไม่เจอ finding
   - **ทดสอบ upgrade ทับ** (จุดเสี่ยงที่สุด) — ต้องไม่ reboot ระหว่างทาง
   - **ทดสอบ uninstall ผ่าน UninstallHelper** — ต้องไม่ reboot และ task ต้องหายไป
   - `net stop OnionAgent` → ควรเห็นเครื่อง reboot ภายใน ~2 นาที (นี่คือพฤติกรรมที่ต้องการ)
   - เปิด VNC จาก Console → ยืนยัน ACL ของ `ultravnc.ini` ไม่ได้ทำให้พัง
   - สั่ง Lock Screen → ยืนยัน named pipe ACL ใหม่ไม่ได้ทำให้ broadcast พัง
2. **deploy Server ก่อน Agent เสมอ** — Server เวอร์ชันใหม่รับ Agent เก่าได้
   (`RequireAgentSecret` = `false`) แต่ Agent ใหม่ยิง `/api/classify` ใส่ Server เก่าจะได้ 404
3. ทยอย deploy Agent ทีละกลุ่ม ไม่ใช่ทั้ง 40 เครื่องพร้อมกัน
4. **เฝ้า Activity Log** จนไม่เห็น `AgentSecretMismatch` และไม่เห็น warning "ไม่ได้ส่ง agent secret"
   แล้วค่อยตั้ง `Security:RequireAgentSecret = true` แล้ว restart Server
5. เปิด Console → `PROGRAMS` ดูคิว ทยอยตัดสินรายการที่เจอบ่อยก่อน ปรับ threshold ถ้าเห็นว่าเข้มหรือหลวมไป

### แผนงานถัดไป (เรียงตามความคุ้ม)

1. **auth ของ loopback API** (`/bypass/set` โดยเฉพาะ) — ช่องที่เหลือที่ **นักเรียนสิทธิ์ปกติ**
   ใช้ได้ทันทีโดยไม่ต้องมีอะไรเลย คุ้มที่สุดในลิสต์นี้
2. **nonce ผูก request** สำหรับ `/vnc/session-result` และ `/processes/list-result`
3. **HTTPS + self-signed cert pin ใน installer** — ทำให้ agent secret มีความหมายเต็ม
4. marker กันลบ `uninstall-guard.json`
5. `WTD_REVOKE_WHOLECHAIN` ถ้าเครื่อง lab ต่อเน็ตได้
6. WDAC (ถ้าอัปเกรดเป็น Windows Education) — ตัวเดียวที่กัน admin ได้จริงโดยไม่ต้องเซ็น driver เอง
7. ให้ Program Control รายงาน "โปรแกรมที่ Flag" เข้าหน้า Dashboard เป็น badge แทนที่จะต้องกดเข้าไปดู

---

## 8. ไฟล์ที่เพิ่มใหม่ทั้งหมด

```
installer/CustomActions/agent-watchdog.ps1
installer/CustomActions/install-watchdog.ps1
installer/CustomActions/maintenance-ticket.ps1

src/OnionProcOparetor.Agent/Models/ProgramControlModels.cs
src/OnionProcOparetor.Agent/Services/AgentCredentials.cs
src/OnionProcOparetor.Agent/Services/ProgramControlService.cs
src/OnionProcOparetor.Agent/Services/ProgramFingerprinter.cs
src/OnionProcOparetor.Agent/Services/TamperProtectionService.cs

src/OnionProcOparetor.Server/Models/ProgramFingerprint.cs
src/OnionProcOparetor.Server/Models/ProgramVerdict.cs
src/OnionProcOparetor.Server/Services/AgentAuthenticator.cs
src/OnionProcOparetor.Server/Services/LoginThrottle.cs
src/OnionProcOparetor.Server/Services/MainDbRetentionService.cs
src/OnionProcOparetor.Server/Services/OnlineGameLookup.cs
src/OnionProcOparetor.Server/Services/ProgramClassifier.cs
src/OnionProcOparetor.Server/Services/ProgramVerdictService.cs
src/OnionProcOparetor.Server/Migrations/*_AddAgentSecret.cs
src/OnionProcOparetor.Server/Migrations/*_AddProgramVerdicts.cs

src/OnionProcOparetor.Console/Models/ProgramVerdictDto.cs
src/OnionProcOparetor.Console/ProgramReviewWindow.xaml(.cs)
```

### API ที่เพิ่ม

| endpoint | auth | ใช้ทำอะไร |
|---|---|---|
| `POST /api/classify` | agent secret | Agent ส่ง fingerprint มาให้จำแนกเป็นชุด |
| `GET /api/program-verdicts?filter=` | admin | คิวตรวจสอบ (`flagged`/`blocked`/`allowed`/`manual`/`all`) |
| `POST /api/program-verdicts/{id}/decide` | admin | ครูตัดสินด้วยมือ (ทับอัลกอริทึมถาวร) |
| `POST /api/tamper-report` | ไม่ต้อง (เขียนอย่างเดียว) | watchdog แจ้งก่อน reboot |
| `POST /api/clients/{id}/reset-agent-secret` | admin | ล้าง enrollment เพื่อให้เครื่อง enroll ใหม่ |
| `GET /api/poll/{guid}` | agent secret (เพิ่ม) | เพิ่ม `blocklistVersion` + `blockedHashes` |
