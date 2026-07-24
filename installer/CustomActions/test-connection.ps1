# source ของ -EncodedCommand blob ที่ฝังใน OnionProcOparetor.wxs (CustomAction "TestConnection")
# เก็บไฟล์นี้ไว้เป็น source of truth สำหรับ regenerate blob เวลาต้องแก้ - ตัวไฟล์นี้เองไม่ได้ถูก
# install ไปเครื่อง user หรือถูกเรียกตรงๆ เพราะปุ่ม "Test Connection" ต้องทำงานได้ตั้งแต่ตอนอยู่ใน
# wizard (ก่อน InstallFiles รันด้วยซ้ำ) จึงต้องฝัง fixed script เป็น -EncodedCommand แทนการเรียก
# ไฟล์ที่ยังไม่ถูกติดตั้ง - regenerate blob ด้วย:
#   $bytes = [System.Text.Encoding]::Unicode.GetBytes((Get-Content -Raw test-connection.ps1))
#   [Convert]::ToBase64String($bytes)
# ค่า HostPort ถูกส่งเข้ามาทาง positional argument ($args / param binding) เท่านั้น ไม่เคยถูก
# splice เข้าไปในตัว script text เอง - กัน command injection จากค่าที่ผู้ใช้พิมพ์ในช่อง
# Server Connection ของ wizard (ดู comment ใน .wxs ที่เรียกใช้ CustomAction นี้)

param([string]$HostPort = "localhost:8787")

$parts = $HostPort -split ':', 2
$hostName = $parts[0]
if ($parts.Count -gt 1 -and $parts[1].Trim() -ne "") { $port = $parts[1] } else { $port = "8787" }

Add-Type -AssemblyName System.Windows.Forms

try {
    $ok = Test-NetConnection -ComputerName $hostName -Port $port -InformationLevel Quiet -WarningAction SilentlyContinue
}
catch {
    $ok = $false
}

if ($ok) {
    [System.Windows.Forms.MessageBox]::Show(
        "Connection successful! The Server is reachable.",
        "Onion ProcOparetor",
        [System.Windows.Forms.MessageBoxButtons]::OK,
        [System.Windows.Forms.MessageBoxIcon]::Information) | Out-Null
}
else {
    [System.Windows.Forms.MessageBox]::Show(
        "Could not reach the Server at this address.`r`n" +
        "This may be normal if the Server is not running yet, or firewall is blocking it.",
        "Onion ProcOparetor",
        [System.Windows.Forms.MessageBoxButtons]::OK,
        [System.Windows.Forms.MessageBoxIcon]::Warning) | Out-Null
}

exit 0
