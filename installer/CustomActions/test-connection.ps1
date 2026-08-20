# source ของ -EncodedCommand blob ที่ฝังใน OnionProcOparetor.wxs (CustomAction "TestConnection")
# เก็บไฟล์นี้ไว้เป็น source of truth สำหรับ regenerate blob เวลาต้องแก้ - ตัวไฟล์นี้เองไม่ได้ถูก
# install ไปเครื่อง user หรือถูกเรียกตรงๆ เพราะปุ่ม "Test Connection" ต้องทำงานได้ตั้งแต่ตอนอยู่ใน
# wizard (ก่อน InstallFiles รันด้วยซ้ำ) จึงต้องฝัง fixed script เป็น -EncodedCommand แทนการเรียก
# ไฟล์ที่ยังไม่ถูกติดตั้ง
#
# regenerate blob ด้วย (สำคัญ: ต้อง STRIP comment/blank line ออกก่อน encode เสมอ - cmd.exe /c "..."
# มี command-line length limit ~8191 ตัวอักษร เจอจริงว่า comment header ยาวๆ ทำให้ blob รวมกับ
# `cmd.exe /c "set ...&& powershell.exe ... -EncodedCommand <blob>"` เกิน limit พอดี ล้มเหลวเงียบๆ
# ด้วย "The command line is too long." - ดู comment เดียวกันใน remove-installfolder-extras.ps1
# สำหรับรายละเอียดเต็ม):
#   $lines = Get-Content test-connection.ps1 | Where-Object {
#       $t = $_.Trim(); $t -ne "" -and -not $t.StartsWith("#")
#   }
#   $bytes = [System.Text.Encoding]::Unicode.GetBytes(($lines -join "`n"))
#   [Convert]::ToBase64String($bytes)
#
# ค่า HostPort มาทาง environment variable ONIONPROC_HOSTPORT (ตั้งโดย .wxs ผ่าน
# `cmd.exe /c "set ONIONPROC_HOSTPORT=[SERVERIP]:[SERVERPORT]&& powershell.exe -EncodedCommand
# ..."`), ไม่ใช่ positional argument ($args) แบบที่เคยเข้าใจผิดไว้แต่แรก - พิสูจน์แล้วจริงด้วย
# minimal repro (2026-08-05) ว่า `powershell.exe -EncodedCommand <blob> "extra arg"` ล้มเหลวเสมอ
# ด้วย "Cannot process command because a command is already specified with -Command or
# -EncodedCommand" (ทดสอบบน Windows PowerShell 5.1.19041) - แปลว่าปุ่ม Test Connection ไม่เคยทำงาน
# จริงเลยตั้งแต่ pattern นี้ถูกเขียนขึ้นมา (ไม่มีใครเคยเจอเพราะเป็นปุ่ม optional ที่ silent/scripted
# install ไม่เคยกดผ่าน) เปลี่ยนมาส่งผ่าน environment variable ผ่าน cmd.exe wrapper แทน - ค่า
# HostPort ยังคงไม่ถูก splice เข้าไปในตัว script text เอง (env var value ไม่ถูก parse เป็น
# PowerShell syntax) กัน command injection เหมือนเดิม - SERVERIP ฝั่ง wizard ยังจำกัดตัวอักษรเป็น
# host/IP-safe charset เท่านั้นด้วย (ดู ServerConnDlg's Next button ใน .wxs) เป็นชั้นป้องกันซ้อนสำหรับ
# cmd.exe wrapper ตัวนี้โดยเฉพาะ (& | < > ^ % ในค่าที่ยังไม่ผ่าน validation จะกระทบ cmd.exe parsing ได้)
#
# บั๊กจริงที่เจอ (2026-08-20, field test): script เองรันได้ไม่ error (env var fix ด้านบนถูกต้องแล้ว
# จริง) แต่ MessageBox.Show() ที่ไม่มี owner window จะไม่ถูกบังคับให้ topmost/foreground - เพราะ MSI
# wizard เองเป็น topmost window อยู่แล้วเสมอ (กัน user เผลอสลับไปหน้าต่างอื่นระหว่างติดตั้ง) popup ที่
# spawn มาจาก process แยกต่างหาก (ไม่มี owner handle) จะโผล่ "หลัง" wizard แทนที่จะโผล่ทับด้านหน้า - ครู
# กดปุ่ม Test Connection แล้วดูเหมือนไม่มีอะไรเกิดขึ้นเลย ทั้งที่จริงๆ script ทำงานสำเร็จและ popup ก็ขึ้น
# มาจริง แค่ซ่อนอยู่หลัง wizard - แก้ด้วยการสร้าง hidden topmost owner form เล็กๆ (ย้ายไปนอกจอ กันแฟลช
# ให้เห็น) แล้วส่งเป็น owner ให้ MessageBox.Show() เพื่อบังคับให้ popup โผล่ทับ wizard แน่นอน

if ([string]::IsNullOrWhiteSpace($env:ONIONPROC_HOSTPORT)) { exit 0 }

$parts = $env:ONIONPROC_HOSTPORT -split ':', 2
$hostName = $parts[0]
if ($parts.Count -gt 1 -and $parts[1].Trim() -ne "") { $port = $parts[1] } else { $port = "8787" }

Add-Type -AssemblyName System.Windows.Forms

$owner = New-Object System.Windows.Forms.Form
$owner.StartPosition = "Manual"
$owner.Location = New-Object System.Drawing.Point(-2000, -2000)
$owner.Size = New-Object System.Drawing.Size(1, 1)
$owner.ShowInTaskbar = $false
$owner.FormBorderStyle = "FixedToolWindow"
$owner.TopMost = $true
$owner.Show()
$owner.Activate()

$ok = $false
$errorDetail = ""
$client = $null
try {
    $portNum = [int]$port
    $client = New-Object System.Net.Sockets.TcpClient
    $connectTask = $client.ConnectAsync($hostName, $portNum)
    if ($connectTask.Wait(5000)) {
        $ok = $client.Connected
        if (-not $ok) { $errorDetail = "Connection did not complete." }
    }
    else {
        $errorDetail = "No response within 5 seconds."
    }
}
catch {
    $ok = $false
    $errorDetail = $_.Exception.Message
}
finally {
    if ($client) { $client.Close() }
}

if ($ok) {
    [System.Windows.Forms.MessageBox]::Show($owner,
        "Connection successful! The Server is reachable at ${hostName}:${port}.",
        "Onion ProcOparetor",
        [System.Windows.Forms.MessageBoxButtons]::OK,
        [System.Windows.Forms.MessageBoxIcon]::Information) | Out-Null
}
else {
    $reasonLine = ""
    if ($errorDetail -ne "") { $reasonLine = "`r`n`r`nDetails: $errorDetail" }
    [System.Windows.Forms.MessageBox]::Show($owner,
        "Could not reach the Server at ${hostName}:${port} within 5 seconds.`r`n" +
        "This may be normal if the Server is not running yet, the address/port is wrong, or a firewall is blocking it.$reasonLine",
        "Onion ProcOparetor",
        [System.Windows.Forms.MessageBoxButtons]::OK,
        [System.Windows.Forms.MessageBoxIcon]::Warning) | Out-Null
}

$owner.Close()
exit 0
