# Publish ทั้ง 7 โปรเจกต์ของ OnionProcOparetor ลง ..\publish\<ProjectName>\ (path ที่ทั้ง 2 .wxs's
# <Files Include=...> คาดหวังไว้พอดี) แล้ว compile installer 2 ตัวแยกกันผ่าน wix build ต่อให้อัตโนมัติ:
# OnionProcOparetor-Agent-Setup.msi (Client Agent เท่านั้น - เครื่อง lab นักเรียน) และ
# OnionProcOparetor-Server-Console-Setup.msi (Server+Console เท่านั้น - เครื่องครู/admin) แทนที่ MSI
# ตัวเดียว-3-mode เดิม (ดู OnionProcOparetor.Agent.wxs/OnionProcOparetor.ServerConsole.wxs header
# comment สำหรับเหตุผลของการแยก)
#
# รันจากตรงไหนก็ได้ - path ทั้งหมดคำนวณจาก $PSScriptRoot (ตำแหน่งไฟล์นี้เอง คือ installer\)
# ไม่ใช่ working directory ปัจจุบัน

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path $PSScriptRoot -Parent
$publishRoot = Join-Path $repoRoot "publish"

# ---- FileVersion ที่เปลี่ยนทุกครั้งที่รัน script นี้ - แก้บั๊กจริงที่เจอ (2026-08-10): Windows
# Installer's InstallFiles ข้ามการเขียนทับไฟล์เป้าหมายที่ FileVersion ไม่ได้ "ใหม่กว่า" ไฟล์ที่ติดตั้ง
# อยู่แล้วเป๊ะๆ (ยืนยันจาก verbose log จริงตอน MSI upgrade: "Won't Overwrite; Existing file is of an
# equal version") - src\Directory.Build.props เดิมตั้ง FileVersion ตายตัว "1.2.5.0" ทุก build เหมือน
# กันหมด (ไม่มีใคร bump ระหว่าง build เลย) เท่ากับทุกครั้งที่ reinstall/upgrade MSI ทับของเดิม อาจไม่ได้
# code ใหม่จริงเลยแม้ install จะรายงานว่าสำเร็จก็ตาม
#
# อ่าน Major.Minor.Build จาก Directory.Build.props เป็นแหล่งความจริงเดียว (ไม่ hardcode ซ้ำที่นี่ กัน
# สองไฟล์ drift ไม่ตรงกันตอนมีคน bump product version) แล้วต่อท้ายด้วย Revision ที่เปลี่ยนทุกรอบ -
# วินาทีตั้งแต่เที่ยงคืน UTC หาร 2 (สูตรเดียวกับ classic AssemblyVersion="1.0.*" auto-increment ของ
# .NET Framework เดิม) ค่าสูงสุด 86400/2 = 43200 อยู่ในช่วง UInt16 (แต่ละ octet ของ FILEVERSION
# resource สูงสุด 65535) พอดี ไม่ overflow
#
# ตั้งใจไม่แตะ Version/AssemblyVersion (คงที่ตาม Directory.Build.props ปกติ) - เปลี่ยนแค่ FileVersion
# ผ่าน -p: override ตอน publish เท่านั้น ดู Directory.Build.props's comment สำหรับเหตุผลเต็ม
$directoryBuildPropsPath = Join-Path $repoRoot "src\Directory.Build.props"
[xml]$directoryBuildProps = Get-Content $directoryBuildPropsPath
$baseVersion = $directoryBuildProps.Project.PropertyGroup.Version
$majorMinorBuild = ($baseVersion -split '\.')[0..2] -join '.'
$revision = [int]([DateTime]::UtcNow.TimeOfDay.TotalSeconds / 2)
$fileVersion = "$majorMinorBuild.$revision"
Write-Host "FileVersion สำหรับรอบ publish นี้: $fileVersion" -ForegroundColor Cyan

$projects = @(
    @{ Name = "OnionProcOparetor.Server";           Path = "src\OnionProcOparetor.Server\OnionProcOparetor.Server.csproj" },
    @{ Name = "OnionProcOparetor.Console";          Path = "src\OnionProcOparetor.Console\OnionProcOparetor.Console.csproj" },
    @{ Name = "OnionProcOparetor.OnlineConsole";    Path = "src\OnionProcOparetor.OnlineConsole\OnionProcOparetor.OnlineConsole.csproj" },
    @{ Name = "OnionProcOparetor.RosterManager";    Path = "src\OnionProcOparetor.RosterManager\OnionProcOparetor.RosterManager.csproj" },
    @{ Name = "OnionProcOparetor.Agent";            Path = "src\OnionProcOparetor.Agent\OnionProcOparetor.Agent.csproj" },
    @{ Name = "OnionProcOparetor.AgentTray";        Path = "src\OnionProcOparetor.AgentTray\OnionProcOparetor.AgentTray.csproj" },
    @{ Name = "OnionProcOparetor.UninstallHelper";  Path = "src\OnionProcOparetor.UninstallHelper\OnionProcOparetor.UninstallHelper.csproj" }
)

# ---- ลบ publish\ เก่าทิ้งก่อน (clean build) กันไฟล์ค้างจากเวอร์ชันก่อนหน้าปนเข้าไปใน installer ----
if (Test-Path $publishRoot) {
    Write-Host "Removing old publish output: $publishRoot"
    Remove-Item -Recurse -Force -Confirm:$false $publishRoot
}

$allSucceeded = $true

foreach ($project in $projects) {
    $csprojPath = Join-Path $repoRoot $project.Path
    $outDir = Join-Path $publishRoot $project.Name

    Write-Host ""
    Write-Host "==== Publishing $($project.Name) ====" -ForegroundColor Cyan
    Write-Host "  csproj: $csprojPath"
    Write-Host "  output: $outDir"

    dotnet publish $csprojPath -c Release -o $outDir -p:FileVersion=$fileVersion

    if ($LASTEXITCODE -ne 0) {
        Write-Host "  FAILED: $($project.Name) (dotnet publish exit code $LASTEXITCODE)" -ForegroundColor Red
        $allSucceeded = $false
    }
    else {
        Write-Host "  OK: $($project.Name)" -ForegroundColor Green
    }
}

if (-not $allSucceeded) {
    Write-Host ""
    Write-Host "One or more projects failed to publish - skipping wix build." -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "All 7 projects published successfully." -ForegroundColor Green

# ---- compile the .msi via WiX v5 (dotnet tool) - primary installer output ----
# WiX is pinned to v5.0.2 (installer\.wix\extensions\) rather than whatever "wix" resolves
# to on PATH, because WiX v6/v7 require accepting a paid Open Source Maintenance Fee EULA
# before `wix build`/`wix extension add` will run - see installer\OnionProcOparetor.wxs
# header comment. Re-run `dotnet tool install --global wix --version 5.0.2` if "wix" isn't
# on PATH at all yet.
$wixOnPath = (Get-Command "wix.exe" -ErrorAction SilentlyContinue).Source
if (-not $wixOnPath) {
    Write-Host ""
    Write-Host "WARNING: wix.exe (WiX Toolset v5 dotnet tool) not found on PATH." -ForegroundColor Yellow
    Write-Host "Install it with: dotnet tool install --global wix --version 5.0.2" -ForegroundColor Yellow
    Write-Host "Then from installer\: wix extension add WixToolset.Util.wixext/5.0.2 WixToolset.Firewall.wixext/5.0.2 WixToolset.UI.wixext/5.0.2" -ForegroundColor Yellow
    exit 1
}

$outputDir = Join-Path $PSScriptRoot "output"
if (-not (Test-Path $outputDir)) {
    New-Item -ItemType Directory -Path $outputDir | Out-Null
}

$installers = @(
    @{ Wxs = "OnionProcOparetor.Agent.wxs";         Msi = "OnionProcOparetor-Agent-Setup.msi" },
    @{ Wxs = "OnionProcOparetor.ServerConsole.wxs";  Msi = "OnionProcOparetor-Server-Console-Setup.msi" }
)

$allBuildsSucceeded = $true
$builtMsiPaths = @()

Push-Location $PSScriptRoot
try {
    foreach ($installer in $installers) {
        $msiPath = Join-Path $outputDir $installer.Msi

        Write-Host ""
        Write-Host "==== Compiling $($installer.Wxs) with wix build ====" -ForegroundColor Cyan

        & wix build $installer.Wxs `
            -ext WixToolset.Util.wixext `
            -ext WixToolset.Firewall.wixext `
            -ext WixToolset.UI.wixext `
            -arch x64 `
            -d "PublishDir=$publishRoot" `
            -o $msiPath

        if ($LASTEXITCODE -ne 0) {
            Write-Host "  FAILED: $($installer.Wxs) (wix build exit code $LASTEXITCODE)" -ForegroundColor Red
            $allBuildsSucceeded = $false
        }
        else {
            Write-Host "  OK: $msiPath" -ForegroundColor Green
            $builtMsiPaths += $msiPath
        }
    }
}
finally {
    Pop-Location
}

if (-not $allBuildsSucceeded) {
    Write-Host ""
    Write-Host "One or more installers failed to build." -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "Both installers compiled successfully:" -ForegroundColor Green
$builtMsiPaths | ForEach-Object { Write-Host "  $_" -ForegroundColor Green }
exit 0
