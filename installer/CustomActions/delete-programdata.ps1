# deferred CA, uninstall only - gated entirely by the CustomAction's own Condition in
# OnionProcOparetor.wxs (REMOVE="ALL" AND DELETEDATA="1"), so this script only ever runs when
# the operator explicitly opted in via `msiexec /x ... DELETEDATA=1`. No interactive prompt
# here (see git history - a WinForms Yes/No MessageBox used to live here, but MSI uninstalls
# run at UILevel=3 with no desktop access, so it always crashed). Deletes {commonappdata}
# \OnionProcOparetor - equivalent to CurUninstallStepChanged(usPostUninstall) in the original
# .iss when DeleteAllData was Yes.

$programDataPath = Join-Path $env:ProgramData "OnionProcOparetor"
if (Test-Path $programDataPath) {
    Remove-Item -Path $programDataPath -Recurse -Force -ErrorAction SilentlyContinue
}

exit 0
