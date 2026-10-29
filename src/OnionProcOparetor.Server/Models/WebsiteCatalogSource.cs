namespace OnionProcOparetor.Server.Models;

/// <summary>สถานะการ sync ล่าสุดของ WebsiteCatalogSource หนึ่งตัว</summary>
public enum WebsiteCatalogSyncStatus
{
    NeverRun = 0,
    Success = 1,
    Failed = 2,

    /// <summary>list ที่ดึงมาเกิน cap ที่ตั้งไว้ - ไม่ import อะไรเลยจนกว่าจะแก้ source/cap
    /// (ดู WebsiteCatalogSyncer - กันเหตุการณ์แบบ blocklistproject/gambling.txt ที่มี 342,627 โดเมน)</summary>
    SkippedTooLarge = 3
}

/// <summary>
/// แหล่งข้อมูล URL ภายนอก (hosts-file format หรือ plain domain list ทีละบรรทัด) ที่ Server จะดึงมา
/// sync เข้า WebsiteRule เป็นระยะ (2026-09-12) - ดู Services/WebsiteCatalogSyncer.cs สำหรับกลไกเต็ม
///
/// ตั้งใจให้ admin พิมพ์ URL อะไรก็ได้เอง ไม่ hardcode แหล่งข้อมูลตายตัว แต่ต้องมี MaxEntries เป็น
/// เกราะกันเหตุ - list สาธารณะบางอันมีเป็นแสนโดเมน (typosquat/pattern-generated) ซึ่งไม่มีทางใช้กับ
/// กลไก registry policy ของเราได้จริง (ดู WebsiteCatalogSyncer.AbsoluteMaxEntries)
/// </summary>
public class WebsiteCatalogSource
{
    public int Id { get; set; }

    /// <summary>ชื่อที่ตั้งเอง (ไว้แสดงผลใน GUI) เช่น "Fortnite Block List"</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>URL ของไฟล์รายชื่อโดเมน (hosts-file format หรือ domain ทีละบรรทัดก็ได้ - ดู parser)</summary>
    public string SourceUrl { get; set; } = string.Empty;

    public bool IsEnabled { get; set; } = true;

    /// <summary>เพดานจำนวนโดเมนที่ยอมรับจาก source นี้ - เกินนี้ถือว่า sync ล้มเหลว ไม่ import อะไรเลย</summary>
    public int MaxEntries { get; set; } = 1000;

    public DateTime? LastSyncedAt { get; set; }

    public WebsiteCatalogSyncStatus LastSyncStatus { get; set; } = WebsiteCatalogSyncStatus.NeverRun;

    /// <summary>จำนวนโดเมนจริงที่เจอในรอบ sync ล่าสุด (แม้ตอนที่ถูกปฏิเสธเพราะเกิน cap ก็เก็บไว้ให้เห็น)</summary>
    public int? LastSyncEntryCount { get; set; }

    public string? LastSyncError { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
