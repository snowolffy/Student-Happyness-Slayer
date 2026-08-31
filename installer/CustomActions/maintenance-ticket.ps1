<#
================================================================================
 maintenance-ticket.ps1 - ออก/ยกเลิก "ใบอนุญาตยุ่งกับ Agent" ให้ watchdog
================================================================================

agent-watchdog.ps1 จะ reboot เครื่องทันทีที่เห็นว่า OnionAgent service/ไฟล์หายไป
สคริปต์นี้คือทางเดียวที่ทำให้ "หายไปแบบถูกกฎ" ได้โดยไม่โดน reboot - ออกเอกสาร JSON
ที่เซ็นด้วย HMAC-SHA256 จาก wd.key ซึ่งอ่านได้เฉพาะ SYSTEM/Administrators

3 โหมด:
  -Action Issue        ออก maintenance ticket อายุ N นาที (ใช้ตอน install/upgrade/uninstall)
  -Action Clear        ลบ ticket ทิ้งทันที (ใช้ตอน install เสร็จ ไม่ต้องรอหมดอายุ)
  -Action Decommission ออก decommission marker ถาวร + ถอน Scheduled Task
                       (ใช้ตอน uninstall สำเร็จจริง = เลิกใช้เครื่องนี้แล้ว)

ทำไมต้องเซ็น ไม่ใช่แค่ "มีไฟล์อยู่": ถ้าใช้แค่การมีอยู่ของไฟล์เป็นเงื่อนไข นักเรียนก็แค่
สร้างไฟล์เปล่าชื่อนี้ก็ปิด watchdog ได้เลย - แต่ไฟล์อยู่ใน DataDir ที่ ACL ตัด Users ออก
แล้ว (ดู install-watchdog.ps1) การเซ็นเพิ่มจึงเป็นชั้นที่สอง เผื่อ ACL ถูกแก้/ถูกข้าม

Trust boundary ที่ยอมรับ: ใครที่เป็น admin จริงบนเครื่องอ่าน wd.key ได้ = ปลอม ticket ได้
ดีไซน์นี้ไม่ได้ตั้งเป้ากัน admin (ทำไม่ได้จริงถ้าไม่มี kernel driver) - ตั้งเป้ากันนักเรียน
สิทธิ์ปกติ และทำให้การถอนแบบครึ่งๆ กลางๆ ของ admin ล้มเหลวเสียงดัง
================================================================================
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('Issue', 'Clear', 'Decommission')]
    [string]$Action,

    # เหตุผลที่ขอ - ไปโผล่ใน watchdog.log ตอน verify ช่วยไล่ย้อนหลังว่าใครออก ticket ตอนไหน
    [string]$Reason = 'maintenance',

    # อายุ ticket (นาที) - 20 นาทีครอบคลุม MSI transaction ที่ช้าที่สุดที่เคยเจอได้สบาย
    # แต่สั้นพอที่จะไม่กลายเป็นช่องให้เปิดทิ้งไว้ยาวๆ
    [int]$ValidMinutes = 20
)

$ErrorActionPreference = 'Stop'

$DataDir    = Join-Path $env:ProgramData 'OnionProcOparetor\Agent'
$SecretPath = Join-Path $DataDir 'wd.key'
$TicketPath = Join-Path $DataDir 'maintenance.ticket'
$DecommPath = Join-Path $DataDir 'decommissioned.flag'
$LogPath    = Join-Path $DataDir 'watchdog-install.log'
$TaskName   = 'OnionProcOparetor Agent Watchdog'

function Write-TicketLog([string]$Message) {
    $line = "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') [ticket] $Message"
    try {
        if (-not (Test-Path $DataDir)) { New-Item -ItemType Directory -Path $DataDir -Force | Out-Null }
        Add-Content -Path $LogPath -Value $line -Encoding utf8
    } catch { }
    Write-Host $line
}

function New-SignedDocument {
    param([string]$Path, [string]$DocReason, [int]$Minutes)

    if (-not (Test-Path $SecretPath)) {
        # ไม่มี key = watchdog ยังไม่เคยถูกติดตั้ง = ไม่มีอะไรจะ reboot อยู่แล้ว ถือว่าสำเร็จ
        Write-TicketLog "ไม่พบ wd.key - ข้ามการออกเอกสาร (watchdog ยังไม่ได้ติดตั้งบนเครื่องนี้)"
        return $false
    }

    $key = [Convert]::FromBase64String((Get-Content -Raw -Encoding ascii -Path $SecretPath).Trim())
    $nonce = [Guid]::NewGuid().ToString('N')
    $issued  = [DateTime]::UtcNow.ToString('o')
    $expires = [DateTime]::UtcNow.AddMinutes($Minutes).ToString('o')
    $machine = $env:COMPUTERNAME

    # ลำดับ field ต้องตรงกับ agent-watchdog.ps1 (Test-SignedDocument) และ C# MaintenanceTicket.cs
    $payload = "$issued|$expires|$DocReason|$machine|$nonce"
    $hmac = New-Object System.Security.Cryptography.HMACSHA256 (,$key)
    try {
        $sig = ([System.BitConverter]::ToString($hmac.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($payload)))).Replace('-','').ToLowerInvariant()
    } finally { $hmac.Dispose() }

    $doc = [ordered]@{
        issuedAtUtc  = $issued
        expiresAtUtc = $expires
        reason       = $DocReason
        machine      = $machine
        nonce        = $nonce
        hmac         = $sig
    }
    ($doc | ConvertTo-Json -Compress) | Set-Content -Path $Path -Encoding utf8 -NoNewline
    Write-TicketLog "ออกเอกสารที่ $Path (reason=$DocReason, หมดอายุ $expires)"
    return $true
}

switch ($Action) {
    'Issue' {
        New-SignedDocument -Path $TicketPath -DocReason $Reason -Minutes $ValidMinutes | Out-Null
    }
    'Clear' {
        try { Remove-Item -Path $TicketPath -Force -ErrorAction Stop; Write-TicketLog "ลบ maintenance ticket แล้ว" }
        catch { Write-TicketLog "ไม่มี maintenance ticket ให้ลบ (ไม่เป็นไร)" }
    }
    'Decommission' {
        # อายุยาว (10 ปี) โดยตั้งใจ - marker นี้แปลว่า "เครื่องนี้เลิกใช้ Agent แล้วถาวร"
        # ไม่ใช่ช่วงเวลาสั้นๆ แบบ ticket ถ้าหมดอายุแล้ว watchdog ที่ถอนตัวเองไม่สำเร็จจะ
        # กลับมา reboot เครื่องเปล่าๆ ที่ไม่มี Agent อยู่แล้วซ้ำๆ
        New-SignedDocument -Path $DecommPath -DocReason 'decommissioned' -Minutes (10 * 365 * 24 * 60) | Out-Null
        try {
            if (Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue) {
                Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false
                Write-TicketLog "ถอน Scheduled Task '$TaskName' แล้ว"
            } else {
                Write-TicketLog "ไม่พบ Scheduled Task '$TaskName' (ถูกถอนไปแล้ว)"
            }
        } catch {
            Write-TicketLog "ถอน Scheduled Task ไม่สำเร็จ: $($_.Exception.Message) - decommission marker ยังกัน reboot ได้อยู่"
        }
        try { Remove-Item -Path $TicketPath -Force -ErrorAction SilentlyContinue } catch { }
    }
}

exit 0
