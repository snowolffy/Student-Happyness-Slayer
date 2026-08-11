# source ของ -EncodedCommand blob ที่ฝังใน OnionProcOparetor.wxs (CustomAction
# "RemoveInstallFolderExtras") เก็บไว้เป็น source of truth สำหรับ regenerate blob - ตัวไฟล์นี้เอง
# ไม่ได้ถูก install ไปเครื่อง user
#
# regenerate blob ด้วย (สำคัญ: ต้อง STRIP comment/blank line ออกก่อน encode เสมอ - ดูเหตุผลด้านล่าง):
#   $lines = Get-Content remove-installfolder-extras.ps1 | Where-Object {
#       $t = $_.Trim(); $t -ne "" -and -not $t.StartsWith("#")
#   }
#   $bytes = [System.Text.Encoding]::Unicode.GetBytes(($lines -join "`n"))
#   [Convert]::ToBase64String($bytes)
#
# ทำไมต้อง strip comment ก่อน encode (2026-08-05 เจอจริง): cmd.exe /c "..." (ดูว่าทำไมต้องมี cmd.exe
# เข้ามาเกี่ยวข้องเลยในคอมเมนต์ด้านล่าง) มี command-line length limit ประมาณ 8191 ตัวอักษร - ตัว
# script นี้เดิมมี comment header ภาษาไทยยาวมาก พอ encode เป็น UTF-16 + Base64 (ขยายขนาดประมาณ 4
# เท่าจาก raw text) รวมกับ `cmd.exe /c "set ...&& powershell.exe ... -EncodedCommand <blob>"` แล้ว
# เกิน limit พอดี - CustomAction ล้มเหลวเงียบๆ ด้วย "The command line is too long." (Return="ignore"
# กลืน error ไว้ ไม่มีอะไรบอกตรงๆ ว่าเกิดจากอะไร ต้อง reproduce คำสั่งเต็มเองถึงเจอ) comment/เอกสาร
# ทั้งหมดในไฟล์นี้ยังคงมีไว้เพื่อคนอ่านโค้ด แต่ตอน generate blob ต้อง strip ออกก่อนเสมอ
#
# ทำไมต้องเป็น -EncodedCommand ฝังคงที่ (ไม่เรียกไฟล์ .ps1 ที่ install ไว้แบบ setup-client.ps1/
# delete-programdata.ps1): จุดประสงค์ของ action นี้คือลบ INSTALLFOLDER ทั้งก้อนเป็นก้าวสุดท้ายของ
# uninstall (รวมไฟล์ที่ util:RemoveFolderEx ควรจะลบให้แต่ลบไม่ได้จริง - ดู comment ยาวใน .wxs) ถ้า
# เขียนเป็นไฟล์ .ps1 แยกแล้วให้ INSTALLFOLDER\CustomActions\<script>.ps1 เรียกตัวเอง จะเจอปัญหา
# "ลบพื้นที่ที่ตัวเองยืนอยู่" (RemoveFiles ที่รันก่อนหน้าอาจลบไฟล์สคริปต์นี้ไปแล้ว หรือ Remove-Item
# บนโฟลเดอร์ที่มีสคริปต์กำลังรันอยู่ข้างในอาจ fail กลางคัน) - ฝังเป็น -EncodedCommand ในตัว msiexec/
# powershell.exe เองแทนจึงไม่ต้องพึ่งไฟล์ใดๆ ใน INSTALLFOLDER เลยระหว่างทำงาน
#
# ค่า InstallDir มาทาง environment variable ONIONPROC_INSTALLDIR (ตั้งโดย .wxs ผ่าน
# `cmd.exe /c "set ONIONPROC_INSTALLDIR=[INSTALLFOLDER]&& powershell.exe -EncodedCommand ..."`)
# ไม่ใช่ positional argument ($args) แบบที่เคยเข้าใจผิดไว้ - พิสูจน์แล้วจริงด้วย minimal repro ว่า
# `powershell.exe -EncodedCommand <blob> "extra arg"` ล้มเหลวเสมอด้วย "Cannot process command
# because a command is already specified with -Command or -EncodedCommand" (ทดสอบบน Windows
# PowerShell 5.1.19041) - ไม่รองรับ trailing positional argument ต่อจาก -EncodedCommand เลยไม่ว่า
# กรณีใด แม้แต่ test-connection.ps1 เดิมที่ใช้ pattern เดียวกันมาก่อนก็ใช้งานไม่ได้จริงมาตลอด (ไม่มีใคร
# เคยเจอเพราะมันเป็นปุ่ม optional ใน UI ที่ silent install ไม่เคยกดผ่าน) เปลี่ยนมาส่งผ่าน environment
# variable ผ่าน cmd.exe wrapper แทน - ยังคงไม่ splice ค่าเข้าไปในตัว script text เอง (env var value
# ไม่ถูก parse เป็น PowerShell syntax) กัน command injection เหมือนเดิม

if ([string]::IsNullOrWhiteSpace($env:ONIONPROC_INSTALLDIR)) { exit 0 }

# หน่วงเล็กน้อยกันชนกับ handle ของไฟล์ที่ RemoveFiles เพิ่งปิดไป (best-effort, ไม่ critical)
Start-Sleep -Milliseconds 500

try {
    Remove-Item -LiteralPath $env:ONIONPROC_INSTALLDIR -Recurse -Force -ErrorAction SilentlyContinue
}
catch {
    # best-effort cleanup เท่านั้น - ไม่ทำให้ uninstall fail ถ้าลบไม่หมด (เช่นไฟล์ยังถูก lock อยู่)
}

exit 0
