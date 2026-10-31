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

    /// <summary>บัญชีนักเรียน (WP3-1, 2026-10-31) - ดู Models/Student.cs</summary>
    public DbSet<Student> Students => Set<Student>();

    /// <summary>ประวัติการใช้เครื่องของนักเรียน (WP3-1, 2026-10-31) - ดู Models/StudentSession.cs</summary>
    public DbSet<StudentSession> StudentSessions => Set<StudentSession>();

    /// <summary>ตั้งค่า Sign-in นักเรียนทั้งระบบ แถวเดียว (WP3-1, 2026-10-31) - ดู Models/SignInSettings.cs</summary>
    public DbSet<SignInSettings> SignInSettings => Set<SignInSettings>();

    /// <summary>ตั้งค่าสำรองข้อมูล DB แถวเดียว (WP3-1, 2026-10-31) - ดู Models/BackupSettings.cs</summary>
    public DbSet<BackupSettings> BackupSettings => Set<BackupSettings>();

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

        // ค่า default ของคอลัมน์ใหม่ (WP4, 2026-10-30) ต้องระบุตรงนี้เพื่อให้ migration เพิ่มคอลัมน์แบบ
        // NOT NULL ลงบน DB เก่าที่มีข้อมูลอยู่แล้วได้โดยไม่พัง (SQLite ต้องมี default ถ้าคอลัมน์ไม่ nullable)
        // - Role default Admin: บัญชีเดิมทุกบัญชีต้องไม่โดนล็อกออกจากระบบ
        // - DisplayName default "" แล้ว migration (Up()) ค่อย backfill เป็น Username ทีหลังด้วย SQL ตรงๆ
        modelBuilder.Entity<AdminUser>()
            .Property(u => u.Role)
            .HasDefaultValue(AdminRole.Admin);

        modelBuilder.Entity<AdminUser>()
            .Property(u => u.DisplayName)
            .HasDefaultValue(string.Empty);

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

        // กฎรายห้อง (WP2-3, 2026-10-31) - /api/poll กรองทั้ง Rules และ WebsiteRules ด้วย RoomId ทุกรอบ
        // poll ของทั้ง 40 เครื่อง เหตุผลเดียวกับ index ของ ClientMachine.RoomId ข้างบน
        modelBuilder.Entity<Rule>()
            .HasIndex(r => r.RoomId);

        modelBuilder.Entity<WebsiteRule>()
            .HasIndex(r => r.RoomId);

        // รหัสนักเรียนต้องไม่ซ้ำกัน (WP3-1, 2026-10-31) - ใช้ login ได้เหมือน Username ของ AdminUser ข้างบน
        modelBuilder.Entity<Student>()
            .HasIndex(s => s.StudentCode)
            .IsUnique();

        // เมลโรงเรียนต้องไม่ซ้ำกัน "ถ้ามี" - filtered unique index เพราะไม่ใช่นักเรียนทุกคนมีเมล (nullable)
        // SQLite ถือว่า NULL ทุกแถวไม่เท่ากัน (ปกติของ unique index อยู่แล้ว) แต่ใส่ HasFilter ไว้ชัดเจน
        // กันกรณีอนาคตเปลี่ยนไปใช้ provider อื่นที่พฤติกรรมต่างกัน
        modelBuilder.Entity<Student>()
            .HasIndex(s => s.SchoolEmail)
            .IsUnique()
            .HasFilter("[SchoolEmail] IS NOT NULL");

        // query หลัก: Roster ค้นด้วยชั้น/ห้องเรียน (nf-roster ฝั่งซ้าย) + Console ประวัติ "ห้องเรียนของนักเรียน"
        modelBuilder.Entity<Student>()
            .HasIndex(s => s.ClassName);

        // query หลักของ "มี session เปิดอยู่ไหมบนเครื่องนี้" (ทุกครั้งที่ login/poll ต้องเช็ค) - EndedAt ==
        // null คือ session ที่ยังเปิดอยู่ มีได้แถวเดียวต่อ ClientGuid ในเวลาเดียวกัน (บังคับที่ endpoint)
        modelBuilder.Entity<StudentSession>()
            .HasIndex(s => new { s.ClientGuid, s.EndedAt });

        // query หลักของ "นักเรียนคนนี้ login อยู่เครื่องไหนอยู่ไหม" (กติกาบัญชีเดียวหลายเครื่อง - ต้องหาทุก
        // session ที่เปิดอยู่ของนักเรียนคนนี้ก่อนยอม login เครื่องใหม่)
        modelBuilder.Entity<StudentSession>()
            .HasIndex(s => new { s.StudentId, s.EndedAt });

        // ประวัติการใช้เครื่อง (nf-attendance) กรองตามช่วงเวลาเป็นหลักเสมอ
        modelBuilder.Entity<StudentSession>()
            .HasIndex(s => s.StartedAt);

        // Seed ค่าเริ่มต้นของ GlobalState ให้มี row เดียวเสมอ (Id = 1, เปิดใช้งานเป็นค่าเริ่มต้น)
        // สำคัญ: ต้องใช้ค่า DateTime แบบ fixed/static เท่านั้น ห้ามใช้ DateTime.UtcNow หรือ
        // Guid.NewGuid() ใน HasData() เด็ดขาด เพราะ EF Core จะมองว่า model "ไม่นิ่ง" แล้ว throw
        // PendingModelChangesWarning ตอน Migrate() ทำงาน
        modelBuilder.Entity<GlobalState>().HasData(
            new GlobalState { Id = 1, IsEnabled = true, Note = "Default state", UpdatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) }
        );

        // Seed ค่าเริ่มต้นของ SignInSettings/BackupSettings (WP3-1, 2026-10-31) - เหตุผลเดียวกับ GlobalState
        // ข้างบน (ต้องมีแถว Id=1 เสมอตั้งแต่ DB ใหม่ที่สุด ไม่ต้องมี GetOrCreate ที่จุดอ่านทุกจุด)
        modelBuilder.Entity<Models.SignInSettings>().HasData(
            new Models.SignInSettings { Id = 1, UpdatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) }
        );
        modelBuilder.Entity<BackupSettings>().HasData(
            new BackupSettings { Id = 1, UpdatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) }
        );
    }
}
