namespace OnionProcOparetor.Console.Models;

/// <summary>
/// ตั้งค่าสำรองข้อมูล DB (WP3-5, 2026-10-31) - mirror ของ BackupSettings.cs ฝั่ง Server (WP3-1) ได้จาก
/// GET /api/settings/backup / ส่งกลับด้วย PUT /api/settings/backup
///
/// Frequency: 0 = Daily, 1 = Weekly (BackupFrequency ฝั่ง Server - serialize เป็น int เหมือนกับ
/// SignInSettingsDto)
///
/// **ไม่มีช่อง "กู้คืน"** - ตาม docs/roadmap.md WP3-5 ไม่มี endpoint restore backup ใน API ที่มีอยู่
/// (มีแค่ GET ดูรายการไฟล์ + PUT ตั้งค่า + POST .../now สั่งสำรองทันที) ห้ามทำปุ่มกู้คืนจนกว่าจะมี endpoint จริง
/// </summary>
public class BackupSettingsDto
{
    public int Id { get; set; }

    public int Frequency { get; set; }
    public int RetentionCount { get; set; } = 14;
    public string? StoragePath { get; set; }

    public DateTime? LastBackupAt { get; set; }
    public long? LastBackupSizeBytes { get; set; }
    public string? LastBackupFileName { get; set; }

    public DateTime UpdatedAt { get; set; }

    /// <summary>path ที่ใช้จริงตอนนี้ (ว่าง/null ใน StoragePath = ใช้ default ของ Server) - Server เติมมาให้</summary>
    public string? EffectiveStoragePath { get; set; }

    public List<BackupFileDto> Files { get; set; } = new();
}

/// <summary>ไฟล์สำรองหนึ่งชุด - ส่วนของ GET /api/settings/backup (files[])</summary>
public class BackupFileDto
{
    public string FileName { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
