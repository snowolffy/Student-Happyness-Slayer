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
    public DbSet<ClientCommand> ClientCommands => Set<ClientCommand>();
    public DbSet<RuleOverride> RuleOverrides => Set<RuleOverride>();

    /// <summary>กฎบล็อกเว็บไซต์ (Website Control, 2026-09-11) - ดู Models/WebsiteRule.cs</summary>
    public DbSet<WebsiteRule> WebsiteRules => Set<WebsiteRule>();

    /// <summary>แหล่งข้อมูล catalog ภายนอกที่ sync เข้า WebsiteRule เป็นระยะ (2026-09-12)</summary>
    public DbSet<WebsiteCatalogSource> WebsiteCatalogSources => Set<WebsiteCatalogSource>();

    /// <summary>ผลจำแนกโปรแกรม key ด้วย SHA-256 - ดู Models/ProgramVerdict.cs</summary>
    public DbSet<ProgramVerdict> ProgramVerdicts => Set<ProgramVerdict>();

    /// <summary>ห้องแล็บ + ผังที่นั่ง (WP3, 2026-10-30) - ดู Models/Room.cs</summary>
    public DbSet<Room> Rooms => Set<Room>();

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

        // ทำ index ให้ query pending command ตาม client ได้เร็ว (ใช้ทุกครั้งที่ poll)
        modelBuilder.Entity<ClientCommand>()
            .HasIndex(c => new { c.ClientGuid, c.Status });

        // (ClientGuid, RuleId) ต้องไม่ซ้ำกัน - override หนึ่งแถวต่อ (กฎ x เครื่อง) เท่านั้น (upsert
        // semantics ไม่ใช่ history) เรียง ClientGuid ไว้หน้าเพราะ query หลักคือ "ดึง override
        // ทั้งหมดของเครื่องนี้" (ทุกครั้งที่ poll) - ให้ index ตัวเดียวใช้ได้ทั้ง unique constraint
        // และ lookup เร็วโดยไม่ต้องมี index แยก
        modelBuilder.Entity<RuleOverride>()
            .HasIndex(o => new { o.ClientGuid, o.RuleId })
            .IsUnique();

        // Console ดึงสถานะทั้งชุดด้วย BatchId (GET /api/commands/batch/{batchId}) ทุกๆ 2 วิ
        // ระหว่างที่หน้า report เปิดอยู่ - ต้องมี index ไม่งั้นเป็น full table scan บนตารางที่
        // โตขึ้นเรื่อยๆ ทุกครั้งที่ครูสั่ง action
        modelBuilder.Entity<ClientCommand>()
            .HasIndex(c => c.BatchId);

        // CommandResultTimeoutService สแกนหา command ที่ยังไม่มีผลจริงกลับมาทุก 5 วิ - query
        // ด้วย ExecutionStatus + CreatedAt ตรงๆ
        modelBuilder.Entity<ClientCommand>()
            .HasIndex(c => new { c.ExecutionStatus, c.CreatedAt });

        // Sha256 คือ key จริงของตารางนี้ (Id เป็นแค่ surrogate) - ทุก lookup จาก /api/classify
        // ค้นด้วยค่านี้ และการมีสองแถวสำหรับไฟล์เดียวกันจะทำให้ครูตัดสินซ้ำซ้อนโดยไม่รู้ตัว
        modelBuilder.Entity<ProgramVerdict>()
            .HasIndex(v => v.Sha256)
            .IsUnique();

        // คิวตรวจสอบของครูเรียงตาม "เจอบ่อยแค่ไหน" - query หลักคือ Action == Flag เรียงตาม
        // SightingCount ซึ่งเป็นตารางที่จะโตเรื่อยๆ ตามจำนวนโปรแกรมที่เคยเจอทั้งห้อง
        modelBuilder.Entity<ProgramVerdict>()
            .HasIndex(v => new { v.Action, v.SightingCount });

        // UrlPattern ต้องไม่ซ้ำกัน (2026-09-12) - เดิมไม่มีอะไรกันเลย ทั้ง bulk-add และ
        // WebsiteCatalogSyncer เช็คซ้ำเองตอน insert อยู่แล้ว แต่ยังเผื่อไว้เป็นเกราะชั้นสุดท้ายกัน
        // race condition เหมือนกับ Username/ClientGuid/Token ข้างบน
        modelBuilder.Entity<WebsiteRule>()
            .HasIndex(r => r.UrlPattern)
            .IsUnique();

        // ชื่อห้องต้องไม่ซ้ำกัน (WP3, 2026-10-30) - ป้องกันครูสร้างห้อง "ห้องคอม 1" สองรอบโดยไม่ตั้งใจ
        // เหมือนกับ index อื่นข้างบนทั้งหมดในกลุ่มนี้
        modelBuilder.Entity<Room>()
            .HasIndex(r => r.Name)
            .IsUnique();

        // query หลักของ GET /api/clients?roomId= (ดู ClientEndpoints.cs) - เครื่อง 40 ตัวไม่จำเป็นต้องมี
        // index จริงจังขนาดนี้ แต่ทำไว้ให้ตรงกับ pattern เดียวกับ index อื่นๆ ของ ClientMachine/ClientCommand
        modelBuilder.Entity<ClientMachine>()
            .HasIndex(c => c.RoomId);

        // Seed ค่าเริ่มต้นของ GlobalState ให้มี row เดียวเสมอ (Id = 1, เปิดใช้งานเป็นค่าเริ่มต้น)
        // สำคัญ: ต้องใช้ค่า DateTime แบบ fixed/static เท่านั้น ห้ามใช้ DateTime.UtcNow หรือ
        // Guid.NewGuid() ใน HasData() เด็ดขาด เพราะ EF Core จะมองว่า model "ไม่นิ่ง" แล้ว throw
        // PendingModelChangesWarning ตอน Migrate() ทำงาน
        modelBuilder.Entity<GlobalState>().HasData(
            new GlobalState { Id = 1, IsEnabled = true, Note = "Default state", UpdatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) }
        );
    }
}
