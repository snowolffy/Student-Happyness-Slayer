# เรียก POST /api/auth/setup ให้อัตโนมัติหลัง OnionCoreService service start เสร็จ
# แยกเป็นไฟล์ .ps1 ต่างหาก (แทนที่จะ inline ใน Inno Setup [Run] Parameters โดยตรง)
# เพราะ inline PowerShell ใน Inno Setup เสี่ยงเรื่อง escape อักขระพิเศษ ({ } " ') สูงมาก
# โดยเฉพาะ nested curly braces ของ for/try/catch ที่ชนกับ syntax ของ Inno Setup constants เอง

$ErrorActionPreference = "Stop"
$maxAttempts = 15
$delaySeconds = 2
$setupUrl = "http://localhost:8787/api/auth/setup"

for ($i = 0; $i -lt $maxAttempts; $i++) {
    try {
        Invoke-RestMethod -Uri $setupUrl -Method Post -TimeoutSec 3 | Out-Null
        # สำเร็จ (หรือ admin มีอยู่แล้ว - ทั้งสองกรณีถือว่าจบงานนี้แล้ว)
        exit 0
    }
    catch {
        # server อาจยังไม่พร้อมรับ request (เพิ่ง start ไม่ทัน) หรือ admin มีอยู่แล้ว (400 Bad Request)
        # ทั้งสองกรณีลอง retry ไปเรื่อยๆ จนครบ maxAttempts ไม่ throw ออกไปให้ installer เห็น error
        Start-Sleep -Seconds $delaySeconds
    }
}

# ลองครบ maxAttempts แล้วยังไม่สำเร็จ - ไม่ throw error ออกไป (installer ไม่ควร fail เพราะจุดนี้)
# ปล่อยให้ user เรียก setup เองด้วยมือทีหลังได้ถ้าจำเป็น
exit 0
