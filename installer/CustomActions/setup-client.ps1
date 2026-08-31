# รันหลัง OnionAgent's files ถูกคัดลอกเสร็จแล้ว (deferred CA, sequence After="InstallServices" -
# WiX's declarative <ServiceInstall> ใน AgentExeComponent สร้าง service ไปแล้วตอนนั้น, ยังไม่ start)
# ทำหน้าที่เดียวกับ CurStepChanged(ssPostInstall) เดิมใน OnionProcOparetor.iss ที่เหลือ: patch
# BaseUrl ใน appsettings.json ทั้ง 2 ไฟล์, ตั้ง recovery, แล้วค่อย start เป็นลำดับสุดท้าย - ต้องเรียง
# ลำดับนี้เท่านั้น (ห้าม start ก่อน patch config เสร็จ) เหมือน .iss เดิม เพราะเคยเจอ race condition
# จริงตอน deploy มาก่อนแล้ว

param(
    [Parameter(Mandatory = $true)][string]$InstallDir,
    [Parameter(Mandatory = $true)][string]$ServerAddr,
    [string]$FallbackServerAddr = "",
    [string]$UninstallPassword = ""
)

$ErrorActionPreference = "Continue"
$logPath = Join-Path $InstallDir "install-client-setup.log"

function Write-Log([string]$Message) {
    "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') $Message" | Out-File -FilePath $logPath -Append -Encoding utf8
}

function Patch-BaseUrl([string]$AppSettingsPath, [string]$NewBaseUrl) {
    if (-not (Test-Path $AppSettingsPath)) {
        Write-Log "SKIP patch (file not found): $AppSettingsPath"
        return
    }
    # บั๊กจริงที่เจอ (2026-08-25, ตอนทดสอบ failover installer patch): Get-Content ใน Windows
    # PowerShell 5.1 ไม่ได้ default เป็น UTF-8 - ไฟล์นี้ไม่มี BOM เลยถูกอ่านด้วย system codepage แทน
    # (mojibake) ทำให้ข้อความไทยใน appsettings.json (comment ของ AgentTray section) พังทุกครั้งที่ patch
    # มาตั้งแต่ก่อนหน้านี้แล้ว (ไม่กระทบ functionality เพราะไม่มีโค้ดไหนอ่าน comment นั้นจริง แต่ยังไงก็ควร
    # แก้) - ระบุ -Encoding utf8 ตรงๆ ที่ทั้ง Get-Content และ Set-Content (Set-Content ระบุไว้แล้วเดิม)
    $content = Get-Content -Raw -Encoding utf8 -Path $AppSettingsPath
    $patched = $content -replace '"BaseUrl"\s*:\s*"http://localhost:8787"', ('"BaseUrl": "' + $NewBaseUrl + '"')
    Set-Content -Path $AppSettingsPath -Value $patched -Encoding utf8 -NoNewline
    Write-Log "Patched BaseUrl in: $AppSettingsPath"
}

# แยกจาก Patch-BaseUrl โดยตั้งใจ (ไม่ generalize รวมกัน คนละ key) - เหมือนกันตรงที่ match แค่ placeholder
# ตัวเดียวที่ template appsettings.json ship มา ("FallbackBaseUrl": "" ว่างๆ) ไม่ใช่ generic key replace
# (idempotent-safe แบบเดียวกับ Patch-BaseUrl: reinstall ทับที่ patch ไปแล้วจะไม่ match ซ้ำ เงียบๆ ข้ามไป
# ไม่ throw - พฤติกรรมเดิมที่มีอยู่แล้วของ Patch-BaseUrl เช่นกัน ไม่ใช่ limitation ใหม่ที่เพิ่มเข้ามา)
function Patch-FallbackBaseUrl([string]$AppSettingsPath, [string]$NewFallbackBaseUrl) {
    if (-not (Test-Path $AppSettingsPath)) {
        Write-Log "SKIP fallback patch (file not found): $AppSettingsPath"
        return
    }
    $content = Get-Content -Raw -Encoding utf8 -Path $AppSettingsPath
    $patched = $content -replace '"FallbackBaseUrl"\s*:\s*""', ('"FallbackBaseUrl": "' + $NewFallbackBaseUrl + '"')
    Set-Content -Path $AppSettingsPath -Value $patched -Encoding utf8 -NoNewline
    Write-Log "Patched FallbackBaseUrl in: $AppSettingsPath"
}

$newBaseUrl = "http://$ServerAddr"

# FALLBACKSERVERADDR ที่ WiX ส่งมาอาจเป็น ":8787" (blank IP + default port property) ถ้าครูไม่ได้กรอก
# อะไรไว้เลยที่ ServerConnDlg - ถือว่า "ไม่ได้ตั้งค่า fallback" ถ้า IP-part (ก่อน ':') ว่าง/เว้นวรรค
$fallbackIpPart = ($FallbackServerAddr -split ':', 2)[0]
$fallbackConfigured = -not [string]::IsNullOrWhiteSpace($fallbackIpPart)

Write-Log "==== setup-client.ps1 starting ===="
Write-Log "InstallDir=$InstallDir ServerAddr=$ServerAddr FallbackServerAddr=$FallbackServerAddr (configured: $fallbackConfigured)"

# 1) patch appsettings.json ของทั้ง Client (Agent Service) และ ClientTray (login window)
#    (service ถูกสร้างไปแล้วโดย WiX's declarative <ServiceInstall> ใน AgentExeComponent -
#    ไม่ต้อง sc.exe create ซ้ำที่นี่ ซ้ำแล้วจะ error "service already exists" เฉยๆ)
Patch-BaseUrl -AppSettingsPath (Join-Path $InstallDir "Client\appsettings.json") -NewBaseUrl $newBaseUrl
Patch-BaseUrl -AppSettingsPath (Join-Path $InstallDir "ClientTray\appsettings.json") -NewBaseUrl $newBaseUrl

# FallbackBaseUrl ใส่ทั้ง Client (Agent Service - poll/command channel) และ ClientTray (AgentTray's
# LoginWindow - teacher-login แบบ one-shot request) - เดิมตั้งใจใส่แค่ Client เท่านั้นเพราะคิดว่าคนละ
# scope กัน แต่พบจริงจาก field test (2026-08-25) ว่าครูล็อกอินดู status ไม่ได้เลยตลอดเวลาที่เครื่อง
# fallback อยู่ ทั้งที่ poll/command ใช้งานได้ปกติผ่าน fallback - LoginWindow.xaml.cs/ClientApiClient.cs
# แก้ให้ retry ไป FallbackBaseUrl เองแล้วถ้า primary "ต่อไม่ได้เลย" (ไม่ retry ถ้าแค่ credentials ผิด)
if ($fallbackConfigured) {
    $newFallbackBaseUrl = "http://$FallbackServerAddr"
    Patch-FallbackBaseUrl -AppSettingsPath (Join-Path $InstallDir "Client\appsettings.json") -NewFallbackBaseUrl $newFallbackBaseUrl
    Patch-FallbackBaseUrl -AppSettingsPath (Join-Path $InstallDir "ClientTray\appsettings.json") -NewFallbackBaseUrl $newFallbackBaseUrl
}
else {
    Write-Log "SKIP fallback patch (not configured)"
}

# 2) ตั้งค่า recovery แล้วค่อย start เป็นลำดับสุดท้าย
& sc.exe failure OnionAgent reset= 86400 actions= restart/0/restart/0/restart/0 | ForEach-Object { Write-Log "sc failure: $_" }
& sc.exe start OnionAgent | ForEach-Object { Write-Log "sc start: $_" }

# 3) ถ้ากรอก uninstall password ไว้ (ไม่ได้ skip) - hash แล้วเก็บลง guard file
if ($UninstallPassword.Trim() -ne "") {
    $salt = (Get-Date -Format "yyyyMMddHHmmss") + "-" + (Get-Random -Maximum 2147483647) + "-" + (Get-Random -Maximum 2147483647)
    $sha256 = [System.Security.Cryptography.SHA256]::Create()
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($salt + $UninstallPassword)
    $hashBytes = $sha256.ComputeHash($bytes)
    $hash = [System.BitConverter]::ToString($hashBytes).Replace("-", "").ToLowerInvariant()

    $guard = [PSCustomObject]@{ salt = $salt; hash = $hash }
    $guardPath = Join-Path $InstallDir "uninstall-guard.json"
    ($guard | ConvertTo-Json -Compress) | Set-Content -Path $guardPath -Encoding utf8 -NoNewline
    Write-Log "Uninstall guard written: $guardPath"
}

# 4) ติดตั้ง tamper watchdog (Scheduled Task + wd.key + ACL) - ต้องทำ "หลัง" sc start เพราะ
#    watchdog เริ่มตรวจทันทีที่ลงทะเบียนเสร็จ ถ้าลงก่อน start มันอาจเจอ SERVICE_NOT_RUNNING
#    ตั้งแต่รอบแรก (ซึ่ง maintenance ticket ที่ MSI ออกไว้กันอยู่แล้ว แต่ไม่มีเหตุผลจะเสี่ยง)
$watchdogScript = Join-Path $InstallDir "CustomActions\install-watchdog.ps1"
if (Test-Path $watchdogScript) {
    try {
        & powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File $watchdogScript -InstallDir $InstallDir 2>&1 |
            ForEach-Object { Write-Log "watchdog: $_" }
        Write-Log "install-watchdog.ps1 exit code: $LASTEXITCODE"
    }
    catch {
        # ไม่ hard-fail ทั้ง install เพราะ watchdog ล้มเหลว - Agent เองจะพยายามสร้าง task ซ้ำ
        # ทุก 1 นาทีอยู่แล้ว (TamperProtectionService) ให้โอกาสมันซ่อมเองดีกว่า block การติดตั้ง
        Write-Log "เรียก install-watchdog.ps1 ไม่สำเร็จ: $($_.Exception.Message) - Agent จะพยายามสร้าง task เองภายหลัง"
    }
}
else {
    Write-Log "ไม่พบ $watchdogScript - ข้ามการติดตั้ง watchdog"
}

Write-Log "==== setup-client.ps1 done ===="
exit 0
