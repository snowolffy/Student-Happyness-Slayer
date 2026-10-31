namespace OnionProcOparetor.Server.Models;

/// <summary>
/// ตั้งค่าสำรองข้อมูล DB (WP3-1, 2026-10-31) - เก็บเป็น single row เดียว (Id = 1) เหมือน GlobalState.cs
/// ตรงตาม wireframe docs/wireframes/nf-settings-more.dc.html หัวข้อ "สำรองข้อมูล"
/// ใช้คู่กับ Services/DatabaseBackupService.cs (ทำสำรองจริง) และ Services/DatabaseBackupSchedulerService.cs
/// (background service ที่เดินตามความถี่นี้)
/// </summary>
public class BackupSettings
{
    public int Id { get; set; }

    public BackupFrequency Frequency { get; set; } = BackupFrequency.Daily;

    /// <summary>เก็บย้อนหลังกี่ชุด - เกินนี้ลบไฟล์เก่าอัตโนมัติ (เรียงตามเวลาสร้างไฟล์)</summary>
    public int RetentionCount { get; set; } = 14;

    /// <summary>ที่เก็บไฟล์สำรอง - null/ว่าง = ใช้ค่า default (%ProgramData%\OnionProcOparetor\backups\)</summary>
    public string? StoragePath { get; set; }

    public DateTime? LastBackupAt { get; set; }
    public long? LastBackupSizeBytes { get; set; }
    public string? LastBackupFileName { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>ห้ามเปลี่ยนลำดับ/ค่าตัวเลขของสมาชิกที่มีอยู่แล้ว (เก็บเป็น int ใน SQLite)</summary>
public enum BackupFrequency
{
    Daily,
    Weekly
}
