# source ของ -EncodedCommand blob ที่ฝังใน OnionProcOparetor.ServerConsole.wxs (CustomAction
# "DeleteProgramData") เก็บไว้เป็น source of truth สำหรับ regenerate blob - ตัวไฟล์นี้เองไม่ได้ถูก
# install ไปเครื่อง user อีกต่อไป (regenerate ด้วย pattern เดียวกับ remove-installfolder-extras.ps1:
# strip comment/blank line ก่อน encode เป็น UTF-16 + Base64)
#
# บั๊กจริงที่เจอ (2026-08-20): เดิมไฟล์นี้ยัง install จริงแล้วให้ deferred CA เรียกผ่าน
# `powershell.exe -File [#DeleteProgramDataPs1]` เหมือน setup-client.ps1/run-setup.ps1 แต่ต่างจาก
# 2 ตัวนั้นตรงที่ action นี้ schedule ไว้ After="RemoveFiles" ตอน uninstall - พอถึงตอนรันจริง ไฟล์นี้
# (component ของมันเองก็โดน REMOVE=ALL ลบไปด้วย) ไม่มีอยู่บนดิสก์อีกต่อไปแล้ว ทำให้ deferred CA ล้มเหลว
# เงียบๆ ทุกครั้ง (Return="ignore") = ติ๊ก "Delete data" ตอน uninstall ไม่เคยลบอะไรจริงเลย บทเรียน
# เดียวกับที่ remove-installfolder-extras.ps1's comment อธิบายไว้แล้ว (ลบพื้นที่ที่ตัวเองยืนอยู่ไม่ได้)
# แก้ด้วยการฝังเป็น -EncodedCommand ตรงๆ ใน .wxs แทน ไม่พึ่งไฟล์ที่ install ไว้อีกต่อไป
#
# deferred CA, uninstall only - gated entirely by the CustomAction's own Condition in
# OnionProcOparetor.ServerConsole.wxs (REMOVE="ALL" AND DELETEDATA="1"), so this script only ever
# runs when the operator explicitly opted in via `msiexec /x ... DELETEDATA=1`. No interactive
# prompt here (see git history - a WinForms Yes/No MessageBox used to live here, but MSI
# uninstalls run at UILevel=3 with no desktop access, so it always crashed). Deletes
# {commonappdata}\OnionProcOparetor - equivalent to CurUninstallStepChanged(usPostUninstall) in
# the original .iss when DeleteAllData was Yes.

$programDataPath = Join-Path $env:ProgramData "OnionProcOparetor"
if (Test-Path $programDataPath) {
    Remove-Item -Path $programDataPath -Recurse -Force -ErrorAction SilentlyContinue
}

exit 0
