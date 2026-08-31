<#
================================================================================
 install-watchdog.ps1 - ติดตั้ง/ซ่อม tamper watchdog ของ OnionAgent
================================================================================

เรียกจาก 2 ที่ (idempotent เต็มรูปแบบ เรียกซ้ำกี่ครั้งก็ได้ ผลลัพธ์เหมือนเดิม):
  1. setup-client.ps1 ตอนติดตั้ง/อัปเกรด Agent (ครั้งแรกที่สร้าง wd.key)
  2. TamperProtectionService ใน Agent เอง ทุกครั้งที่ตรวจพบว่า Scheduled Task หายไป
     -> ลบ task ทิ้งอย่างเดียวไม่พอที่จะปิด watchdog ต้องฆ่า Agent ด้วยพร้อมกัน

สิ่งที่ทำ:
  - สร้าง %ProgramData%\OnionProcOparetor\Agent\ พร้อม ACL ที่ตัด "Users" ออก
    (นักเรียนสิทธิ์ปกติอ่าน wd.key ไม่ได้ = ปลอม maintenance ticket ไม่ได้)
  - สร้าง wd.key (32 ไบต์สุ่ม base64) ถ้ายังไม่มี - ห้ามสร้างทับของเดิม ไม่งั้น ticket
    ที่ Agent/UninstallHelper ออกไว้ก่อนหน้าจะ verify ไม่ผ่านทันที
  - เขียน watchdog-config.json (ถ้ายังไม่มี)
  - บันทึก InstallFolder ลง registry ให้ watchdog หา .exe เจอโดยไม่ต้อง hardcode
  - ลงทะเบียน Scheduled Task รันเป็น SYSTEM: ตอนบูต + ซ้ำทุก 1 นาที ไม่มีวันหมด

หมายเหตุเรื่องสิทธิ์: ต้องรันด้วยสิทธิ์ admin/SYSTEM เท่านั้น (schtasks /ru SYSTEM และ
icacls ต้องการสิทธิ์นั้น) - ทั้ง 2 ผู้เรียกข้างบนรันด้วยสิทธิ์นั้นอยู่แล้ว
================================================================================
#>

[CmdletBinding()]
param(
    # โฟลเดอร์ติดตั้งจริงของ Agent product (มี Client\ และ CustomActions\ อยู่ข้างใน)
    [Parameter(Mandatory = $true)][string]$InstallDir,

    # ทุกกี่นาทีที่ watchdog จะตื่นมาตรวจ - 1 นาทีคือค่าที่ตั้งใจ (ดู README ใน agent-watchdog.ps1)
    [int]$IntervalMinutes = 1
)

$ErrorActionPreference = 'Stop'

$DataDir     = Join-Path $env:ProgramData 'OnionProcOparetor\Agent'
$SecretPath  = Join-Path $DataDir 'wd.key'
$ConfigPath  = Join-Path $DataDir 'watchdog-config.json'
$LogPath     = Join-Path $DataDir 'watchdog-install.log'
$TaskName    = 'OnionProcOparetor Agent Watchdog'
$ScriptPath  = Join-Path $InstallDir 'CustomActions\agent-watchdog.ps1'

function Write-InstallLog([string]$Message) {
    $line = "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') $Message"
    try {
        if (-not (Test-Path $DataDir)) { New-Item -ItemType Directory -Path $DataDir -Force | Out-Null }
        Add-Content -Path $LogPath -Value $line -Encoding utf8
    } catch { }
    Write-Host $line
}

Write-InstallLog "==== install-watchdog.ps1 starting (InstallDir=$InstallDir) ===="

# ---------------------------------------------------------------------------
# 1) โฟลเดอร์ข้อมูล + ACL
# ---------------------------------------------------------------------------
if (-not (Test-Path $DataDir)) {
    New-Item -ItemType Directory -Path $DataDir -Force | Out-Null
    Write-InstallLog "สร้างโฟลเดอร์ $DataDir"
}

# ตัด inheritance แล้วให้สิทธิ์เฉพาะ SYSTEM + Administrators - จุดสำคัญของทั้งดีไซน์:
# ถ้านักเรียนสิทธิ์ปกติอ่าน wd.key ได้ ก็ปลอม maintenance ticket ปิด watchdog ได้เลย
# (/inheritance:r ต้องมาก่อน /grant ไม่งั้น ACE ที่สืบทอดมาจะยังอยู่)
& icacls.exe "$DataDir" /inheritance:r /grant:r "*S-1-5-18:(OI)(CI)F" "*S-1-5-32-544:(OI)(CI)F" /T /C /Q 2>&1 |
    ForEach-Object { Write-InstallLog "icacls: $_" }

# ---------------------------------------------------------------------------
# 2) wd.key - สร้างครั้งเดียวเท่านั้น ห้ามทับของเดิม
# ---------------------------------------------------------------------------
if (Test-Path $SecretPath) {
    Write-InstallLog "wd.key มีอยู่แล้ว - ไม่สร้างทับ (ทับแล้ว ticket เดิมจะ verify ไม่ผ่าน)"
}
else {
    $keyBytes = New-Object byte[] 32
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try { $rng.GetBytes($keyBytes) } finally { $rng.Dispose() }
    # ascii + NoNewline เพราะฝั่ง C# กับ PowerShell อ่านกลับด้วย .Trim() ทั้งคู่ ต้องไม่มี BOM ปน
    [System.IO.File]::WriteAllText($SecretPath, [Convert]::ToBase64String($keyBytes), [System.Text.Encoding]::ASCII)
    Write-InstallLog "สร้าง wd.key ใหม่แล้ว"
}

# ---------------------------------------------------------------------------
# 3) watchdog-config.json - เขียนเฉพาะตอนยังไม่มี (ครูแก้เองแล้วต้องไม่โดนทับตอน upgrade)
# ---------------------------------------------------------------------------
if (-not (Test-Path $ConfigPath)) {
    $cfg = [ordered]@{
        MinUptimeSeconds      = 180
        HeartbeatStaleSeconds = 240
        SoftHitsBeforeReboot  = 2
        MaxRebootsPer24h      = 3
        ReportTimeoutSeconds  = 4
        RebootDelaySeconds    = 5
    }
    ($cfg | ConvertTo-Json) | Set-Content -Path $ConfigPath -Encoding utf8
    Write-InstallLog "เขียน watchdog-config.json ค่าเริ่มต้นแล้ว"
}

# ---------------------------------------------------------------------------
# 4) บันทึก InstallFolder ลง registry ให้ watchdog หา Agent .exe เจอ
# ---------------------------------------------------------------------------
try {
    if (-not (Test-Path 'HKLM:\SOFTWARE\OnionProcOparetor')) {
        New-Item -Path 'HKLM:\SOFTWARE\OnionProcOparetor' -Force | Out-Null
    }
    Set-ItemProperty -Path 'HKLM:\SOFTWARE\OnionProcOparetor' -Name 'InstallFolder' -Value $InstallDir.TrimEnd('\')
    Write-InstallLog "บันทึก InstallFolder ลง registry แล้ว"
} catch {
    Write-InstallLog "เขียน InstallFolder ลง registry ไม่สำเร็จ: $($_.Exception.Message)"
}

# ---------------------------------------------------------------------------
# 5) Scheduled Task
# ---------------------------------------------------------------------------
if (-not (Test-Path $ScriptPath)) {
    Write-InstallLog "ไม่พบ $ScriptPath - ยกเลิกการลงทะเบียน task (ลง task ที่ชี้ไปไฟล์ที่ไม่มีอยู่ = task ที่พังเงียบๆ)"
    exit 1
}

try {
    $action = New-ScheduledTaskAction -Execute 'powershell.exe' `
        -Argument "-NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$ScriptPath`""

    # AtStartup = ตรวจทันทีหลังบูต (หลังพ้น MinUptimeSeconds ที่สคริปต์เช็คเอง)
    # Once+Repetition = วนทุก IntervalMinutes ตลอดไป ไม่ต้องพึ่ง trigger รายวันที่ต้อง reset
    $trigStartup = New-ScheduledTaskTrigger -AtStartup
    $trigRepeat  = New-ScheduledTaskTrigger -Once -At (Get-Date).AddMinutes(1) `
        -RepetitionInterval (New-TimeSpan -Minutes $IntervalMinutes) `
        -RepetitionDuration ([TimeSpan]::MaxValue)

    $principal = New-ScheduledTaskPrincipal -UserId 'S-1-5-18' -LogonType ServiceAccount -RunLevel Highest

    # ทุก option ที่ทำให้ task "ไม่รัน" ต้องปิดให้หมด: เครื่อง lab มักใช้แบตไม่มี/สลับ user บ่อย
    # ถ้าปล่อย default ไว้ Task Scheduler จะข้ามรอบเงียบๆ ในสถานการณ์พวกนั้นแล้ว watchdog ก็ไร้ผล
    $settings = New-ScheduledTaskSettingsSet `
        -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -DontStopOnIdleEnd `
        -StartWhenAvailable -MultipleInstances IgnoreNew `
        -ExecutionTimeLimit (New-TimeSpan -Minutes 5) `
        -RestartCount 3 -RestartInterval (New-TimeSpan -Minutes 1)
    $settings.Hidden = $true
    $settings.DisallowStartIfOnBatteries = $false

    # ลบของเดิมก่อนเสมอ - Register-ScheduledTask -Force ก็ทับได้ แต่ลบก่อนทำให้แน่ใจว่า
    # trigger/settings เก่าที่ไม่มีใน definition ใหม่ไม่หลงเหลืออยู่
    if (Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue) {
        Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false
        Write-InstallLog "ลบ task เดิมออกก่อนลงทะเบียนใหม่"
    }

    Register-ScheduledTask -TaskName $TaskName -Action $action `
        -Trigger @($trigStartup, $trigRepeat) -Principal $principal -Settings $settings `
        -Description 'ตรวจสอบว่า OnionAgent ยังทำงานอยู่ และรีสตาร์ทเครื่องหากถูกแก้ไข/ถอนออกโดยไม่ได้รับอนุญาต' | Out-Null

    Write-InstallLog "ลงทะเบียน Scheduled Task '$TaskName' สำเร็จ (ทุก $IntervalMinutes นาที + ตอนบูต)"
}
catch {
    Write-InstallLog "ลงทะเบียน Scheduled Task ไม่สำเร็จ: $($_.Exception.Message)"
    exit 1
}

Write-InstallLog "==== install-watchdog.ps1 done ===="
exit 0
