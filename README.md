# Student-Happyness-Slayer

> **โปรแกรม: Onion ProcOparetor** — *"Peeling processes since 2026."*
> (ชื่อ repo บน GitHub ยังคงเป็น `Student-Happyness-Slayer` ตามเดิม ไม่เปลี่ยน)
>
> ทำไมต้อง "Onion"? เพราะ **Oh No, It's Observing Now** 🧅

ระบบเฝ้าระวังและบังคับใช้นโยบายซอฟต์แวร์สำหรับห้องคอมพิวเตอร์ในความดูแล
ตรวจจับการรันโปรแกรมจาก **publisher** (digital signature / file metadata) ที่กำหนดไว้
แล้วสั่ง action ตามกฎ (log, block, รันสคริปต์ตาม ฯลฯ) พร้อมระบบควบคุมจากส่วนกลาง
แบบเดียวกับ Reboot Restore Rx Endpoint Manager

> สถานะ: ผ่านการ implement และทดสอบเบื้องต้นแล้ว (server, client, console, tray, installer)
> ดูรายละเอียดความคืบหน้าล่าสุดที่ `docs/architecture.md`

## แบรนด์ / ชื่อเรียกส่วนต่างๆ (Onion ProcOparetor)

| ส่วนประกอบ | ชื่อแบรนด์ | ชื่อโปรเจกต์ในโค้ด (ปัจจุบัน) |
|---|---|---|
| Server (Windows Service + API + DB) | **Onion Core Service (OCS)** | `MyLabGuard.Server` |
| Admin GUI | **Onion Console** | `MyLabGuard.Console` |
| Client Service + Tray (เครื่องนักเรียน) | **Onion Agent** | `MyLabGuard.Client` + `MyLabGuard.ClientTray` |

> **หมายเหตุ**: ตอนนี้เป็นการเปลี่ยนชื่อแบรนด์ในเอกสารเท่านั้น ชื่อจริงในโค้ด (namespace, ชื่อ
> Windows Service, ชื่อไฟล์ .exe, ชื่อ .csproj) ยังเป็น `MyLabGuard.*` เหมือนเดิม — การเปลี่ยนชื่อ
> เต็มรูปแบบในโค้ดเป็น **TODO ลำดับสูงสุด** ที่ยังไม่ได้ทำ (ดูหัวข้อ TODO ใน `docs/architecture.md`)

## แนวคิดหลัก (เหมือน RRRx)

ติดตั้งจาก installer เดียว เลือกได้ 3 โหมด ผสมกันได้ในเครื่องเดียว:

| โหมด | ใช้ที่ไหน | มีอะไรบ้าง |
|---|---|---|
| **Onion Core Service + Console** | เครื่อง server (เครื่องเดียวกับที่มี RRRx Endpoint Manager อยู่แล้ว) | Windows Service (API+DB) + GUI ที่ผูก localhost อัตโนมัติ |
| **Console only** | เครื่องครู/แอดมิน/โน้ตบุ๊คใดๆ | แค่ GUI, พิมพ์ IP:Port ของ server ทุกครั้งที่เปิด |
| **Onion Agent** | เครื่องนักเรียน x40 | Windows Service (ตรวจจับ+บังคับใช้) + Tray GUI เล็กๆ ที่ต้อง login ก่อนเห็น/แก้อะไรได้ |

## สถาปัตยกรรม

```
[Onion Console] ---- REST API (HTTP, พิมพ์ IP:Port ทุกครั้ง) ----> [Onion Core Service]
                                                                          |
                                                                       SQLite
                                                                    (rules, clients,
                                                                     toggle state, logs)
                                                                          ^
                                                                          | REST API (poll ทุก ~5-10s)
                                                                          |
                                                                    [Onion Agent] x40
                                                                    - WMI __InstanceCreationEvent
                                                                      (ตรวจจับ process ใหม่)
                                                                    - Periodic full-scan ทุก 10s
                                                                      (จับ process ที่ค้างอยู่ก่อน)
                                                                    - เช็ค publisher:
                                                                      1) Authenticode signature (หลัก)
                                                                      2) FileVersionInfo.CompanyName
                                                                         (fallback/hint)
                                                                    - เทียบ rules (publisher +
                                                                      optional process-name-contains)
                                                                      -> รัน task ตามกำหนด
                                                                    - fail-secure ถ้าติดต่อ server ไม่ได้
                                                                          |
                                                                    [Onion Agent Tray]
                                                                    - คนละ process จาก Service
                                                                    - login-gated (password)
                                                                    - ดู status/log เท่านั้น
                                                                    - ปิด tray ได้ แต่ service ไม่ตาย
                                                                    - auto-start ตอน user login
```

รายละเอียดเต็มดูที่ `docs/architecture.md`

## โครงสร้าง repo

```
MyLabGuard/
├── src/
│   ├── MyLabGuard.Server/       Onion Core Service - Windows Service + REST API + SQLite
│   ├── MyLabGuard.Console/      Onion Console - Admin GUI (WPF), connect by IP:Port
│   ├── MyLabGuard.Client/       Onion Agent (service part) - Windows Service บนเครื่องนักเรียน
│   ├── MyLabGuard.ClientTray/   Onion Agent (tray part) - Tray GUI, login-gated
│   └── MyLabGuard.Shared/       Models/DTOs ที่ใช้ร่วมกันทุกโปรเจกต์
├── installer/                   Inno Setup installer (เลือกโหมดติดตั้งได้ 3 แบบ)
├── docs/                        เอกสารประกอบ / บันทึกการตัดสินใจ (architecture.md)
├── .gitignore
└── MyLabGuard.sln
```

## Tech stack

- .NET 10 (LTS)
- Server (Onion Core Service): Worker Service (`Microsoft.Extensions.Hosting`) + Minimal API +
  SQLite + EF Core Migrations
- Client (Onion Agent): Worker Service + `System.Management` (WMI) + WinTrust/Authenticode check +
  periodic process scan
- GUI (Console + Tray): WPF, dark theme
- Auth: PBKDF2 (password hash + salt) ฝั่ง server, built-in Administrator account, forced password
  change on first login, client↔server ผูกด้วย unique GUID ต่อเครื่อง
- Installer: Inno Setup (portable), รองรับ 3-mode ติดตั้งจาก installer เดียว

## เริ่มพัฒนา (บนเครื่อง Windows, VS Code หรือ Visual Studio)

โค้ดหลักทั้ง 4 โปรเจกต์ implement เสร็จแล้ว (ดูสถานะละเอียดที่ `docs/architecture.md`) เริ่มจาก:

```powershell
# รัน Server (Onion Core Service) ก่อน
cd src/MyLabGuard.Server
dotnet run

# เรียก setup ครั้งแรก (สร้าง built-in Administrator, password ว่างเปล่า)
Invoke-RestMethod -Uri "http://localhost:8787/api/auth/setup" -Method Post

# รัน Console (Onion Console) แล้ว login ด้วย Administrator + password ว่าง
cd src/MyLabGuard.Console
dotnet run
```

ดูขั้นตอนติดตั้งแบบเต็มผ่าน installer และ TODO ที่ยังค้างอยู่ที่ `docs/architecture.md`
