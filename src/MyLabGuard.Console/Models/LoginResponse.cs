namespace MyLabGuard.Console.Models;

/// <summary>ผลลัพธ์จาก POST /api/auth/login</summary>
public class LoginResponse
{
    public string Message { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Token { get; set; } = string.Empty;
}