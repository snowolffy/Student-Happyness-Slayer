Onion ProcOparetor - Uninstall Notes for IT / Teachers
=======================================================

If an uninstall password was set during install (Client Agent mode's "Uninstall
Protection" page), the normal Control Panel > Programs and Features > Uninstall
button will FAIL with this message:

    This installation is password-protected. To uninstall, run from an elevated
    command prompt: msiexec /x {PRODUCT-CODE-GUID} UNINSTPWD=<password>.
    Contact your system administrator if you don't have the password.

This is expected. Uninstall it correctly instead:

1. Open an ELEVATED command prompt or PowerShell (Run as Administrator).

2. Run:

    msiexec /x "C:\Program Files\Onion ProcOparetor\..." UNINSTPWD=the-password

   Or, if you still have the original installer .msi file:

    msiexec /x "C:\path\to\OnionProcOparetor-Setup.msi" UNINSTPWD=the-password

   Either the .msi path or the product's GUID (shown in the error message above,
   and in Programs and Features' "more information" / registry) works with /x.

3. If you also want to wipe the database/logs/rules under ProgramData (equivalent
   to answering "Yes" to the old "delete all data?" prompt), add DELETEDATA=1:

    msiexec /x "C:\path\to\OnionProcOparetor-Setup.msi" UNINSTPWD=the-password DELETEDATA=1

   Leaving DELETEDATA out (or setting it to anything other than 1) keeps the data,
   e.g. for a later reinstall - this is the default, matching the old installer's
   "Choose No if you plan to reinstall later" guidance.

4. If NO uninstall password was ever set for that machine, none of this applies -
   Control Panel's normal Uninstall button works exactly as before, no properties
   needed.

Why no popup?
-------------
Windows Installer runs uninstalls (both Control Panel's button and a plain
`msiexec /x` with no /q flag) at a reduced UI level with no access to the
interactive desktop, so this installer can't show its own password prompt or
"delete data?" dialog during uninstall - anything it tried to pop up would just
crash. The password and delete-data choice are passed as command-line properties
instead. This is a deliberate, permanent design choice, not a bug to work around
with a fancier popup.

Command-line properties are visible to anyone who can see the running process
list or a verbose install log (`msiexec /l*v`) on this machine - the same as any
other password typed into a command line. This gate exists to stop a student on
a shared lab login from casually removing the program via Control Panel, not to
withstand a determined local attacker with admin access.
