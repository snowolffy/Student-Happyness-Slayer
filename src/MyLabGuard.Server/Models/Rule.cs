namespace MyLabGuard.Server.Models;

/// <summary>
/// กฎสำหรับจับคู่ publisher ของโปรแกรมที่รันบนเครื่อง client
/// แล้วกำหนด action ที่จะทำเมื่อ match
/// </summary>
public class Rule
{
    public int Id { get; set; }

    /// <summary>ชื่อกฎ (ไว้แสดงผลใน GUI)</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// ชื่อ publisher ที่ต้องการจับคู่ (เช่น "Valve Corporation")
    /// รองรับ exact match ก่อน จะเพิ่ม wildcard ทีหลังได้
    /// </summary>
    public string PublisherName { get; set; } = string.Empty;

    /// <summary>
    /// true = ต้อง match จาก digital signature เท่านั้นถึงจะนับ
    /// false = ยอมรับ match จาก metadata (CompanyName) ได้ด้วย (ความน่าเชื่อถือต่ำกว่า)
    /// </summary>
    public bool RequireSignedMatch { get; set; } = true;

    /// <summary>คำสั่ง/สคริปต์ที่จะรันเมื่อ match (path ของ .exe หรือ .bat/.ps1)</summary>
    public string? ActionCommand { get; set; }

    /// <summary>arguments ที่จะส่งให้ ActionCommand</summary>
    public string? ActionArguments { get; set; }

    /// <summary>กฎนี้เปิดใช้งานอยู่ไหม</summary>
    public bool IsEnabled { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}