# ล็อก/ปลดล็อก Tailscale บนเครื่อง lab ที่ใช้เป็น fallback path ของ Agent (ดู
# OnionProcOparetor.Agent's ServerSettings.FallbackBaseUrl / setup-client.ps1) กันนักเรียนสั่ง
# disconnect/exit Tailscale เอง ซึ่งจะตัด fallback connection ทิ้งไปเงียบๆ โดยไม่มีใครรู้
#
# อิงตาม official Windows policy ของ Tailscale เอง (ไม่ใช่ hack) - อ่านตรงจาก ADMX ต้นทาง:
# https://github.com/tailscale/tailscale/blob/main/docs/windows/policy/tailscale.admx
# เก็บที่ HKLM\SOFTWARE\Policies\Tailscale (Group-Policy-style key มาตรฐานของ Tailscale เอง)
#
# หมายเหตุสำคัญ: Tailscale ไม่มี policy "ซ่อนไอคอน tray ทั้งดวง" หรือ "ซ่อนปุ่ม Exit/Quit" ให้เลย
# (เช็คจาก ADMX แล้ว - รายชื่อ policy ทั้งหมดมีแค่ AdminConsole/NetworkDevices/TestMenu/UpdateMenu/
# PreferencesMenu/ExitNodesPicker ที่ซ่อนได้ ไม่มี ExitMenu หรือเทียบเท่า) การป้องกันจริงที่ทำได้คือ
# AlwaysOn.Enabled ซึ่งทำให้กด Exit/Disconnect แล้ว "ไม่มีผล" (connection ยังอยู่) ไม่ใช่ทำให้กดไม่ได้ -
# สคริปต์นี้เลยซ่อนเมนูย่อยที่ซ่อนได้จริงเพื่อลดความอยากลอง/ความสับสน ควบคู่กับ AlwaysOn ที่เป็นตัวล็อกจริง
#
# AlwaysOn.OverrideWithReason=0 (ไม่ใช้ 1/AllowedWithAudit) - เลือกให้ล็อกเด็ดขาด ไม่ให้ override
# เองได้แม้จะต้องใส่เหตุผลก็ตาม (นักเรียนกลุ่มที่มีปัญหาจริงมักลองมากกว่าแค่คลิกเฉยๆ - เห็นได้จาก
# ประวัติ bypass enforcement/ถอนโปรแกรมที่เจอมาก่อนหน้านี้ในโปรเจกต์เดียวกัน)
#
# ย้ายมาจาก installer/ (build-time only) มาที่ installer/CustomActions/ (2026-08-25) เพราะตอนนี้
# OnionProcOparetor.Agent (LocalSystem) จะเรียกสคริปต์นี้เองผ่าน CommandProcessor's SetTailscaleLockdown
# command (remote toggle จาก Console) - ต้องถูก harvest เข้า MSI จริง ไม่ใช่แค่ไฟล์ที่ dev รันมือ
#
# วิธีปรับแก้/แก้ปัญหาเครื่องเอง (สำหรับ admin เท่านั้น เพราะ key นี้อยู่ใต้ HKLM เขียนได้เฉพาะสิทธิ์ admin):
#   1. รัน:  .\set-tailscale-lockdown.ps1 -Unlock   (elevated PowerShell)
#   2. ทำสิ่งที่ต้องการ (เช่น tailscale login เปลี่ยน tailnet, เปิด GUI ปรับ preferences ฯลฯ)
#   3. รันซ้ำแบบไม่มี -Unlock (หรือใส่ -Lock เฉยๆ) เพื่อล็อกกลับ
#
# หรือสั่งจาก Console ระยะไกล (SetTailscaleLockdown command -> Agent รันสคริปต์นี้เองในนาม LocalSystem)

param(
    [switch]$Unlock
)

$ErrorActionPreference = "Stop"
$policyKey = "HKLM:\SOFTWARE\Policies\Tailscale"

function Write-Status([string]$Message) {
    Write-Host $Message -ForegroundColor Cyan
}

# บั๊กจริงที่เจอ (2026-08-25, ตอนย้ายมาให้ Agent เรียกเอง): เดิมเช็คแค่ IsInRole(Administrator) ซึ่ง
# ใช้ได้ตอน admin รันเองผ่าน elevated PowerShell แต่ Agent เป็น Windows Service ที่รันในนาม LocalSystem
# (NT AUTHORITY\SYSTEM) - SYSTEM ไม่ได้เป็นสมาชิกกลุ่ม "Administrators" (คนละ SID กันเลย) ถึงจะมีสิทธิ์
# เขียน HKLM ได้เต็มที่จริงก็ตาม ผลคือ IsInRole(Administrator) คืน false ให้ SYSTEM เสมอ สคริปต์จะ exit 1
# ทุกครั้งที่ Agent เรียก ทั้งที่ควรทำงานได้ปกติ - เช็คเพิ่ม WindowsIdentity.IsSystem ด้วย
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)
$isAdmin = $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin -and -not $identity.IsSystem) {
    Write-Host "ต้องรันด้วยสิทธิ์ Administrator หรือ LocalSystem (เขียน HKLM\SOFTWARE\Policies\Tailscale ได้เฉพาะสิทธิ์นี้เท่านั้น)" -ForegroundColor Red
    exit 1
}

if (-not (Test-Path $policyKey)) {
    New-Item -Path $policyKey -Force | Out-Null
}

if ($Unlock) {
    Write-Status "==== ปลดล็อก Tailscale (สำหรับ admin ปรับแก้ค่า/แก้ปัญหาเครื่องนี้ชั่วคราว) ===="

    # ลบค่าที่เคยตั้งไว้ทั้งหมด (ไม่ set กลับเป็น 0/never - ลบทิ้งไปเลยให้ Tailscale ใช้ default ปกติของ
    # มันเอง ตรงไปตรงมากว่าคง key ไว้แล้วตั้งค่า "ปิด" ซึ่งบาง policy อาจตีความ "ค่าที่ set ไว้" ต่างจาก
    # "ไม่มี policy เลย" ได้ - ลบให้สะอาดที่สุด)
    $valuesToRemove = @(
        "AlwaysOn.Enabled",
        "AlwaysOn.OverrideWithReason",
        "UnattendedMode",
        "PreferencesMenu",
        "TestMenu",
        "UpdateMenu",
        "NetworkDevices",
        "AdminConsole",
        "ExitNodesPicker"
    )
    foreach ($name in $valuesToRemove) {
        Remove-ItemProperty -Path $policyKey -Name $name -ErrorAction SilentlyContinue
    }

    Write-Host "ปลดล็อกแล้ว - ทำสิ่งที่ต้องการเสร็จแล้วอย่าลืมรันสคริปต์นี้ซ้ำแบบไม่มี -Unlock เพื่อล็อกกลับ" -ForegroundColor Yellow
}
else {
    Write-Status "==== ล็อก Tailscale (กันนักเรียน disconnect/exit เอง) ===="

    # AlwaysOn: ตัวล็อกจริง - ทำให้ disconnect/exit จาก tray (ไม่ว่าจะยังกดได้อยู่หรือไม่) ไม่มีผลจริง
    # connection ยังอยู่เสมอ ค่าเป็น REG_DWORD (decimal 1/0 ตาม ADMX ต้นทาง ไม่ใช่ string)
    New-ItemProperty -Path $policyKey -Name "AlwaysOn.Enabled" -Value 1 -PropertyType DWord -Force | Out-Null
    New-ItemProperty -Path $policyKey -Name "AlwaysOn.OverrideWithReason" -Value 0 -PropertyType DWord -Force | Out-Null

    # UnattendedMode: แยกเป็นคนละเรื่องจาก AlwaysOn - ทำให้ connection ไม่ผูกกับ user session/tray GUI
    # เลย (เช่น log off ไปก็ยังต่ออยู่) ค่าเป็น string "always"/"never" ตาม ADMX ต้นทาง ไม่ใช่ 1/0
    New-ItemProperty -Path $policyKey -Name "UnattendedMode" -Value "always" -PropertyType String -Force | Out-Null

    # ซ่อนเมนูย่อยที่ Tailscale เปิดให้ซ่อนได้จริง (ลดความสับสน/ความอยากลอง - ไม่ใช่ตัวล็อกหลัก
    # ตัวล็อกหลักคือ AlwaysOn ด้านบน) ค่าเป็น string "show"/"hide" ตาม ADMX ต้นทาง
    $menusToHide = @("PreferencesMenu", "TestMenu", "UpdateMenu", "NetworkDevices", "AdminConsole", "ExitNodesPicker")
    foreach ($name in $menusToHide) {
        New-ItemProperty -Path $policyKey -Name $name -Value "hide" -PropertyType String -Force | Out-Null
    }

    Write-Host "ล็อกแล้ว - หมายเหตุ: ไม่มี policy ซ่อนปุ่ม Exit/Quit ตัวจริงได้ (Tailscale ไม่มี policy นี้ให้)" -ForegroundColor Yellow
    Write-Host "         นักเรียนยังกด Exit ได้เหมือนเดิม แต่ AlwaysOn ทำให้กดแล้วไม่มีผลจริง (connection ไม่ขาด)" -ForegroundColor Yellow
}

# รีสตาร์ท Tailscale service ให้ policy ใหม่มีผลทันที (ไม่ต้องรอ reboot/relog) - ถ้า service ชื่ออื่น
# หรือยังไม่ได้ติดตั้ง Tailscale จะ log ไว้เฉยๆ ไม่ throw
$service = Get-Service -Name "Tailscale" -ErrorAction SilentlyContinue
if ($service) {
    Write-Status "รีสตาร์ท Tailscale service ให้อ่าน policy ใหม่..."
    Restart-Service -Name "Tailscale" -Force
    Write-Host "รีสตาร์ทแล้ว (เมนู tray อาจต้องรอ tray icon process รีเฟรชเอง หรือ log off/on ใหม่ถึงจะเห็นเมนูที่ซ่อนหายไปจริง)" -ForegroundColor Green
}
else {
    Write-Host "ไม่พบ Tailscale service บนเครื่องนี้ (ยังไม่ได้ติดตั้ง Tailscale หรือชื่อ service ไม่ตรง) - ตั้งค่า registry ไว้ให้แล้ว จะมีผลตอนติดตั้ง/เปิด Tailscale ครั้งถัดไป" -ForegroundColor Yellow
}
