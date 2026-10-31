namespace OnionProcOparetor.Server.Models;

/// <summary>
/// รหัสฉุกเฉิน TOTP ทั้งระบบ (WP3-4, 2026-10-31) - เก็บเป็น single row เดียว (Id = 1) เหมือน
/// SignInSettings/BackupSettings สร้างแบบ lazy ตอนอ่านครั้งแรก (GetOrCreateEmergencyCodeSettingsAsync)
/// ไม่ใช้ HasData seed เพราะ secret ต้องสุ่มใหม่ทุกการติดตั้ง (ห้ามมีค่า default คงที่ใน migration -
/// ต่างจาก SignInSettings/BackupSettings ที่ field อื่นๆ เป็นค่าคงที่ปลอดภัยที่จะ seed ได้)
///
/// Secret ตัวนี้คือต้นทางเดียวที่ Console ใช้สร้าง QR/ทดสอบรหัส - ไม่ได้ใช้ตรวจรหัสที่เครื่องนักเรียน
/// เลย (Agent เก็บสำเนาของตัวเองจาก MSI property ตอนติดตั้ง ตรวจ "offline" ได้โดยไม่ต้องถาม Server -
/// ดู docs/roadmap.md WP3-4) ครูต้อง copy ค่านี้ไปใส่ MSI/Apps Script เองตอน rotate
/// </summary>
public class EmergencyCodeSettings
{
    public int Id { get; set; }

    /// <summary>Base32 (RFC 4648, ไม่มี padding) - 160 บิต</summary>
    public string SecretBase32 { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>null = ยังไม่เคยเปลี่ยนตั้งแต่สร้างครั้งแรก</summary>
    public DateTime? RotatedAt { get; set; }

    /// <summary>username ของ Admin ที่กด "เปลี่ยน secret ใหม่" ครั้งล่าสุด - null ถ้ายังไม่เคย rotate</summary>
    public string? RotatedByUsername { get; set; }
}
