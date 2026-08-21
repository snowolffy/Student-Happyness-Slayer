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
#
# บั๊กจริงที่เจอ (2026-08-20, field test, รอบที่ 1): ลบไม่หมดเป็นบางครั้ง - เพิ่ม retry สั้นๆ ไม่กี่รอบ
# (เผื่อ file handle ปล่อยตามทัน) และ log รายชื่อไฟล์ที่ยังลบไม่ได้ไว้ (นอกโฟลเดอร์เป้าหมายเอง เพราะโฟลเดอร์
# นั้นกำลังจะถูกลบ) ให้ diagnose ได้ - ไม่ทำให้ uninstall fail ไม่ว่ากรณีไหน (ลบข้อมูลเป็นแค่ best-effort)
#
# บั๊กจริงที่เจอ (2026-08-20, field test, รอบที่ 2 - ตัวใหญ่กว่า): retry ก็ยังไม่ช่วย - log ที่เพิ่งเพิ่มบอก
# ชัดว่า onionprocoparetor.db/-shm/-wal ยัง lock อยู่ครบ 3 ไฟล์ แต่พอ uninstall เสร็จสมบูรณ์แล้วลองลบด้วย
# มือกลับลบได้ทันทีไม่มี error เลย (ยืนยันจาก live-test: Task Manager ไม่เห็น OnionCoreService.exe เหลือ
# อยู่แล้วตอนนั้น) - ต้นตอจริงอยู่ที่การ schedule ใน .wxs ไม่ใช่ script นี้: CustomAction ฝั่ง .wxs เดิมใช้
# Execute="immediate" ซึ่งรันทันทีตอน engine เดินมาถึงจุดนั้นใน sequence table - แต่ StopServices/RemoveFiles
# ที่แท้จริงเป็น "deferred" standard action แค่ถูก "คิว" ไว้ตอนนั้น ยังไม่ได้รันจริง (deferred action ทั้งหมด
# จะไปรันเป็นชุดพร้อมกันตอน InstallFinalize ท้ายสุดของ sequence) เท่ากับ DeleteProgramData (immediate)
# วิ่งไปลบ DB ก่อนที่ service จะถูกสั่งหยุดจริงด้วยซ้ำ - retry แค่ไม่กี่วิไม่มีทางช่วยได้เลยเพราะ service ยังไม่
# แม้แต่จะเริ่มหยุดในช่วงเวลานั้น แก้โดยเปลี่ยน CustomAction ฝั่ง .wxs กลับไปเป็น Execute="deferred" (pattern
# เดียวกับ SetupClient/SetupServer เดิม) ให้ไปอยู่ในคิวเดียวกับ StopServices/RemoveFiles ตามลำดับจริง แทนที่
# จะรันแซงหน้า - ตัว script logic ในไฟล์นี้ (retry+log) ยังมีประโยชน์อยู่เผื่อกรณี handle ปล่อยช้าจริงๆ แม้จะ
# schedule ถูกที่แล้วก็ตาม เลยเก็บไว้ไม่ได้ตัดออก

$programDataPath = Join-Path $env:ProgramData "OnionProcOparetor"

if (Test-Path $programDataPath) {
    $maxAttempts = 5
    $delayMs = 500

    for ($attempt = 1; $attempt -le $maxAttempts; $attempt++) {
        Remove-Item -Path $programDataPath -Recurse -Force -ErrorAction SilentlyContinue

        if (-not (Test-Path $programDataPath)) {
            break
        }

        Start-Sleep -Milliseconds $delayMs
        $delayMs = [Math]::Min($delayMs * 2, 4000)
    }

    if (Test-Path $programDataPath) {
        try {
            $leftoverLogDir = Join-Path $env:ProgramData "OnionProcOparetorCleanup"
            New-Item -ItemType Directory -Path $leftoverLogDir -Force -ErrorAction SilentlyContinue | Out-Null

            $leftovers = Get-ChildItem -Path $programDataPath -Recurse -Force -ErrorAction SilentlyContinue |
                Select-Object -ExpandProperty FullName
            $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
            $logLine = "[$timestamp] ลบ $programDataPath ไม่หมดหลังลอง $maxAttempts รอบ - เหลือ:`r`n$($leftovers -join "`r`n")"
            Add-Content -Path (Join-Path $leftoverLogDir "delete-programdata-leftovers.log") -Value $logLine -Encoding UTF8 -ErrorAction SilentlyContinue
        }
        catch {
            # log ไม่สำเร็จก็ไม่เป็นไร - best effort เท่านั้น ไม่ทำให้ uninstall fail
        }
    }
}

exit 0
