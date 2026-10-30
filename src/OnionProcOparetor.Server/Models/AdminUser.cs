namespace OnionProcOparetor.Server.Models;

/// <summary>
/// บัญชี admin สำหรับ login เข้า Console GUI
/// เก็บเฉพาะ hash + salt ไม่เก็บ password จริงเด็ดขาด (ดู PasswordHasher)
/// </summary>
public class AdminUser
{
    public int Id { get; set; }

    public string Username { get; set; } = string.Empty;

    /// <summary>ผลลัพธ์ hash ของ password (PBKDF2)</summary>
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>salt แบบสุ่มเฉพาะ user คนนี้ ผสมก่อน hash กัน rainbow table</summary>
    public string PasswordSalt { get; set; } = string.Empty;

    /// <summary>true = built-in account ("Administrator") ลบไม่ได้ ป้องกัน lockout</summary>
    public bool IsBuiltIn { get; set; } = false;

    /// <summary>true = ยังไม่เคยเปลี่ยน password จาก default (ว่างเปล่า) - ต้องบังคับเปลี่ยนก่อนใช้งานอื่น</summary>
    public bool HasDefaultPassword { get; set; } = false;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastLoginAt { get; set; }

    /// <summary>
    /// ระดับสิทธิ์ของบัญชี (WP4, 2026-10-30) - ดู AuthHelpers.RequireAdmin สำหรับจุดที่ใช้ตรวจ
    /// ค่า default (0) ต้องเป็น Admin เสมอ: migration AddRoleAndDisplayName ที่เพิ่ม column นี้ตั้งให้
    /// บัญชีเดิมทุกบัญชีเป็น Admin หมด (ไม่ให้ใครโดนล็อกออกจากระบบตอน deploy ครั้งแรก)
    /// </summary>
    public AdminRole Role { get; set; } = AdminRole.Admin;

    /// <summary>
    /// ชื่อที่แสดงต่อคนอื่น (WP4, 2026-10-30) - ต่างจาก Username ที่ใช้ login และแก้ไม่ได้
    /// เช่น "Staff session (ครูสมชาย)", "ล็อกโดย ครูสมชาย" ผู้ส่งข้อความ, แถบล่าง Console
    /// เจ้าของบัญชีแก้เองได้ Admin แก้ให้คนอื่นได้ (ดู PUT /api/admin/users/{id}/display-name)
    /// ค่า default ตอนสร้างบัญชีใหม่และตอน migrate บัญชีเดิม = Username
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;
}

/// <summary>
/// ระดับสิทธิ์ของบัญชี admin/teacher (WP4, 2026-10-30) - ห้ามเปลี่ยนลำดับ/ค่าตัวเลขของสมาชิกที่มีอยู่แล้ว
/// เพราะ EF Core เก็บเป็น int ใน SQLite ค่าที่มีอยู่แล้วใน DB จะเพี้ยนทันที (ต่อท้ายเท่านั้น)
/// </summary>
public enum AdminRole
{
    /// <summary>ทำได้ทุกอย่าง - แก้กฎถาวร, ผู้ใช้, ห้อง, ตั้งค่าระบบ รวมถึงงานประจำวันทั้งหมด</summary>
    Admin,

    /// <summary>
    /// คุมห้อง ดูจอ เปิดโหมดสอบ รีเซ็ตรหัสนักเรียน (ถ้าเปิดสิทธิ์) เปิด Staff session ได้
    /// แก้กฎถาวร/ผู้ใช้/ห้อง/ตั้งค่าระบบไม่ได้ - endpoint ที่ห้ามใช้ตอบ 403 (ดู AuthHelpers.RequireAdmin)
    /// </summary>
    Teacher
}
