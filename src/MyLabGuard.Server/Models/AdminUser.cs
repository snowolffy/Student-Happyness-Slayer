namespace MyLabGuard.Server.Models;

/// <summary>
/// บัญชี admin สำหรับ login เข้า Console GUI
/// เก็บเฉพาะ hash + salt ไม่เก็บ password จริงเด็ดขาด (ดู PasswordHasher ในภายหลัง)
/// </summary>
public class AdminUser
{
    public int Id { get; set; }

    public string Username { get; set; } = string.Empty;

    /// <summary>ผลลัพธ์ hash ของ password (PBKDF2)</summary>
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>salt แบบสุ่มเฉพาะ user คนนี้ ผสมก่อน hash กัน rainbow table</summary>
    public string PasswordSalt { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastLoginAt { get; set; }
}