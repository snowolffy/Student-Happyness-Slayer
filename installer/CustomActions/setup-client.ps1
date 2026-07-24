# รันหลัง OnionAgent's files ถูกคัดลอกเสร็จแล้ว (deferred CA, sequence After="InstallServices" -
# WiX's declarative <ServiceInstall> ใน AgentExeComponent สร้าง service ไปแล้วตอนนั้น, ยังไม่ start)
# ทำหน้าที่เดียวกับ CurStepChanged(ssPostInstall) เดิมใน OnionProcOparetor.iss ที่เหลือ: patch
# BaseUrl ใน appsettings.json ทั้ง 2 ไฟล์, ตั้ง recovery, แล้วค่อย start เป็นลำดับสุดท้าย - ต้องเรียง
# ลำดับนี้เท่านั้น (ห้าม start ก่อน patch config เสร็จ) เหมือน .iss เดิม เพราะเคยเจอ race condition
# จริงตอน deploy มาก่อนแล้ว

param(
    [Parameter(Mandatory = $true)][string]$InstallDir,
    [Parameter(Mandatory = $true)][string]$ServerAddr,
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
    $content = Get-Content -Raw -Path $AppSettingsPath
    $patched = $content -replace '"BaseUrl"\s*:\s*"http://localhost:8787"', ('"BaseUrl": "' + $NewBaseUrl + '"')
    Set-Content -Path $AppSettingsPath -Value $patched -Encoding utf8 -NoNewline
    Write-Log "Patched BaseUrl in: $AppSettingsPath"
}

$newBaseUrl = "http://$ServerAddr"

Write-Log "==== setup-client.ps1 starting ===="
Write-Log "InstallDir=$InstallDir ServerAddr=$ServerAddr"

# 1) patch appsettings.json ของทั้ง Client (Agent Service) และ ClientTray (login window)
#    (service ถูกสร้างไปแล้วโดย WiX's declarative <ServiceInstall> ใน AgentExeComponent -
#    ไม่ต้อง sc.exe create ซ้ำที่นี่ ซ้ำแล้วจะ error "service already exists" เฉยๆ)
Patch-BaseUrl -AppSettingsPath (Join-Path $InstallDir "Client\appsettings.json") -NewBaseUrl $newBaseUrl
Patch-BaseUrl -AppSettingsPath (Join-Path $InstallDir "ClientTray\appsettings.json") -NewBaseUrl $newBaseUrl

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

Write-Log "==== setup-client.ps1 done ===="
exit 0
