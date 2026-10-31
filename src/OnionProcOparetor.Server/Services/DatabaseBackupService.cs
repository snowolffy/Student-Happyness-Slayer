using Microsoft.EntityFrameworkCore;
using OnionProcOparetor.Server.Data;
using OnionProcOparetor.Server.Models;

namespace OnionProcOparetor.Server.Services;

/// <summary>
/// สำรองข้อมูล DB ทั้งสองไฟล์ (onionprocoparetor.db หลัก + activitylog.db) ตามตั้งค่า BackupSettings
/// (WP3-1, 2026-10-31) - ดู wireframe docs/wireframes/nf-settings-more.dc.html หัวข้อ "สำรองข้อมูล"
///
/// ใช้ `VACUUM INTO` ของ SQLite แทนการ copy ไฟล์ตรงๆ เพราะเครื่อง server รันอยู่ตลอดเวลาขณะทำสำรอง
/// (40 เครื่อง poll เข้ามาตลอด + journal_mode=WAL) การ copy ไฟล์ .db เฉยๆ ขณะมี writer ทำงานอยู่จะได้ snapshot
/// ที่ไม่ครบ (WAL/SHM แยกไฟล์ - ดู comment ของ WAL ใน Program.cs) `VACUUM INTO` เขียน snapshot ที่ consistent
/// เสมอแม้มี writer อื่นทำงานคู่กันอยู่ (SQLite online backup semantics)
///
/// เรียกใช้จาก:
///   - Endpoints/SignInSettingsEndpoints.cs (ปุ่ม "สำรองตอนนี้" ของครู)
///   - Services/DatabaseBackupSchedulerService.cs (ตัวเดินเวลาตามความถี่ที่ตั้งไว้)
///   - Endpoints/RosterEndpoints.cs (ก่อนเลื่อนชั้นปีการศึกษาเสมอ - ดู docs/roadmap.md "เลื่อนชั้นปี...
///     สำรอง DB ให้อัตโนมัติก่อนเลื่อน")
/// </summary>
public class DatabaseBackupService
{
    private readonly AppDbContext _db;
    private readonly ActivityLogDbContext _activityLogDb;
    private readonly ILogger<DatabaseBackupService> _logger;

    public DatabaseBackupService(AppDbContext db, ActivityLogDbContext activityLogDb, ILogger<DatabaseBackupService> logger)
    {
        _db = db;
        _activityLogDb = activityLogDb;
        _logger = logger;
    }

    /// <summary>โฟลเดอร์ default เมื่อ BackupSettings.StoragePath ว่าง/ไม่ได้ตั้ง</summary>
    public static string DefaultBackupDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "OnionProcOparetor", "backups");

    /// <summary>ไฟล์สำรองหนึ่งชุด (สองไฟล์ - หลัก + activity log) ที่ถูกสร้างในรอบนี้</summary>
    public record BackupResult(string MainFileName, string ActivityLogFileName, string Directory, long TotalSizeBytes, DateTime CreatedAtUtc);

    /// <summary>
    /// สำรองทันที (ไม่รอรอบตั้งเวลา) - ใช้ timestamp เดียวกันตั้งชื่อทั้งสองไฟล์ให้จับคู่กันง่ายตอนดูในโฟลเดอร์
    /// อัปเดต BackupSettings.LastBackup* แล้วล้างไฟล์เก่าที่เกิน RetentionCount ทิ้งก่อน return
    /// </summary>
    public async Task<BackupResult> BackupNowAsync()
    {
        var settings = await GetOrCreateSettingsAsync();
        var dir = string.IsNullOrWhiteSpace(settings.StoragePath) ? DefaultBackupDirectory : settings.StoragePath;
        Directory.CreateDirectory(dir);

        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        var mainFileName = $"onionprocoparetor-{timestamp}.db";
        var activityLogFileName = $"activitylog-{timestamp}.db";
        var mainFullPath = Path.Combine(dir, mainFileName);
        var activityLogFullPath = Path.Combine(dir, activityLogFileName);

        // ExecuteSqlInterpolated parameterize path ให้เอง (กัน SQL injection แม้ StoragePath เป็นค่าที่
        // Admin ตั้งเองอยู่แล้ว - ไม่ใช่ input จากนักเรียน ก็ยังเลือกทางที่ปลอดภัยกว่าไว้)
        await _db.Database.ExecuteSqlInterpolatedAsync($"VACUUM INTO {mainFullPath}");
        await _activityLogDb.Database.ExecuteSqlInterpolatedAsync($"VACUUM INTO {activityLogFullPath}");

        var totalSize = new FileInfo(mainFullPath).Length + new FileInfo(activityLogFullPath).Length;

        settings.LastBackupAt = DateTime.UtcNow;
        settings.LastBackupFileName = mainFileName;
        settings.LastBackupSizeBytes = totalSize;
        settings.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        _logger.LogInformation("Database backup created: {MainFile} + {ActivityLogFile} ({SizeBytes} bytes) in {Dir}",
            mainFileName, activityLogFileName, totalSize, dir);

        CleanupOldBackups(dir, settings.RetentionCount);

        return new BackupResult(mainFileName, activityLogFileName, dir, totalSize, DateTime.UtcNow);
    }

    /// <summary>
    /// ลบไฟล์สำรองเก่าที่เกินจำนวนชุด (RetentionCount) ทิ้ง - จัดกลุ่มเป็น "ชุด" ด้วย timestamp suffix ของ
    /// ชื่อไฟล์ (ไฟล์คู่ onionprocoparetor-X.db + activitylog-X.db ถือเป็นชุดเดียวกัน) เรียงจากใหม่ไปเก่า
    /// เก็บ RetentionCount ชุดแรก ลบที่เหลือทั้งหมด - ไม่ throw ถ้าโฟลเดอร์มีไฟล์แปลกปลอมอื่นปนอยู่ (ข้ามเฉยๆ)
    /// </summary>
    private void CleanupOldBackups(string dir, int retentionCount)
    {
        if (retentionCount <= 0) return; // 0 หรือติดลบ = ไม่ลบอะไรเลย (ตั้งผิดก็ไม่ควรทำให้ข้อมูลหาย)

        try
        {
            var timestamps = Directory.GetFiles(dir, "onionprocoparetor-*.db")
                .Select(f => Path.GetFileNameWithoutExtension(f).Replace("onionprocoparetor-", ""))
                .OrderByDescending(t => t)
                .ToList();

            foreach (var oldTimestamp in timestamps.Skip(retentionCount))
            {
                foreach (var prefix in new[] { "onionprocoparetor-", "activitylog-" })
                {
                    var path = Path.Combine(dir, $"{prefix}{oldTimestamp}.db");
                    if (File.Exists(path))
                    {
                        File.Delete(path);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            // ไม่ hard-fail การสำรองเพราะลบไฟล์เก่าไม่สำเร็จ (เช่นไฟล์ถูกเปิดอยู่โดยโปรแกรมอื่น) - ไฟล์ใหม่
            // ที่สร้างเสร็จแล้วสำคัญกว่าการลบไฟล์เก่า
            _logger.LogWarning(ex, "ลบไฟล์สำรองเก่าไม่สำเร็จ (ไม่กระทบไฟล์สำรองใหม่ที่สร้างเสร็จแล้ว)");
        }
    }

    /// <summary>รายการไฟล์สำรองที่มีอยู่ตอนนี้ในโฟลเดอร์ปัจจุบัน (ใหม่ไปเก่า) - ให้ GET /api/settings/backup ใช้แสดงผล</summary>
    public async Task<List<(string FileName, long SizeBytes, DateTime CreatedAtUtc)>> ListBackupsAsync()
    {
        var settings = await GetOrCreateSettingsAsync();
        var dir = string.IsNullOrWhiteSpace(settings.StoragePath) ? DefaultBackupDirectory : settings.StoragePath;
        if (!Directory.Exists(dir))
        {
            return new List<(string, long, DateTime)>();
        }

        return Directory.GetFiles(dir, "onionprocoparetor-*.db")
            .Select(f => new FileInfo(f))
            .OrderByDescending(f => f.CreationTimeUtc)
            .Select(f => (f.Name, f.Length, f.CreationTimeUtc))
            .ToList();
    }

    /// <summary>
    /// GlobalState/SignInSettings ของ Server นี้มี seed row Id=1 เสมอตอน migrate (ดู AppDbContext.OnModelCreating)
    /// แต่ยังกันพลาดไว้เผื่อ DB เก่าที่ seed ไม่ติด (เช่นถูกลบแถวทิ้งด้วยมือ) - pattern เดียวกับ
    /// GlobalStateEndpoints.GetOrCreateGlobalStateAsync
    /// </summary>
    public async Task<BackupSettings> GetOrCreateSettingsAsync()
    {
        var settings = await _db.BackupSettings.FirstOrDefaultAsync(s => s.Id == 1);
        if (settings is null)
        {
            settings = new BackupSettings { Id = 1 };
            _db.BackupSettings.Add(settings);
            await _db.SaveChangesAsync();
        }
        return settings;
    }
}
