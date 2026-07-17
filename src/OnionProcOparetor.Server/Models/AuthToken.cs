namespace OnionProcOparetor.Server.Models;

/// <summary>
/// Token ที่ออกให้ Console GUI หลัง login สำเร็จ
/// ใช้แนบมากับ request header เพื่อยืนยันตัวตนแทนการส่ง password ซ้ำทุกครั้ง
/// </summary>
public class AuthToken
{
    public int Id { get; set; }

    /// <summary>token string แบบสุ่ม (ไม่ใช่ JWT ตอนนี้ - ง่ายและพอเพียงสำหรับ LAN ปิด)</summary>
    public string Token { get; set; } = string.Empty;

    public int AdminUserId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>token หมดอายุเมื่อไหร่ - กันเคส token หลุดแล้วใช้ได้ตลอดไป</summary>
    public DateTime ExpiresAt { get; set; }
}
