# Student-Happyness-Slayer

> **โปรแกรม: Onion ProcOparetor** — *"Peeling processes since 2026."*
> (ชื่อ repo บน GitHub ยังคงเป็น `Student-Happyness-Slayer` ตามเดิม ไม่เปลี่ยน)
>
> ทำไมต้อง "Onion"? เพราะ **Oh No, It's Observing Now** 🧅

ระบบเฝ้าระวังและบังคับใช้นโยบายซอฟต์แวร์สำหรับห้องคอมพิวเตอร์ในความดูแล
ตรวจจับการรันโปรแกรมจาก **publisher** (digital signature / file metadata) ที่กำหนดไว้
แล้วสั่ง action ตามกฎ (log, block, รันสคริปต์ตาม ฯลฯ) พร้อมระบบควบคุมจากส่วนกลาง
แบบเดียวกับ Reboot Restore Rx Endpoint Manager

> สถานะ: rename ไปยัง Onion ProcOparetor แล้วและโครงสร้างโปรเจกต์ถูกอัปเดตตามแผน
> ดูรายละเอียดความคืบหน้าล่าสุดที่ `docs/architecture.md`

## แบรนด์ / ชื่อเรียกส่วนต่างๆ (Onion ProcOparetor)

| ส่วนประกอบ | ชื่อแบรนด์ | ชื่อโปรเจกต์ในโค้ด |
|---|---|---|
| Server (Windows Service + API + DB) | **Onion Core Service (OCS)** | `OnionProcOparetor.Server` |
| Admin GUI | **Onion Console** | `OnionProcOparetor.Console` |
| Client Service + Tray (เครื่องนักเรียน) | **Onion Agent** | `OnionProcOparetor.Agent` + `OnionProcOparetor.AgentTray` |

## โครงสร้าง repo

```
OnionProcOparetor/
├── src/
│   ├── OnionProcOparetor.Server/      Onion Core Service - Windows Service + REST API + SQLite
│   ├── OnionProcOparetor.Console/     Onion Console - Admin GUI (WPF), connect by IP:Port
│   ├── OnionProcOparetor.Agent/       Onion Agent (service part) - Windows Service บนเครื่องนักเรียน
│   ├── OnionProcOparetor.AgentTray/   Onion Agent (tray part) - Tray GUI, login-gated
│   └── OnionProcOparetor.Shared/      Models/DTOs ที่ใช้ร่วมกันทุกโปรเจกต์
├── installer/                        Inno Setup installer (เลือกโหมดติดตั้งได้ 3 แบบ)
├── docs/                             เอกสารประกอบ / บันทึกการตัดสินใจ (architecture.md)
├── .gitignore
└── OnionProcOparetor.sln
```

## เริ่มพัฒนา (บนเครื่อง Windows, VS Code หรือ Visual Studio)

```powershell
# รัน Server (Onion Core Service) ก่อน
cd src/OnionProcOparetor.Server
dotnet run

# เรียก setup ครั้งแรก (สร้าง built-in Administrator, password ว่างเปล่า)
Invoke-RestMethod -Uri "http://localhost:8787/api/auth/setup" -Method Post

# รัน Console (Onion Console) แล้ว login ด้วย Administrator + password ว่าง
cd src/OnionProcOparetor.Console
dotnet run
```
