namespace OnionProcOparetor.Server.Models;

/// <summary>
/// บัญชี admin สำหรับ login เข้า Console GUI
/// </summary>
public class AdminUser
{
    public int Id { get; set; }

    public string Username { get; set; } = string.Empty;

    public string PasswordHash { get; set; } = string.Empty;

    public string PasswordSalt { get; set; } = string.Empty;

    public bool IsBuiltIn { get; set; } = false;

    public bool HasDefaultPassword { get; set; } = false;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastLoginAt { get; set; }
}
