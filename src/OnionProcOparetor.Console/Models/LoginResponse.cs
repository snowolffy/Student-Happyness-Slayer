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

    /// <summary>"Admin" หรือ "Teacher" (WP4b, 2026-10-30) - ใช้ซ่อน/ปิดปุ่มที่ Teacher ใช้ไม่ได้ฝั่ง UI
    /// (Server ตอบ 403 อยู่แล้วถ้าเกิดกดได้ นี่แค่เพื่อ UX)</summary>
    public string Role { get; set; } = "Admin";

    /// <summary>ชื่อที่แสดง - ใช้แทน Username ในแถบล่าง Console และที่อื่นที่คนเห็น (WP4b, 2026-10-30)</summary>
    public string DisplayName { get; set; } = string.Empty;
}
