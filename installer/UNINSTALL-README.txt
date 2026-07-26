Onion ProcOparetor - Uninstall Notes for IT / Teachers
=======================================================

Normal path
-----------
Control Panel > Programs and Features (or Settings > Apps) > Onion ProcOparetor >
Uninstall now opens a small "Uninstall Onion ProcOparetor" window (a UAC prompt
appears first - this is expected, uninstalling requires admin rights).

- If no uninstall password was ever set on this machine, it just asks you to
  confirm, optionally offers a "Also delete database, logs and rules" checkbox,
  and uninstalls.
- If an uninstall password WAS set (Client Agent mode's "Uninstall Protection"
  page during install), the window asks for it first. Wrong password shows
  "Incorrect password. Please try again." and lets you retry - it does not
  silently fail or need to be re-launched.

This window is just a friendly front end - it runs the exact same `msiexec /x`
command described below on your behalf, so there is nothing extra to configure.

Scripted / command-line path (SCCM, remote PowerShell, etc.)
--------------------------------------------------------------
The command-line path still works exactly as before and is unaffected by the
window above - useful for mass/remote uninstalls where no one is at the console
to click through a prompt:

1. Open an ELEVATED command prompt or PowerShell (Run as Administrator).

2. Run:

    msiexec /x "C:\Program Files\Onion ProcOparetor\..." UNINSTPWD=the-password /qn

   Or, if you still have the original installer .msi file:

    msiexec /x "C:\path\to\OnionProcOparetor-Setup.msi" UNINSTPWD=the-password /qn

   Either the .msi path or the product's GUID (visible in the uninstall window's
   title, or in the registry - see "Finding the product GUID" below) works with /x.
   Leave off UNINSTPWD entirely if no password was ever set on this machine.

3. If you also want to wipe the database/logs/rules under ProgramData (equivalent
   to checking the "Also delete..." box in the window above), add DELETEDATA=1:

    msiexec /x "C:\path\to\OnionProcOparetor-Setup.msi" UNINSTPWD=the-password DELETEDATA=1 /qn

   Leaving DELETEDATA out (or setting it to anything other than 1) keeps the data,
   e.g. for a later reinstall - this is the default.

Finding the product GUID
-------------------------
The uninstall window's own ARP entry is a synthetic one (so the friendly window
can appear at all - see "Why a wrapper?" below) and does not carry the real
ProductCode GUID in the registry the way a normal MSI entry would. To find it:
HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\OnionProcOparetor's
UninstallString value has it as the second token, e.g.
`"...\UninstallHelper.exe" {62CF6E28-213B-4D11-B9E9-BC77680AE1FF}`.

Why a wrapper instead of a raw MSI uninstall entry?
----------------------------------------------------
Windows Installer runs uninstalls invoked directly through msiexec (both the
classic default ARP entry and a plain `msiexec /x` with no /q flag) at a reduced
UI level with no access to the interactive desktop, so the installer itself can't
show a password prompt or "delete data?" dialog during that phase - anything it
tried to pop up would just crash. OnionProcOparetor.UninstallHelper.exe sidesteps
this the same way most commercial installers do: Add/Remove Programs' entry for
this product points at that small .exe directly (launched by Explorer with full
desktop access, not by the Windows Installer engine), which shows the real
prompt and then calls the same `msiexec /x ... UNINSTPWD=...` command described
above on your behalf. The password check itself is unchanged - still the same
salted-hash comparison against uninstall-guard.json, still enforced by msiexec's
own LaunchCondition even if someone bypasses the window and calls msiexec
directly, so there is only one place that actually decides pass/fail.

One side effect: this product no longer appears in the classic `Win32_Product`
WMI/COM enumeration or the visible Add/Remove Programs list under its real MSI
identity (ARPSYSTEMCOMPONENT=1 hides that, deliberately, so only the friendly
entry shows). It is still a fully registered Windows Installer product under the
hood - `msiexec /x`, `Get-Package`, and verbose install logs all still see it -
only the ARP *list UI* and the deprecated Win32_Product query are affected.

Command-line properties are visible to anyone who can see the running process
list or a verbose install log (`msiexec /l*v`) on this machine - the same as any
other password typed into a command line, or into the uninstall window above.
This gate exists to stop a student on a shared lab login from casually removing
the program via Control Panel, not to withstand a determined local attacker with
admin access.
