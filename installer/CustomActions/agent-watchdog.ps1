<#
================================================================================
 agent-watchdog.ps1 - Tamper watchdog ของ OnionAgent
================================================================================

โจทย์ (2026-08-31): ถ้า Agent ถูก "ลบ/ปิด/ทำให้ตาย" ด้วยวิธีที่ไม่ได้ตั้งใจให้ใช้
(ไม่ผ่าน UninstallHelper.exe หรือ msiexec /x ที่มีรหัสผ่านถูกต้อง) ให้ reboot เครื่อง
ทันที - เพราะเครื่อง lab มี Reboot Restore Rx อยู่แล้ว การ reboot = ย้อนสถานะกลับ
สมบูรณ์ ซึ่งเป็นการ "ซ่อมตัวเอง" ที่แรงและง่ายที่สุดที่มีอยู่ในสภาพแวดล้อมนี้

ทำไมต้องเป็นสคริปต์แยก ไม่ใช่โค้ดใน Agent เอง:
  ตัว Agent เองตรวจ "ตัวเองถูกลบ" ไม่ได้ (โดนลบไปแล้วก็ไม่มีใครรัน) จึงต้องมี process
  อิสระที่ไม่ได้อยู่ใน lifetime เดียวกัน - เลือก Scheduled Task (SYSTEM) แทน service ตัวที่ 2
  เพราะ (ก) ไม่โผล่ใน services.msc ให้เห็นเป็นเป้าถัดไป (ข) Task Scheduler มี trigger
  "AtStartup" ในตัว ไม่ต้องเขียน retry logic เอง (ค) Agent สร้าง task นี้กลับมาใหม่ได้เอง
  ทุกครั้งที่ poll (ดู TamperProtectionService.cs) = ต้องลบทั้ง service และ task ให้เสร็จ
  ภายในหน้าต่างเวลาสั้นๆ พร้อมกันถึงจะรอด ซึ่งเป็นการยกระดับ "ความยาก" ที่ต้องการ

ขอบเขตที่ทำได้จริง (พูดตรงๆ ไม่ขายเกินจริง):
  ผู้ใช้ที่มีสิทธิ์ admin จริงบนเครื่องลบได้ทั้ง service + task + ไฟล์นี้พร้อมกันได้เสมอ
  สคริปต์นี้ไม่ได้ตั้งเป้ากัน admin (ต้องใช้ kernel driver ถึงจะทำได้จริง) แต่ตั้งเป้า
  (1) กันนักเรียนสิทธิ์ปกติ 100% (2) ทำให้ admin ที่ทำครึ่งๆ กลางๆ โดน reboot ทันที
  (3) เหลือหลักฐานใน Event Log + แจ้ง Server ก่อน reboot เสมอ

================================================================================
#>

[CmdletBinding()]
param(
    # ใช้ตอน debug ด้วยมือ: ประเมินและ log ตามปกติ แต่ไม่ reboot จริง
    [switch]$WhatIfReboot
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# ---------------------------------------------------------------------------
# ค่าคงที่ / path
# ---------------------------------------------------------------------------
$DataDir        = Join-Path $env:ProgramData 'OnionProcOparetor\Agent'
$LogPath        = Join-Path $DataDir 'watchdog.log'
$StatePath      = Join-Path $DataDir 'watchdog-state.json'
$HeartbeatPath  = Join-Path $DataDir 'heartbeat.json'
$SecretPath     = Join-Path $DataDir 'wd.key'
$TicketPath     = Join-Path $DataDir 'maintenance.ticket'
$DecommPath     = Join-Path $DataDir 'decommissioned.flag'
$ConfigPath     = Join-Path $DataDir 'watchdog-config.json'

$ServiceName    = 'OnionAgent'
$TaskName       = 'OnionProcOparetor Agent Watchdog'
$EventSource    = 'OnionProcOparetorWatchdog'

# ค่า default - override ได้ผ่าน watchdog-config.json (เขียนโดย install-watchdog.ps1)
$Config = [ordered]@{
    # ไม่ประเมินอะไรเลยถ้าเครื่องเพิ่งบูตไม่ถึงเท่านี้ - service ต้องมีเวลาสตาร์ท ไม่งั้น
    # ทุกครั้งที่บูตจะเจอ "service ยังไม่ RUNNING" แล้ว reboot ซ้ำเป็น loop
    MinUptimeSeconds          = 180

    # heartbeat เก่ากว่านี้ = ถือว่า Agent ตาย/ถูก freeze (Agent เขียนทุก 30 วิ)
    HeartbeatStaleSeconds     = 240

    # SOFT condition ต้องเจอติดกันกี่รอบถึงจะ reboot (รอบละ ~1 นาทีตาม trigger ของ task)
    SoftHitsBeforeReboot      = 2

    # ---- กัน reboot loop บนเครื่องที่มี disk-freeze (Reboot Restore Rx) ----
    # เดิมใช้ตัวนับ "reboot ได้ไม่เกิน 3 ครั้งต่อ 24 ชม." ที่เก็บใน watchdog-state.json ซึ่ง
    # "ใช้ไม่ได้เลย" บนเครื่อง lab: ไฟล์นั้นถูกเขียนตอน runtime = อยู่นอก baseline = หายทุก reboot
    # ตัวนับจึงกลับเป็น 0 ทุกครั้งที่บูต และไม่มีวันแตะเพดาน (ดู REBOOT-LOOP ในหัวข้อ MAIN)
    #
    # แทนด้วยเงื่อนไข "ต้องเคยเห็น Agent ปกติต่อเนื่องอย่างน้อยเท่านี้ในรอบบูตนี้ ถึงจะ reboot ได้"
    # ซึ่งวัดจากข้อมูลภายในรอบบูตเดียว ไม่ต้องพึ่งอะไรที่ข้าม reboot ได้เลย
    MinHealthySecondsBeforeArming = 120

    # timeout ต่อการยิง 1 ครั้งไปยัง Server (วินาที)
    ReportTimeoutSeconds      = 4

    # หน่วงก่อน reboot จริง - ตั้งไว้ยาวพอให้ใช้ช่วงนับถอยหลังนี้ยิง tamper report ซ้ำได้หลายรอบ
    # เครื่อง lab ถูก restore ทุกครั้งที่เปิดเครื่อง ทั้ง watchdog.log และ Event Log จึงถูกล้าง
    # ไปพร้อมกับ reboot ที่บรรทัดนี้สั่ง = รายงานที่ถึง Server คือหลักฐานชิ้นเดียวที่เหลืออยู่จริง
    # จึงคุ้มที่จะยอมช้าอีกไม่กี่วินาทีเพื่อเพิ่มโอกาสให้มันไปถึง (เครื่องโดน tamper ไปแล้ว
    # การช้าอีก 15 วิไม่ได้ทำให้อะไรแย่ลง แต่การไม่มีหลักฐานเลยทำให้ครูตามเรื่องต่อไม่ได้)
    # ผลพลอยได้: ผู้ใช้ที่นั่งอยู่หน้าเครื่องเห็นกล่องเตือนนับถอยหลังของ Windows นานพอจะอ่านทัน
    RebootDelaySeconds        = 20

    # ยิง report ซ้ำได้ไม่เกินกี่วินาทีรวม (ต้องน้อยกว่า RebootDelaySeconds เสมอ)
    ReportRetryBudgetSeconds  = 15
}

# ---------------------------------------------------------------------------
# Logging - เขียนไฟล์เสมอ + Windows Event Log แบบ best-effort
# ---------------------------------------------------------------------------
function Write-WdLog {
    param([string]$Message, [ValidateSet('INFO','WARN','ALERT')][string]$Level = 'INFO')
    $line = "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') [$Level] $Message"
    try {
        if (-not (Test-Path $DataDir)) { New-Item -ItemType Directory -Path $DataDir -Force | Out-Null }
        Add-Content -Path $LogPath -Value $line -Encoding utf8
    } catch { }
}

function Write-WdEvent {
    param([string]$Message, [int]$EventId = 9001, [string]$EntryType = 'Warning')
    # Event Log = หลักฐานที่นักเรียนลบยากกว่าไฟล์ log ธรรมดา - แต่ใช้ได้เฉพาะ "ภายในรอบบูตนั้น"
    # เท่านั้น (แก้ comment เดิมที่เคลมเกินจริงว่าดูย้อนหลังได้หลัง restore, 2026-08-31):
    # Application.evtx อยู่ที่ C:\Windows\System32\winevt\Logs ซึ่งอยู่บนไดรฟ์ที่ Reboot Restore Rx
    # freeze ไว้เหมือนกัน จึงถูกล้างไปพร้อมกับ reboot ที่บรรทัดนี้กำลังจะสั่ง
    # หลักฐานที่คงอยู่จริงคือ Send-TamperReport ที่ยิงไป Server (ไม่ถูก freeze) ก่อน reboot เท่านั้น
    try {
        if (-not [System.Diagnostics.EventLog]::SourceExists($EventSource)) {
            New-EventLog -LogName Application -Source $EventSource
        }
        Write-EventLog -LogName Application -Source $EventSource -EventId $EventId `
            -EntryType $EntryType -Message $Message
    } catch { }
}

# ---------------------------------------------------------------------------
# Config override
# ---------------------------------------------------------------------------
try {
    if (Test-Path $ConfigPath) {
        $userConfig = Get-Content -Raw -Encoding utf8 -Path $ConfigPath | ConvertFrom-Json
        foreach ($key in @($Config.Keys)) {
            $prop = $userConfig.PSObject.Properties[$key]
            if ($null -ne $prop -and $null -ne $prop.Value) { $Config[$key] = [int]$prop.Value }
        }
    }
} catch {
    Write-WdLog "อ่าน watchdog-config.json ไม่สำเร็จ ใช้ค่า default แทน: $($_.Exception.Message)" 'WARN'
}

# ---------------------------------------------------------------------------
# HMAC helper - ใช้ verify maintenance ticket / decommission marker
# ต้องตรงกับฝั่ง C# (MaintenanceTicket.cs) เป๊ะๆ: HMACSHA256 ของ
# "issuedAtUtc|expiresAtUtc|reason|machine|nonce" (UTF-8) แล้ว hex ตัวเล็ก
# ---------------------------------------------------------------------------
function Get-WdSecret {
    if (-not (Test-Path $SecretPath)) { return $null }
    try { return (Get-Content -Raw -Encoding ascii -Path $SecretPath).Trim() } catch { return $null }
}

function Get-WdHmac {
    param([string]$Payload, [string]$Base64Key)
    $keyBytes = [Convert]::FromBase64String($Base64Key)
    $hmac = New-Object System.Security.Cryptography.HMACSHA256 (,$keyBytes)
    try {
        $bytes = $hmac.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($Payload))
        return ([System.BitConverter]::ToString($bytes)).Replace('-','').ToLowerInvariant()
    } finally { $hmac.Dispose() }
}

function Test-SignedDocument {
    <#
      คืน $true เมื่อไฟล์ JSON ที่ path นี้มีอยู่จริง, ลายเซ็นถูกต้อง, และยังไม่หมดอายุ
      เจตนา: ต้อง "พิสูจน์ได้ว่าถูกออกโดยคนที่ถือ secret" เท่านั้นถึงจะยกเว้นการ reboot
      ไม่ใช่แค่ "มีไฟล์นี้อยู่" (ไม่งั้นนักเรียนสร้างไฟล์เปล่าชื่อนี้ก็ปิด watchdog ได้เลย)
    #>
    param([string]$Path, [string]$ExpectedReasonPrefix = $null)

    if (-not (Test-Path $Path)) { return $false }
    $secret = Get-WdSecret
    if ([string]::IsNullOrWhiteSpace($secret)) {
        # ไม่มี key = verify ไม่ได้ = ต้องถือว่าไม่ผ่าน (fail-secure) ไม่ใช่ปล่อยผ่าน
        Write-WdLog "พบ $Path แต่ไม่มี wd.key ให้ verify - ถือว่าไม่ถูกต้อง (fail-secure)" 'WARN'
        return $false
    }
    try {
        $doc = Get-Content -Raw -Encoding utf8 -Path $Path | ConvertFrom-Json
        $payload = "$($doc.issuedAtUtc)|$($doc.expiresAtUtc)|$($doc.reason)|$($doc.machine)|$($doc.nonce)"
        $expected = Get-WdHmac -Payload $payload -Base64Key $secret
        if ($expected -ne ([string]$doc.hmac).ToLowerInvariant()) {
            Write-WdLog "ลายเซ็นของ $Path ไม่ถูกต้อง - อาจถูกปลอม" 'ALERT'
            return $false
        }
        if ($doc.machine -ne $env:COMPUTERNAME) {
            Write-WdLog "$Path ออกให้เครื่อง '$($doc.machine)' ไม่ใช่ '$env:COMPUTERNAME' - ไม่รับ" 'ALERT'
            return $false
        }
        if ($ExpectedReasonPrefix -and -not ([string]$doc.reason).StartsWith($ExpectedReasonPrefix)) {
            return $false
        }
        $styles = [System.Globalization.DateTimeStyles]::AdjustToUniversal -bor [System.Globalization.DateTimeStyles]::AssumeUniversal
        $expires = [DateTime]::Parse($doc.expiresAtUtc, [System.Globalization.CultureInfo]::InvariantCulture, $styles)
        if ([DateTime]::UtcNow -gt $expires) {
            Write-WdLog "$Path หมดอายุแล้ว (expires $($doc.expiresAtUtc))" 'INFO'
            return $false
        }
        return $true
    } catch {
        Write-WdLog "อ่าน/verify $Path ไม่สำเร็จ: $($_.Exception.Message)" 'WARN'
        return $false
    }
}

# ---------------------------------------------------------------------------
# State (นับ soft hit ติดกัน + ประวัติ reboot สำหรับเพดานกัน loop)
# ---------------------------------------------------------------------------
function New-WdSessionState([string]$BootId) {
    return [PSCustomObject]@{
        bootId        = $BootId
        firstHealthy  = $null   # ครั้งแรกในรอบบูตนี้ที่เห็นว่า Agent ปกติ
        lastHealthy   = $null   # ครั้งล่าสุดที่เห็นว่าปกติ
        softHits      = 0
    }
}

<#
  state ทั้งหมดเป็น "ต่อรอบบูต" เท่านั้น - ผูกกับ bootId (เวลาบูตล่าสุดของ OS)
  ถ้า bootId ไม่ตรง แปลว่าเป็นคนละรอบบูต ให้เริ่มนับใหม่หมด

  จงใจไม่มีอะไรที่ต้อง "ข้าม reboot" ให้ได้เลยสักอย่าง เพราะบนเครื่อง lab ที่มี disk-freeze
  ไม่มีไฟล์ไหนข้าม reboot ได้จริง (ยกเว้นของที่อยู่ใน baseline ตั้งแต่ตอนติดตั้ง) - การออกแบบ
  ให้ logic ขึ้นกับ state ข้าม reboot จึงเป็นบั๊กที่มองไม่เห็นบนเครื่อง dev แต่พังเงียบๆ ในห้องจริง
#>
function Read-WdState([string]$BootId) {
    if (-not (Test-Path $StatePath)) { return (New-WdSessionState $BootId) }
    try {
        $s = Get-Content -Raw -Encoding utf8 -Path $StatePath | ConvertFrom-Json
        if ($s.bootId -ne $BootId) {
            Write-WdLog "พบ state ของรอบบูตก่อน (bootId ไม่ตรง) - เริ่มนับใหม่สำหรับรอบบูตนี้"
            return (New-WdSessionState $BootId)
        }
        foreach ($prop in @('firstHealthy','lastHealthy','softHits')) {
            if ($null -eq $s.PSObject.Properties[$prop]) {
                $s | Add-Member -NotePropertyName $prop -NotePropertyValue $(if ($prop -eq 'softHits') { 0 } else { $null }) -Force
            }
        }
        return $s
    } catch { return (New-WdSessionState $BootId) }
}

function Write-WdState {
    param($State)
    try {
        if (-not (Test-Path $DataDir)) { New-Item -ItemType Directory -Path $DataDir -Force | Out-Null }
        ($State | ConvertTo-Json -Depth 5 -Compress) | Set-Content -Path $StatePath -Encoding utf8 -NoNewline
    } catch {
        Write-WdLog "เขียน watchdog-state.json ไม่สำเร็จ: $($_.Exception.Message)" 'WARN'
    }
}

function ConvertFrom-Iso8601Utc {
    param([string]$Value)
    $styles = [System.Globalization.DateTimeStyles]::AdjustToUniversal -bor [System.Globalization.DateTimeStyles]::AssumeUniversal
    return [DateTime]::Parse($Value, [System.Globalization.CultureInfo]::InvariantCulture, $styles)
}

# ---------------------------------------------------------------------------
# ตรวจสภาพจริง
# ---------------------------------------------------------------------------
function Get-InstallFolder {
    # อ่านจาก registry ที่ Agent เขียนไว้ (TamperProtectionService) - ถ้าไม่มีค่อยเดาจาก path
    # มาตรฐาน เพื่อไม่ให้ hardcode path เดียวแล้วพังเมื่อครูเลือก INSTALLFOLDER อื่นตอนติดตั้ง
    try {
        $v = (Get-ItemProperty -Path 'HKLM:\SOFTWARE\OnionProcOparetor' -Name 'InstallFolder' -ErrorAction Stop).InstallFolder
        if (-not [string]::IsNullOrWhiteSpace($v)) { return $v }
    } catch { }
    return (Join-Path ${env:ProgramFiles} 'Onion ProcOparetor Agent')
}

function Get-TamperFindings {
    <#
      คืน array ของ finding: @{ Severity='HARD'|'SOFT'; Code='...'; Detail='...' }
        HARD = ไม่มีสาเหตุปกติที่อธิบายได้เลยนอกจากถูกลบด้วยมือ -> reboot ทันทีรอบเดียว
        SOFT = อาจเกิดจากเหตุชั่วคราวได้ (service กำลัง restart, ดิสก์ช้า) -> ต้องเจอติดกัน
               SoftHitsBeforeReboot รอบก่อนถึงจะ reboot
      การแยก 2 ระดับนี้จงใจ: reboot กลางคาบเรียนเพราะ false-positive แพงกว่าการรอเพิ่มอีก 1 นาที
    #>
    $findings = @()

    # --- 1) service ยังถูกลงทะเบียนใน SCM อยู่ไหม ---
    $svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    if ($null -eq $svc) {
        $findings += @{ Severity='HARD'; Code='SERVICE_MISSING'
                        Detail="ไม่พบ service '$ServiceName' ใน Service Control Manager" }
    }
    else {
        # --- 2) ถูกตั้งเป็น Disabled/Manual ไหม (sc config OnionAgent start= disabled) ---
        try {
            $startMode = (Get-CimInstance -ClassName Win32_Service -Filter "Name='$ServiceName'" -ErrorAction Stop).StartMode
            if ($startMode -ne 'Auto') {
                $findings += @{ Severity='HARD'; Code='SERVICE_STARTMODE_CHANGED'
                                Detail="StartMode ของ service ถูกเปลี่ยนเป็น '$startMode' (ต้องเป็น Auto)" }
            }
        } catch {
            Write-WdLog "อ่าน StartMode ไม่สำเร็จ: $($_.Exception.Message)" 'WARN'
        }

        # --- 3) กำลังรันอยู่จริงไหม ---
        if ($svc.Status -ne 'Running') {
            $findings += @{ Severity='SOFT'; Code='SERVICE_NOT_RUNNING'
                            Detail="service อยู่ในสถานะ '$($svc.Status)' ไม่ใช่ Running" }
        }
    }

    # --- 4) ไฟล์ .exe ยังอยู่ไหม ---
    $agentExe = Join-Path (Get-InstallFolder) 'Client\OnionProcOparetor.Agent.exe'
    if (-not (Test-Path $agentExe)) {
        $findings += @{ Severity='HARD'; Code='AGENT_BINARY_MISSING'
                        Detail="ไม่พบไฟล์ '$agentExe'" }
    }

    # --- 5) heartbeat สดอยู่ไหม (จับกรณี process ยังอยู่แต่ถูก suspend/แขวน) ---
    #     เช็คเฉพาะเมื่อ service ยังรายงานว่า Running - ถ้ามันหยุดไปแล้ว ข้อ 3 รายงานไปแล้ว
    #     ไม่ต้องนับซ้ำเป็น 2 finding จากเหตุเดียวกัน
    if ($null -ne $svc -and $svc.Status -eq 'Running') {
        if (-not (Test-Path $HeartbeatPath)) {
            $findings += @{ Severity='SOFT'; Code='HEARTBEAT_MISSING'
                            Detail="service รันอยู่แต่ไม่มีไฟล์ heartbeat.json" }
        }
        else {
            try {
                $hb = Get-Content -Raw -Encoding utf8 -Path $HeartbeatPath | ConvertFrom-Json
                $age = ([DateTime]::UtcNow - (ConvertFrom-Iso8601Utc $hb.utc)).TotalSeconds
                if ($age -gt $Config.HeartbeatStaleSeconds) {
                    $findings += @{ Severity='SOFT'; Code='HEARTBEAT_STALE'
                                    Detail=("heartbeat เก่า {0:N0} วินาที (เพดาน {1})" -f $age, $Config.HeartbeatStaleSeconds) }
                }
            } catch {
                $findings += @{ Severity='SOFT'; Code='HEARTBEAT_UNREADABLE'
                                Detail="อ่าน heartbeat.json ไม่ได้: $($_.Exception.Message)" }
            }
        }
    }

    return ,$findings
}

# ---------------------------------------------------------------------------
# แจ้ง Server ก่อน reboot (best-effort) - ให้ครูเห็นที่ Console ว่าเครื่องไหน
# reboot เพราะอะไร แทนที่จะเห็นแค่ "เครื่องหลุดไปเฉยๆ"
# ---------------------------------------------------------------------------
function Send-TamperReport {
    <# คืน $true เมื่อส่งถึง Server สำเร็จ - ผู้เรียกใช้ค่านี้ตัดสินใจว่าจะยิงซ้ำไหม #>
    param([string]$Reason, [string]$Detail)
    try {
        $appSettings = Join-Path (Get-InstallFolder) 'Client\appsettings.json'
        if (-not (Test-Path $appSettings)) { return $false }
        $cfg = Get-Content -Raw -Encoding utf8 -Path $appSettings | ConvertFrom-Json
        $urls = @()
        if ($cfg.ServerSettings.BaseUrl)         { $urls += $cfg.ServerSettings.BaseUrl }
        if ($cfg.ServerSettings.FallbackBaseUrl) { $urls += $cfg.ServerSettings.FallbackBaseUrl }

        $guid = ''
        try { $guid = (Get-ItemProperty -Path 'HKLM:\SOFTWARE\OnionProcOparetor' -Name 'ClientGuid' -ErrorAction Stop).ClientGuid } catch { }

        $body = @{
            clientGuid    = $guid
            machineName   = $env:COMPUTERNAME
            reasonCode    = $Reason
            detail        = $Detail
            detectedAtUtc = [DateTime]::UtcNow.ToString('o')
        } | ConvertTo-Json -Compress

        foreach ($u in $urls) {
            if ([string]::IsNullOrWhiteSpace($u)) { continue }
            try {
                Invoke-RestMethod -Method Post -Uri "$($u.TrimEnd('/'))/api/tamper-report" `
                    -Body $body -ContentType 'application/json' `
                    -TimeoutSec $Config.ReportTimeoutSeconds | Out-Null
                Write-WdLog "ส่ง tamper report ไปที่ $u สำเร็จ"
                return $true
            } catch {
                Write-WdLog "ส่ง tamper report ไปที่ $u ไม่สำเร็จ: $($_.Exception.Message)" 'WARN'
            }
        }
    } catch {
        Write-WdLog "เตรียม tamper report ไม่สำเร็จ: $($_.Exception.Message)" 'WARN'
    }

    return $false
}

# ---------------------------------------------------------------------------
# พยายามซ่อมเองแทนการ reboot - ใช้เฉพาะเส้นทางที่ตัดสินแล้วว่า "reboot ไปก็ไม่ช่วย"
# (baseline พัง / ถูกฆ่าซ้ำทุกบูต) การลอง start service ตรงๆ ราคาถูกและบางครั้งได้ผลจริง
# เช่นกรณี service ล้มตอนบูตเพราะเครือข่าย/ดิสก์ยังไม่พร้อม แล้ว recovery action ของ SCM
# ใช้โควตาหมดไปแล้ว
#
# จงใจไม่เรียกในเส้นทางปกติที่จะ reboot: โจทย์ที่ตั้งไว้คือ "ถูกลบผิดวิธี = restart เครื่องทันที"
# ไม่ใช่ "พยายามแอบสตาร์ทกลับมาเงียบๆ" ซึ่งจะทำให้นักเรียนลองใหม่ได้เรื่อยๆ โดยไม่มีผลอะไร
# ---------------------------------------------------------------------------
function Try-SelfHeal {
    try {
        $svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
        if ($null -eq $svc) {
            Write-WdLog "ไม่มี service ให้สตาร์ท - ข้ามการซ่อมเอง (ต้องติดตั้งใหม่/แก้ baseline)"
            return
        }

        if ($svc.StartMode -ne 'Auto') {
            & sc.exe config $ServiceName start= auto | Out-Null
            Write-WdLog "ตั้ง StartMode กลับเป็น Auto แล้ว"
        }

        if ($svc.Status -ne 'Running') {
            Write-WdLog "กำลังลองสตาร์ท service เอง (แทนการ reboot ที่จะวนไม่จบ)"
            & sc.exe start $ServiceName | Out-Null
        }
    } catch {
        Write-WdLog "ซ่อมเองไม่สำเร็จ: $($_.Exception.Message)" 'WARN'
    }
}

# ---------------------------------------------------------------------------
# MAIN
# ---------------------------------------------------------------------------
try {
    if (-not (Test-Path $DataDir)) { New-Item -ItemType Directory -Path $DataDir -Force | Out-Null }

    # (ก) ถูก uninstall อย่างถูกต้องไปแล้ว -> ถอน task ตัวเองทิ้งแล้วจบ ห้าม reboot
    if (Test-SignedDocument -Path $DecommPath) {
        Write-WdLog "พบ decommission marker ที่ลายเซ็นถูกต้อง - ถอน watchdog task ตัวเองแล้วจบ"
        try { Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false -ErrorAction Stop } catch { }
        return
    }

    # bootId = เวลาบูตล่าสุดของ OS - ใช้ผูก state ให้อยู่ในรอบบูตเดียวเท่านั้น
    $lastBoot = (Get-CimInstance Win32_OperatingSystem).LastBootUpTime
    $bootId = $lastBoot.ToUniversalTime().ToString('o')
    $uptime = ([DateTime]::Now - $lastBoot).TotalSeconds

    # (ข) กำลังอยู่ในช่วง maintenance ที่ได้รับอนุญาต (install/upgrade/uninstall ที่ถูกต้อง)
    if (Test-SignedDocument -Path $TicketPath) {
        Write-WdLog "อยู่ในช่วง maintenance ticket ที่ยังไม่หมดอายุ - ข้ามการตรวจรอบนี้"
        $st = Read-WdState $bootId; $st.softHits = 0; Write-WdState $st
        return
    }

    # (ค) เครื่องเพิ่งบูต - ให้เวลา service สตาร์ทก่อน
    if ($uptime -lt $Config.MinUptimeSeconds) {
        Write-WdLog ("เครื่องเพิ่งบูตมา {0:N0} วินาที (< {1}) - ข้ามการตรวจรอบนี้" -f $uptime, $Config.MinUptimeSeconds)
        return
    }

    $state = Read-WdState $bootId
    $findings = Get-TamperFindings
    $now = [DateTime]::UtcNow

    # ---- ปกติดี: บันทึกว่า "เคยเห็น Agent ทำงานอยู่จริงในรอบบูตนี้" ----
    if ($findings.Count -eq 0) {
        if ($null -eq $state.firstHealthy) {
            $state.firstHealthy = $now.ToString('o')
            Write-WdLog "เห็น Agent ทำงานปกติครั้งแรกของรอบบูตนี้ - เริ่มนับเวลาเพื่อ arm การ reboot"
        }
        $state.lastHealthy = $now.ToString('o')
        if ($state.softHits -ne 0) {
            Write-WdLog "ทุกอย่างกลับมาปกติ - reset ตัวนับ soft hit จาก $($state.softHits) เป็น 0"
            $state.softHits = 0
        }
        Write-WdState $state
        return
    }

    $hard = @($findings | Where-Object { $_.Severity -eq 'HARD' })
    $summary = ($findings | ForEach-Object { "$($_.Code): $($_.Detail)" }) -join ' | '

    $shouldReboot = $false
    if ($hard.Count -gt 0) {
        Write-WdLog "พบความผิดปกติระดับ HARD - $summary" 'ALERT'
        $shouldReboot = $true
    }
    else {
        $state.softHits = [int]$state.softHits + 1
        Write-WdLog "พบความผิดปกติระดับ SOFT (ครั้งที่ $($state.softHits)/$($Config.SoftHitsBeforeReboot)) - $summary" 'WARN'
        if ($state.softHits -ge $Config.SoftHitsBeforeReboot) { $shouldReboot = $true }
    }
    Write-WdState $state

    if (-not $shouldReboot) { return }

    # =========================================================================
    # REBOOT-LOOP: กันวนบูตไม่จบ บนเครื่องที่มี disk-freeze
    # =========================================================================
    # เครื่อง lab ทุกเครื่องมี Reboot Restore Rx = ทุกอย่างที่เขียนลงดิสก์หลัง baseline หายทุก reboot
    # ตัวนับ "reboot ได้ไม่เกิน N ครั้งต่อ 24 ชม." แบบเดิมจึงไร้ผลโดยสิ้นเชิง (ไฟล์ที่เก็บตัวนับหาย
    # ไปพร้อมกับ reboot ที่มันควรจะนับ) - ต้องตัดสินจากข้อมูลภายในรอบบูตเดียวเท่านั้น
    #
    # ใช้หลักว่า "การ reboot จะช่วยได้ก็ต่อเมื่อ baseline ที่จะถูก restore กลับมานั้นดีอยู่" ซึ่ง
    # พิสูจน์ได้ทางเดียวคือ เราเคยเห็นมันดีมาแล้วในรอบบูตนี้:
    #
    #   ไม่เคยเห็นปกติเลยตั้งแต่บูต -> baseline เองมีปัญหา (เช่น commit ตอน service พังอยู่)
    #                                  reboot ก็ได้ของพังกลับมาเหมือนเดิม = วนไม่จบ -> ห้าม reboot
    #   เคยปกติแต่สั้นเกินไป        -> มีอะไรฆ่า Agent ทุกครั้งที่บูต (เช่น startup script ของนักเรียน)
    #                                  reboot ก็เจอเหมือนเดิม = วนไม่จบ -> ห้าม reboot
    #   เคยปกติต่อเนื่องนานพอ       -> เพิ่งมีคนมายุ่งระหว่างใช้งาน = reboot แล้วหายจริง -> reboot
    #
    # เกณฑ์นี้แยก "นักเรียนหยุด service กลางคาบ" (ปกติมานาน -> reboot) ออกจาก "เครื่องพังตั้งแต่บูต"
    # (ไม่เคยปกติ -> ไม่ reboot แต่ส่งเสียงดัง) ได้ตรงตามที่ต้องการ โดยไม่ต้องพึ่ง state ข้าม reboot เลย
    $healthySeconds = 0
    if ($null -ne $state.firstHealthy -and $null -ne $state.lastHealthy) {
        $healthySeconds = ((ConvertFrom-Iso8601Utc $state.lastHealthy) - (ConvertFrom-Iso8601Utc $state.firstHealthy)).TotalSeconds
    }

    if ($null -eq $state.firstHealthy) {
        $msg = "ตรวจพบความผิดปกติ ($summary) แต่ไม่เคยเห็น Agent ทำงานปกติเลยตั้งแต่เครื่องบูต - แปลว่าปัญหาอยู่ที่ baseline เอง การ reboot จะได้ของเดิมกลับมาและวนไม่จบ จึงไม่ reboot ต้องให้คนไปแก้ baseline ของเครื่องนี้"
        Write-WdLog $msg 'ALERT'
        Write-WdEvent -Message $msg -EventId 9003 -EntryType 'Error'
        Send-TamperReport -Reason 'BASELINE_UNHEALTHY' -Detail $summary
        Try-SelfHeal
        return
    }

    if ($healthySeconds -lt $Config.MinHealthySecondsBeforeArming) {
        $msg = ("ตรวจพบความผิดปกติ ($summary) แต่ Agent เพิ่งทำงานปกติได้แค่ {0:N0} วินาทีในรอบบูตนี้ (ต้องได้ {1} วินาที) - มีอะไรฆ่ามันทุกครั้งที่บูต การ reboot จะวนไม่จบ จึงไม่ reboot" -f $healthySeconds, $Config.MinHealthySecondsBeforeArming)
        Write-WdLog $msg 'ALERT'
        Write-WdEvent -Message $msg -EventId 9003 -EntryType 'Error'
        Send-TamperReport -Reason 'REBOOT_NOT_ARMED' -Detail $summary
        Try-SelfHeal
        return
    }

    $reasonCode = if ($hard.Count -gt 0) { $hard[0].Code } else { @($findings)[0].Code }
    $msg = ("TAMPER DETECTED - $summary. Agent ทำงานปกติมาแล้ว {0:N0} วินาทีในรอบบูตนี้ก่อนจะผิดปกติ - กำลังจะ restart เครื่องภายใน {1} วินาที" -f $healthySeconds, $Config.RebootDelaySeconds)
    Write-WdLog $msg 'ALERT'
    Write-WdEvent -Message $msg -EventId 9002 -EntryType 'Error'

    $state.softHits = 0
    Write-WdState $state

    # ---- สั่ง reboot "ก่อน" แล้วค่อยใช้ช่วงนับถอยหลังยิงรายงาน ----
    # ลำดับนี้สำคัญ: เดิมยิงรายงานก่อนแล้วค่อยสั่ง reboot ซึ่งแปลว่าถ้าเครือข่ายมีปัญหา การ
    # remediation (สิ่งที่สำคัญที่สุด) ต้องรอ timeout ของสิ่งที่สำคัญรองลงมาให้ครบก่อน
    # สั่ง shutdown ก่อนทำให้ remediation เกิดขึ้นแน่นอนแม้โค้ดรายงานจะพังหรือค้าง แล้วเราได้
    # หน้าต่างเวลาฟรีๆ ระหว่างนับถอยหลังไปใช้ยิงซ้ำ - ไม่ต้องแลกอะไรเลย
    if ($WhatIfReboot) {
        Write-WdLog "(-WhatIfReboot) ข้ามการสั่ง reboot จริง (ยังทดสอบการส่งรายงานต่อ)" 'WARN'
    }
    else {
        # /f = บังคับปิดโปรแกรมที่ค้าง (นักเรียนอาจมีหน้าต่างที่ block shutdown อยู่)
        # /d p:4:1 = Planned / Application / Maintenance - ให้ขึ้นเหตุผลที่อ่านออกใน Event Log
        & shutdown.exe /r /f /t $Config.RebootDelaySeconds /c "OnionProcOparetor: found unauthorized system modification - restarting" /d p:4:1
        Write-WdLog "สั่ง reboot แล้ว (อีก $($Config.RebootDelaySeconds) วินาที) - ระหว่างนี้จะพยายามส่งรายงานให้ถึง Server"
    }

    # ยิงซ้ำจนกว่าจะสำเร็จหรือหมดงบเวลา - เครื่องจะดับตอน RebootDelaySeconds พอดี
    # ไม่ต้องกลัวว่าจะยิงค้างเกินเวลา เพราะแต่ละรอบมี timeout ของตัวเองอยู่แล้ว
    $reportDeadline = [DateTime]::UtcNow.AddSeconds($Config.ReportRetryBudgetSeconds)
    $reported = $false
    $attempt = 0
    while (-not $reported -and [DateTime]::UtcNow -lt $reportDeadline) {
        $attempt++
        $reported = Send-TamperReport -Reason $reasonCode -Detail $summary
        if (-not $reported) {
            Write-WdLog "ส่ง tamper report ไม่สำเร็จ (ครั้งที่ $attempt) - จะลองใหม่จนกว่าจะหมดเวลา" 'WARN'
            Start-Sleep -Seconds 2
        }
    }

    if (-not $reported) {
        # ไม่มีที่ไหนให้บันทึกที่รอด reboot ได้อีกแล้ว - อย่างน้อยเขียนไว้ให้คนที่มาดูทัน
        # ภายในไม่กี่วินาทีที่เหลือ (และเผื่อกรณีที่เครื่องนี้ไม่ได้เปิด freeze อยู่จริง)
        Write-WdLog "ส่ง tamper report ไม่สำเร็จเลยภายใน $($Config.ReportRetryBudgetSeconds) วินาที - เครื่องกำลังจะ reboot และหลักฐานในเครื่องจะถูกล้างทั้งหมด" 'ALERT'
        Write-WdEvent -Message "tamper report ส่งไม่ถึง Server: $summary" -EventId 9005 -EntryType 'Error'
    }
}
catch {
    # watchdog ห้ามพังเงียบๆ และห้าม reboot เพราะตัวเองพัง
    Write-WdLog "watchdog เกิดข้อผิดพลาดที่ไม่คาดคิด (ไม่ reboot): $($_.Exception.Message)" 'ALERT'
    Write-WdEvent -Message "watchdog error: $($_.Exception)" -EventId 9004 -EntryType 'Error'
}
