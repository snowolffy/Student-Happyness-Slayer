namespace OnionProcOparetor.RosterManager.Models;

/// <summary>ผลลัพธ์จาก POST /api/auth/login - รูปร่างเดียวกับ Console/Models/LoginResponse.cs</summary>
public class LoginResponse
{
    public string Message { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Token { get; set; } = string.Empty;
    public bool HasDefaultPassword { get; set; }
    public int AdminId { get; set; }
    public string Role { get; set; } = "Admin";
    public string DisplayName { get; set; } = string.Empty;
}
