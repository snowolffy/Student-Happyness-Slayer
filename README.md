<p align="center">
  <img src="src/OnionProcOparetor.OnlineConsole/wwwroot/img/logo-256.png" alt="Onion ProcOparetor" width="140">
</p>

<h1 align="center">Onion ProcOparetor</h1>

<p align="center"><em>"Peeling processes since 2026."</em></p>

<p align="center">
  <img alt=".NET 10" src="https://img.shields.io/badge/.NET-10-512BD4">
  <img alt="Windows" src="https://img.shields.io/badge/Windows-10%2F11-0078D4">
  <img alt="SQLite" src="https://img.shields.io/badge/EF%20Core-SQLite-003B57">
  <img alt="WiX v5" src="https://img.shields.io/badge/installer-WiX%20v5-orange">
</p>

> ทำไมต้อง "Onion"? เพราะ **Oh No, It's Observing Now** 🧅
>
> (ชื่อ repo บน GitHub ยังคงเป็น `Student-Happyness-Slayer` ตามเดิม ไม่เปลี่ยน)

ระบบเฝ้าระวังและบังคับใช้นโยบายซอฟต์แวร์สำหรับห้องคอมพิวเตอร์ในความดูแล (ใช้งานจริงกับเครื่อง lab
40 เครื่อง) ตรวจจับการรันโปรแกรมจาก **publisher** (Authenticode signature / file metadata) ที่กำหนดไว้
แล้วสั่ง action ตามกฎ พร้อมระบบควบคุมจากส่วนกลาง แนวเดียวกับ Reboot Restore Rx Endpoint Manager

---

## ทำอะไรได้บ้าง

| ความสามารถ | รายละเอียด |
|---|---|
| **Rule enforcement** | จับ process ด้วย Authenticode signature (fallback เป็น `CompanyName`) → kill และ/หรือรัน `ActionCommand` ต่อ · ตรวจผ่าน WMI event + full-scan ทุก 10 วิ · override รายเครื่องได้ |
| **Program Control** | จำแนกโปรแกรมที่ระบบ rule เดิมมองไม่เห็นด้วยคะแนนอัตโนมัติ → block ทันที / เข้าคิวให้ครูตัดสิน · blocklist กระจายเป็น SHA-256 ไปทุกเครื่องพร้อม poll |
| **Remote commands** | Lock / Unlock screen, Shutdown, Restart, Broadcast message, ดูรายการ process ที่รันอยู่ · มี ack + ผลจริงรายเครื่อง |
| **Screen View (VNC)** | ดูและควบคุมหน้าจอเครื่องนักเรียนแบบ full-screen (เมาส์/คีย์บอร์ด) · data plane ต่อ TCP ตรง ไม่ผ่าน Server |
| **Tamper protection** | watchdog เฝ้า Agent ผ่าน Scheduled Task + heartbeat · สร้าง task คืนเองถ้าถูกลบ |
| **Failover** | สลับไป Tailscale อัตโนมัติเมื่อ LAN ล่ม แล้วสลับกลับเองเมื่อ primary ฟื้น |
| **Activity Log** | log แยกฐานข้อมูลสำหรับการกระทำของแอดมิน + เหตุการณ์ฝั่ง agent |

## ส่วนประกอบ

| ส่วนประกอบ | ชื่อแบรนด์ | โปรเจกต์ | หมายเหตุ |
|---|---|---|---|
| Server (Windows Service + REST API + SignalR + DB) | **Onion Core Service (OCS)** | `OnionProcOparetor.Server` | single source of truth ทั้งระบบ · พอร์ต 8787 |
| Admin GUI (เดสก์ท็อป) | **Onion Console** | `OnionProcOparetor.Console` | WPF, ต่อด้วย IP:Port |
| Admin GUI (เว็บ/มือถือ) | **Onion Online Console** | `OnionProcOparetor.OnlineConsole` | Blazor Server, PWA, เข้าผ่าน Tailscale · พอร์ต 8788 |
| Client Service (เครื่องนักเรียน) | **Onion Agent** | `OnionProcOparetor.Agent` | LocalSystem, ไม่มี UI · local API ที่ 127.0.0.1:8788 |
| Client Tray (เครื่องนักเรียน) | **Onion Agent Tray** | `OnionProcOparetor.AgentTray` | รันใน user session จริง (lock screen, VNC, popup) |
| ตัวช่วยถอนการติดตั้ง | — | `OnionProcOparetor.UninstallHelper` | binary เดียว bundle เข้าทั้ง 2 MSI แยกพฤติกรรมด้วย argument |

## โครงสร้าง repo

```text
Student-Happyness-Slayer/
├── src/
│   ├── OnionProcOparetor.Server/          Onion Core Service - Minimal API + SignalR + EF Core/SQLite
│   ├── OnionProcOparetor.Console/         Onion Console - Admin GUI (WPF)
│   ├── OnionProcOparetor.OnlineConsole/   Onion Online Console - Blazor Server (มือถือ/PWA)
│   ├── OnionProcOparetor.Agent/           Onion Agent - Windows Service บนเครื่องนักเรียน
│   ├── OnionProcOparetor.AgentTray/       Onion Agent Tray - GUI ใน user session
│   ├── OnionProcOparetor.UninstallHelper/ ตัวช่วย uninstall (password/login gate)
│   └── Directory.Build.props              Version/AssemblyVersion/FileVersion ร่วมทุกโปรเจกต์
├── installer/                             WiX v5 - 2 MSI แยกขาดกัน + publish-all.ps1
├── deploy/onlineconsole/                  สคริปต์/คู่มือ deploy Online Console เป็น Windows Service
├── docs/                                  สถาปัตยกรรม + บันทึกการตัดสินใจ
└── archive/                               โปรเจกต์ MyLabGuard เดิมก่อน rename (อ้างอิงเท่านั้น)
```

## เริ่มพัฒนา

> [!WARNING]
> **เครื่อง dev เครื่องนี้อาจรัน Server ตัวจริงอยู่ด้วย** — `dotnet run` เฉยๆ จะไปใช้ DB เดียวกันที่
> `C:\ProgramData\OnionProcOparetor\` และชนพอร์ต 8787 ของจริง แยก DB/พอร์ตก่อนทดสอบเสมอ

```powershell
# แยก DB + พอร์ตออกจากของจริงก่อน แล้วค่อยรัน Server
$env:Server__Port = "18787"
$env:ConnectionStrings__DefaultConnection    = "Data Source=$env:TEMP\dev.db"
$env:ConnectionStrings__ActivityLogConnection = "Data Source=$env:TEMP\dev-activity.db"
dotnet run --project src\OnionProcOparetor.Server

# สร้าง built-in Administrator (password ว่าง) - เรียกได้จากเครื่อง server เองเท่านั้น (loopback)
Invoke-RestMethod -Uri "http://localhost:18787/api/auth/setup" -Method Post
```

> [!IMPORTANT]
> หลัง setup บัญชีจะติดสถานะ `HasDefaultPassword` — **API เกือบทุกตัวจะตอบ 403 จนกว่าจะเปลี่ยนรหัสผ่าน**
> ไม่ใช่บั๊ก เป็นการบังคับใช้ที่ตั้งใจ (ดู `GetAuthorizedAdmin` ใน `Server/Program.cs`)

```powershell
# Console เดสก์ท็อป
dotnet run --project src\OnionProcOparetor.Console

# หรือ Online Console (ต้องชี้ Server:BaseUrl ให้ถูกถ้าไม่ได้รันเครื่องเดียวกับ Server)
dotnet run --project src\OnionProcOparetor.OnlineConsole
```

## Build installer

```powershell
# ต้องมี WiX v5 ก่อน (v6/v7 ใช้ไม่ได้ - ติด EULA ค่าบำรุงรักษา)
dotnet tool install --global wix --version 5.0.2

# publish 5 โปรเจกต์ที่เข้า MSI + compile MSI ทั้ง 2 ตัว ในคำสั่งเดียว
.\installer\publish-all.ps1
```

ได้ผลลัพธ์ที่ `installer\output\`:

| MSI | ลงที่ไหน |
|---|---|
| `OnionProcOparetor-Server-Console-Setup.msi` | เครื่องครู/แอดมิน (เลือกได้ว่าจะลง Server, Console หรือทั้งคู่) |
| `OnionProcOparetor-Agent-Setup.msi` | เครื่องนักเรียนทั้ง 40 เครื่อง |

**Online Console ไม่ได้อยู่ใน MSI ตัวไหน** — deploy แยกด้วย `dotnet publish` แล้วติดตั้งเป็น Windows
Service ตาม [`deploy/onlineconsole/README.md`](deploy/onlineconsole/README.md)

สคริปต์ bump `FileVersion` ให้อัตโนมัติทุกรอบ — จำเป็นจริงๆ ไม่ใช่ของประดับ เพราะ Windows Installer
จะ **ข้ามการเขียนทับ** ไฟล์ที่ `FileVersion` เท่ากับของเดิมเป๊ะ (เคยทำให้ upgrade "สำเร็จ" แต่ไม่ได้
โค้ดใหม่จริงมาแล้ว)

**การอัปเกรด**: ดับเบิลคลิก MSI ตัวใหม่ทับได้เลย ไม่ต้องถอนก่อน (`AllowSameVersionUpgrades`) และ
ฐานข้อมูลใน `%ProgramData%` **ไม่ถูกลบ** — มันไม่ใช่ component ของ MSI และ custom action ที่ลบข้อมูล
ต้องมี `DELETEDATA=1` เท่านั้นถึงจะทำงาน (ไม่ใช่ค่า default และตอน upgrade ถูกกันไว้อีกชั้นด้วย)
แต่ `appsettings.json` **จะถูกเขียนทับ** ด้วยของใน repo — สำรองไว้ก่อนถ้าเคยแก้บนเครื่องจริง

## เอกสาร

| ไฟล์ | เนื้อหา |
|---|---|
| [`docs/architecture.md`](docs/architecture.md) | สถาปัตยกรรม + **Current Implementation Status** — อ่านไฟล์นี้ก่อนไฟล์อื่น |
| [`HARDENING-REPORT-2026-08-31.md`](HARDENING-REPORT-2026-08-31.md) | audit + hardening pass 17 รายการ (อ่านหัวข้อ 0 เรื่อง disk freeze ก่อนแตะระบบพวกนี้) |
| [`docs/architecture-log.md`](docs/architecture-log.md) | บันทึกรายวันของแต่ละรอบพัฒนา (เหตุผลเบื้องหลังการตัดสินใจ) |
| [`docs/onlineconsole-ui-notes.md`](docs/onlineconsole-ui-notes.md) | บันทึกการออกแบบ UI ของ Online Console |
| [`deploy/onlineconsole/README.md`](deploy/onlineconsole/README.md) | วิธี deploy Online Console + เครื่องไหนรันอะไรอยู่จริง |

## ข้อจำกัดที่ต้องรู้

- **เครื่อง lab ทั้ง 40 เครื่องรัน Reboot Restore Rx** — ทุกอย่างที่เขียนลงดิสก์ตอน runtime หายหมด
  ทุกครั้งที่รีบูต มีแต่ state ตอน install เท่านั้นที่รอด ข้อจำกัดนี้คุมการตัดสินใจฝั่ง client แทบทุกข้อ
- **Transport เป็น HTTP ธรรมดา** (LAN โรงเรียน) ไม่ใช่ HTTPS — ดูเหตุผลใน `docs/architecture.md`
- **hardening pass (2026-08-31) ยังไม่ผ่าน field test ครบ** และ **Online Console ยังไม่เคยใช้งานจริง
  ในห้อง** — ดูสถานะล่าสุดที่ `docs/architecture.md`
