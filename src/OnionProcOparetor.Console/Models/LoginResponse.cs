namespace OnionProcOparetor.Console.Models;

/// <summary>ผลลัพธ์จาก POST /api/auth/login</summary>
public class LoginResponse
{
    public string Message { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Token { get; set; } = string.Empty;

    /// <summary>true = ยังไม่เคยเปลี่ยน password จาก default (ว่างเปล่า) - ต้องบังคับเปลี่ยนก่อนใช้งานอื่น</summary>
    public bool HasDefaultPassword { get; set; }

    /// <summary>Id ของ admin คนนี้ - ใช้เรียก /api/admin/users/{id}/change-password</summary>
    public int AdminId { get; set; }
}
