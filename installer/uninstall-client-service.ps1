<#
    ถอนการติดตั้ง MyLabGuard.Client Windows Service
    ต้องรันด้วยสิทธิ์ Administrator
#>

$ErrorActionPreference = "Stop"
$ServiceName = "MyLabGuardClient"

$currentPrincipal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $currentPrincipal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Host "ต้องรัน script นี้ด้วยสิทธิ์ Administrator" -ForegroundColor Red
    exit 1
}

$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if (-not $existing) {
    Write-Host "ไม่พบ service '$ServiceName' ในเครื่องนี้"
    exit 0
}

Write-Host "กำลังหยุด service..."
Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 2

Write-Host "กำลังลบ service..."
sc.exe delete $ServiceName | Out-Null

Write-Host "ถอนการติดตั้งสำเร็จ" -ForegroundColor Green