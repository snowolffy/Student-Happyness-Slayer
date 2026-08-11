Onion ProcOparetor - Uninstall Notes for IT / Teachers
=======================================================

Two separate products now (2026-08-10 split)
---------------------------------------------
Onion ProcOparetor ships as two independent installers that can be installed
side by side on the same machine without interfering with each other:

- "Onion ProcOparetor" - Server + Console (teacher/admin machine)
- "Onion ProcOparetor Agent" - Client Agent (student lab machine)

Each has its own entry in Add/Remove Programs, its own install folder, and is
uninstalled completely independently - uninstalling one never touches the
other, even if both are installed on the same machine.

Normal path
-----------
Control Panel > Programs and Features (or Settings > Apps) > pick the entry
for the product you want to remove ("Onion ProcOparetor" or
"Onion ProcOparetor Agent") > Uninstall now opens a small uninstall window (a
UAC prompt appears first - this is expected, uninstalling requires admin
rights).

- "Onion ProcOparetor" (Server + Console): just asks you to confirm, offers
  an "Also delete database, logs and rules" checkbox, and uninstalls. No
  password prompt - this product has no uninstall-password protection.
- "Onion ProcOparetor Agent" (Client Agent): if no uninstall password was set
  during install (the "Uninstall Protection" wizard page), it behaves the
  same as above. If a password WAS set, the window asks for it first - wrong
  password shows "Incorrect password. Please try again." and lets you retry.

This window is just a friendly front end - it runs the exact same `msiexec /x`
command described below on your behalf, so there is nothing extra to configure.

Scripted / command-line path (SCCM, remote PowerShell, etc.)
--------------------------------------------------------------
The command-line path still works exactly as before and is unaffected by the
window above - useful for mass/remote uninstalls where no one is at the console
to click through a prompt:

1. Open an ELEVATED command prompt or PowerShell (Run as Administrator).

2. Run, for the Server + Console product:

    msiexec /x "C:\path\to\OnionProcOparetor-Server-Console-Setup.msi" /qn

   or for the Client Agent product:

    msiexec /x "C:\path\to\OnionProcOparetor-Agent-Setup.msi" UNINSTPWD=the-password /qn

   Either the .msi path or the product's own GUID (visible in the uninstall
   window's title, or in the registry - see "Finding the product GUID" below)
   works with /x. Leave off UNINSTPWD entirely for the Server + Console
   product (it has no password gate) or if no password was ever set on an
   Agent machine.

3. If you also want to wipe the database/logs/rules under ProgramData
   (equivalent to checking the "Also delete..." box in the window above), add
   DELETEDATA=1 - this only applies to the Server + Console product, which is
   the one that owns that data:

    msiexec /x "C:\path\to\OnionProcOparetor-Server-Console-Setup.msi" DELETEDATA=1 /qn

   Leaving DELETEDATA out (or setting it to anything other than 1) keeps the
   data, e.g. for a later reinstall - this is the default.

Finding the product GUID
-------------------------
Each product's uninstall window has its own synthetic ARP entry (so the
friendly window can appear at all - see "Why a wrapper?" below) and does not
carry the real ProductCode GUID in the registry the way a normal MSI entry
would. To find it:

- Server + Console: HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\
  OnionProcOparetor's UninstallString value has it as the second token.
- Client Agent: HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\
  OnionProcOparetorAgent's UninstallString value has it as the second token.

e.g. `"...\UninstallHelper.exe" {62CF6E28-213B-4D11-B9E9-BC77680AE1FF}`.

Why a wrapper instead of a raw MSI uninstall entry?
----------------------------------------------------
Windows Installer runs uninstalls invoked directly through msiexec (both the
classic default ARP entry and a plain `msiexec /x` with no /q flag) at a reduced
UI level with no access to the interactive desktop, so the installer itself can't
show a password prompt or "delete data?" dialog during that phase - anything it
tried to pop up would just crash. OnionProcOparetor.UninstallHelper.exe sidesteps
this the same way most commercial installers do: each product's Add/Remove
Programs entry points at its own copy of that small .exe directly (launched by
Explorer with full desktop access, not by the Windows Installer engine), which
shows the real prompt and then calls the same `msiexec /x ... UNINSTPWD=...`
command described above on your behalf, passing that product's own ProductCode.
It's the same generic .exe in both products - it resolves the guard/flag/install
paths relative to its own install location, so it needs no per-product changes.
The password check itself is unchanged - still the same salted-hash comparison
against uninstall-guard.json, still enforced by msiexec's own LaunchCondition
even if someone bypasses the window and calls msiexec directly, so there is only
one place that actually decides pass/fail (Client Agent product only - the
Server + Console product has no such gate).

One side effect: neither product appears in the classic `Win32_Product`
WMI/COM enumeration or the visible Add/Remove Programs list under its real MSI
identity (ARPSYSTEMCOMPONENT=1 hides that, deliberately, so only the friendly
entry shows). Both are still fully registered Windows Installer products under
the hood - `msiexec /x`, `Get-Package`, and verbose install logs all still see
them - only the ARP *list UI* and the deprecated Win32_Product query are
affected.

Command-line properties are visible to anyone who can see the running process
list or a verbose install log (`msiexec /l*v`) on this machine - the same as any
other password typed into a command line, or into the uninstall window above.
This gate exists to stop a student on a shared lab login from casually removing
the Agent via Control Panel, not to withstand a determined local attacker with
admin access.
