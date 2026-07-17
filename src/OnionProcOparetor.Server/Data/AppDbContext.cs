using Microsoft.EntityFrameworkCore;
using OnionProcOparetor.Server.Models;

namespace OnionProcOparetor.Server.Data;

/// <summary>
/// EF Core DbContext หลักของ Server ผูกกับ SQLite
/// ไฟล์ DB จริงจะอยู่ที่ %ProgramData%\OnionProcOparetor\onionprocoparetor.db
/// (ตั้งค่า connection string ใน appsettings.json)
/// </summary>
public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<Rule> Rules => Set<Rule>();
    public DbSet<ClientMachine> ClientMachines => Set<ClientMachine>();
    public DbSet<LogEntry> LogEntries => Set<LogEntry>();
    public DbSet<GlobalState> GlobalStates => Set<GlobalState>();
    public DbSet<AdminUser> AdminUsers => Set<AdminUser>();
    public DbSet<AuthToken> AuthTokens => Set<AuthToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ClientGuid ต้องไม่ซ้ำกัน - ใช้ระบุตัวตนเครื่อง client
        modelBuilder.Entity<ClientMachine>()
            .HasIndex(c => c.ClientGuid)
            .IsUnique();

        // Username ต้องไม่ซ้ำกัน
        modelBuilder.Entity<AdminUser>()
            .HasIndex(u => u.Username)
            .IsUnique();

        // Token ต้องไม่ซ้ำกัน และ query หา token ได้เร็ว
        modelBuilder.Entity<AuthToken>()
            .HasIndex(t => t.Token)
            .IsUnique();

        // ทำ index ให้ query log ตาม client + เวลาได้เร็ว (จะมี log เยอะสุดในบรรดา table ทั้งหมด)
        modelBuilder.Entity<LogEntry>()
            .HasIndex(l => new { l.ClientGuid, l.Timestamp });

        // Seed ค่าเริ่มต้นของ GlobalState ให้มี row เดียวเสมอ (Id = 1, เปิดใช้งานเป็นค่าเริ่มต้น)
        // สำคัญ: ต้องใช้ค่า DateTime แบบ fixed/static เท่านั้น ห้ามใช้ DateTime.UtcNow หรือ
        // Guid.NewGuid() ใน HasData() เด็ดขาด เพราะ EF Core จะมองว่า model "ไม่นิ่ง" แล้ว throw
        // PendingModelChangesWarning ตอน Migrate() ทำงาน
        modelBuilder.Entity<GlobalState>().HasData(
            new GlobalState { Id = 1, IsEnabled = true, Note = "Default state", UpdatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) }
        );
    }
}
