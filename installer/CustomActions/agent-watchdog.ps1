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

    # เพดานกัน reboot loop: ห้าม reboot เพราะ tamper เกินกี่ครั้งใน 24 ชม.
    MaxRebootsPer24h          = 3

    # ให้เวลา Server รับ tamper report ก่อน reboot (วินาที)
    ReportTimeoutSeconds      = 4

    # หน่วงก่อน reboot จริง เพื่อให้ log/report เขียนลงดิสก์เสร็จ
    RebootDelaySeconds        = 5
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
    # Event Log = หลักฐานที่นักเรียนลบยากกว่าไฟล์ log ธรรมดา และ admin เปิดดูย้อนหลังได้
    # ผ่าน eventvwr แม้เครื่องจะถูก restore ไปแล้วก็ยังเห็นในช่วงก่อน reboot
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
function Read-WdState {
    $default = [PSCustomObject]@{ softHits = 0; reboots = @() }
    if (-not (Test-Path $StatePath)) { return $default }
    try {
        $s = Get-Content -Raw -Encoding utf8 -Path $StatePath | ConvertFrom-Json
        if ($null -eq $s.softHits) { $s | Add-Member -NotePropertyName softHits -NotePropertyValue 0 -Force }
        if ($null -eq $s.reboots)  { $s | Add-Member -NotePropertyName reboots  -NotePropertyValue @() -Force }
        return $s
    } catch { return $default }
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
    param([string]$Reason, [string]$Detail)
    try {
        $appSettings = Join-Path (Get-InstallFolder) 'Client\appsettings.json'
        if (-not (Test-Path $appSettings)) { return }
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
                return
            } catch {
                Write-WdLog "ส่ง tamper report ไปที่ $u ไม่สำเร็จ: $($_.Exception.Message)" 'WARN'
            }
        }
    } catch {
        Write-WdLog "เตรียม tamper report ไม่สำเร็จ: $($_.Exception.Message)" 'WARN'
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

    # (ข) กำลังอยู่ในช่วง maintenance ที่ได้รับอนุญาต (install/upgrade/uninstall ที่ถูกต้อง)
    if (Test-SignedDocument -Path $TicketPath) {
        Write-WdLog "อยู่ในช่วง maintenance ticket ที่ยังไม่หมดอายุ - ข้ามการตรวจรอบนี้"
        $st = Read-WdState; $st.softHits = 0; Write-WdState $st
        return
    }

    # (ค) เครื่องเพิ่งบูต - ให้เวลา service สตาร์ทก่อน (กัน reboot loop ตอนบูต)
    $uptime = ([DateTime]::Now - (Get-CimInstance Win32_OperatingSystem).LastBootUpTime).TotalSeconds
    if ($uptime -lt $Config.MinUptimeSeconds) {
        Write-WdLog ("เครื่องเพิ่งบูตมา {0:N0} วินาที (< {1}) - ข้ามการตรวจรอบนี้" -f $uptime, $Config.MinUptimeSeconds)
        return
    }

    $state = Read-WdState
    $findings = Get-TamperFindings

    if ($findings.Count -eq 0) {
        if ($state.softHits -ne 0) {
            Write-WdLog "ทุกอย่างกลับมาปกติ - reset ตัวนับ soft hit จาก $($state.softHits) เป็น 0"
            $state.softHits = 0
            Write-WdState $state
        }
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
        Write-WdState $state
    }

    if (-not $shouldReboot) { return }

    # --- เพดานกัน reboot loop ---
    # ถ้า restore ไม่ได้เอา Agent กลับมา (เช่นถูกลบตอน RRRx อยู่ในโหมด update) การ reboot
    # ซ้ำจะกลายเป็น loop ที่ทำให้เครื่องใช้งานไม่ได้เลย - แย่กว่าปัญหาเดิมมาก จึงต้องมีเพดาน
    $cutoff = [DateTime]::UtcNow.AddHours(-24)
    $recent = @(@($state.reboots) | Where-Object { $_ -and $_.utc -and ((ConvertFrom-Iso8601Utc $_.utc) -gt $cutoff) })

    if ($recent.Count -ge $Config.MaxRebootsPer24h) {
        $msg = "ตรวจพบ tamper ($summary) แต่ reboot ไปแล้ว $($recent.Count) ครั้งใน 24 ชม. (เพดาน $($Config.MaxRebootsPer24h)) - หยุด reboot เพื่อกัน boot loop เครื่องนี้ต้องให้คนไปดูด้วยตัวเอง"
        Write-WdLog $msg 'ALERT'
        Write-WdEvent -Message $msg -EventId 9003 -EntryType 'Error'
        Send-TamperReport -Reason 'TAMPER_REBOOT_SUPPRESSED' -Detail $summary
        return
    }

    $reasonCode = if ($hard.Count -gt 0) { $hard[0].Code } else { @($findings)[0].Code }
    $msg = "TAMPER DETECTED - $summary. กำลังจะ restart เครื่องภายใน $($Config.RebootDelaySeconds) วินาที (reboot ครั้งที่ $($recent.Count + 1) ใน 24 ชม.)"
    Write-WdLog $msg 'ALERT'
    Write-WdEvent -Message $msg -EventId 9002 -EntryType 'Error'
    Send-TamperReport -Reason $reasonCode -Detail $summary

    $recent += ,([PSCustomObject]@{ utc = [DateTime]::UtcNow.ToString('o'); reason = $reasonCode })
    $state.reboots  = $recent
    $state.softHits = 0
    Write-WdState $state

    if ($WhatIfReboot) {
        Write-WdLog "(-WhatIfReboot) ข้ามการ reboot จริง" 'WARN'
        return
    }

    # /f = บังคับปิดโปรแกรมที่ค้าง (นักเรียนอาจมีหน้าต่างที่ block shutdown อยู่)
    # /d p:4:1 = Planned / Application / Maintenance - ให้ขึ้นเหตุผลที่อ่านออกใน Event Log
    & shutdown.exe /r /f /t $Config.RebootDelaySeconds /c "OnionProcOparetor: found unauthorized system modification - restarting" /d p:4:1
}
catch {
    # watchdog ห้ามพังเงียบๆ และห้าม reboot เพราะตัวเองพัง
    Write-WdLog "watchdog เกิดข้อผิดพลาดที่ไม่คาดคิด (ไม่ reboot): $($_.Exception.Message)" 'ALERT'
    Write-WdEvent -Message "watchdog error: $($_.Exception)" -EventId 9004 -EntryType 'Error'
}
