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
}
