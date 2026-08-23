# Headless (no UI/desktop dependency) - runs very early in InstallExecuteSequence, Before=
# AppSearch, on BOTH install and uninstall. MSI uninstalls normally run at UILevel=3 (basic)
# where InstallExecuteSequence executes entirely inside the elevated engine process with no
# desktop/window-station access - a WinForms popup here always fails (see git history: the
# original version of this script used ShowDialog() and reliably crashed with exit code 1
# on every uninstall). Since external EXE-launch CustomActions have no channel to write MSI
# properties directly (only Type 1/Dll or Type 5-6/Script CAs can call Session.Property, and
# VBScript/JScript CAs are deprecated Windows components not worth depending on), this script
# communicates its result via a marker FILE instead: OnionProcOparetor.wxs then uses a
# declarative <FileSearch> (AppSearch) to read that file's existence into an MSI property,
# which a <Launch Condition="..."> checks with a proper, readable error message - all native,
# non-deprecated MSI mechanisms, no scripting needed downstream of this script.
#
# Password now arrives via the UNINSTPWD property (command line: msiexec /x ... UNINSTPWD=xxx)
# instead of an interactive prompt - see installer/UNINSTALL-README.txt.
#
# Second, ephemeral guard (added for the UninstallHelper Server-login flow): a sibling file
# "uninstall-session-guard.json" next to $GuardPath, written by UninstallHelper.exe right before it
# invokes msiexec, containing a one-time random password's {salt, hash, expiresAtUtc} - checked here
# IN ADDITION TO (never instead of) the original guard below. This lets an admin authenticate via
# their Server account instead of typing the local uninstall password, without ever touching/
# overwriting uninstall-guard.json itself - the original local password keeps working forever no
# matter how many times someone logs in via Server (needed so the offline fallback in UninstallHelper
# never breaks). Single-use: always deleted immediately after being read, matched or not, to prevent
# replay. This script's own logic and the scripted/remote `msiexec /x ... UNINSTPWD=` flow (SCCM,
# Reboot Restore Rx) are completely unaffected either way - this is purely additive.

param(
    [Parameter(Mandatory = $true)][string]$GuardPath,
    [string]$Password = "",
    [Parameter(Mandatory = $true)][string]$FlagPath
)

Remove-Item -Path $FlagPath -Force -ErrorAction SilentlyContinue

function Grant-UninstallAuth {
    New-Item -Path $FlagPath -ItemType File -Force | Out-Null
}

$sessionGuardPath = Join-Path (Split-Path -Path $GuardPath -Parent) "uninstall-session-guard.json"
if (Test-Path $sessionGuardPath) {
    try {
        $sessionGuard = Get-Content -Raw -Path $sessionGuardPath | ConvertFrom-Json
        $sessionSalt = [string]$sessionGuard.salt
        $sessionHash = [string]$sessionGuard.hash
        $sessionExpiresAtUtc = [string]$sessionGuard.expiresAtUtc
    }
    catch {
        $sessionSalt = $null
        $sessionHash = $null
        $sessionExpiresAtUtc = $null
    }

    # Single-use regardless of outcome - remove now so a leftover/expired file can never be reused.
    Remove-Item -Path $sessionGuardPath -Force -ErrorAction SilentlyContinue

    if (-not [string]::IsNullOrEmpty($sessionSalt) -and -not [string]::IsNullOrEmpty($sessionHash)) {
        $notExpired = $false
        try {
            $notExpired = ([DateTime]::Parse($sessionExpiresAtUtc, $null, [System.Globalization.DateTimeStyles]::RoundtripKind)) -gt (Get-Date).ToUniversalTime()
        }
        catch {
            $notExpired = $false
        }

        if ($notExpired) {
            $sha256Session = [System.Security.Cryptography.SHA256]::Create()
            $sessionBytes = [System.Text.Encoding]::UTF8.GetBytes($sessionSalt + $Password)
            $sessionHashBytes = $sha256Session.ComputeHash($sessionBytes)
            $enteredSessionHash = [System.BitConverter]::ToString($sessionHashBytes).Replace("-", "").ToLowerInvariant()

            if ($enteredSessionHash -eq $sessionHash) {
                Grant-UninstallAuth
                exit 0
            }
        }
    }
}
# Session guard didn't match/exist/expired - fall through to the original guard check below unchanged.

if (-not (Test-Path $GuardPath)) {
    # ไม่เคยตั้ง uninstall password ไว้ตอน install (เลือก skip) - อนุญาตให้ uninstall ได้ปกติ
    Grant-UninstallAuth
    exit 0
}

try {
    $guard = Get-Content -Raw -Path $GuardPath | ConvertFrom-Json
    $salt = [string]$guard.salt
    $expectedHash = [string]$guard.hash
}
catch {
    # guard file เสียหาย/อ่านไม่ได้ - ถือว่าไม่มี gate (เหมือนเดิมกับ TryLoadUninstallGuard เดิมที่คืน False)
    Grant-UninstallAuth
    exit 0
}

if ([string]::IsNullOrEmpty($salt) -or [string]::IsNullOrEmpty($expectedHash)) {
    Grant-UninstallAuth
    exit 0
}

$sha256 = [System.Security.Cryptography.SHA256]::Create()
$bytes = [System.Text.Encoding]::UTF8.GetBytes($salt + $Password)
$hashBytes = $sha256.ComputeHash($bytes)
$enteredHash = [System.BitConverter]::ToString($hashBytes).Replace("-", "").ToLowerInvariant()

if ($enteredHash -eq $expectedHash) {
    Grant-UninstallAuth
}
# ไม่ตรง - ไม่เขียน flag file, ปล่อยให้ Launch Condition ใน .wxs abort uninstall เอง

exit 0
