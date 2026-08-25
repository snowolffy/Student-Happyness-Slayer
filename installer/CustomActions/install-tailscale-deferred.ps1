# รันจริงโดย Scheduled Task ที่ schedule-tailscale-install.ps1 สร้างไว้ (ไม่ใช่รันตรงจาก MSI
# CustomAction เลย) - เหตุผล: Windows Installer ใช้ mutex เดียวทั้งเครื่อง (_MSIExecute) ระหว่างที่
# Agent MSI transaction กำลังติดตั้งอยู่ ถ้าเรียก msiexec /i ซ้อนเข้าไปตรงๆ จาก CustomAction ที่รันอยู่
# ข้างในเวลาเดียวกัน จะชน mutex เดิมที่ยังไม่ถูกปล่อยจนกว่า transaction นอกจะจบ - deadlock/fail แน่นอน
# (บั๊กจริงที่เจอ 2026-08-25, field test: ทั้ง Agent MSI install ล้มทันทีตอนพยายามรัน nested msiexec)
#
# แก้ด้วยการแยกให้รันทีหลัง (ผ่าน Scheduled Task ที่ schedule-tailscale-install.ps1 ตั้งเวลาไว้ ~90 วิ
# ถัดจากตอนที่ Agent MSI เริ่ม custom action นี้ - นานพอให้ transaction นอกจบและปล่อย mutex ก่อนแน่นอน)
# ผลคือ Agent MSI ไม่สามารถ hard-fail ถ้า Tailscale ลงไม่สำเร็จได้อีกต่อไป (รู้ผลไม่ทันตอน transaction
# นอกจบ) - ตรวจสอบผลจริงทีหลังผ่าน GetTailscaleStatus command แทน (ดู CommandProcessor.cs)

param(
    [Parameter(Mandatory = $true)][string]$TailscaleMsiPath
)

$ErrorActionPreference = "Continue"
$logDir = Join-Path $env:ProgramData "OnionProcOparetor\Agent\logs"
if (-not (Test-Path $logDir)) {
    New-Item -ItemType Directory -Path $logDir -Force | Out-Null
}
$logPath = Join-Path $logDir "tailscale-install.log"

function Write-Log([string]$Message) {
    "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') $Message" | Out-File -FilePath $logPath -Append -Encoding utf8
}

Write-Log "==== install-tailscale-deferred.ps1 starting (รันจาก Scheduled Task) ===="
Write-Log "TailscaleMsiPath=$TailscaleMsiPath"

if (-not (Test-Path $TailscaleMsiPath)) {
    Write-Log "SKIP - หาไฟล์ Tailscale MSI ไม่เจอที่ $TailscaleMsiPath"
}
else {
    # TS_UNATTENDEDMODE/TS_NOLAUNCH เป็น MSI property จริงของ Tailscale (verify จาก
    # https://tailscale.com/docs/install/windows/msi แล้ว ไม่ใช่เดา) - AlwaysOn.Enabled ไม่ใช่ MSI
    # property (เป็น registry policy) เลยไม่ได้ตั้งตรงนี้ - ตั้งทีหลังผ่าน SetTailscaleLockdown command
    $msiArgs = @(
        "/i", "`"$TailscaleMsiPath`"",
        "/quiet", "/norestart",
        "TS_UNATTENDEDMODE=always",
        "TS_NOLAUNCH=1",
        "/L*V", "`"$logDir\tailscale-msi.log`""
    )

    Write-Log "กำลังรัน: msiexec.exe $($msiArgs -join ' ')"
    $process = Start-Process -FilePath "msiexec.exe" -ArgumentList $msiArgs -Wait -PassThru -NoNewWindow
    Write-Log "msiexec.exe exit code: $($process.ExitCode)"

    if ($process.ExitCode -eq 0) {
        Write-Log "ติดตั้ง Tailscale สำเร็จ"
    }
    else {
        Write-Log "ติดตั้ง Tailscale ไม่สำเร็จ (exit code $($process.ExitCode)) - เช็ค $logDir\tailscale-msi.log สำหรับรายละเอียด"
    }
}

# ลบ Scheduled Task ตัวเองทิ้งหลังรันเสร็จ (one-time task ไม่ต้องค้างไว้ - รันซ้ำได้จาก
# SetTailscaleLockdown/GetTailscaleStatus command ทีหลังถ้าจำเป็น ไม่ต้องพึ่ง task นี้อีก)
Write-Log "ลบ Scheduled Task ตัวเอง..."
& schtasks.exe /delete /tn "OnionProcOparetor-InstallTailscale" /f 2>&1 | ForEach-Object { Write-Log "schtasks delete: $_" }

Write-Log "==== install-tailscale-deferred.ps1 done ===="
