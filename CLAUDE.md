# OnionProcOparetor: กฎกลางสำหรับทุก session (orchestrator + worker)

ระบบคุมห้องคอม 40 เครื่องของโรงเรียน (.NET 10)
- **Server:** Windows Service, Minimal API + SignalR + EF Core/SQLite
- **Agent:** Windows Service ที่รันบนเครื่องนักเรียน
- **AgentTray:** WPF รันใน session ของผู้ใช้
- **Console:** WPF ของครู
- **OnlineConsole:** Blazor Server สำหรับใช้บนมือถือผ่าน Tailscale
- **RemoteBridge:** ESP32 knob
- **UninstallHelper**
- **ตัวติดตั้ง:** WiX v5 สอง MSI

**แผนงานและการตัดสินใจทั้งหมดอยู่ที่ [`docs/roadmap.md`](docs/roadmap.md) อ่านก่อนเริ่มงานทุกครั้ง**
รายละเอียดสถาปัตยกรรมของที่มีอยู่แล้วอยู่ที่ [`docs/architecture.md`](docs/architecture.md)

## ข้อจำกัดที่คุมทุกการตัดสินใจ

- **Disk freeze:** เครื่องนักเรียนทั้ง 40 เครื่องรัน Reboot Restore Rx ทุกอย่างที่เขียนลงดิสก์ระหว่างทำงานจะหายเมื่อรีบูต เหลือรอดเฉพาะสิ่งที่ติดตั้งตอน freeze ปิด ดังนั้น state ฝั่ง client ต้องมาจาก MSI หรือดึงจาก Server ใหม่ทุกครั้ง ห้ามพึ่งไฟล์หรือ registry ที่เขียนตอน runtime
- **โค้ดฝั่ง Agent/AgentTray จะยังไม่มีผลในแล็บ** จนกว่าจะลง MSI ใหม่ ส่วนงานฝั่ง Server/Console มีผลทันทีที่ redeploy Server
- **Server จริงอยู่ที่ 192.168.17.249** เครื่อง dev นี้รันแค่ Console จึงรัน Server ทดสอบในเครื่องได้ แต่ให้ใช้ port และ DB แยก เช่น port 18787 กับ DB ใน scratch
- **ห้ามแตะ Chrome/Edge policy (HKLM) จริงของเครื่อง dev** ถ้าจะทดสอบ registry ให้ใช้ key ทดลองใต้ HKCU แล้วลบทิ้งหลังทดสอบ
- **AgentTray เปิดเองตอนล็อกอินผ่าน HKCU Run ของแต่ละ user** บัญชีใหม่ที่ไม่เคยล็อกอินก่อน commit baseline จะไม่มี tray เลย
- **Agent secret พักไว้** (`RequireAgentSecret=false`) อย่าแก้เรื่องนี้ถ้าไม่ได้รับมอบหมาย

## กับดักเครื่องมือที่เคยเจอจริง

- **`wix build` และ `msiexec` ต้องรันผ่าน PowerShell ห้ามใช้ Bash** เพราะ Bash กิน backslash ใน path แล้วเขียน output ผิดที่โดยยังคืน exit 0
- **`.ps1` ที่มีภาษาไทยต้องบันทึกเป็น UTF-8 with BOM** ไม่อย่างนั้นจะ parse ไม่ผ่านแบบเงียบๆ ต้องตรวจด้วย `[System.Management.Automation.Language.Parser]::ParseFile` ทุกครั้ง
- **ห้ามเรียก `msiexec /i` ตัวที่สองจากใน CustomAction ของ MSI** จะเกิด mutex deadlock ให้ใช้ Scheduled Task แบบหน่วงเวลาแทน
- **จับภาพหน้าจอ WPF ให้ใช้ `RenderTargetBitmap` ในแอป** การจับจาก desktop จะได้ผิดหน้าต่างหรือได้ภาพว่าง
- **build:** `dotnet build OnionProcOparetor.sln -c Release` ถ้ามี AgentTray.exe รันอยู่ในเครื่องจะเจอ MSB3027 (ไฟล์ถูกล็อก) ซึ่งไม่ใช่บั๊ก
- **ทำ MSI:** `installer/publish-all.ps1` จะ bump FileVersion ให้เอง ถ้าไม่ bump ตอน upgrade จะไม่เขียนทับไฟล์

## กติกาการทำงานแบบ orchestrator/worker

- 1 work package (WP) = 1 branch `feat/p1-<wp>` ทำใน worktree ของตัวเอง ห้าม commit ตรงเข้า main
- แตะได้เฉพาะไฟล์ที่ WP ของตัวเองระบุไว้ใน roadmap ถ้าต้องแก้ไฟล์ของ WP อื่น ให้แจ้ง orchestrator ห้ามแก้เอง
- **`Server/Program.cs` เป็นจุดชนหลัก:** endpoint ใหม่ให้เขียนใน `src/OnionProcOparetor.Server/Endpoints/<Feature>Endpoints.cs` เป็น extension method `MapXxxEndpoints()` แล้วเพิ่มใน Program.cs แค่บรรทัดเดียว
- **EF migration สร้างได้ทีละ WP เท่านั้น** ตามลำดับใน roadmap ต้องเป็นการเพิ่มคอลัมน์หรือตารางใหม่ (additive) ห้ามลบหรือ rename คอลัมน์ ถ้าเจอ ModelSnapshot ชนกัน ให้ rebase แล้วสร้าง migration ใหม่ ห้ามแก้ snapshot ด้วยมือ
- ทุก WP ต้อง build ผ่านทั้ง solution (0 error) ก่อนส่งงาน และมีวิธีทดสอบที่ทำจริงแล้วเขียนไว้ใน PR/commit ถ้ายังไม่ได้ทดสอบ ให้เขียนว่ายังไม่ได้ทดสอบตรงๆ
- ไม่มีระบบ plugin หรือ hot-load: โมดูลใหม่ = โฟลเดอร์แยก + การเปลี่ยน DB แบบ additive
- คอมเมนต์ในโค้ดเขียนภาษาไทยได้ตามสไตล์เดิม ใช้ commit message แบบ conventional (`feat(scope): ...`)
- รายงานสถานะให้ผู้ใช้เป็นภาษาไทย
- งานเสร็จแล้วอัปเดตหัวข้อ "สถานะ" ใน `docs/roadmap.md` และส่วนที่เกี่ยวข้องใน `docs/architecture.md`
