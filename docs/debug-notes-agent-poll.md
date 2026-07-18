# Debug Notes: Agent poll ไม่สำเร็จ (lastPollSucceeded = false ค้าง)

> บันทึกนี้สรุปการไล่ debug ที่ทำมาแล้วในเซสชันก่อนหน้า (ผ่าน Claude chat)
> เพื่อให้ Claude Code เริ่มงานต่อได้ทันทีโดยไม่ต้องไล่ซ้ำ

## อัพเดต (Claude Code เซสชันนี้): เจอ root cause แล้ว + แก้ + verify ในเครื่องแล้ว

**Root cause ยืนยันแล้ว**: `Program.cs` เดิมเรียก `WebApplication.CreateBuilder(args)` ซึ่งอ่าน
`appsettings.json` ทันทีโดยอิง `Directory.GetCurrentDirectory()` ณ ตอนนั้น แต่
`builder.Services.AddWindowsService()` (ที่อาจช่วย fix current directory) ถูกเรียก**หลัง**
Configuration ถูกอ่านไปแล้ว - สายเกินไป เมื่อ Windows SCM สั่ง start service (`sc.exe create`
ไม่มี option ตั้ง working directory เลย) working directory ของ process จะเป็น
`%SystemRoot%\System32` เสมอ ไม่ใช่ install path ทำให้ `appsettings.json` หาไม่เจอ (โหลดแบบ
`optional: true` เงียบๆ ไม่ throw) แล้ว `ServerSettings` fallback ไปใช้ default ในโค้ด
(`BaseUrl = "http://localhost:8787"`) แทนค่าจริงเสมอ - ตรงกับอาการทุกจุด

**Verify แล้วจริงในเครื่อง (ก่อนแก้เครื่องจริง)**: รัน `.exe` ที่ build แล้วโดยตั้ง
`-WorkingDirectory "C:\Windows"` (จำลอง SCM) พร้อมตั้ง `appsettings.json` (ใน build output)
ให้ BaseUrl เป็นค่าทดสอบที่แปลกๆ (`http://192.168.99.99:9999`) → `startup-debug.log` ยืนยันว่า
อ่านค่านี้ได้ถูกต้อง ไม่ fallback ไป localhost เลย

**สิ่งที่แก้ใน `src/OnionProcOparetor.Agent/`:**

1. `Program.cs` - ตั้ง `WebApplicationOptions.ContentRootPath = AppContext.BaseDirectory`
   ตอนสร้าง `builder` (ก่อน config จะถูกอ่านเลย) เพื่อบังคับให้หา `appsettings.json` จากตำแหน่ง
   .exe จริงเสมอ ไม่พึ่ง current directory ที่ SCM ตั้งให้
2. `Services/StartupDiagnostics.cs` (ใหม่) - เขียน log ตรงไฟล์ ไม่พึ่ง ILogger/DI เลย ใช้ log
   ค่า `CurrentDirectory`, `AppContext.BaseDirectory`, `ContentRootPath`, appsettings.json
   เจอไหม, ค่า BaseUrl ที่อ่านได้ในแต่ละขั้น (config / env var / resolved สุดท้าย)
   → เขียนที่ `%ProgramData%\OnionProcOparetor\Agent\logs\startup-debug.log`
3. `Services/FileLoggerProvider.cs` (ใหม่) - ILoggerProvider เขียนไฟล์ ผูกเข้ากับ
   `builder.Logging` เพิ่มจาก console/debug/eventlog เดิม เพื่อให้ log ทั้งหมด (รวม
   `Worker`/`ServerClient` warning ตอน poll fail) เห็นได้แน่นอนแม้รันเป็น Windows Service
   (ไม่มี console) → เขียนที่ `%ProgramData%\OnionProcOparetor\Agent\logs\agent.log`
4. `/status` endpoint เพิ่ม field `serverBaseUrl` - เรียก `GET http://localhost:8788/status`
   แล้วเห็น BaseUrl จริงที่ agent ตัวที่รันอยู่ใช้ได้เลย ไม่ต้องเดาจากไฟล์ config

**เช็คแล้วว่าไม่ใช่ปัญหา**: `dotnet publish -c Release -r win-x64 --self-contained false`
copy `appsettings.json` ไปที่ publish output ถูกต้อง (มี `<CopyToOutputDirectory>` ใน
`.csproj` อยู่แล้ว) และ `.iss` copy ทั้งโฟลเดอร์แบบ `recursesubdirs` อยู่แล้ว - ไม่ต้องแก้ `.iss`
ส่วนนี้ ส่วนเรื่อง "ตั้ง WorkingDirectory ให้ service" นั้น `sc.exe create` **ไม่มี option ให้ตั้ง
เลย** (นี่คือเหตุผลที่ปัญหานี้เกิดได้ - ไม่มีทางแก้จาก installer ได้ ต้อง fix ที่โค้ดเท่านั้น
ซึ่งแก้แล้วด้วย `ContentRootPath` ด้านบน)

## ✅ ยืนยันแล้วว่าแก้จบ (2026-07-18 บ่าย) — deploy จริงข้ามเครื่อง

Test บนเครื่อง client จริง (COM-22, ต่อ server ผ่าน Ethernet ข้ามเครื่องจริงที่ IP
`192.168.200.106:8787`):

- `startup-debug.log` ยืนยันว่า `Environment.CurrentDirectory` เป็น `C:\Windows\system32`
  จริงตามที่คาดไว้ (SCM ตั้งให้) แต่ `ContentRootPath`/`AppContext.BaseDirectory` ยัง resolve
  ถูกไปที่ `C:\Program Files\Onion ProcOparetor\Client\` และเจอ `appsettings.json` จริง
  (`True`) — พิสูจน์ว่า fix `ContentRootPath` ทำงานถูกต้องกับ deployment จริง ไม่ใช่แค่เครื่อง dev
- `resolvedBaseUrl` อ่านค่าได้ถูกต้องตรงกับ `appsettings.json` (`http://192.168.200.106:8787`)
- `GET http://localhost:8788/status` → `lastPollSucceeded: true` แล้ว — **poll สำเร็จจริง**

### บทเรียนเสริมที่เจอระหว่างทดสอบรอบนี้ (ไม่ใช่บั๊กโค้ด แต่เป็นขั้นตอน install ที่ต้องระวัง)

ระหว่างทดสอบเจอ 2 เรื่องที่ทำให้หลงทางไปพักหนึ่งก่อนจะเจอผลลัพธ์ที่ถูกต้องจริง — บันทึกไว้กันพลาดซ้ำ:

1. **Inno Setup ไม่ touch timestamp ตอน extract ไฟล์** — ทำให้ดู `LastWriteTime` ของ
   `.exe`/`Setup.exe` เฉยๆ ไม่พอจะรู้ว่าไฟล์ไหน "ใหม่กว่า" จริง (สับสนไปหนึ่งรอบว่า installer
   ที่ใช้ compile ก่อน publish ล่าสุดหรือหลัง) — วิธีเช็คที่ชัวร์กว่า: เทียบ `Get-FileHash` ของ
   exe ที่ publish กับที่ install จริง หรือ compile installer ใหม่สดๆ ผ่าน command line
   (`ISCC.exe`, พบที่ `F:\innosetup-portable\app\ISCC.exe`) ทุกครั้งก่อนเอาไปทดสอบ แทนที่จะเดา
   จาก timestamp ของไฟล์ที่ compile ไว้ก่อนหน้า
2. **Race condition จริงใน `.iss` (เจอ + แก้แล้ว)**: ผู้ใช้ยืนยันว่าพิมพ์ IP:Port ของ server
   ถูกต้องตอน install จริง (ไม่ได้ปล่อย default `localhost:8787`) แต่ log รอบแรกหลัง install
   กลับอ่านได้ `localhost:8787` อยู่ดี — พอ `Restart-Service OnionAgent` เฉยๆ (ไม่ได้แก้ไฟล์อะไร
   เพิ่ม) กลับอ่านค่าถูกต้องทันที นี่คือหลักฐานของ **race condition**: `CurStepChanged(ssPostInstall)`
   ที่แก้ `appsettings.json` ให้ถูก กับ `[Run]` entry `sc.exe start OnionAgent` (แยกกันคนละ
   mechanism) ไม่มีอะไรการันตีลำดับก่อนหลังระหว่างกัน ทำให้บางครั้ง service start ไปอ่าน
   `appsettings.json` **ก่อน** ที่ patch จะเขียนค่าจริงเสร็จ (อ่านค่า default ที่ file ยังไม่ทัน
   ถูกแก้ไปใช้) พอ restart รอบสองไฟล์ถูกแก้เสร็จแล้วเลยอ่านถูก
   - **แก้แล้ว**: ย้าย `sc.exe create/start/failure OnionAgent` ทั้งหมดจาก `[Run]` เข้าไปทำใน
     `CurStepChanged(ssPostInstall)` แทน เรียงลำดับชัดเจนใน Pascal เดียวกัน: create → patch
     `appsettings.json` → ตั้ง failure recovery → start เท่านั้น รับประกัน 100% ว่า service จะ
     start หลัง config ถูกแก้เสร็จแล้วเสมอ ไม่พึ่งพา timing ที่ Inno Setup จัดการเองอีกต่อไป
3. **`OnionProcOparetor.AgentTray` มี `appsettings.json` แยกต่างหากจาก `Client` (เจอ + แก้แล้ว)**:
   หลัง service poll สำเร็จแล้ว พบว่า Tray login window ยังต่อ Server ไม่ได้ + ข้อความ error ยาว
   ล้นออกนอกกรอบหน้าต่าง (`LoginWindow.xaml` เป็น fixed-size, `ResizeMode="NoResize"`, ไม่มี
   scroll) — สาเหตุจริง: `.iss` เดิม patch แค่ `{app}\Client\appsettings.json` (ของ Agent
   Service) เท่านั้น **ไม่เคย patch `{app}\ClientTray\appsettings.json`** เลยตั้งแต่แรก ทำให้
   Tray ยังชี้ไป `localhost:8787` เสมอไม่ว่าจะกรอก IP ถูกตอน install แค่ไหนก็ตาม
   - **แก้แล้ว**: เพิ่ม helper `PatchServerBaseUrl()` ใน `.iss` แล้วเรียกทั้งกับ
     `{app}\Client\appsettings.json` และ `{app}\ClientTray\appsettings.json`
   - **แก้เพิ่ม**: `ClientApiClient.LoginToServerAsync` (ใน
     `src/OnionProcOparetor.AgentTray/Services/`) เปลี่ยนจากโชว์ `ex.Message` ดิบๆ (ยาวเกินไป
     สำหรับหน้าต่างขนาดคงที่) เป็นข้อความสั้นๆ อ่านง่ายแทน
   - Publish + compile installer ใหม่แล้ว (`ISCC.exe` สำเร็จไม่มี error) — **ผู้ใช้ยืนยันแล้วว่า
     ใช้งานได้จริง** (2026-07-18 บ่าย)

## อาการ (Symptom)

- `OnionProcOparetor.Agent` (Windows Service ชื่อ `OnionAgent`) เรียก poll ไปยัง
  `OnionProcOparetor.Server` ผ่าน `GET /api/poll/{clientGuid}?machineName=...`
- เช็คสถานะผ่าน local API ของ Agent เอง: `GET http://localhost:8788/status`
  → `lastPollSucceeded: false` ค้างอยู่แบบนี้ตลอด แม้ผ่านไปหลายรอบ poll แล้ว
  (ค่า default `PollIntervalSeconds` = 30 วิ ต่อรอบ ตาม `appsettings.json`)

## สิ่งที่ทดสอบแล้ว และ "ตัดออก" ว่าไม่ใช่ต้นเหตุ

ทุกข้อด้านล่างนี้ **ทดสอบจริงแล้วผ่านหมด** — อย่าไปวนเช็คซ้ำจุดเหล่านี้ก่อน:

1. **Network/firewall ระหว่าง client ↔ server ใช้ได้ปกติ**
   ทดสอบด้วย `Invoke-RestMethod -Uri "http://<server-ip>:8787" -Method Get` จากเครื่อง client
   ข้ามเครือข่ายได้ผลลัพธ์ปกติ (`{ service: "OnionProcOparetor.Server", status: "running" }`)

2. **Endpoint `/api/poll/{clientGuid}` ทำงานถูกต้องจริง**
   ดึง `ClientGuid` จริงจาก Registry:
   ```powershell
   Get-ItemProperty -Path 'HKLM:\SOFTWARE\OnionProcOparetor' -Name ClientGuid
   ```
   แล้วยิงตรงๆ:
   ```powershell
   $guid = (Get-ItemProperty -Path 'HKLM:\SOFTWARE\OnionProcOparetor' -Name ClientGuid).ClientGuid
   $machine = $env:COMPUTERNAME
   $url = "http://<server-ip>:8787/api/poll/" + $guid + "?machineName=" + $machine
   Invoke-RestMethod -Uri $url -Method Get
   ```
   ผลลัพธ์: `enabled: True, rules: {}` — สำเร็จปกติทุกครั้งที่ลอง (คนละครั้ง คนละเวลา)

3. **`appsettings.json` ของ Agent ตั้งค่าถูกต้อง**
   `ServerSettings:BaseUrl` ชี้ไป IP จริงของ server (ไม่ใช่ `localhost`) ยืนยันแล้วว่าไม่ใช่ default ค้าง

4. **ไม่ใช่ stale build**
   เทียบ `LastWriteTime` ของ `Worker.cs` / `Services/ServerClient.cs` กับ `.dll` ที่ publish ออกมา
   → DLL ใหม่กว่า source เสมอ ดังนั้น build ที่ deploy คือโค้ดล่าสุดจริง (ไม่ใช่ปัญหาลืม publish)

## สิ่งที่ยังไม่ได้เช็ค / ควรทำต่อเป็นลำดับแรก

### 1. ดู exception จริงจาก `ServerClient.PollAsync` โดยตรง

`ServerClient.PollAsync` (ใน `src/OnionProcOparetor.Agent/Services/ServerClient.cs`) catch exception
ทุกแบบเงียบๆ แล้ว log ผ่าน `ILogger<ServerClient>.LogWarning` แต่ไม่เคย verify ว่า log นี้ไปโผล่ที่ไหน
ตอนรันเป็น Windows Service (default logging provider ของ `WebApplication.CreateBuilder` คือ console
+ debug — พอรันเป็น Windows Service (ไม่มี console attach) จะไม่เห็น log เลยถ้าไม่มี provider อื่นเสริม)

**วิธีดูให้เห็นชัด:** หยุด service แล้วรัน `.exe` ตรงๆ แบบ console (ไม่ผ่าน `sc.exe`):
```powershell
Stop-Service OnionAgent
cd "C:\Program Files\Onion ProcOparetor\Client"   # ปรับ path ตามจริง
.\OnionProcOparetor.Agent.exe
```
ปล่อยรันอย่างน้อย 1 poll cycle (30+ วิ) แล้วดู log ที่ print ออก console ตรงๆ
ควรเห็น exception message จริงจาก `catch (Exception ex)` ใน `PollAsync`

### 2. เช็คว่า `HttpClient.BaseAddress` runtime ตรงกับ config จริงไหม

`Program.cs` ของ Agent ผูก `HttpClient` ผ่าน:
```csharp
builder.Services.AddHttpClient<ServerClient>((serviceProvider, client) =>
{
    var settings = builder.Configuration.GetSection("ServerSettings").Get<ServerSettings>()
        ?? new ServerSettings();
    client.BaseAddress = new Uri(settings.BaseUrl);
    client.Timeout = TimeSpan.FromSeconds(10);
});
```
ควร log ค่า `settings.BaseUrl` ตรงนี้ตอน startup (`Console.WriteLine` หรือ `ILogger`) เพื่อยืนยันว่า
ตอน runtime จริงมันอ่านค่าเดียวกับที่เห็นใน `appsettings.json` ไฟล์ — สงสัยเรื่อง working directory
ที่ Windows Service รันจาก (`CurrentDirectory` อาจไม่ใช่โฟลเดอร์ที่มี `appsettings.json` จริงถ้า service
ถูกตั้งด้วย path ผิด ทำให้ `IConfiguration` fallback ไปใช้ default `ServerSettings.BaseUrl` =
`"http://localhost:8787"` แทน ซึ่งจะ fail แน่นอนถ้า server อยู่คนละเครื่อง)

**นี่คือสมมติฐานที่น่าจะเป็นไปได้มากที่สุดตอนนี้** เพราะอาการเข้ากันพอดี:
ยิงด้วยมือ (จาก client, ระบุ IP ตรงๆ) สำเร็จเสมอ แต่ Agent เองซึ่งอ่าน BaseUrl จาก config ไฟล์
กลับ fail ตลอด — ถ้า Windows Service มองไม่เห็น `appsettings.json` (เพราะ working directory ผิด
หรือไฟล์ไม่ได้ถูก copy ไปที่ install path จริง) มันจะ fallback ไปยิง `localhost:8787` ซึ่งบนเครื่อง client
ไม่มี server รันอยู่ที่ localhost เลย → ทุก poll จะ fail แบบเงียบๆ ตรงตามอาการที่เห็น

### 3. เช็คว่า service ถูก register ด้วย working directory ที่ถูกต้อง

```powershell
sc.exe qc OnionAgent
```
ดู `BINARY_PATH_NAME` — .NET Windows Service ปกติจะใช้ location ของ .exe เป็น working directory
โดย default แต่ถ้า installer ตั้ง service ผ่าน `sc.exe create` ด้วย path ที่ไม่ตรง หรือไม่ได้ตั้ง
`WorkingDirectory` ชัดเจน อาจไปหา `appsettings.json` ไม่เจอ

## ไฟล์ที่เกี่ยวข้อง (ทั้งหมดอยู่ใต้ `src/OnionProcOparetor.Agent/`)

- `Program.cs` — DI setup, HttpClient binding, local API (`/status`, `/logs/recent`)
- `Worker.cs` — main poll loop + process scanning + rule matching
- `Services/ServerClient.cs` — `PollAsync` / `SendLogAsync` (HTTP calls ไป Server)
- `Services/ClientState.cs` — เก็บ `LastPollSucceeded` แบบ in-memory
- `Services/ClientIdentity.cs` — อ่าน/เขียน `ClientGuid` ผ่าน Registry (`HKLM:\SOFTWARE\OnionProcOparetor`)
- `appsettings.json` — `ServerSettings:BaseUrl`, `ServerSettings:PollIntervalSeconds`

## Environment

- Server IP ที่ใช้ทดสอบ: `192.168.200.106:8787` (ปรับตามจริงถ้าเปลี่ยน)
- ทุกเครื่อง (server + client ทดสอบ) ถูก uninstall ทิ้งหมดแล้วก่อนเริ่มเซสชันนี้
  ต้อง publish + compile installer ใหม่ + install ใหม่ทั้งหมดตั้งแต่ต้น
