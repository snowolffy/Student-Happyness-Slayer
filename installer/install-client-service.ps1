<#
    ติดตั้ง MyLabGuard.Client เป็น Windows Service
    ต้องรันด้วยสิทธิ์ Administrator
#>

$ErrorActionPreference = "Stop"

$ServiceName = "MyLabGuardClient"
$ServiceDisplayName = "MyLabGuard Client"
$ServiceDescription = "ตรวจจับและบังคับใช้นโยบายซอฟต์แวร์ตาม publisher ที่กำหนด"

# path ของ exe หลัง publish - แก้ตามจริงถ้าย้ายตำแหน่งไฟล์
$ExePath = Join-Path $PSScriptRoot "..\src\MyLabGuard.Client\publish\MyLabGuard.Client.exe"
$ExePath = (Resolve-Path $ExePath).Path

# เช็คสิทธิ์ Administrator ก่อน
$currentPrincipal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $currentPrincipal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Host "ต้องรัน script นี้ด้วยสิทธิ์ Administrator" -ForegroundColor Red
    exit 1
}

# ถ้ามี service เดิมอยู่แล้ว ให้ลบก่อน (กันซ้ำ)
$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existing) {
    Write-Host "พบ service เดิมอยู่แล้ว กำลังลบก่อนติดตั้งใหม่..."
    Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
    sc.exe delete $ServiceName | Out-Null
    Start-Sleep -Seconds 2
}

Write-Host "กำลังสร้าง service จาก: $ExePath"

New-Service -Name $ServiceName `
    -DisplayName $ServiceDisplayName `
    -Description $ServiceDescription `
    -BinaryPathName $ExePath `
    -StartupType Automatic

# ตั้งค่า Service Recovery: restart อัตโนมัติเมื่อ crash
# restart ทันทีในครั้งที่ 1, 2 และครั้งถัดๆ ไป, reset failure counter ทุก 1 วัน (86400 วินาที)
sc.exe failure $ServiceName reset= 86400 actions= restart/0/restart/0/restart/0 | Out-Null

Write-Host "ติดตั้งสำเร็จ - กำลังเริ่ม service..." -ForegroundColor Green
Start-Service -Name $ServiceName

Write-Host "เสร็จสิ้น! Service '$ServiceDisplayName' กำลังทำงานอยู่" -ForegroundColor Green
Get-Service -Name $ServiceName