# Publish ทั้ง 4 โปรเจกต์ของ OnionProcOparetor ลง ..\publish\<ProjectName>\ (path ที่
# OnionProcOparetor.iss's [Files] section คาดหวังไว้พอดี) แล้วลอง compile installer
# ผ่าน ISCC.exe (Inno Setup Compiler) ต่อให้อัตโนมัติถ้าเจอในเครื่องนี้
#
# รันจากตรงไหนก็ได้ - path ทั้งหมดคำนวณจาก $PSScriptRoot (ตำแหน่งไฟล์นี้เอง คือ installer\)
# ไม่ใช่ working directory ปัจจุบัน

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path $PSScriptRoot -Parent
$publishRoot = Join-Path $repoRoot "publish"

$projects = @(
    @{ Name = "OnionProcOparetor.Server";    Path = "src\OnionProcOparetor.Server\OnionProcOparetor.Server.csproj" },
    @{ Name = "OnionProcOparetor.Console";   Path = "src\OnionProcOparetor.Console\OnionProcOparetor.Console.csproj" },
    @{ Name = "OnionProcOparetor.Agent";     Path = "src\OnionProcOparetor.Agent\OnionProcOparetor.Agent.csproj" },
    @{ Name = "OnionProcOparetor.AgentTray"; Path = "src\OnionProcOparetor.AgentTray\OnionProcOparetor.AgentTray.csproj" }
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

    dotnet publish $csprojPath -c Release -o $outDir

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
    Write-Host "One or more projects failed to publish - skipping ISCC.exe." -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "All 4 projects published successfully." -ForegroundColor Green

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
$msiPath = Join-Path $outputDir "OnionProcOparetor-Setup.msi"

Write-Host ""
Write-Host "==== Compiling installer with wix build ====" -ForegroundColor Cyan
Push-Location $PSScriptRoot
try {
    & wix build "OnionProcOparetor.wxs" `
        -ext WixToolset.Util.wixext `
        -ext WixToolset.Firewall.wixext `
        -ext WixToolset.UI.wixext `
        -arch x64 `
        -d "PublishDir=$publishRoot" `
        -o $msiPath
    $wixExitCode = $LASTEXITCODE
}
finally {
    Pop-Location
}

if ($wixExitCode -ne 0) {
    Write-Host "wix build failed (exit code $wixExitCode)" -ForegroundColor Red
    exit 1
}

Write-Host "Installer compiled successfully: $msiPath" -ForegroundColor Green

# ---- also try the legacy Inno Setup .exe if ISCC.exe is available (fallback installer,
# kept until the .msi has been confirmed fully equivalent - see OnionProcOparetor.iss) ----
$isccOnPath = (Get-Command "ISCC.exe" -ErrorAction SilentlyContinue).Source
$isccCandidates = @(
    $isccOnPath,
    "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
    "C:\Program Files\Inno Setup 6\ISCC.exe"
) | Where-Object { $_ -and (Test-Path $_) }

$isccPath = $isccCandidates | Select-Object -First 1

if (-not $isccPath) {
    Write-Host ""
    Write-Host "NOTE: ISCC.exe (Inno Setup Compiler) not found - skipping the legacy .exe fallback build." -ForegroundColor Yellow
    exit 0
}

$issPath = Join-Path $PSScriptRoot "OnionProcOparetor.iss"

Write-Host ""
Write-Host "==== Compiling legacy fallback installer with $isccPath ====" -ForegroundColor Cyan
& $isccPath $issPath

if ($LASTEXITCODE -ne 0) {
    Write-Host "ISCC.exe failed (exit code $LASTEXITCODE)" -ForegroundColor Red
    exit 1
}

Write-Host "Legacy .exe installer compiled successfully." -ForegroundColor Green
exit 0
