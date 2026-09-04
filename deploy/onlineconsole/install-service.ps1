# Installs OnionProcOparetor.OnlineConsole as a Windows Service.
# Run from an elevated PowerShell (Run as administrator) on whichever machine is hosting the console.
#
# Comments and output are English on purpose: a .ps1 containing Thai text fails to parse unless the
# file is saved with a UTF-8 BOM, and that failure is silent. English keeps this script immune to it.

[CmdletBinding()]
param(
    [string] $InstallPath  = 'C:\OnionProcOparetor\OnlineConsole',
    [string] $ServiceName  = 'OnionOnlineConsole',
    [string] $DisplayName  = 'OnionProcOparetor Online Console'
)

$ErrorActionPreference = 'Stop'

if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'This script must be run from an elevated PowerShell (Run as administrator).'
}

$exe = Join-Path $InstallPath 'OnionProcOparetor.OnlineConsole.exe'
if (-not (Test-Path $exe)) {
    throw "Not found: $exe  --  publish first: dotnet publish ... -c Release -o $InstallPath"
}

# Stop and delete any previous copy so re-running this is an upgrade, not an error.
$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existing) {
    Write-Host "Existing service found - stopping and removing it first."
    if ($existing.Status -ne 'Stopped') { Stop-Service -Name $ServiceName -Force; Start-Sleep -Seconds 3 }
    sc.exe delete $ServiceName | Out-Null
    Start-Sleep -Seconds 2
}

# binPath must be quoted: the default install path has no spaces, but an operator passing
# "C:\Program Files\..." would otherwise produce a service that silently fails to start.
Write-Host "Creating service '$ServiceName' -> $exe"
sc.exe create $ServiceName binPath= "`"$exe`"" start= auto DisplayName= "$DisplayName" | Out-Null

sc.exe description $ServiceName "Blazor web console for OnionProcOparetor, reachable only over the Tailscale tailnet." | Out-Null

# Restart on failure rather than staying down after a transient fault (e.g. Tailscale not up yet at
# boot, which makes the tailnet bind in Program.cs throw).
sc.exe failure $ServiceName reset= 86400 actions= restart/30000/restart/60000/restart/120000 | Out-Null

Write-Host "Starting service..."
Start-Service -Name $ServiceName
Start-Sleep -Seconds 6

$svc = Get-Service -Name $ServiceName
Write-Host ("Service status: {0}" -f $svc.Status)

if ($svc.Status -ne 'Running') {
    Write-Warning "Service is not running. Most likely cause: Tailscale was not connected, so the tailnet bind failed."
    Write-Warning "Check the Application event log: Get-EventLog -LogName Application -Newest 20 | Format-List"
    return
}

# Prove it is actually serving, on the address it is supposed to be serving on.
$tailnetIp = (& 'C:\Program Files\Tailscale\tailscale.exe' ip -4 2>$null | Select-Object -First 1)
if ($tailnetIp) {
    $url = "http://${tailnetIp}:8788/login"
    try {
        $code = (Invoke-WebRequest -Uri $url -UseBasicParsing -TimeoutSec 10).StatusCode
        Write-Host "OK  $url -> $code"
        Write-Host ""
        Write-Host "Open this on the phone (must be connected to the tailnet):  http://${tailnetIp}:8788"
    } catch {
        Write-Warning "Service is running but $url did not answer: $($_.Exception.Message)"
    }
} else {
    Write-Warning "Could not read this host's Tailscale IP - is Tailscale connected?"
}
