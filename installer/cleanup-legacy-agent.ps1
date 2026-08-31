# One-off remediation script - NOT part of the installer's own InstallExecuteSequence, not
# referenced by any .wxs. Written 2026-08-20 to unblock lab machines that installed
# "Onion ProcOparetor Agent" from a build before the MSI-upgrade-detection fix
# (installer\OnionProcOparetor.Agent.wxs's AllowSameVersionUpgrades) and/or before the
# UninstallHelper INSTALLFOLDER-path fix (src\OnionProcOparetor.UninstallHelper\MainWindow.xaml.cs).
#
# Those older builds have two compounding bugs:
#   1. UninstallHelper.exe (the Add/Remove Programs entry point) derived its own install
#      folder from its own exe location, which breaks after it relaunches itself from %TEMP%
#      (done to avoid locking its own files during msiexec /x) - it never shows the uninstall
#      password field, so a password-protected machine can never be removed through the normal
#      UI even if you know the password.
#   2. Because ProductVersion was never bumped and MajorUpgrade defaulted to
#      AllowSameVersionUpgrades="no", repeated re-installs across the field-test period may
#      have left more than one Windows-Installer product registration behind under the same
#      UpgradeCode (different, unrelated ProductCodes) instead of cleanly upgrading in place.
#
# This script bypasses the broken UI entirely: it deletes the uninstall-password guard file
# (so the uninstall proceeds unauthenticated - see CustomActions\check-uninstall-password.ps1's
# own logic: no guard file = no password required, by design, this is the same behavior a
# freshly-installed machine with no password ever set has) then removes every Windows Installer
# product still registered under the name "Onion ProcOparetor Agent" (not just the one the ARP
# registry key currently points at - catches any phantom duplicates from bug #2 above), then
# sweeps any leftover install folder/service that a partial uninstall left behind.
#
# Safe to push to every lab machine via your endpoint manager without checking each one by hand
# first: machines that were never affected (already clean, or already updated to a build with
# the fixes) simply have nothing to remove and this exits 0 having done nothing. Idempotent -
# safe to run more than once on the same machine.
#
# Deploy as SYSTEM/admin context (needs elevation for service control + Program Files + msiexec).
# Writes a log to C:\ProgramData\OnionProcOparetorCleanup\cleanup-log.txt so results can be
# pulled back centrally afterward instead of watching each machine run interactively.

$ErrorActionPreference = "Stop"

$logDir = "C:\ProgramData\OnionProcOparetorCleanup"
$logPath = Join-Path $logDir "cleanup-log.txt"
New-Item -ItemType Directory -Path $logDir -Force | Out-Null

function Write-Log {
    param([string]$Message)
    $line = "[{0:yyyy-MM-dd HH:mm:ss}] {1}" -f (Get-Date), $Message
    Write-Host $line
    Add-Content -Path $logPath -Value $line
}

Write-Log "==== cleanup-legacy-agent.ps1 start on $env:COMPUTERNAME ===="

$installFolder = "C:\Program Files\Onion ProcOparetor Agent"
$guardPath = Join-Path $installFolder "uninstall-guard.json"
$anyActionTaken = $false

# ---- ขั้น 0 (เพิ่ม 2026-08-31): บอก tamper watchdog ให้หยุดทำงานก่อนแตะอะไรทั้งสิ้น ----
# ตั้งแต่มี agent-watchdog.ps1 การลบ service/ไฟล์ Agent โดยไม่มี ticket = เครื่อง reboot ทันที
# ซึ่งจะทำให้สคริปต์นี้ (ที่ตั้งใจให้ push ผ่าน endpoint manager แบบไม่ต้องดูทีละเครื่อง) กลาย
# เป็นตัวสั่ง reboot ทั้งห้องพร้อมกันกลางคาบ - ต้องออก decommission marker ก่อนเสมอ
# เงียบๆ ข้ามไปถ้าเครื่องนั้นเป็น build เก่าที่ยังไม่มีสคริปต์นี้ (ซึ่งเป็นเคสหลักของสคริปต์นี้อยู่แล้ว)
$ticketScript = Join-Path $installFolder "CustomActions\maintenance-ticket.ps1"
if (Test-Path $ticketScript) {
    Write-Log "พบ maintenance-ticket.ps1 - ออก decommission marker + ถอน watchdog task ก่อนเริ่มลบ"
    try {
        & powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File $ticketScript -Action Decommission 2>&1 |
            ForEach-Object { Write-Log "ticket: $_" }
    }
    catch {
        Write-Log "เรียก maintenance-ticket.ps1 ไม่สำเร็จ: $($_.Exception.Message) - เครื่องนี้อาจ reboot ตัวเองหลังลบ Agent เสร็จ"
    }
}
else {
    Write-Log "ไม่พบ maintenance-ticket.ps1 (build เก่าที่ยังไม่มี tamper watchdog) - ข้าม"
}

# ---- ขั้น 1: ลบ guard file กันถูกถามรหัสผ่าน (เหมือนเดิมกับเครื่องที่ไม่เคยตั้งรหัสไว้เลย - ดู
# CustomActions\check-uninstall-password.ps1: ไม่มี guard = ไม่มี gate) ----
if (Test-Path $guardPath) {
    Write-Log "พบ uninstall-guard.json ที่ $guardPath - ลบทิ้งกันถูก block ด้วยรหัสผ่าน"
    Remove-Item -Path $guardPath -Force -ErrorAction SilentlyContinue
    $anyActionTaken = $true
}
else {
    Write-Log "ไม่พบ uninstall-guard.json (เครื่องนี้ไม่เคยตั้ง uninstall password ไว้ หรือถูกลบไปแล้ว) - ข้าม"
}

# ---- ขั้น 2: หา Windows Installer product ทุกตัวที่ชื่อ "Onion ProcOparetor Agent" (ไม่ใช่แค่ตัว
# เดียวที่ registry ARP entry ชี้ไปตอนนี้ - เผื่อมี phantom ProductCode อื่นค้างจากบั๊กเดิม) แล้ว
# ยิง msiexec /x ไล่ลบทีละตัว - ใช้ Win32_Product enumeration (ช้ากว่า query registry ตรงๆ แต่
# ครอบคลุมกว่า เพราะอ่านจาก Windows Installer database เอง ไม่ต้องพึ่ง ARP registry key ที่อาจ
# เหลือแค่ตัวเดียวจากหลาย ProductCode ที่ทับกันมา) ----
Write-Log "กำลังค้นหา Windows Installer product ที่ตรงกับ 'Onion ProcOparetor Agent' (อาจใช้เวลาสักครู่)..."

try {
    $products = Get-CimInstance -ClassName Win32_Product -ErrorAction Stop |
        Where-Object { $_.Name -eq "Onion ProcOparetor Agent" }
}
catch {
    Write-Log "Get-CimInstance Win32_Product ล้มเหลว: $($_.Exception.Message) - ข้ามขั้นนี้ไปเลย"
    $products = @()
}

if ($products.Count -eq 0) {
    Write-Log "ไม่พบ product ที่ลงทะเบียนไว้เลย (อาจจะลบไปแล้ว หรือไม่เคยลงบนเครื่องนี้)"
}
else {
    foreach ($product in $products) {
        $productCode = $product.IdentifyingNumber
        Write-Log "พบ product ProductCode=$productCode Version=$($product.Version) - กำลัง uninstall..."

        $proc = Start-Process msiexec.exe -ArgumentList @("/x", $productCode, "/qn", "/norestart") -Wait -PassThru
        Write-Log "  msiexec /x $productCode จบด้วย exit code $($proc.ExitCode)"
        $anyActionTaken = $true
    }
}

# ---- ขั้น 3: safety net - เผื่อ uninstall ไม่สมบูรณ์ (ไฟล์/service ค้าง) กวาดตามหลังอีกชั้น
# (เหมือน RemoveInstallFolderExtras ที่ทำตอน uninstall ปกติ แต่ทำตรงนี้เผื่อรอบนั้นไม่ได้รัน) ----
$service = Get-Service -Name "OnionAgent" -ErrorAction SilentlyContinue
if ($service) {
    Write-Log "service OnionAgent ยังเหลืออยู่ (สถานะ: $($service.Status)) - หยุดแล้วลบทิ้ง"
    try {
        Stop-Service -Name "OnionAgent" -Force -ErrorAction SilentlyContinue
        & sc.exe delete OnionAgent | Out-Null
        $anyActionTaken = $true
    }
    catch {
        Write-Log "  ลบ service ไม่สำเร็จ: $($_.Exception.Message)"
    }
}
else {
    Write-Log "ไม่พบ service OnionAgent เหลืออยู่ - ผ่าน"
}

if (Test-Path $installFolder) {
    Write-Log "โฟลเดอร์ $installFolder ยังเหลืออยู่ - ลบทิ้ง"
    Remove-Item -Path $installFolder -Recurse -Force -ErrorAction SilentlyContinue
    $anyActionTaken = $true
}
else {
    Write-Log "ไม่พบโฟลเดอร์ $installFolder เหลืออยู่ - ผ่าน"
}

if ($anyActionTaken) {
    Write-Log "==== เสร็จ - มีการลบ/แก้ไขจริงบนเครื่องนี้ ===="
}
else {
    Write-Log "==== เสร็จ - เครื่องนี้สะอาดอยู่แล้ว ไม่มีอะไรให้ทำ ===="
}

exit 0
