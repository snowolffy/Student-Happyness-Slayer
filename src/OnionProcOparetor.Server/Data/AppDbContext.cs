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

    /// <summary>ตั้งค่า "ป้องกันการเลี่ยง" ของ Website Control (WP2-1, 2026-10-31) - ดู Models/BrowserHardeningSettings.cs</summary>
    public DbSet<BrowserHardeningSettings> BrowserHardeningSettings => Set<BrowserHardeningSettings>();

    /// <summary>ชุด Allow-list สำเร็จรูป/กำหนดเอง ของ "โหมดสอบ" (WP2-2, 2026-10-31) - ดู Models/AllowListPreset.cs</summary>
    public DbSet<AllowListPreset> AllowListPresets => Set<AllowListPreset>();

    /// <summary>โดเมนของแต่ละชุด Allow-list (WP2-2, 2026-10-31) - ดู Models/AllowListPresetDomain.cs</summary>
    public DbSet<AllowListPresetDomain> AllowListPresetDomains => Set<AllowListPresetDomain>();

    /// <summary>ประวัติ "โหมดสอบ" แต่ละครั้ง (WP2-2, 2026-10-31) - ดู Models/ExamSession.cs</summary>
    public DbSet<ExamSession> ExamSessions => Set<ExamSession>();

    /// <summary>ประวัติการเข้าเว็บที่ Agent ส่งขึ้นมาทุก 1 นาที (WP2-4, 2026-10-31) - ดู Models/WebVisit.cs</summary>
    public DbSet<WebVisit> WebVisits => Set<WebVisit>();

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

        // โดเมนของชุด Allow-list (WP2-2, 2026-10-31) - query หลักคือ "โดเมนทั้งหมดของชุดนี้" ทุกครั้งที่
        // แก้ชุดในหน้า Website Control และทุกครั้งที่ประกอบ allow-list ตอนเปิด/poll โหมดสอบ
        modelBuilder.Entity<AllowListPresetDomain>()
            .HasIndex(d => d.PresetId);

        // query หลักของ /api/poll (ทุก 30 วิ x 40 เครื่อง): หา ExamSession ที่ "ยังไม่จบ" (EndedAt null)
        // ก่อนเสมอ แล้วเทียบ RoomId/ClientGuidsCsv ในโค้ด (ไม่ query ด้วย RoomId ตรงๆ เพราะ
        // ClientGuidsCsv เป็น CSV string match แบบ exact ไม่ได้ด้วย SQL index)
        modelBuilder.Entity<ExamSession>()
            .HasIndex(e => e.EndedAt);

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

        // ประวัติเว็บ (WP2-4, 2026-10-31) - หน้า "ประวัติเว็บ" กรองตามช่วงเวลาเป็นหลักเสมอ (เหมือน
        // StudentSession ข้างบน) และ cleanup (MainDbRetentionService) ก็ scan ด้วยคอลัมน์เดียวกัน
        modelBuilder.Entity<WebVisit>()
            .HasIndex(v => v.VisitedAt);

        // query หลักของ /api/web-visits/top-domains (GroupBy Domain) + ตัวกรองโดเมนในหน้าประวัติเว็บ
        modelBuilder.Entity<WebVisit>()
            .HasIndex(v => v.Domain);

        // เผื่อ query ประวัติของเครื่องเดียวเจาะจง (ยังไม่มี UI ใช้ตรงนี้ในรอบแรก แต่ pattern เดียวกับ
        // LogEntry.(ClientGuid, Timestamp) ข้างบน - เครื่อง 40 ตัวจะมี WebVisit มากกว่าตารางอื่นๆ ทั้งหมด
        // รวมกันในระยะยาว)
        modelBuilder.Entity<WebVisit>()
            .HasIndex(v => new { v.ClientGuid, v.VisitedAt });

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

        // Seed ค่าเริ่มต้นของ BrowserHardeningSettings (WP2-1, 2026-10-31) - เหตุผล DateTime คงที่
        // เดียวกับ GlobalState ข้างบน (ห้าม DateTime.UtcNow/Guid.NewGuid() ใน HasData)
        // ค่า default ตรงกับ wireframe con-website.dc.html: 4 ข้อแรกเปิดไว้ก่อน, "บล็อกเบราว์เซอร์อื่น" ปิดไว้
        modelBuilder.Entity<BrowserHardeningSettings>().HasData(
            new BrowserHardeningSettings
            {
                Id = 1,
                BlockExtensions = true,
                BlockGuestAndIncognito = true,
                BlockDevTools = true,
                LockDnsAndProxy = true,
                BlockOtherBrowsers = false,
                UpdatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            }
        );

        // ---- ชุด Allow-list สำเร็จรูป (WP2-2, 2026-10-31) ----
        // Id ของทั้ง Preset และ Domain ต้องตั้งตรงๆ (ไม่ปล่อยให้ EF auto-increment เอง) เพราะ HasData
        // ต้องระบุ key ที่แน่นอนเสมอ - เหตุผล DateTime คงที่เดียวกับ seed อื่นๆ ข้างบน (ห้าม
        // DateTime.UtcNow ใน HasData ไม่งั้น EF มองว่า model ไม่นิ่งแล้ว throw PendingModelChangesWarning)
        //
        // รายชื่อโดเมนประกอบนี้เป็น best-effort จากการอ่านเอกสาร/สังเกตการใช้งานจริงของแต่ละบริการ
        // (ไม่มีเครื่องมือ browser ให้ทดสอบจริงตอนเขียน WP นี้ - ดู docs/roadmap.md WP2-2 "เกณฑ์เสร็จ"
        // ที่ระบุไว้ตรงๆ ว่าต้องทดสอบเปิดเว็บจริงในแล็บ) ครู/Admin แก้เพิ่ม/ลบโดเมนได้เองที่ Website
        // Control › ชุด Allow-list ถ้าเจอว่าเปิดไม่ครบ
        var seedTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        modelBuilder.Entity<AllowListPreset>().HasData(
            new AllowListPreset { Id = 1, Name = "Google Forms", IsBuiltIn = true, CreatedAt = seedTime, UpdatedAt = seedTime },
            new AllowListPreset { Id = 2, Name = "Google Classroom", IsBuiltIn = true, CreatedAt = seedTime, UpdatedAt = seedTime },
            new AllowListPreset { Id = 3, Name = "Quizizz / Wayground", IsBuiltIn = true, CreatedAt = seedTime, UpdatedAt = seedTime },
            new AllowListPreset { Id = 4, Name = "Kahoot", IsBuiltIn = true, CreatedAt = seedTime, UpdatedAt = seedTime },
            new AllowListPreset { Id = 5, Name = "Scratch", IsBuiltIn = true, CreatedAt = seedTime, UpdatedAt = seedTime },
            new AllowListPreset { Id = 6, Name = "Canva", IsBuiltIn = true, CreatedAt = seedTime, UpdatedAt = seedTime }
        );

        modelBuilder.Entity<AllowListPresetDomain>().HasData(
            // Google Forms (7 โดเมน - ตรงกับ wireframe con-website-allow.dc.html)
            new AllowListPresetDomain { Id = 1, PresetId = 1, Domain = "docs.google.com" },
            new AllowListPresetDomain { Id = 2, PresetId = 1, Domain = "forms.gle" },
            new AllowListPresetDomain { Id = 3, PresetId = 1, Domain = "accounts.google.com" },
            new AllowListPresetDomain { Id = 4, PresetId = 1, Domain = "ssl.gstatic.com" },
            new AllowListPresetDomain { Id = 5, PresetId = 1, Domain = "www.gstatic.com" },
            new AllowListPresetDomain { Id = 6, PresetId = 1, Domain = "fonts.googleapis.com" },
            new AllowListPresetDomain { Id = 7, PresetId = 1, Domain = "lh3.googleusercontent.com" },

            // Google Classroom (9 โดเมน - ต่อยอดจาก Google Forms เพิ่ม classroom.google.com/drive)
            new AllowListPresetDomain { Id = 8, PresetId = 2, Domain = "classroom.google.com" },
            new AllowListPresetDomain { Id = 9, PresetId = 2, Domain = "drive.google.com" },
            new AllowListPresetDomain { Id = 10, PresetId = 2, Domain = "docs.google.com" },
            new AllowListPresetDomain { Id = 11, PresetId = 2, Domain = "accounts.google.com" },
            new AllowListPresetDomain { Id = 12, PresetId = 2, Domain = "ssl.gstatic.com" },
            new AllowListPresetDomain { Id = 13, PresetId = 2, Domain = "www.gstatic.com" },
            new AllowListPresetDomain { Id = 14, PresetId = 2, Domain = "fonts.googleapis.com" },
            new AllowListPresetDomain { Id = 15, PresetId = 2, Domain = "lh3.googleusercontent.com" },
            new AllowListPresetDomain { Id = 16, PresetId = 2, Domain = "ogs.google.com" },

            // Quizizz / Wayground (4 โดเมน - รีแบรนด์เป็น Wayground แล้วแต่โดเมนเก่ายังใช้งานอยู่)
            new AllowListPresetDomain { Id = 17, PresetId = 3, Domain = "quizizz.com" },
            new AllowListPresetDomain { Id = 18, PresetId = 3, Domain = "cdn.quizizz.com" },
            new AllowListPresetDomain { Id = 19, PresetId = 3, Domain = "wayground.com" },
            new AllowListPresetDomain { Id = 20, PresetId = 3, Domain = "cdn.wayground.com" },

            // Kahoot (3 โดเมน)
            new AllowListPresetDomain { Id = 21, PresetId = 4, Domain = "kahoot.it" },
            new AllowListPresetDomain { Id = 22, PresetId = 4, Domain = "kahoot.com" },
            new AllowListPresetDomain { Id = 23, PresetId = 4, Domain = "akamaized.net" },

            // Scratch (3 โดเมน)
            new AllowListPresetDomain { Id = 24, PresetId = 5, Domain = "scratch.mit.edu" },
            new AllowListPresetDomain { Id = 25, PresetId = 5, Domain = "assets.scratch.mit.edu" },
            new AllowListPresetDomain { Id = 26, PresetId = 5, Domain = "cdn.scratch.mit.edu" },

            // Canva (5 โดเมน)
            new AllowListPresetDomain { Id = 27, PresetId = 6, Domain = "canva.com" },
            new AllowListPresetDomain { Id = 28, PresetId = 6, Domain = "www.canva.com" },
            new AllowListPresetDomain { Id = 29, PresetId = 6, Domain = "static.canva.com" },
            new AllowListPresetDomain { Id = 30, PresetId = 6, Domain = "media-public.canva.com" },
            new AllowListPresetDomain { Id = 31, PresetId = 6, Domain = "app-api.canva.com" }
        );
    }
}
