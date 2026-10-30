using Microsoft.EntityFrameworkCore;
using OnionProcOparetor.Server.Data;
using OnionProcOparetor.Server.Endpoints;
using OnionProcOparetor.Server.Models;
using OnionProcOparetor.Server.Services;

// ---- Diagnostic เขียนไฟล์ตรงๆ ตั้งแต่บรรทัดแรกสุด (ก่อน WebApplication.CreateBuilder) ----
// เหตุผลเดียวกับ OnionProcOparetor.Agent: ตอนรันเป็น Windows Service ไม่มี console ให้ดู
// เลยต้องมีที่เขียน diagnostic ไว้ก่อน ILogger จะพร้อมใช้งาน
StartupDiagnostics.WriteLine("==================================================");
StartupDiagnostics.WriteLine($"OnionProcOparetor.Server starting - PID {Environment.ProcessId}");
StartupDiagnostics.WriteLine($"Environment.CurrentDirectory (raw, ตอน process เริ่ม): {Environment.CurrentDirectory}");
StartupDiagnostics.WriteLine($"AppContext.BaseDirectory (ตำแหน่ง .exe จริงเสมอ): {AppContext.BaseDirectory}");

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    // บังคับ ContentRootPath ให้เป็นตำแหน่ง .exe จริงเสมอ (เหมือน Agent) กัน appsettings.json
    // หาไม่เจอเงียบๆ ถ้า Windows Service ตั้ง working directory เป็นอย่างอื่น
    ContentRootPath = AppContext.BaseDirectory
});

StartupDiagnostics.WriteLine($"builder.Environment.ContentRootPath: {builder.Environment.ContentRootPath}");

// ---- เสริม file logger เข้ากับ logging pipeline ปกติ - เปิดดูได้เสมอไม่ว่าจะรันแบบ console
// ตรงๆ หรือเป็น Windows Service จริง (ต่างจาก console/debug provider ที่หายไปเงียบๆ ตอนเป็น service) ----
var serverLogPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
    "OnionProcOparetor", "Server", "logs", "server.log");
var fileLoggerProvider = new FileLoggerProvider(serverLogPath);
builder.Logging.AddProvider(fileLoggerProvider);
StartupDiagnostics.WriteLine($"ILogger ทั้งหมดจะถูกเขียนไปที่ไฟล์นี้ด้วย: {serverLogPath}");

// ---- ตั้งค่า Kestrel ให้ฟัง port ตามที่กำหนดใน appsettings.json ----
var serverPort = builder.Configuration.GetValue<int>("Server:Port", 8787);

// อายุของคำสั่งประเภท "เหตุการณ์" (Shutdown/Restart/Lock/...) - ดู CommandExpiry สำหรับเหตุผลเต็ม
// ค่าที่ตั้งผิด (0 หรือติดลบ) จะทำให้ทุกคำสั่งหมดอายุทันทีที่สร้าง = สั่งอะไรไม่ได้เลยทั้งห้อง
// จึงตกกลับไปใช้ค่า default แทนที่จะเชื่อ config ดื้อ ๆ (pattern เดียวกับ CommandResultTimeoutService)
var configuredCommandLifetime = builder.Configuration.GetValue<int>(
    "CommandTracking:EventCommandLifetimeSeconds",
    (int)CommandExpiry.EventCommandLifetime.TotalSeconds);
var eventCommandLifetime = configuredCommandLifetime > 0
    ? TimeSpan.FromSeconds(configuredCommandLifetime)
    : CommandExpiry.EventCommandLifetime;
builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(serverPort); // ฟังทุก network interface เพื่อให้ client/console เครื่องอื่นต่อเข้ามาได้

    // จำกัดขนาด request body (2026-08-31, จาก security audit) - default ของ Kestrel คือ ~30MB
    // ไม่มี endpoint ไหนในระบบนี้รับ payload ใหญ่กว่าหลักร้อย KB เลย (ใหญ่สุดคือ process list ที่
    // relay ผ่าน /api/commands/{id}/result) แต่ /api/logs กับ /api/tamper-report ไม่ต้อง auth
    // ทำให้ 30MB ต่อ request กลายเป็นเครื่องมือถมดิสก์/กิน RAM ที่ยิงจากเครื่องไหนใน LAN ก็ได้
    options.Limits.MaxRequestBodySize = 4 * 1024 * 1024;
});

// ---- สร้างโฟลเดอร์เก็บ DB ถ้ายังไม่มี (เช่นตอน dev หรือเครื่องใหม่) ----
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? "Data Source=C:\\ProgramData\\OnionProcOparetor\\onionprocoparetor.db";
var dbDir = Path.GetDirectoryName(connectionString.Replace("Data Source=", ""));
if (!string.IsNullOrEmpty(dbDir) && !Directory.Exists(dbDir))
{
    Directory.CreateDirectory(dbDir);
}

// ---- ปรับ connection string ของ SQLite ให้ทนการเขียนพร้อมกัน (2026-08-31) ----
// ภาระจริงตอนใช้งาน: 40 Agent poll เข้ามา + Console อ่านทุกไม่กี่วินาที + BackgroundService อีก 3 ตัว
// (CommandResultTimeout ทุก 5 วิ, ClientOnlineStatusMonitor ทุก 30 วิ, ActivityLogCleanup ทุกชั่วโมง)
// ทั้งหมดเขียนลงไฟล์ SQLite ไฟล์เดียวกัน - default ของ Microsoft.Data.Sqlite คือ rollback journal
// (writer ตัวเดียวล็อกทั้งไฟล์ ผู้อ่านก็โดนบล็อกด้วย) + busy timeout 0 = ชนเมื่อไหร่ throw
// "database is locked" ทันทีไม่รอเลย ซึ่งจะโผล่ตอนที่แย่ที่สุดพอดี (ครูสั่ง batch command ใส่หลาย
// เครื่องพร้อมกันขณะที่ poll กำลังลงพอดี) ตั้ง Default Timeout ให้รอแทนที่จะ throw
// journal_mode=WAL ตั้งด้านล่างหลัง Migrate() (เป็น setting ที่ persist ในไฟล์ DB เอง ตั้งครั้งเดียวพอ)
static string WithConcurrencySettings(string raw)
{
    var csb = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder(raw)
    {
        // วินาที - เผื่อ writer ตัวอื่นถือ lock อยู่ ให้รอจนกว่าจะได้ แทนที่จะล้มทันที
        DefaultTimeout = 30,
        Pooling = true
    };
    return csb.ToString();
}

// ---- ผูก EF Core + SQLite เข้ากับ Dependency Injection ----
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(WithConcurrencySettings(connectionString)));

// ---- Activity Log: DB แยกต่างหาก (คนละไฟล์ SQLite) จาก DB หลัก เพราะคาดว่า volume จะสูงกว่า
// (audit ทุก action ของครู + ทุก agent connect/disconnect + ทุก warning/error) และมี auto-cleanup
// ทุก 7 วัน (ดู ActivityLogCleanupService) ต่างจาก DB หลักที่ไม่มี retention ----
var activityLogConnectionString = builder.Configuration.GetConnectionString("ActivityLogConnection")
    ?? "Data Source=C:\\ProgramData\\OnionProcOparetor\\activitylog.db";
builder.Services.AddDbContext<ActivityLogDbContext>(options =>
    options.UseSqlite(WithConcurrencySettings(activityLogConnectionString)));
builder.Services.AddScoped<IActivityLogService, ActivityLogService>();
builder.Services.AddHostedService<ActivityLogCleanupService>();

// ---- Retention ของ DB หลัก (2026-08-31) - LogEntries/AuthTokens ไม่เคยมี cleanup มาก่อนเลย ----
builder.Services.AddHostedService<MainDbRetentionService>();

// ---- จำกัดการเดารหัสผ่านที่ /api/auth/login (2026-08-31) - singleton เพราะตัวนับต้องอยู่ข้าม request ----
builder.Services.AddSingleton<LoginThrottle>();

// ---- ตรวจ agent secret ของ endpoint ฝั่ง agent ทั้งหมด (2026-08-31) ----
// singleton ได้เพราะไม่ถือ state ใดๆ เอง (อ่าน config + รับ ClientMachine เข้ามาเป็น argument)
builder.Services.AddSingleton<AgentAuthenticator>();

// ---- Program Control (2026-08-31) - จำแนกว่าโปรแกรมที่เจอเป็นเกม/ของไม่เหมาะสมหรือไม่ ----
// OnlineGameLookup เป็น singleton เพราะถือ index ชื่อเกมจาก Steam ไว้ในหน่วยความจำ (หลักแสนรายการ)
// ที่ต้องใช้ร่วมกันทุก request - ถ้าเป็น scoped จะโหลดใหม่ทุกครั้งที่มีเครื่องไหน poll เข้ามา
builder.Services.AddHttpClient();
builder.Services.AddSingleton<ProgramClassifier>();
builder.Services.AddSingleton<OnlineGameLookup>();
builder.Services.AddSingleton<ProgramVerdictService>();

// ---- SignalR: real-time push คู่ขนานกับ HTTP polling เดิม (ไม่แทนที่ poll) ----
builder.Services.AddSignalR();

// ---- เก็บว่า Agent เครื่องไหนเปิด SignalR connection ค้างอยู่ตอนนี้บ้าง (LabHub.RegisterAgent/
// OnDisconnectedAsync เขียน, endpoint ต่างๆ ที่คืน/broadcast ClientMachine อ่าน) - ดู
// AgentConnectionRegistry สำหรับเหตุผลเต็ม ต้องเป็น singleton (state ต้องอยู่ข้ามทุก request/hub
// invocation ไม่ใช่ scoped ตาม request เดียว) ----
builder.Services.AddSingleton<AgentConnectionRegistry>();

// ---- Background scan หาเครื่องที่เปลี่ยนสถานะ online/offline (ดู
// ClientOnlineStatusMonitorService) - ชั้นเสริมสำหรับเครื่องที่ไม่เคย connect SignalR เลย ----
builder.Services.AddHostedService<ClientOnlineStatusMonitorService>();

// ---- Background scan หา command ที่ส่งไปแล้วแต่ Agent ไม่เคยรายงานผลจริงกลับมา แล้ว mark
// เป็น TimedOut (ดู CommandResultTimeoutService) - ต้องอยู่ฝั่ง Server เพราะครูอาจปิดหน้า
// report ไปแล้ว แต่ command ที่ค้างยังต้องถูกสรุปผลอยู่ดี ----
builder.Services.AddHostedService<CommandResultTimeoutService>();

// ---- Website Control catalog sync (2026-09-12) - ดึง WebsiteRule จาก list ภายนอกเป็นระยะ
// (ดู WebsiteCatalogSyncer สำหรับ fetch/parse/diff, WebsiteCatalogSyncService สำหรับตัวเดินเวลา
// ทุก 24 ชม.) แยก syncer ออกมาเป็น service ต่างหากเพื่อให้ endpoint "sync-now" เรียกโค้ดเดียวกับ
// timer ได้โดยไม่ต้องพึ่ง BackgroundService ----
builder.Services.AddScoped<WebsiteCatalogSyncer>();
builder.Services.AddHostedService<WebsiteCatalogSyncService>();

// ---- รองรับ Windows Service host (ให้ publish เป็น service ได้ทีหลัง) ----
builder.Host.UseWindowsService();

StartupDiagnostics.WriteLine("Host build เสร็จแล้ว");

var app = builder.Build();

// ---- ให้ FileLoggerProvider เริ่มบันทึก Warning ขึ้นไปลง Activity Log ได้ (ต้องรอ app.Build()
// เสร็จก่อนเพราะ provider ถูกสร้างก่อนหน้านั้นมาก - ดู FileLoggerProvider.AttachServiceScopeFactory) ----
fileLoggerProvider.AttachServiceScopeFactory(app.Services.GetRequiredService<IServiceScopeFactory>());

// ---- สร้าง/migrate DB อัตโนมัติตอน start ----
// StartupDiagnostics คร่อม Migrate() ไว้ทั้งสองด้านโดยตั้งใจ: ถ้า log ค้างอยู่ที่บรรทัด
// "กำลัง migrate..." โดยไม่มี "migrate เสร็จแล้ว" ตามมา แปลว่า Migrate() เองค้าง (เช่น SQLite
// file lock ค้างจาก process ก่อนหน้าที่ถูก kill แบบไม่ graceful, หรือ disk-freeze/restore
// software บางตัวไปแทรกแซง OS-level file locking ของ SQLite ทำให้ hang แบบไม่มี exception -
// ยืนยันจาก incident จริงที่เจอ SCM error 1053/7009 (timeout 30 วิ) ตอนรันบนเครื่องที่มี
// freeze software โดยไม่มี Application-log exception ใดๆ เลย
StartupDiagnostics.WriteLine($"กำลัง migrate database: {builder.Configuration.GetConnectionString("DefaultConnection")}");
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();

    var activityLogDb = scope.ServiceProvider.GetRequiredService<ActivityLogDbContext>();
    activityLogDb.Database.Migrate();

    // ---- เปิด WAL ให้ทั้ง 2 DB (2026-08-31) ----
    // WAL แยก writer ออกจาก reader: ผู้อ่าน (Console, poll) ไม่ถูกบล็อกโดย writer ที่กำลังเขียนอยู่
    // อีกต่อไป ซึ่งเป็นรูปแบบโหลดของระบบนี้พอดี (อ่านบ่อยมาก เขียนสั้นๆ ถี่ๆ จากหลายทาง)
    // journal_mode เป็น persistent setting ที่เก็บใน header ของไฟล์ DB เอง - ตั้งครั้งเดียวติดตลอด
    // แต่เรียกซ้ำทุก start ก็ไม่เสียหาย (idempotent, คืนค่าปัจจุบันกลับมาเฉยๆ)
    //
    // ข้อควรรู้ตอน backup/ย้ายเครื่อง: WAL สร้างไฟล์คู่เพิ่ม (-wal, -shm) ข้างไฟล์ .db
    // การก๊อปแค่ .db ไฟล์เดียวขณะ service ยังรันอยู่จะได้ข้อมูลไม่ครบ ต้องหยุด service ก่อนก๊อป
    foreach (var context in new DbContext[] { db, activityLogDb })
    {
        try
        {
            context.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
        }
        catch (Exception ex)
        {
            // ไม่ hard-fail: WAL เป็นการ "ทำให้ดีขึ้น" ไม่ใช่เงื่อนไขจำเป็นในการทำงาน ถ้าตั้งไม่ได้
            // (เช่น DB อยู่บน network share ที่ไม่รองรับ shared memory) ให้รันต่อด้วย journal เดิม
            StartupDiagnostics.WriteLine($"ตั้ง journal_mode=WAL ไม่สำเร็จ (รันต่อด้วย mode เดิม): {ex.Message}");
        }
    }
}
StartupDiagnostics.WriteLine("Migrate เสร็จแล้ว กำลัง Run()...");

// ---- API Endpoints ----
// แยกตามกลุ่มไว้ใน Endpoints/<Group>Endpoints.cs (WP0, 2026-10-30) - endpoint ใหม่ให้เขียนเป็นไฟล์
// ใหม่ในโฟลเดอร์นั้นแล้วเพิ่มบรรทัด app.MapXxxEndpoints() ตรงนี้บรรทัดเดียว ไม่ต้องแตะไฟล์นี้อีก
app.MapRootEndpoints();
app.MapAuthEndpoints();
app.MapClientEndpoints();
app.MapGlobalStateEndpoints();
app.MapRuleEndpoints();
app.MapWebsiteRuleEndpoints();
app.MapWebsiteCatalogEndpoints();
app.MapPollEndpoints();
app.MapCommandEndpoints(eventCommandLifetime);
app.MapLogEndpoints();
app.MapProgramControlEndpoints();
app.MapTamperReportEndpoints();
app.MapActivityLogEndpoints();
app.MapUserEndpoints();

app.Run();
