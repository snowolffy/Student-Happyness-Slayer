using Microsoft.EntityFrameworkCore;
using OnionProcOparetor.Server.Models;

namespace OnionProcOparetor.Server.Data;

/// <summary>
/// EF Core DbContext แยกต่างหากสำหรับ Activity Log - ตั้งใจแยกจาก AppDbContext เพราะคาดว่า
/// volume จะสูงกว่าข้อมูลหลักมาก (audit ทุก action ของครู + ทุก agent connect/disconnect + ทุก
/// warning/error) และมี lifecycle ต่างกัน (auto-cleanup ทุกชั่วโมง ลบข้อมูลเกิน 7 วัน - ดู
/// ActivityLogCleanupService) ไฟล์ DB จริงอยู่ที่ %ProgramData%\OnionProcOparetor\activitylog.db
/// (ตั้งค่า connection string ใน appsettings.json)
/// </summary>
public class ActivityLogDbContext : DbContext
{
    public ActivityLogDbContext(DbContextOptions<ActivityLogDbContext> options) : base(options)
    {
    }

    public DbSet<ActivityLogEntry> ActivityLogEntries => Set<ActivityLogEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ทุก query หลักกรอง/เรียงตามเวลาเสมอ (รวมถึง cleanup job ที่สแกนหาแถวเก่ากว่า cutoff)
        modelBuilder.Entity<ActivityLogEntry>()
            .HasIndex(e => e.Timestamp);

        // filter ตามหมวดใน Console (ComboBox หมวด) + เรียงเวลา
        modelBuilder.Entity<ActivityLogEntry>()
            .HasIndex(e => new { e.Category, e.Timestamp });

        // filter ตามเครื่องเป้าหมายใน Console
        modelBuilder.Entity<ActivityLogEntry>()
            .HasIndex(e => new { e.TargetClientGuid, e.Timestamp });

        // filter ตามผู้กระทำใน Console
        modelBuilder.Entity<ActivityLogEntry>()
            .HasIndex(e => new { e.ActorAdminUserId, e.Timestamp });

        // filter ตามนักเรียนผู้กระทำ (WP3-1, 2026-10-31) - เหตุผลเดียวกับ index ของ ActorAdminUserId ข้างบน
        modelBuilder.Entity<ActivityLogEntry>()
            .HasIndex(e => new { e.ActorStudentId, e.Timestamp });
    }
}
