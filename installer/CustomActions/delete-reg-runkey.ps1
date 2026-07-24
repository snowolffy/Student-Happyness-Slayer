# immediate CA, uninstall only - gated entirely by the CustomAction's own Condition in
# OnionProcOparetor.wxs (REMOVE="ALL" AND DELETEDATA="1"). Runs in the elevated engine
# process (not the interactive user's session) same as every InstallExecuteSequence action,
# so HKCU here is SYSTEM's own hive, not the real user's - this key was originally written by
# AgentTray to ITS OWN HKCU at first run, under whichever user account that was, so this
# best-effort cleanup can silently miss it. Known limitation, not a crash risk (registry
# writes never throw here even when they no-op).

Remove-ItemProperty -Path "HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run" `
    -Name "OnionProcOparetorAgentTray" -ErrorAction SilentlyContinue

exit 0
