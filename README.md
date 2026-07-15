# Student-Happyness-Slayer

ระบบเฝ้าระวังและบังคับใช้นโยบายซอฟต์แวร์สำหรับห้องคอมพิวเตอร์ในความดูแล
ตรวจจับการรันโปรแกรมจาก **publisher** (digital signature / file metadata) ที่กำหนดไว้
แล้วสั่ง action ตามกฎ (log, block, รันสคริปต์ตาม ฯลฯ) พร้อมระบบควบคุมจากส่วนกลาง
แบบเดียวกับ Reboot Restore Rx Endpoint Manager

> สถานะ: อยู่ในขั้นวางแผน/scaffold ยังไม่มีโค้ด implementation จริง

## แนวคิดหลัก (เหมือน RRRx)

ติดตั้งจาก installer เดียว เลือกได้ 3 โหมด ผสมกันได้ในเครื่องเดียว:

| โหมด | ใช้ที่ไหน | มีอะไรบ้าง |
|---|---|---|
| **Server + Console** | เครื่อง server (เครื่องเดียวกับที่มี RRRx Endpoint Manager อยู่แล้ว) | Windows Service (API+DB) + GUI ที่ผูก localhost อัตโนมัติ |
| **Console only** | เครื่องครู/แอดมิน/โน้ตบุ๊คใดๆ | แค่ GUI, พิมพ์ IP:Port ของ server ทุกครั้งที่เปิด |
| **Client (Agent)** | เครื่องนักเรียน x40 | Windows Service (ตรวจจับ+บังคับใช้) + Tray GUI เล็กๆ ที่ต้อง login ก่อนเห็น/แก้อะไรได้ |

## สถาปัตยกรรม
[Console GUI] ---- REST API (HTTP, พิมพ์ IP:Port ทุกครั้ง) ----> [Server]
|
SQLite
(rules, clients,
toggle state, logs)
^
| REST API (poll ทุก ~30s)
|
[Client Service] x40
- WMI __InstanceCreationEvent
(ตรวจจับ process ใหม่)
- เช็ค publisher:
1) Authenticode signature (หลัก)
2) FileVersionInfo.CompanyName (fallback/hint)
- เทียบ rules -> รัน task ตามกำหนด
- fail-secure ถ้าติดต่อ server ไม่ได้
|
[Client Tray GUI]
- คนละ process จาก Service
- login-gated (password)
- ดู status/log เท่านั้น
- ปิด tray ได้ แต่ service ไม่ตาย

รายละเอียดเต็มดูที่ `docs/architecture.md`

## โครงสร้าง repo
MyLabGuard/
├── src/
│   ├── MyLabGuard.Server/       Windows Service + REST API + SQLite
│   ├── MyLabGuard.Console/      Admin GUI (WPF), connect by IP:Port
│   ├── MyLabGuard.Client/       Windows Service บนเครื่องนักเรียน (detection core)
│   ├── MyLabGuard.ClientTray/   Tray GUI, login-gated, คนละ process จาก Client service
│   └── MyLabGuard.Shared/       Models/DTOs ที่ใช้ร่วมกันทุกโปรเจกต์
├── installer/                   Installer script/wizard (เลือกโหมดติดตั้ง)
├── docs/                        เอกสารประกอบ / บันทึกการตัดสินใจ
├── .gitignore
└── MyLabGuard.sln               (จะสร้างตอนเปิด Visual Studio)

## Tech stack (แผนเบื้องต้น)

- .NET 8
- Server: Worker Service (`Microsoft.Extensions.Hosting`) + Minimal API + SQLite
- Client: Worker Service + `System.Management` (WMI) + WinTrust/Authenticode check
- GUI (Console + Tray): WPF
- Auth: password hash (PBKDF2/BCrypt + salt) ฝั่ง server, client↔server ผูกด้วย unique GUID ต่อเครื่อง

## เริ่มพัฒนา (บนเครื่อง Windows + Visual Studio)

ยังไม่ได้ scaffold โค้ดจริง — ขั้นตอนถัดไปคือสร้าง solution + projects ใน `src/`
ตามรายชื่อด้านบน แล้วเริ่มจากฝั่ง Server (API + DB schema) ก่อน
