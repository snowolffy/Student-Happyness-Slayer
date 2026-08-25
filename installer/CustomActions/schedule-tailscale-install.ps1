# รันตอน Agent MSI ติดตั้ง (deferred CustomAction After="InstallFiles") - "จอง" การติดตั้ง Tailscale
# ไว้ผ่าน Scheduled Task ที่ตั้งเวลาให้เริ่มอีก ~90 วิถัดจากนี้ แทนที่จะรัน msiexec /i ของ Tailscale
# ตรงๆ ที่นี่ (ดู install-tailscale-deferred.ps1 comment เต็มสำหรับเหตุผล: nested msiexec ชน
# _MSIExecute mutex ที่ Agent MSI ตัวนอกยังถือค้างไว้อยู่ระหว่าง transaction - deadlock/fail แน่นอน)
#
# 90 วิ = buffer ให้พอสำหรับ Agent MSI transaction จบจริง (ผ่าน InstallFinalize, msiexec.exe ตัวนอก
# exit จริง ปล่อย mutex) ก่อนที่ Tailscale's msiexec จะเริ่มพยายามรัน - ไม่ fire ทันที (/run) เพราะ
# ตอนนั้น mutex ยังไม่ว่างแน่นอน (CustomAction นี้เองยังรันอยู่ = transaction นอกยังไม่จบ)
#
# CustomAction นี้เอง return ทันทีไม่รอผลจริงของ Tailscale เลย (แค่ "จองคิว" สำเร็จหรือไม่) - เช็คผล
# การติดตั้งจริงทีหลังผ่าน GetTailscaleStatus command หรือ log ที่ install-tailscale-deferred.ps1 เขียนไว้
#
# บั๊กจริงที่เจอ (2026-08-25, field test): เดิมใช้ schtasks.exe ตรงๆ ผ่าน `& schtasks.exe /tr $taskCommand
# ...` โดย $taskCommand เป็น string ตัวเดียวที่มี path มี space อยู่ข้างใน (เช่น "Onion ProcOparetor
# Agent") - PowerShell (5.1 โดยเฉพาะ) ไม่ห่อ quote ให้อัตโนมัติตอนส่ง string variable ไปเป็น argument
# ของ native exe ถึงแม้ string นั้นจะมี `" ฝังอยู่ข้างในแล้วก็ตาม ผลคือ argv ถูกตัดแบ่งผิดที่ space
# (ยืนยันจริงจากการทดสอบ: "ERROR: Invalid argument/option - 'Files\Onion'.") ลองห่อ quote ซ้อนเองแล้ว
# ก็ยังพังอีกแบบ (schtasks.exe's command-line quoting เชื่อถือไม่ได้พอสำหรับ path ที่มี space+quote
# ซ้อนกันหลายชั้นแบบนี้) แก้เด็ดขาดด้วยการเลิกพึ่ง schtasks.exe ทั้งหมด เปลี่ยนไปใช้ ScheduledTasks
# PowerShell module แทน (New-ScheduledTaskAction -Argument รับ string เป็น parameter ของ cmdlet ตรงๆ
# ผ่าน .NET/COM object ไม่ต้องผ่านการ re-tokenize แบบ native command-line เลย - ทดสอบแล้วว่า path ที่มี
# space เก็บค่าถูกต้อง 100% ไม่มีปัญหา quoting อีก)

param(
    [Parameter(Mandatory = $true)][string]$TailscaleMsiPath,
    [Parameter(Mandatory = $true)][string]$DeferredScriptPath
)

$ErrorActionPreference = "Continue"
$taskName = "OnionProcOparetor-InstallTailscale"
$logPath = Join-Path $env:ProgramData "OnionProcOparetor\Agent\logs\schedule-tailscale-install.log"

function Write-Log([string]$Message) {
    $logDir = Split-Path $logPath -Parent
    if (-not (Test-Path $logDir)) {
        New-Item -ItemType Directory -Path $logDir -Force | Out-Null
    }
    "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') $Message" | Out-File -FilePath $logPath -Append -Encoding utf8
}

Write-Log "==== schedule-tailscale-install.ps1 starting ===="
Write-Log "TailscaleMsiPath=$TailscaleMsiPath"
Write-Log "DeferredScriptPath=$DeferredScriptPath"

try {
    # ลบ task เดิมทิ้งก่อนถ้ามีค้างจาก repair/reinstall ครั้งก่อน (idempotent)
    Unregister-ScheduledTask -TaskName $taskName -Confirm:$false -ErrorAction SilentlyContinue

    $argumentString = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File `"$DeferredScriptPath`" -TailscaleMsiPath `"$TailscaleMsiPath`""
    Write-Log "Task action argument string: $argumentString"

    $action = New-ScheduledTaskAction -Execute "powershell.exe" -Argument $argumentString
    $trigger = New-ScheduledTaskTrigger -Once -At ((Get-Date).AddSeconds(90))
    $principal = New-ScheduledTaskPrincipal -UserId "SYSTEM" -LogonType ServiceAccount -RunLevel Highest

    Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger -Principal $principal -Force -ErrorAction Stop | Out-Null

    Write-Log "ตั้ง Scheduled Task สำเร็จ - จะเริ่มรันจริงประมาณ 90 วิถัดจากนี้"
}
catch {
    Write-Log "ตั้ง Scheduled Task ไม่สำเร็จ: $($_.Exception.Message)"
    Write-Log "Tailscale จะไม่ถูกติดตั้งอัตโนมัติรอบนี้ - เช็ค log นี้เป็นจุดแรกถ้า TAILSCALE STATUS บอกว่ายังไม่ได้ติดตั้ง"
}

Write-Log "==== schedule-tailscale-install.ps1 done ===="

# ตั้งใจ exit 0 เสมอไม่ว่าจะสำเร็จหรือไม่ (Return="ignore" ฝั่ง .wxs อยู่แล้วก็จริง แต่ explicit ไว้ให้
# ชัดว่าตั้งใจ ไม่ใช่ลืมเช็ค) - Tailscale เป็นแค่ fallback path เสริม ไม่ควรกระทบการติดตั้ง Agent หลัก
exit 0
