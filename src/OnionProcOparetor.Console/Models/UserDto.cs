namespace OnionProcOparetor.Console.Models;

/// <summary>ข้อมูล admin user ที่ได้จาก GET /api/admin/users (ไม่มี password hash/salt)</summary>
public class UserDto
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public bool IsBuiltIn { get; set; }
    public bool HasDefaultPassword { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastLoginAt { get; set; }

    /// <summary>"Admin" หรือ "Teacher" (WP4b, 2026-10-30) - Server ส่งเป็น string เสมอ (ดู UserEndpoints.cs)</summary>
    public string Role { get; set; } = "Admin";

    /// <summary>ชื่อที่แสดง - ต่างจาก Username ที่ใช้ login และแก้ไม่ได้ (WP4b, 2026-10-30)</summary>
    public string DisplayName { get; set; } = string.Empty;
}
