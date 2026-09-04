# Deploy OnionProcOparetor.OnlineConsole

สรุปสิ่งที่สำรวจได้จากเครื่องจริง (2026-09-04) ก่อนตัดสินใจว่าจะรันที่ไหน:

| เครื่อง | LAN | Tailscale | บทบาทตอนนี้ |
|---|---|---|---|
| `onionprocoparetor-dev` (เครื่อง dev นี้) | 192.168.17.253 | 100.121.228.49 | รัน AgentTray, เคยรัน OnlineConsole ด้วย `dotnet run` |
| `epm-server` | 192.168.17.249 | 100.66.55.81 | **รัน OnionProcOparetor.Server** (ตอบ 401 ที่ `/api/clients` = ทำงานปกติ) |

**ข้อสำคัญ:** `appsettings.json` ในโปรเจกต์ตั้ง `Server:BaseUrl = http://127.0.0.1:8787`
ซึ่งถูกต้องเฉพาะเมื่อรัน OnlineConsole **บนเครื่องเดียวกับ Server** เท่านั้น
บนเครื่อง dev ค่านี้ต่อไม่ติด (ไม่มีอะไร listen ที่ 8787 บนเครื่องนั้น)

---

## ทางเลือก A — รันบน `epm-server` (แนะนำ)

เหตุผล: เป็นเครื่องที่ Server อยู่แล้ว (ค่า default ในโปรเจกต์ถูกต้องทันที ไม่ต้อง override),
เปิดอยู่ตลอดเพราะต้องรับ agent 40 เครื่อง, อยู่ LAN เดียวกับ agent (จำเป็นสำหรับ Screen View
ที่ต้องต่อ TCP ตรงไปยัง winvnc), และอยู่บน tailnet อยู่แล้ว

URL หลังติดตั้ง: **http://100.66.55.81:8788**

```powershell
# 1) บนเครื่อง dev: publish (ทำไปแล้ว อยู่ที่ C:\OnionProcOparetor\OnlineConsole)
dotnet publish src\OnionProcOparetor.OnlineConsole\OnionProcOparetor.OnlineConsole.csproj `
  -c Release -o C:\OnionProcOparetor\OnlineConsole

# 2) คัดลอกทั้งโฟลเดอร์ไปที่ epm-server เช่น C:\OnionProcOparetor\OnlineConsole
#    (ไม่ต้องใส่ appsettings.Production.json - ค่า default 127.0.0.1:8787 ถูกอยู่แล้วบนเครื่องนั้น)

# 3) บน epm-server เปิด PowerShell แบบ Run as administrator แล้วรัน
.\install-service.ps1
```

## ทางเลือก B — รันบนเครื่อง dev นี้ต่อไป

ใช้ได้ (เครื่องนี้อยู่ LAN เดียวกับ agent และมี Tailscale) แต่ **คอนโซลจะดับทุกครั้งที่เครื่องนี้ปิด**
และต้อง override ที่อยู่ Server

URL: **http://100.121.228.49:8788**

```powershell
# คัดลอก config override เข้าไปในโฟลเดอร์ที่ publish ไว้
Copy-Item deploy\onlineconsole\appsettings.Production.dev-box.json `
          C:\OnionProcOparetor\OnlineConsole\appsettings.Production.json

# แล้วติดตั้งเป็น service (Run as administrator)
.\install-service.ps1
```

---

## ทำไมต้องเป็น Windows Service ไม่ใช่ `dotnet run`

`dotnet run` ที่ใช้อยู่เดิมมีปัญหาสามข้อสำหรับการใช้งานจริง:

1. **ดับพร้อมหน้าต่างเทอร์มินัล** และไม่กลับมาเองหลังรีบูต
2. **เสิร์ฟ `wwwroot/` ตรงจากโฟลเดอร์ source** (static web assets manifest ชี้ไปที่ไฟล์ใน repo)
   แปลว่าการแก้ CSS/JS ระหว่างพัฒนา **มีผลกับผู้ใช้จริงทันที** โดยไม่ได้ตั้งใจ
   ส่วนที่ publish แล้วจะมีสำเนา `wwwroot` ของตัวเอง แยกขาดจาก repo
3. เป็น Debug build ไม่ใช่ Release และไม่มีไฟล์บีบอัด `.br`/`.gz` ที่ `MapStaticAssets` เตรียมให้

`Program.cs` เขียนรองรับ service ไว้แล้ว (`builder.Host.UseWindowsService()` และ
`ContentRootPath = AppContext.BaseDirectory` ซึ่งจำเป็นเพราะ SCM ไม่ได้ตั้ง working directory
ให้เป็นโฟลเดอร์ของแอป)

## ตรวจหลังติดตั้ง

```powershell
Get-Service OnionOnlineConsole                      # ต้องเป็น Running
(Invoke-WebRequest http://<tailnet-ip>:8788/login).StatusCode   # ต้องได้ 200
```

ถ้า service ไม่ขึ้น ให้ดู Windows Event Log (`Get-EventLog -LogName Application -Newest 20`)
— เมื่อรันเป็น service `UseWindowsService()` จะส่ง log ไปที่นั่นแทน console
