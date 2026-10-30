# Roadmap: ไปสู่ระบบคุมห้องคอมแบบเต็ม (เมมกลาง)

> **ไฟล์นี้คือ "เมมกลาง"** ของงานรอบนี้ เก็บการตัดสินใจทั้งหมดที่คุยกับผู้ใช้แล้ว (29–30 ต.ค. 2569)
> และแบ่งงานขั้น 1 เป็น work package (WP) ให้ orchestrator แจกให้ worker ทำขนานกันได้
> กฎการทำงานร่วมกันอยู่ใน [`CLAUDE.md`](../CLAUDE.md)
>
> **Wireframe (แหล่งอ้างอิงหน้าตา):** https://claude.ai/artifact/7KbHzR3PrVq8r5upXrrJoW
> (เป็นของส่วนตัวผู้ใช้ worker อาจเปิดไม่ได้ ไฟล์นี้จึงอธิบายทุกอย่างที่ต้องใช้ไว้เป็นข้อความครบแล้ว)

## สถานะ

| ขั้น | สถานะ |
|---|---|
| 0 Pilot | MSI build แล้วเมื่อ 29 ต.ค. (`installer/output/`) · ผู้ใช้จะไปลง 2–3 เครื่อง |
| 1 ฐานราก | **กำลังทำ** · WP0 ✅ merge แล้ว (`ee94167`) · WP5, WP6 กำลังทำ · WP2, WP3 (เฉพาะ Server+OnlineConsole) รอบสอง · WP1 รอ WP5+WP6 merge · WP4 รอ WP3 merge · WP3 ส่วน Console รอ WP6 |
| 2–5 | ออกแบบเสร็จ (wireframe) · ยังไม่เริ่ม |

งานที่ผู้ใช้รับไปทำเองที่แล็บ: เปิด Wake-on-LAN ใน BIOS · เดินสาย LAN แทน WiFi · เลือกเครื่อง pilot · ตรวจว่ามี Google Workspace สำหรับหน้า Apps Script

---

## การตัดสินใจทั้งหมด (ยืนยันกับผู้ใช้แล้ว)

### Sign-in และบัญชี
- **บัญชีนักเรียน:** ล็อกอินด้วยเมลโรงเรียนหรือรหัสนักเรียน + รหัสผ่าน โดย Admin เป็นคนจัดการบัญชี **เป้าหมายคือ log รายบุคคล**
  - ไม่สร้างบัญชี Windows ให้นักเรียน (ชนกับ disk freeze) ล็อกอินที่ระดับแอปบนจอล็อกของ AgentTray แทน
- **Roster Manager** เป็นโปรแกรมแยก (WPF) แต่ใช้ Server และ DB ตัวเดียวกัน
  - **ข้อมูลนำเข้า:** CSV จับคู่คอลัมน์ได้ · รหัสนักเรียนซ้ำกับของเดิม = อัปเดตชื่อและห้องโดยไม่แตะรหัสผ่าน
  - **รหัสเริ่มต้น:** สุ่ม 6 หลักต่อคน + บังคับเปลี่ยนตอนล็อกอินครั้งแรก · พิมพ์ใบแจกได้ A4 ละ 10 ใบ · เก็บแบบ hash ย้อนดูไม่ได้
  - **เลื่อนชั้นปี:** ทำปีละครั้ง นักเรียนที่จบปิดบัญชีได้ และสำรอง DB ให้อัตโนมัติก่อนเลื่อน
- **การบังคับล็อกอินเปิด/ปิดได้ที่ตั้งค่า Server** (ทั้งระบบหรือเลือกห้อง)
  - เปิดอยู่: ไม่ล็อกอิน = ใช้เครื่องไม่ได้
  - ปิด: เครื่องที่ค้างอยู่หน้าล็อกอินจะปลดเองภายใน 1 รอบ poll
  - สลับไปเปิด: ไม่เตะคนที่ใช้เครื่องอยู่ออกกลางคัน
- **ความปลอดภัยบัญชี:** ล็อกบัญชีเมื่อกรอกผิด 5 ครั้ง (ครูปลดได้) · รหัสขั้นต่ำ 6 ตัว · Teacher รีเซ็ตรหัสนักเรียนได้ (ปิดสิทธิ์ได้) · บัญชีเดียวล็อกอินเครื่องใหม่ = เตะเครื่องเก่าออก (ค่าเริ่มต้น) + บันทึก log
- **logout อัตโนมัติ:** เมื่อไม่ได้ใช้งาน N นาที (ค่าเริ่มต้น 15) และเมื่อปิด/รีสตาร์ทเครื่อง มีปุ่ม "ไม่ใช่ฉัน" ในเมนู tray
- **ไม่มีตาราง/คาบเรียน** เพราะวิชาอื่นขอยืมห้องและครูพานักเรียนเข้าคาบว่างบ่อย ห้องอยู่โหมดใช้งานทั่วไปเสมอ
  - รายงาน = "ประวัติการใช้เครื่อง" กรองตามช่วงเวลา ห้องคอม และห้องเรียนของนักเรียน ใช้ดูประกอบเท่านั้น ไม่ใช่ใบเช็คชื่อทางการ
- **Role มี 2 ระดับ**
  - **Admin:** ทำได้ทุกอย่าง
  - **Teacher:** คุมห้อง ดูจอ เปิดโหมดสอบ รีเซ็ตรหัสนักเรียน (ถ้าเปิดสิทธิ์) และเปิด Staff session ได้ แต่แก้กฎถาวร ผู้ใช้ ห้อง และตั้งค่าระบบไม่ได้
- **ชื่อที่แสดง (display name)** สำหรับบัญชี Admin/Teacher
  - ใช้แสดงทุกที่ที่คนเห็น เช่น "Staff session (ครูสมชาย)", "ล็อกโดย ครูสมชาย", ผู้ส่งข้อความ, แถบล่าง Console
  - เจ้าของบัญชีแก้เองได้ Admin แก้ให้ได้ · username ใช้ล็อกอินและแก้ไม่ได้
  - **Audit Log เก็บทั้ง username และชื่อที่แสดง ณ ตอนนั้น**
- **Staff session:** ครูล็อกอินที่เครื่องนักเรียนด้วยบัญชี Console
  - ปิดกฎทั้งเครื่องชั่วคราว: ค่าเริ่มต้น 30 นาที สูงสุด 120 นาที ต่อเวลาได้ จบเองเมื่อหมดเวลาหรือรีบูต
  - บันทึกเหมือนตอน admin ล็อกอิน Console (บัญชี + เครื่อง) และไม่นับเป็นการใช้เครื่องของนักเรียน
  - **ใช้กลไก token จาก Server แบบเดียวกับที่ใช้แทน `bypass/set`** (ดู WP2)
- **รหัสฉุกเฉิน (Server ล่ม):** ใช้ TOTP มาตรฐาน 6 หลัก เปลี่ยนทุก 30 วินาที secret ฝังมาตอนติดตั้ง Agent
  - ครูดูรหัสได้ 2 ทาง: แอป Authenticator หรือหน้าเว็บ Google Apps Script (จำกัดให้เปิดได้เฉพาะบัญชีครู และบันทึกว่าใครเปิดดู) · secret ของ Apps Script เก็บใน Script Properties
  - ปลดเครื่องได้แบบไม่ระบุชื่อ แล้วบันทึกย้อนหลังเมื่อ Server กลับมา
  - **ห้ามใช้ OnlineConsole แสดงรหัส** เพราะรันบนเครื่อง Server จึงล่มไปพร้อมกัน

### เครือข่ายและความปลอดภัย
- **HTTPS ด้วย self-signed certificate:** MSI ติดตั้งใบรับรองให้เครื่องนักเรียนเชื่อถือ **ต้องเสร็จก่อนเริ่มเก็บรหัสผ่านนักเรียน** · ช่วงย้ายระบบมีตัวเลือก "ยอมรับ HTTP เดิม"
- **Agent ตอนบูต: แข่งกันระหว่าง LAN กับ Tailscale โดยให้ LAN เป็นหลัก**
  - ยิง LAN ก่อน ถ้าไม่ตอบใน 2 วินาที ยิง Tailscale ตามไปด้วย ทางไหนตอบก่อนใช้ทางนั้น
  - ถ้าได้ Tailscale ให้ probe LAN อยู่เบื้องหลัง พอ LAN ติดค่อยสลับกลับ
  - หลังบูตแล้วใช้กติกาเดิม (เสีย 5 ครั้งสลับไป / ดี 7 ครั้งสลับกลับ ใน `Worker.cs`)

### จอนักเรียน (AgentTray)
- **แนวทางการ** · ข้อความบนจอล็อกแก้ได้ (ค่าเริ่มต้นในตั้งค่า + ครูพิมพ์ทับเฉพาะครั้งตอนกดล็อก)
- **หน้าล็อกอิน:** การ์ดแบ่งสองฝั่ง ซ้ายเป็นภาพจาก `Assets\LoginHero.png` (ฝังมากับ MSI ตอนนี้ใช้ placeholder) ขวาเป็นฟอร์ม
  - การ์ดลอยมีเงา พื้นหลังเป็นสีพื้นเดิมโรย **จุดเรียงแบบตาราง**
  - **มีปุ่มปิดเครื่อง/รีสตาร์ทที่มุมขวาล่าง ใครก็กดได้** · จอที่ครูสั่งล็อกห้ามมีปุ่มนี้
- **ระหว่างใช้งานห้ามมีแถบลอยด้านบน** เพราะทับแท็บเบราว์เซอร์
  - โหมดปกติ: เด้งแจ้งชื่อ 5 วินาที + เมนูที่ไอคอน tray
  - โหมดสอบ / Staff session: ป้ายเล็กมุมขวาล่าง ซ่อนไม่ได้
- **สถานะผิดพลาดของจอล็อกอิน:** รหัสผิด (ไม่บอกว่าผิดช่องไหน) · บัญชีถูกล็อก · บัญชีใช้อยู่ที่เครื่องอื่น · บังคับตั้งรหัสใหม่

### Console (WPF): โครงใหม่
- **ทุกอย่างเป็นหน้าใน sidebar แบบจัดกลุ่ม** ไม่ใช้หน้าต่างลอยแล้ว
  - ห้องเรียน: Clients, ผังที่นั่ง, Live View, โหมดสอบ
  - กฎ: กฎโปรแกรม, Program Review, Website Control
  - บันทึก: Logs/Audit, ประวัติการใช้เครื่อง
  - ระบบ: ผู้ใช้, ห้อง, ตั้งค่า
- **Toast มุมขวาล่าง** แทนข้อความที่แทรกในหน้า
  - สำเร็จ/ข้อมูลหายเองใน 4 วินาที · error ค้างจนกดปิด
  - ซ้อนกันสูงสุด 3 อัน · ถ้าแผงข้างเปิดอยู่ ให้ขยับหลบ
  - ใช้กับ: ล็อกอินสำเร็จ, ผลคำสั่ง (มีลิงก์ "ดูผล"), error, ข้อมูลทั่วไป
- **แผงข้อมูลเครื่อง (inspector) สไลด์จากขวา ทับอยู่บน grid** ไม่ดันคอลัมน์
  - หุบเหลือแถบแคบได้ · Esc หรือ ✕ ปิด
  - เลือกหลายเครื่อง = แผงเปลี่ยนเป็นคำสั่งแบบกลุ่ม
- **ประกาศสถานะของทั้งระบบอยู่ในแถบหัวของหน้า** เช่น ปิดการบังคับใช้ทั้งระบบ, โหมดสอบกำลังทำงาน, Server กำลังเชื่อมต่อใหม่
  - แถบหัวสูงเท่าเดิมเสมอ เปลี่ยนแค่สี และมีปุ่มแก้ในตัว · ขึ้นทุกหน้า
  - หลายเรื่องพร้อมกัน = แสดงเรื่องที่ร้ายแรงที่สุด + ป้าย "+N"
- **หน้าล็อกอิน Console:** เปิดเป็นหน้าต่างขนาด Console ปกติ ไม่ใช่หน้าต่างเล็ก
  - ตรงกลางเป็นการ์ดแบ่งสองฝั่ง: ภาพ `Assets\LoginHero.png` + ฟอร์ม (Server IP, Port, Username, Password)
  - พื้นหลังจุดเรียงแบบตาราง
- แสดงเวลาไทยแทน UTC (ตั้งกลับเป็น UTC ได้)
- ธีมมืดใช้ theme token เดิม (`Console/Theme/Colors.*.xaml`) ไม่ต้องออกแบบแยก

### ฟีเจอร์ขั้น 2–5 (ออกแบบแล้ว ยังไม่เริ่ม)
- **ขั้น 2 Website**
  - ป้องกันการเลี่ยง: ห้ามติดตั้ง extension, ปิด Guest/InPrivate, ปิด DevTools, ล็อก DoH/proxy, บล็อกเบราว์เซอร์อื่น
  - **โหมดสอบ:** allow-list ชั่วคราวที่ต้องมีเวลาหมดเสมอ มีชุดสำเร็จรูป เช่น Google Forms ที่รวมโดเมนประกอบไว้ครบแล้ว เปิดจากมือถือได้
  - กฎรายห้อง · ประวัติเว็บ (Agent อ่าน History ของ Chrome/Edge ส่งขึ้น Server ทุกนาที)
- **ขั้น 3 Sign-in:** ตามหัวข้อ Sign-in ด้านบน
- **ขั้น 4 เครื่องมือสอน**
  - เปิด URL / โปรแกรมทั้งห้อง: โปรแกรมเลือกได้เฉพาะรายการที่ Admin ตั้ง
  - ยกมือ: กดซ้ำได้หลัง 1 นาที · มีคิวใน Console
  - แจก/เก็บไฟล์: เก็บแยกโฟลเดอร์ตามชื่อนักเรียนจากการล็อกอิน
  - ส่งจอครู: ลดเฟรมเหลือ 5–10 fps แล้วส่งผ่าน Server · มีแถบแดงเตือนบนจอครูตลอดที่ส่ง
- **ขั้น 5 รายงาน/ดูแลเครื่อง:** รายงานรายคน · ตั้งเวลาเปิด-ปิดเครื่อง + Wake-on-LAN · รายการฮาร์ดแวร์/ซอฟต์แวร์

---

## ขั้น 1: ฐานราก · work packages

### ลำดับที่แนะนำ

```
WP0 (แยก endpoint ออกจาก Program.cs) ─┬─> WP1 HTTPS
                                      ├─> WP2 Token แทน bypass
                                      ├─> WP3 ห้อง ──────┐  (migration ลำดับที่ 1)
                                      └─> WP4 Role/ชื่อที่แสดง ┘  (migration ลำดับที่ 2 · rebase หลัง WP3)
WP5 Agent boot race      ── อิสระ เริ่มได้ทันที (ฝั่ง Agent อย่างเดียว)
WP6 Console shell ใหม่    ── อิสระ เริ่มได้ทันที (ฝั่ง Console อย่างเดียว)
WP7 หน้าล็อกอิน Console   ── หลัง WP6 (ใช้ shell/toast ใหม่)
```

orchestrator ส่ง WP0, WP5 และ WP6 ออกพร้อมกันได้ทันที พอ WP0 merge แล้วค่อยส่ง WP1–WP4

### WP0: แยก endpoint ออกจาก `Server/Program.cs` (refactor ล้วน ไม่เปลี่ยนพฤติกรรม)
- **ไฟล์:** `src/OnionProcOparetor.Server/Program.cs` และ `src/OnionProcOparetor.Server/Endpoints/*.cs` (ใหม่)
- **งาน:** ย้าย `app.MapXxx(...)` แยกตามกลุ่ม (Auth, Users, Clients, Commands, Rules, WebsiteRules, Catalog, ActivityLog, Poll, ProgramControl ฯลฯ) ไปเป็นไฟล์ `XxxEndpoints.cs` แบบ `public static WebApplication MapXxxEndpoints(this WebApplication app)`
  - ตัวช่วยร่วมอย่าง `GetAuthorizedAdmin` ให้ย้ายไป `Endpoints/AuthHelpers.cs`
- **เกณฑ์เสร็จ:** build ผ่าน · route ทุกตัวยังเหมือนเดิม (เทียบรายการ route ก่อนและหลัง) · รัน Server ทดสอบบน port แยกแล้วเรียก login, poll และ CRUD ตัวอย่างผ่าน

### WP1: HTTPS (self-signed)
- **ไฟล์:**
  - Server: Kestrel config, `Services/CertificateManager.cs` (ใหม่), `Endpoints/SecurityEndpoints.cs` (ใหม่)
  - HttpClient setup ใน Agent, AgentTray, Console, OnlineConsole, RemoteBridge และ UninstallHelper
  - `installer/*.wxs`
- **งาน:**
  - Server สร้าง cert ครั้งแรกแล้วเก็บใน `%ProgramData%\OnionProcOparetor\` ฟัง HTTPS · มีตัวเลือก "ยอมรับ HTTP เดิม" ช่วงย้ายระบบ
  - ส่งออก `.cer` ได้ · MSI ของ Agent ติดตั้ง cert ลง `LocalMachine\Root` ตอนติดตั้ง (อยู่รอด disk freeze)
  - Client ตรวจ cert แบบ pin thumbprint หรือเชื่อถือผ่าน store
- **เกณฑ์เสร็จ:** Console และ Agent ต่อ HTTPS ได้บน Server ทดสอบ · ถ้า cert ผิด ต้องเชื่อมต่อไม่ได้ (ไม่ fallback เงียบๆ) · โหมด HTTP เดิมยังใช้ได้เมื่อเปิดตัวเลือกไว้

### WP2: Token แทน `bypass/set` ที่ไม่มี auth (ฐานของ Staff session)
- **ไฟล์:**
  - Agent: `Program.cs` (loopback endpoints), `Services/BypassCoordinator.cs`
  - AgentTray: `Services/ClientApiClient.cs`, `StatusWindow.xaml.cs`
  - Server: `Endpoints/StaffSessionEndpoints.cs` (ใหม่)
- **งาน:**
  - AgentTray ล็อกอินกับ Server แล้วขอ token แบบมีอายุ ผูกกับ clientGuid + บัญชี
  - Agent ตรวจ token กับ Server ก่อนยอม bypass · ถ้า Server ล่ม ให้ปฏิเสธ (ขั้น 3 จะใช้รหัสฉุกเฉินแทน)
  - ปิดช่อง `/vnc/session-result` และ `/processes/list-result` ที่ไม่มี auth ด้วยวิธีเดียวกัน หรือใช้ shared secret ที่ AgentTray ได้จาก Agent ผ่าน named pipe
  - Audit Log: บันทึก `StaffSessionStarted` (username + ชื่อที่แสดง + เครื่อง)
- **เกณฑ์เสร็จ:** `curl -X POST 127.0.0.1:8788/bypass/set` แบบไม่มี token ต้องถูกปฏิเสธ · ปุ่ม BYPASS ใน tray ยังใช้ได้เมื่อล็อกอินถูกต้อง · bypass หมดอายุเองตามเวลา

### WP3: ห้อง + ผังที่นั่ง (migration ลำดับที่ 1)
- **ไฟล์:**
  - Server: `Models/Room.cs` (ใหม่), `ClientMachine.cs` (+`RoomId`, `SeatRow`, `SeatCol` เป็น nullable), `AppDbContext.cs`, migration, `Endpoints/RoomEndpoints.cs`
  - Console: ApiClient + ตัวเลือกห้องบนหัว Clients (ยังไม่ต้องทำหน้าผังที่นั่งเต็ม)
  - OnlineConsole: ตัวเลือกห้องบนหัว Clients
- **งาน:**
  - CRUD ห้อง · ย้ายเครื่องเข้าห้อง · `GET /api/clients?roomId=`
  - เครื่องที่ยังไม่มีห้อง = "ยังไม่จัดห้อง"
  - migration แบบ additive (ทุกคอลัมน์ nullable)
- **เกณฑ์เสร็จ:** migration รันบน DB เก่าที่มีข้อมูลแล้วไม่พัง · กรองตามห้องได้ทั้ง Console และ OnlineConsole

### WP4: Role + ชื่อที่แสดง (migration ลำดับที่ 2 · rebase หลัง WP3)
- **ไฟล์:**
  - Server: `Models/AdminUser.cs` (+`Role`, +`DisplayName`), migration, `Endpoints/AuthHelpers.cs` (ตรวจ role), endpoints ที่ต้องจำกัดสิทธิ์, `ActivityLogService` (บันทึก username + ชื่อที่แสดง)
  - Console: หน้าผู้ใช้ + แถบล่าง
  - OnlineConsole: หน้า Users
- **งาน:**
  - บัญชีเดิมทั้งหมดเป็น Admin เพื่อไม่ให้ใครโดนล็อกออกจากระบบ · `DisplayName` เริ่มต้น = Username
  - endpoint ที่ Teacher ห้ามใช้ ให้ตอบ 403 · แก้ชื่อที่แสดงของตัวเองได้ Admin แก้ให้คนอื่นได้
- **เกณฑ์เสร็จ:** บัญชี Teacher เรียก endpoint ที่ห้ามแล้วได้ 403 · Audit Log มีทั้งสองชื่อ · บัญชีเดิมยังล็อกอินได้

### WP5: Agent ตอนบูตแข่ง LAN กับ Tailscale
- **ไฟล์:** `src/OnionProcOparetor.Agent/Worker.cs`, `Services/ServerClient.cs`, `Services/LabHubConnection.cs` (ถ้าจำเป็น)
- **งาน:** ทำตามหัวข้อเครือข่ายด้านบน (LAN ได้เปรียบ 2 วิ, ทางไหนตอบก่อนใช้ทางนั้น, probe LAN เบื้องหลังเพื่อสลับกลับ) · หลังบูตใช้กติกา 5/7 เดิมโดยไม่แก้
  - ถ้าไม่ได้ตั้ง `FallbackBaseUrl` ให้ทำงานแบบเดิมทุกอย่าง
- **เกณฑ์เสร็จ:** ทดสอบด้วย Server 2 ตัวบน port ต่างกันจำลอง LAN กับ Tailscale ให้ครบ 3 กรณี: LAN ไม่ตอบตอนเริ่ม → ได้ Tailscale แล้วสลับกลับเมื่อ LAN ตอบ · ทั้งคู่ตอบ → เลือก LAN · ไม่มี fallback → พฤติกรรมเดิม
- **หมายเหตุ:** มีผลในแล็บเมื่อลง Agent MSI ใหม่เท่านั้น

### WP6: Console shell ใหม่
- **ไฟล์:** `src/OnionProcOparetor.Console/` ทั้งหมด ยกเว้นหน้าล็อกอิน (WP7)
- **งาน:**
  1. `ToastService` + ตัวแสดง toast มุมขวาล่าง แล้วแทนข้อความเดิมที่แทรกในหน้า
  2. แถบหัวประกาศสถานะ (global enforcement ปิด / Server reconnecting ฯลฯ) ต้องสูงเท่าเดิมทุกกรณี
  3. inspector ของ Clients เปลี่ยนเป็นแผงสไลด์ทับ grid หุบได้ · Esc ปิด
  4. sidebar แบบกลุ่ม และแปลงหน้าต่างลอย (Program Review, Website Control, Settings) ให้เป็นหน้า
  5. แสดงเวลาไทย
  - ใช้ theme token เดิม ต้องดูถูกต้องทั้งธีมสว่างและธีมมืด
- **เกณฑ์เสร็จ:** จับภาพทุกหน้าทั้งสองธีมด้วย RenderTargetBitmap · grid ไม่ขยับตอนเปิดแผงหรือตอนประกาศโผล่ · ฟีเจอร์เดิมไม่หาย
- **อ้างอิงเดิม:** `docs/console-ui-notes.md`

### WP7: หน้าล็อกอิน Console แบบใหม่
- **ไฟล์:** `Console/MainWindow.xaml(.cs)`, `Console/Assets/LoginHero.png` (placeholder)
- **งาน:** ทำตามหัวข้อหน้าล็อกอิน Console ด้านบน · ภาพโหลดจากไฟล์ข้างแอป ถ้าไม่มีไฟล์ให้ใช้ภาพที่ฝังไว้ · ล็อกอินสำเร็จแล้วแสดง toast
- **เกณฑ์เสร็จ:** จับภาพทั้งสองธีม · ล็อกอินกับ Server ทดสอบได้ · ลบไฟล์ภาพแล้วไม่ crash

---

## ข้อมูลอ้างอิงในโค้ด (ณ 30 ต.ค. 2569)

- **Endpoint ของ Server:** อยู่ใน `Server/Endpoints/<Group>Endpoints.cs` 14 กลุ่ม (หลัง WP0) · Program.cs เหลือแค่ตั้งค่า + `app.MapXxxEndpoints()` · `MapCommandEndpoints(eventCommandLifetime)` รับพารามิเตอร์เพิ่ม · DTO อยู่ใน `Endpoints/EndpointRequests.cs`
- **Auth ของ Console:** header `X-Auth-Token` ตรวจที่ `GetAuthorizedAdmin` ใน `Server/Endpoints/AuthHelpers.cs` · token อยู่ในตาราง `AuthTokens` · `PasswordHasher` = PBKDF2 · มี `LoginThrottle` อยู่แล้ว
- **Agent loopback API:** ผูกไว้ที่ `127.0.0.1:8788` ใน `Agent/Program.cs` (`/bypass/set` อยู่ราวบรรทัด 200)
- **Failover:** `Agent/Worker.cs` (`FailuresBeforeFallback = 5`, `SuccessesBeforeRecovery = 7`) · `ServerClient.SwitchTo()`
- **Console:** `DashboardWindow.xaml` คือหน้าหลักแบบแท็บ + sidebar (ราว 1,079 บรรทัด) · ธีมอยู่ใน `Console/Theme/`
- **ปัญหาที่รู้อยู่:** ดู "Known limitations" ใน `docs/architecture.md`
