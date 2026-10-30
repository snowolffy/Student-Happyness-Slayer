namespace OnionProcOparetor.OnlineConsole.Models;

/// <summary>Ported from OnionProcOparetor.Console/Models/UserDto.cs (identical - GET /api/admin/users)</summary>
public class UserDto
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public bool IsBuiltIn { get; set; }
    public bool HasDefaultPassword { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastLoginAt { get; set; }

    /// <summary>"Admin" หรือ "Teacher" (WP4, 2026-10-30) - Server ส่งเป็น string เสมอ (ดู UserEndpoints.cs)</summary>
    public string Role { get; set; } = "Admin";

    /// <summary>ชื่อที่แสดง - ต่างจาก Username ที่ใช้ login (WP4, 2026-10-30)</summary>
    public string DisplayName { get; set; } = string.Empty;
}
