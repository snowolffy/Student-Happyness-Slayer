namespace OnionProcOparetor.Server.Models;

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
    /// เป็นเงื่อนไข "บังคับ" เสมอ - ทุก match ต้องผ่านตัวนี้ก่อน
    /// </summary>
    public string PublisherName { get; set; } = string.Empty;

    /// <summary>
    /// [OPTIONAL] ตัวกรองเสริมเพื่อ narrow การ match ให้แคบลงกว่าแค่ publisher เฉยๆ
    /// เช็คแบบ "contains" (case-insensitive) กับชื่อไฟล์ .exe เช่น "notepad" จะ match ทั้ง "notepad.exe", "notepad2.exe"
    /// ถ้าเว้นว่างไว้ (null/empty) จะ fallback เป็น publisher-only เหมือนเดิมทุกอย่าง (backward compatible)
    /// ประโยชน์: กันพลาดกรณี publisher เดียวกันมีหลายโปรแกรม (เช่น Microsoft Corporation เซ็นทั้ง Notepad และ VS Code)
    /// </summary>
    public string? ProcessNameContains { get; set; }

    /// <summary>
    /// true = ต้อง match จาก digital signature เท่านั้นถึงจะนับ
    /// false = ยอมรับ match จาก metadata (CompanyName) ได้ด้วย (ความน่าเชื่อถือต่ำกว่า)
    /// </summary>
    public bool RequireSignedMatch { get; set; } = true;

    /// <summary>true = สั่ง kill process ทันทีที่ match (ก่อนรัน ActionCommand ถ้ามี)</summary>
    public bool KillProcess { get; set; } = false;

    /// <summary>คำสั่ง/สคริปต์ที่จะรันเมื่อ match (path ของ .exe หรือ .bat/.ps1)</summary>
    public string? ActionCommand { get; set; }

    /// <summary>arguments ที่จะส่งให้ ActionCommand</summary>
    public string? ActionArguments { get; set; }

    /// <summary>กฎนี้เปิดใช้งานอยู่ไหม</summary>
    public bool IsEnabled { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
