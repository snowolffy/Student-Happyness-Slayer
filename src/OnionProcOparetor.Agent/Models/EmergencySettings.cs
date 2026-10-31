namespace OnionProcOparetor.Agent.Models;

/// <summary>
/// ตั้งค่าที่ผูกกับ appsettings.json ส่วน "EmergencySettings" (WP3-4, 2026-10-31) - ค่าใน template
/// (appsettings.json ที่ build มา) เป็นค่าว่างเสมอ ถูก patch เข้ามาจริงตอนติดตั้ง MSI เท่านั้น (ดู
/// installer/CustomActions/setup-client.ps1 ฟังก์ชัน Patch-EmergencySecret - มา MSI property
/// EMERGENCYTOTPSECRET) - ต้องมาจาก MSI เพราะ disk freeze ลบไฟล์ที่เขียนตอน runtime ทุก reboot
/// </summary>
public class EmergencySettings
{
    /// <summary>Base32 secret ของ TOTP - ว่าง/null = ยังไม่ได้ตั้งค่า (ปิดฟีเจอร์นี้ไปเลยสำหรับเครื่องนั้น)</summary>
    public string? Secret { get; set; }
}
