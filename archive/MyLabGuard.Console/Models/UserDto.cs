namespace MyLabGuard.Console.Models;

/// <summary>ข้อมูล admin user ที่ได้จาก GET /api/admin/users (ไม่มี password hash/salt)</summary>
public class UserDto
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public bool IsBuiltIn { get; set; }
    public bool HasDefaultPassword { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastLoginAt { get; set; }
}