using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using OnionProcOparetor.Server.Data;
using OnionProcOparetor.Server.Hubs;
using OnionProcOparetor.Server.Models;
using OnionProcOparetor.Server.Services;
using System.Security.Cryptography;

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

app.MapGet("/", () => Results.Ok(new { service = "OnionProcOparetor.Server", status = "running" }));

// ---- SignalR hub: real-time command push + live status (ชั้นเสริมคู่ขนานกับ poll เดิม) ----
app.MapHub<LabHub>("/hubs/lab");

// ---- Auth Endpoints ----

// สร้าง admin คนแรก - built-in "Administrator" เสมอ, password ว่างเปล่า (ต้องบังคับเปลี่ยนหลัง login ครั้งแรก)
app.MapPost("/api/auth/setup", async (HttpRequest httpRequest, AppDbContext db, ILoggerFactory loggerFactory) =>
{
    // ---- จำกัดให้เรียกได้จากเครื่อง server เองเท่านั้น (2026-08-31, จาก security audit) ----
    // ช่องโหว่เดิม: endpoint นี้ไม่ต้อง auth, สร้าง Administrator ที่มี password ว่าง, และ
    // /api/auth/login ก็ยอมรับ password ว่างนั้น ส่วน change-password ก็ไม่ต้องรู้ password เดิม
    // ต่อกันเป็นลูกโซ่ = ใครก็ตามใน LAN ที่ยิง setup -> login -> change-password ได้ก่อนครู
    // จะยึด Console ได้ถาวร และ built-in account ก็ reset จากทางอื่นไม่ได้ด้วย (โดยตั้งใจ)
    // หน้าต่างนี้เปิดใหม่ทุกครั้งที่ติดตั้ง server ใหม่/ล้าง DB (ต้นปีการศึกษา)
    //
    // ล็อกเป็น loopback ตรงกับ deployment จริงที่ออกแบบไว้: MSI ตัว Server+Console ลง 2 อย่างนี้
    // บนเครื่องเดียวกันเสมอ และ Console ก็ default ไปที่ localhost อยู่แล้ว
    // ผลข้างเคียงที่ยอมรับ: ถ้าจะติดตั้ง Console แยกเครื่องล้วนๆ ต้องเปิด Console บนเครื่อง server
    // หนึ่งครั้งเพื่อสร้าง account แรกก่อน - ครั้งเดียวตลอดอายุการติดตั้ง
    var remoteIp = httpRequest.HttpContext.Connection.RemoteIpAddress;
    if (remoteIp is null || !System.Net.IPAddress.IsLoopback(remoteIp))
    {
        loggerFactory.CreateLogger("Auth").LogWarning(
            "ปฏิเสธ /api/auth/setup จาก {RemoteIp} - อนุญาตเฉพาะ loopback เท่านั้น", remoteIp);
        return Results.Json(
            new { error = "First-time setup must be performed from the server machine itself (open the Console on the server once)." },
            statusCode: StatusCodes.Status403Forbidden);
    }

    var existingAdmin = await db.AdminUsers.AnyAsync();
    if (existingAdmin)
    {
        return Results.BadRequest(new { error = "Admin already exists. Setup can only run once." });
    }

    var salt = PasswordHasher.GenerateSalt();
    var hash = PasswordHasher.HashPassword(string.Empty, salt);

    var admin = new AdminUser
    {
        Username = "Administrator",
        PasswordSalt = salt,
        PasswordHash = hash,
        IsBuiltIn = true,
        HasDefaultPassword = true,
        CreatedAt = DateTime.UtcNow
    };

    db.AdminUsers.Add(admin);
    await db.SaveChangesAsync();

    return Results.Ok(new
    {
        message = "Built-in Administrator account created with empty password. Please log in and change the password immediately.",
        username = admin.Username
    });
});

// login - ตรวจ username/password
app.MapPost("/api/auth/login", async (
    LoginRequest req,
    HttpRequest httpRequest,
    AppDbContext db,
    IActivityLogService activityLog,
    LoginThrottle loginThrottle) =>
{
    // ---- ตรวจ lockout ก่อนแตะ DB/PBKDF2 (2026-08-31, จาก security audit) ----
    // ต้องอยู่บนสุดจริงๆ ไม่ใช่หลังตรวจรหัสผ่าน: ถ้าปล่อยให้คำนวณ PBKDF2 100k iterations ทุก request
    // ที่ยิงเข้ามา endpoint นี้จะกลายเป็นช่อง CPU-exhaustion DoS ในตัวมันเอง
    var remoteIp = httpRequest.HttpContext.Connection.RemoteIpAddress?.ToString();
    if (loginThrottle.GetRemainingLockout(req.Username, remoteIp) is { } remainingLockout)
    {
        return Results.Json(
            new { error = $"ลองผิดหลายครั้งเกินไป กรุณารออีก {Math.Ceiling(remainingLockout.TotalSeconds)} วินาที" },
            statusCode: StatusCodes.Status429TooManyRequests);
    }

    var admin = await db.AdminUsers.FirstOrDefaultAsync(u => u.Username == req.Username);
    if (admin is null)
    {
        // นับ username ที่ไม่มีอยู่จริงด้วย - ไม่งั้นการไล่เดา username ไม่ถูกจำกัดเลย
        loginThrottle.RecordFailure(req.Username, remoteIp);
        return Results.Unauthorized();
    }

    var isValid = PasswordHasher.VerifyPassword(req.Password, admin.PasswordSalt, admin.PasswordHash);
    if (!isValid)
    {
        loginThrottle.RecordFailure(req.Username, remoteIp);
        return Results.Unauthorized();
    }

    loginThrottle.RecordSuccess(req.Username, remoteIp);
    admin.LastLoginAt = DateTime.UtcNow;

    // ออก token ใหม่ อายุ 12 ชั่วโมง (พอสำหรับใช้งานหนึ่งวันเรียน)
    var tokenString = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    var authToken = new AuthToken
    {
        Token = tokenString,
        AdminUserId = admin.Id,
        CreatedAt = DateTime.UtcNow,
        ExpiresAt = DateTime.UtcNow.AddHours(12)
    };
    db.AuthTokens.Add(authToken);

    await db.SaveChangesAsync();

    await activityLog.LogAdminActionAsync(admin, "AdminLoggedIn", $"{admin.Username} เข้าสู่ระบบ");

    return Results.Ok(new
    {
        message = "Login successful.",
        username = admin.Username,
        token = tokenString,
        hasDefaultPassword = admin.HasDefaultPassword,
        adminId = admin.Id
    });
});

// ดึงรายชื่อ client ทั้งหมด + สถานะ
app.MapGet("/api/clients", async (HttpRequest request, AppDbContext db, AgentConnectionRegistry connectionRegistry) =>
{
    var admin = await GetAuthorizedAdmin(request, db);
    if (admin is null)
        return Results.Unauthorized();
    if (admin.HasDefaultPassword)
        return Results.Json(new { error = "Password change required before continuing." }, statusCode: 403);

    var clients = await db.ClientMachines.OrderBy(c => c.MachineName).ToListAsync();
    // เติม IsConnectedViaSignalR + HasEverConnectedViaSignalR ก่อน serialize เสมอ (ไม่ set = default
    // false ทั้งคู่ = IsOnline พึ่ง LastSeenAt อย่างเดียว ยังถูกอยู่แต่ไม่ได้สัญญาณเร็วจาก SignalR - ดู
    // ClientMachine.IsConnectedViaSignalR/HasEverConnectedViaSignalR)
    foreach (var client in clients)
    {
        client.IsConnectedViaSignalR = connectionRegistry.IsConnected(client.ClientGuid);
        client.HasEverConnectedViaSignalR = connectionRegistry.HasEverConnected(client.ClientGuid);
    }
    return Results.Ok(clients);
});

// toggle เครื่องใดเครื่องหนึ่ง (per-machine)
app.MapPost("/api/clients/{id:int}/toggle", async (int id, HttpRequest request, AppDbContext db, AgentConnectionRegistry connectionRegistry, IActivityLogService activityLog) =>
{
    var admin = await GetAuthorizedAdmin(request, db);
    if (admin is null)
        return Results.Unauthorized();
    if (admin.HasDefaultPassword)
        return Results.Json(new { error = "Password change required before continuing." }, statusCode: 403);

    var client = await db.ClientMachines.FindAsync(id);
    if (client is null) return Results.NotFound();

    client.IsEnabled = !client.IsEnabled;
    await db.SaveChangesAsync();
    client.IsConnectedViaSignalR = connectionRegistry.IsConnected(client.ClientGuid);
    client.HasEverConnectedViaSignalR = connectionRegistry.HasEverConnected(client.ClientGuid);

    await activityLog.LogAdminActionAsync(admin, "ClientEnforcementToggled",
        $"{admin.Username} {(client.IsEnabled ? "เปิด" : "ปิด")} enforcement ของเครื่อง '{client.MachineName}'",
        client.ClientGuid, client.MachineName);

    return Results.Ok(client);
});

// ลบเครื่อง client ออกจากระบบ (LogEntry.ClientGuid ไม่มี FK ผูกกับ ClientMachine โดยตั้งใจ
// จึงลบ ClientMachine ได้โดย log เก่าของเครื่องนั้นยังอยู่ครบ ไม่ต้อง cascade)
app.MapDelete("/api/clients/{id:int}", async (int id, HttpRequest request, AppDbContext db, IActivityLogService activityLog) =>
{
    var admin = await GetAuthorizedAdmin(request, db);
    if (admin is null)
        return Results.Unauthorized();
    if (admin.HasDefaultPassword)
        return Results.Json(new { error = "Password change required before continuing." }, statusCode: 403);

    var client = await db.ClientMachines.FindAsync(id);
    if (client is null) return Results.NotFound();

    db.ClientMachines.Remove(client);
    await db.SaveChangesAsync();

    await activityLog.LogAdminActionAsync(admin, "ClientDeleted",
        $"{admin.Username} ลบเครื่อง '{client.MachineName}' ออกจากระบบ",
        client.ClientGuid, client.MachineName);

    return Results.Ok(new { message = "Client deleted." });
});

// toggle ทั้งห้อง (global)
app.MapPost("/api/global/toggle", async (HttpRequest request, AppDbContext db, IActivityLogService activityLog) =>
{
    var admin = await GetAuthorizedAdmin(request, db);
    if (admin is null)
        return Results.Unauthorized();
    if (admin.HasDefaultPassword)
        return Results.Json(new { error = "Password change required before continuing." }, statusCode: 403);

    var state = await db.GlobalStates.FirstOrDefaultAsync(g => g.Id == 1);
    if (state is null)
    {
        state = new GlobalState { Id = 1, IsEnabled = true };
        db.GlobalStates.Add(state);
    }

    state.IsEnabled = !state.IsEnabled;
    state.UpdatedAt = DateTime.UtcNow;
    await db.SaveChangesAsync();

    await activityLog.LogAdminActionAsync(admin, "GlobalEnforcementToggled",
        $"{admin.Username} {(state.IsEnabled ? "เปิด" : "ปิด")} enforcement ทั้งห้อง");

    return Results.Ok(state);
});

// ดึงกฎทั้งหมด
app.MapGet("/api/rules", async (HttpRequest request, AppDbContext db) =>
{
    var admin = await GetAuthorizedAdmin(request, db);
    if (admin is null)
        return Results.Unauthorized();
    if (admin.HasDefaultPassword)
        return Results.Json(new { error = "Password change required before continuing." }, statusCode: 403);

    var rules = await db.Rules.OrderBy(r => r.Name).ToListAsync();
    return Results.Ok(rules);
});

// เพิ่มกฎใหม่
app.MapPost("/api/rules", async (Rule rule, HttpRequest request, AppDbContext db, IActivityLogService activityLog) =>
{
    var admin = await GetAuthorizedAdmin(request, db);
    if (admin is null)
        return Results.Unauthorized();
    if (admin.HasDefaultPassword)
        return Results.Json(new { error = "Password change required before continuing." }, statusCode: 403);

    rule.CreatedAt = DateTime.UtcNow;
    rule.UpdatedAt = DateTime.UtcNow;
    db.Rules.Add(rule);
    await db.SaveChangesAsync();

    await activityLog.LogAdminActionAsync(admin, "RuleCreated", $"{admin.Username} สร้างกฎ '{rule.Name}'");

    return Results.Created($"/api/rules/{rule.Id}", rule);
});

// ลบกฎทิ้ง (ใช้เมื่อกฎตั้งผิดหรือกว้างเกินไป - สำคัญมากเพื่อความปลอดภัย)
app.MapDelete("/api/rules/{id:int}", async (int id, HttpRequest request, AppDbContext db, IActivityLogService activityLog) =>
{
    var admin = await GetAuthorizedAdmin(request, db);
    if (admin is null)
        return Results.Unauthorized();
    if (admin.HasDefaultPassword)
        return Results.Json(new { error = "Password change required before continuing." }, statusCode: 403);

    var rule = await db.Rules.FindAsync(id);
    if (rule is null) return Results.NotFound();

    db.Rules.Remove(rule);
    await db.SaveChangesAsync();

    await activityLog.LogAdminActionAsync(admin, "RuleDeleted", $"{admin.Username} ลบกฎ '{rule.Name}'");

    return Results.Ok(new { message = "Rule deleted." });
});

// แก้ไขกฎแบบ in-place (ไม่ต้องลบแล้วสร้างใหม่ - เก็บ Id/CreatedAt/RuleOverride ของกฎเดิมไว้ครบ)
app.MapPut("/api/rules/{id:int}", async (int id, Rule updated, HttpRequest request, AppDbContext db, IActivityLogService activityLog) =>
{
    var admin = await GetAuthorizedAdmin(request, db);
    if (admin is null)
        return Results.Unauthorized();
    if (admin.HasDefaultPassword)
        return Results.Json(new { error = "Password change required before continuing." }, statusCode: 403);

    var rule = await db.Rules.FindAsync(id);
    if (rule is null) return Results.NotFound();

    rule.Name = updated.Name;
    rule.PublisherName = updated.PublisherName;
    rule.ProcessNameContains = updated.ProcessNameContains;
    rule.RequireSignedMatch = updated.RequireSignedMatch;
    rule.KillProcess = updated.KillProcess;
    rule.ActionCommand = updated.ActionCommand;
    rule.ActionArguments = updated.ActionArguments;
    rule.IsEnabled = updated.IsEnabled;
    rule.UpdatedAt = DateTime.UtcNow;

    await db.SaveChangesAsync();

    await activityLog.LogAdminActionAsync(admin, "RuleUpdated", $"{admin.Username} แก้ไขกฎ '{rule.Name}'");

    return Results.Ok(rule);
});

// toggle เปิด/ปิดกฎ (ไม่ต้องลบทิ้งถาวร แค่ปิดชั่วคราวได้)
app.MapPost("/api/rules/{id:int}/toggle", async (int id, HttpRequest request, AppDbContext db, IActivityLogService activityLog) =>
{
    var admin = await GetAuthorizedAdmin(request, db);
    if (admin is null)
        return Results.Unauthorized();
    if (admin.HasDefaultPassword)
        return Results.Json(new { error = "Password change required before continuing." }, statusCode: 403);

    var rule = await db.Rules.FindAsync(id);
    if (rule is null) return Results.NotFound();

    rule.IsEnabled = !rule.IsEnabled;
    rule.UpdatedAt = DateTime.UtcNow;
    await db.SaveChangesAsync();

    await activityLog.LogAdminActionAsync(admin, "RuleToggled",
        $"{admin.Username} {(rule.IsEnabled ? "เปิด" : "ปิด")} กฎ '{rule.Name}'");

    return Results.Ok(rule);
});

// ---- Per-agent rule override endpoints (Stable 1 เรื่องที่ 3) ----
// override ชนะ global เสมอ 100% - ดู /api/poll/{clientGuid} สำหรับจุดที่ resolve ค่าจริง

// ดึง override ทั้งหมดของเครื่องหนึ่ง (Console ใช้ merge กับ GET /api/rules เองฝั่ง client เพื่อโชว์
// รายการกฎทั้งหมด + สถานะ effective ต่อเครื่องนี้)
app.MapGet("/api/clients/{clientGuid}/rule-overrides", async (string clientGuid, HttpRequest request, AppDbContext db) =>
{
    var admin = await GetAuthorizedAdmin(request, db);
    if (admin is null)
        return Results.Unauthorized();
    if (admin.HasDefaultPassword)
        return Results.Json(new { error = "Password change required before continuing." }, statusCode: 403);

    var overrides = await db.RuleOverrides.Where(o => o.ClientGuid == clientGuid).ToListAsync();
    return Results.Ok(overrides);
});

// ตั้ง/แก้ override ของกฎหนึ่งสำหรับเครื่องหนึ่ง (upsert - ไม่มีแล้วสร้างใหม่, มีอยู่แล้วแก้ IsEnabled)
app.MapPut("/api/clients/{clientGuid}/rule-overrides/{ruleId:int}", async (string clientGuid, int ruleId, RuleOverrideRequest req, HttpRequest request, AppDbContext db, IActivityLogService activityLog) =>
{
    var admin = await GetAuthorizedAdmin(request, db);
    if (admin is null)
        return Results.Unauthorized();
    if (admin.HasDefaultPassword)
        return Results.Json(new { error = "Password change required before continuing." }, statusCode: 403);

    var rule = await db.Rules.FindAsync(ruleId);
    if (rule is null) return Results.NotFound();

    var existing = await db.RuleOverrides.FirstOrDefaultAsync(o => o.ClientGuid == clientGuid && o.RuleId == ruleId);
    if (existing is null)
    {
        existing = new RuleOverride { ClientGuid = clientGuid, RuleId = ruleId, IsEnabled = req.IsEnabled };
        db.RuleOverrides.Add(existing);
    }
    else
    {
        existing.IsEnabled = req.IsEnabled;
        existing.UpdatedAt = DateTime.UtcNow;
    }

    await db.SaveChangesAsync();

    var client = await db.ClientMachines.FirstOrDefaultAsync(c => c.ClientGuid == clientGuid);
    await activityLog.LogAdminActionAsync(admin, "RuleOverrideSet",
        $"{admin.Username} ตั้ง override กฎ '{rule.Name}' = {(req.IsEnabled ? "เปิด" : "ปิด")} สำหรับเครื่อง '{client?.MachineName}'",
        clientGuid, client?.MachineName);

    return Results.Ok(existing);
});

// ลบ override ทิ้ง - เครื่องนี้กลับไปใช้ค่า global ของกฎนี้ตามปกติ (ไม่ error ถ้าไม่เคยมี override อยู่แล้ว)
app.MapDelete("/api/clients/{clientGuid}/rule-overrides/{ruleId:int}", async (string clientGuid, int ruleId, HttpRequest request, AppDbContext db, IActivityLogService activityLog) =>
{
    var admin = await GetAuthorizedAdmin(request, db);
    if (admin is null)
        return Results.Unauthorized();
    if (admin.HasDefaultPassword)
        return Results.Json(new { error = "Password change required before continuing." }, statusCode: 403);

    var existing = await db.RuleOverrides.FirstOrDefaultAsync(o => o.ClientGuid == clientGuid && o.RuleId == ruleId);
    if (existing is not null)
    {
        db.RuleOverrides.Remove(existing);
        await db.SaveChangesAsync();

        var client = await db.ClientMachines.FirstOrDefaultAsync(c => c.ClientGuid == clientGuid);
        var rule = await db.Rules.FindAsync(ruleId);
        await activityLog.LogAdminActionAsync(admin, "RuleOverrideCleared",
            $"{admin.Username} ล้าง override กฎ '{rule?.Name}' ของเครื่อง '{client?.MachineName}' (กลับไปใช้ค่า global)",
            clientGuid, client?.MachineName);
    }

    return Results.Ok(new { message = "Override cleared." });
});

// ล้าง agent secret ที่ enroll ไว้ของเครื่องหนึ่ง เพื่อให้ enroll ใหม่ในรอบ poll ถัดไป (2026-08-31)
//
// จำเป็นเพราะ agent secret ผูกกับไฟล์ agent.key ใน %ProgramData% ของเครื่องนั้น ซึ่งหายไปได้จริง
// ในสถานการณ์ปกติของห้องนี้: ลง Windows ใหม่, Reboot Restore Rx restore ทับตอนที่ยังไม่ได้ commit
// baseline, หรือติดตั้ง Agent ใหม่หลังล้างเครื่อง - ถ้าไม่มีทางล้างจากฝั่ง Server เครื่องนั้นจะติด
// Mismatch ถาวรและ (เมื่อเปิดโหมดบังคับใช้) หลุดจากระบบไปเลยโดยที่ครูแก้เองไม่ได้
app.MapPost("/api/clients/{id:int}/reset-agent-secret", async (int id, HttpRequest request, AppDbContext db, IActivityLogService activityLog) =>
{
    var admin = await GetAuthorizedAdmin(request, db);
    if (admin is null)
        return Results.Unauthorized();
    if (admin.HasDefaultPassword)
        return Results.Json(new { error = "Password change required before continuing." }, statusCode: 403);

    var client = await db.ClientMachines.FindAsync(id);
    if (client is null) return Results.NotFound();

    client.AgentSecretHash = null;
    client.AgentSecretSalt = null;
    client.AgentSecretEnrolledAt = null;
    await db.SaveChangesAsync();

    await activityLog.LogAdminActionAsync(admin, "AgentSecretReset",
        $"{admin.Username} ล้าง agent secret ของเครื่อง '{client.MachineName}' - เครื่องนี้จะ enroll ใหม่ในรอบ poll ถัดไป",
        client.ClientGuid, client.MachineName);

    return Results.Ok(new { message = "Agent secret cleared. The machine will re-enroll on its next poll." });
});

// แก้ setting ของเครื่องใดเครื่องหนึ่งจากศูนย์กลาง (ตอนนี้มีแค่ poll interval override)
app.MapPut("/api/clients/{id:int}/settings", async (int id, ClientSettingsRequest request, HttpRequest httpRequest, AppDbContext db, AgentConnectionRegistry connectionRegistry, IActivityLogService activityLog) =>
{
    var admin = await GetAuthorizedAdmin(httpRequest, db);
    if (admin is null)
        return Results.Unauthorized();
    if (admin.HasDefaultPassword)
        return Results.Json(new { error = "Password change required before continuing." }, statusCode: 403);

    var client = await db.ClientMachines.FindAsync(id);
    if (client is null) return Results.NotFound();

    client.PollIntervalOverrideSeconds = request.PollIntervalOverrideSeconds;
    await db.SaveChangesAsync();

    client.IsConnectedViaSignalR = connectionRegistry.IsConnected(client.ClientGuid);
    client.HasEverConnectedViaSignalR = connectionRegistry.HasEverConnected(client.ClientGuid);

    await activityLog.LogAdminActionAsync(admin, "ClientSettingsChanged",
        $"{admin.Username} เปลี่ยน poll interval override ของเครื่อง '{client.MachineName}' เป็น {(client.PollIntervalOverrideSeconds?.ToString() ?? "ค่า default")}",
        client.ClientGuid, client.MachineName);

    return Results.Ok(client);
});

// client เอาไว้ poll เพื่อดึง rules + สถานะ enabled/disabled ของตัวเอง
// (endpoint สำคัญที่สุดสำหรับฝั่ง Client Service)
//
// เดิมไม่มี auth เลย ("ใช้ clientGuid ยืนยันตัวเองแทน") - แต่ clientGuid เดินทางเป็น path segment
// บน HTTP ธรรมดาทุกๆ 30 วินาที ใครที่อยู่ใน LAN เดียวกันเก็บ GUID ของทุกเครื่องได้ครบแล้วดึงกฎ
// ทั้งชุดมาดูเพื่อหาทางเลี่ยง ตั้งแต่ 2026-08-31 ตรวจ agent secret เพิ่ม (ดู AgentAuthenticator -
// โหมดบังคับใช้เปิด/ปิดด้วย config Security:RequireAgentSecret)
app.MapGet("/api/poll/{clientGuid}", async (string clientGuid, string machineName, HttpContext httpContext, AppDbContext db, IHubContext<LabHub> hub, AgentConnectionRegistry connectionRegistry, AgentAuthenticator agentAuth, IActivityLogService activityLog, bool usingFallback = false) =>
{
    var client = await db.ClientMachines.FirstOrDefaultAsync(c => c.ClientGuid == clientGuid);
    var providedSecret = httpContext.Request.Headers[AgentAuthenticator.SecretHeaderName].ToString();

    // เครื่องที่มีอยู่แล้วต้องตรวจก่อนทำอะไรทั้งสิ้น - รวมถึงก่อนอัปเดต MachineName/LastKnownIp ด้วย
    // (ไม่งั้นคนที่ปลอม GUID จะเขียนทับ LastKnownIp ของเครื่องจริงได้ ซึ่งเป็น IP ที่ Console ใช้ต่อ VNC)
    // เครื่องใหม่ที่ยังไม่มีใน DB ไม่ต้องตรวจ - จะถูก enroll ตอนสร้าง record ด้านล่าง
    if (client is not null &&
        !await agentAuth.AuthorizeAsync(client, providedSecret, activityLog, "GET /api/poll"))
    {
        return Results.Unauthorized();
    }

    // เก็บ IP ล่าสุดที่ client นี้ poll เข้ามา - แต่เดิมเป็น field ที่มีอยู่ใน model/DTO แล้วแต่ไม่เคยถูก
    // set จริงที่ไหนเลย (dead field) จำเป็นต้อง populate ให้ใช้งานได้จริงตอนนี้เพราะ Console ต้องใช้ IP
    // นี้เชื่อม TCP ตรงไปยัง Agent เพื่อทำ RFB handshake (VNC data plane ไม่ผ่าน Server เป็น relay)
    var remoteIp = httpContext.Connection.RemoteIpAddress?.ToString();

    if (client is null)
    {
        client = new ClientMachine
        {
            ClientGuid = clientGuid,
            MachineName = machineName,
            IsEnabled = true,
            RegisteredAt = DateTime.UtcNow,
            LastSeenAt = DateTime.UtcNow,
            LastKnownIp = remoteIp,
            UsingFallback = usingFallback
        };

        // เครื่องใหม่: จด secret ที่ส่งมาเป็นของจริงทันที (trust-on-first-use) ถ้าไม่ส่งมาก็ปล่อยว่างไว้
        // แล้วจะถูก enroll ในรอบ poll ถัดไปที่ส่งมา - ดู AgentAuthenticator เรื่องช่องที่เหลือของ TOFU
        agentAuth.Authenticate(client, providedSecret);

        db.ClientMachines.Add(client);
    }
    else
    {
        client.LastSeenAt = DateTime.UtcNow;
        client.MachineName = machineName;
        client.LastKnownIp = remoteIp;
        client.UsingFallback = usingFallback;
    }

    var globalState = await db.GlobalStates.FirstOrDefaultAsync(g => g.Id == 1);
    var globalEnabled = globalState?.IsEnabled ?? true;

    // Effective rule list เฉพาะเครื่องนี้: เริ่มจาก global IsEnabled ของแต่ละกฎ แล้วให้ RuleOverride
    // ของ clientGuid นี้ (ถ้ามี) ชนะเสมอ 100% ไม่มี merge/priority ซับซ้อน - เครื่องอื่นไม่เห็นผลอะไรเลย
    // เพราะ query filter ด้วย ClientGuid นี้เท่านั้น ผลลัพธ์ยังเป็น List<Rule> รูปแบบเดิมทุกอย่าง
    // (Agent ฝั่งรับไม่ต้องรู้เรื่อง override เลย - แค่ apply list ที่ได้เหมือนเดิม)
    var allRules = await db.Rules.ToListAsync();
    var ruleOverrides = await db.RuleOverrides
        .Where(o => o.ClientGuid == clientGuid)
        .ToDictionaryAsync(o => o.RuleId, o => o.IsEnabled);
    var rules = allRules
        .Where(r => ruleOverrides.TryGetValue(r.Id, out var overrideEnabled) ? overrideEnabled : r.IsEnabled)
        .ToList();

    // fallback path สำหรับ command ที่ยังไม่ถึง Agent ผ่าน SignalR (เช่น เครื่อง disconnect อยู่ตอนที่ถูกสั่ง)
    // Agent จะเห็น command เหล่านี้ผ่าน poll รอบถัดไปเสมอ ไม่ว่า SignalR จะต่อติดอยู่หรือไม่ก็ตาม
    var pendingCommands = await db.ClientCommands
        .Where(c => c.ClientGuid == clientGuid && c.Status == ClientCommandStatus.Pending)
        .OrderBy(c => c.CreatedAt)
        .Select(c => new { c.Id, c.CommandType, c.PayloadJson, c.CreatedAt })
        .ToListAsync();

    await db.SaveChangesAsync();

    // เสริม real-time status ให้ console ที่เปิดอยู่ - ไม่กระทบ response ที่คืนให้ agent เลย
    client.IsConnectedViaSignalR = connectionRegistry.IsConnected(clientGuid);
    client.HasEverConnectedViaSignalR = connectionRegistry.HasEverConnected(clientGuid);
    await hub.Clients.BroadcastClientStatus(client);

    return Results.Ok(new
    {
        enabled = globalEnabled && client.IsEnabled,
        rules,
        pollIntervalOverrideSeconds = client.PollIntervalOverrideSeconds,
        pendingCommands
    });
});

// Console สั่ง command ไปยัง Agent เครื่องใดเครื่องหนึ่ง - บันทึกลง DB เป็น pending เสมอ (fallback ผ่าน poll)
// แล้ว push ทันทีผ่าน SignalR ถ้า Agent เครื่องนั้น connected อยู่ตอนนี้
app.MapPost("/api/commands/{clientGuid}", async (string clientGuid, CommandRequest req, HttpRequest request, AppDbContext db, IHubContext<LabHub> hub, IActivityLogService activityLog) =>
{
    var admin = await GetAuthorizedAdmin(request, db);
    if (admin is null)
        return Results.Unauthorized();
    if (admin.HasDefaultPassword)
        return Results.Json(new { error = "Password change required before continuing." }, statusCode: 403);

    var command = new ClientCommand
    {
        ClientGuid = clientGuid,
        CommandType = req.CommandType,
        PayloadJson = req.Payload,
        CreatedAt = DateTime.UtcNow,
        Status = ClientCommandStatus.Pending,
        // BatchId ผูกคำสั่งทุกเครื่องที่ครูกดพร้อมกันครั้งเดียวไว้ด้วยกัน - Console สร้างค่านี้เอง
        // แล้วส่งมาให้เหมือนกันทุก request ในชุดนั้น (null ได้ถ้ายิง API ตรงโดยไม่ผ่าน Console)
        BatchId = string.IsNullOrWhiteSpace(req.BatchId) ? null : req.BatchId,
        ExecutionStatus = CommandExecutionStatus.Pending
    };
    db.ClientCommands.Add(command);
    await db.SaveChangesAsync(); // ต้อง save ก่อนถึงจะได้ command.Id จริง (SQLite autoincrement) เอาไปส่งต่อให้ SignalR ได้

    await hub.Clients.SendCommandToAgent(clientGuid, command.Id, command.CommandType, command.PayloadJson);

    var targetClient = await db.ClientMachines.FirstOrDefaultAsync(c => c.ClientGuid == clientGuid);
    await activityLog.LogAdminActionAsync(admin, "CommandSent",
        $"{admin.Username} สั่ง '{command.CommandType}' ไปยังเครื่อง '{targetClient?.MachineName}'",
        clientGuid, targetClient?.MachineName);

    return Results.Created($"/api/commands/{command.Id}", command);
});

// Agent รายงาน "ผลจริง" ของ command กลับมาหลัง process เสร็จ - fallback ของ SignalR hub method
// LabHub.ReportCommandResult (ใช้เมื่อ Agent ไม่ได้ connect SignalR อยู่) ทั้ง 2 ทางเรียก
// CommandResultRecorder ตัวเดียวกัน
//
// ต่างจาก /ack ตรงที่ /ack บอกแค่ "ได้รับแล้ว" (delivery) ส่วนอันนี้บอก "ทำได้จริงไหม" (execution)
//
// 2026-08-31: เพิ่ม agent auth + ตรวจว่า command เป็นของเครื่องที่เรียกมาจริง - เดิมไม่มีทั้งคู่ และ
// commandId เป็นเลขรันนิ่งที่เดาได้ ประกอบกับ CommandResultRecorder เป็นแบบ first-write-wins
// (ผลจริงที่มาทีหลังถูกทิ้ง) = ยิง {"success":true} ใส่เลขถัดๆ ไปทันทีหลังครูสั่งงาน แล้วผลปลอมจะ
// ค้างเป็นผลถาวรทับผลจริง ทั้งฝั่ง enforcement และฝั่ง audit trail
app.MapPost("/api/commands/{commandId:int}/result", async (
    int commandId,
    CommandResultRequest req,
    HttpRequest httpRequest,
    AppDbContext db,
    IHubContext<LabHub> hub,
    AgentAuthenticator agentAuth,
    IActivityLogService activityLog) =>
{
    var (allowed, authenticatedClient) = await agentAuth.AuthorizeByHeadersAsync(httpRequest, db, activityLog, "POST /api/commands/{id}/result");
    if (!allowed)
    {
        return Results.Unauthorized();
    }

    if (authenticatedClient is not null && !await OwnsCommandAsync(db, commandId, authenticatedClient.ClientGuid))
    {
        return Results.Unauthorized();
    }

    var command = await CommandResultRecorder.RecordAsync(db, commandId, req.Success, req.Message);
    if (command is null) return Results.NotFound();

    await hub.Clients.BroadcastCommandResult(command);

    return Results.Ok(CommandResultDto.From(command));
});

// Console ดึงสถานะทั้งชุดของ action ที่กดไปครั้งเดียว (หลายเครื่อง = หลาย command แต่ BatchId
// เดียวกัน) - ใช้เป็น fallback ของ SignalR "CommandResultChanged" และใช้ตอนเปิดหน้า report
// ครั้งแรกเพื่อดึงสถานะปัจจุบันทั้งหมดทีเดียว (บาง result อาจมาถึงก่อนหน้าต่างจะ subscribe ทัน)
app.MapGet("/api/commands/batch/{batchId}", async (string batchId, HttpRequest request, AppDbContext db) =>
{
    var admin = await GetAuthorizedAdmin(request, db);
    if (admin is null)
        return Results.Unauthorized();
    if (admin.HasDefaultPassword)
        return Results.Json(new { error = "Password change required before continuing." }, statusCode: 403);

    var commands = await db.ClientCommands
        .Where(c => c.BatchId == batchId)
        .OrderBy(c => c.Id)
        .ToListAsync();

    return Results.Ok(commands.Select(CommandResultDto.From).ToList());
});

// Agent เรียก ack กลับมาหลังได้รับ command แล้ว (ไม่ว่าจะรับผ่าน SignalR หรือเจอผ่าน poll fallback ก็ตาม)
// กัน command เดิมถูกส่งซ้ำไปเรื่อยๆ ทุกรอบ poll
//
// 2026-08-31: เพิ่ม agent auth + ตรวจ ownership ด้วยเหตุผลเดียวกับ /result ด้านบน แต่ผลกระทบของ
// ช่องนี้ตรงกว่า: Status เป็นสิ่งเดียวที่ตัดสินว่า /api/poll จะส่ง command นั้นซ้ำอีกไหม
// (filter Status == Pending) นักเรียนที่วน POST ack ใส่เลข 1..N รัวๆ จึงทำให้คำสั่งของครูที่ส่งมา
// หาเครื่องตัวเองถูก mark ว่า Delivered แล้วไม่ถูกส่งซ้ำอีกเลย ทั้งที่ไม่เคยถูกรันจริงสักครั้ง
app.MapPost("/api/commands/{commandId:int}/ack", async (
    int commandId,
    HttpRequest httpRequest,
    AppDbContext db,
    AgentAuthenticator agentAuth,
    IActivityLogService activityLog) =>
{
    var (allowed, authenticatedClient) = await agentAuth.AuthorizeByHeadersAsync(httpRequest, db, activityLog, "POST /api/commands/{id}/ack");
    if (!allowed)
    {
        return Results.Unauthorized();
    }

    var command = await db.ClientCommands.FindAsync(commandId);
    if (command is null) return Results.NotFound();

    // ตรวจ ownership แยกจาก auth: แม้ agent จะพิสูจน์ตัวตนของ "ตัวเอง" ได้ ก็ยังไม่ควร ack
    // command ของเครื่องอื่น - เป็นการกัน lateral movement ในกรณีที่ secret ของเครื่องหนึ่งหลุด
    if (authenticatedClient is not null && command.ClientGuid != authenticatedClient.ClientGuid)
    {
        return Results.Unauthorized();
    }

    // idempotent โดยตั้งใจ: ต้องเรียกซ้ำได้ปลอดภัย เพราะ Agent อาจ ack command เดียวกันมาทั้งจาก
    // SignalR path และ poll path พร้อมกัน (race) - ถ้า deliver ไปแล้วไม่ต้อง overwrite DeliveredAt ซ้ำ
    // (ไม่งั้น DeliveredAt จะขยับเวลาไปเรื่อยๆ ทุกครั้งที่ ack ซ้ำ ทั้งที่จริงควรเป็นเวลาส่งมอบครั้งแรก)
    if (command.Status != ClientCommandStatus.Delivered)
    {
        command.Status = ClientCommandStatus.Delivered;
        command.DeliveredAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    return Results.Ok(command);
});

// Console ดึงรายการ command ที่ยังค้าง Pending อยู่ของเครื่องหนึ่งๆ - คู่กับ /cancel ด้านล่าง
// (ActionReportWindow ยกเลิกได้แค่ตอนหน้าต่างนั้นยังเปิดอยู่เท่านั้น แต่ command ที่ค้างจริงอาจมา
// จากรอบก่อน เช่น หน้าต่างปิดไปแล้วหรือ Console เปิดใหม่ทับ - endpoint นี้ให้ ActionPanelWindow's
// PENDING COMMANDS... ดึงสถานะปัจจุบันจริงจาก Server ได้เสมอ ไม่ต้องพึ่งว่าเคยเห็นหน้า report
// ตอนสั่งหรือเปล่า)
app.MapGet("/api/commands/pending/{clientGuid}", async (string clientGuid, HttpRequest request, AppDbContext db) =>
{
    var admin = await GetAuthorizedAdmin(request, db);
    if (admin is null)
        return Results.Unauthorized();

    var pending = await db.ClientCommands
        .Where(c => c.ClientGuid == clientGuid && c.Status == ClientCommandStatus.Pending)
        .OrderBy(c => c.CreatedAt)
        .ToListAsync();

    return Results.Ok(pending.Select(CommandResultDto.From).ToList());
});

// Console ยกเลิก command ที่ยังค้าง Pending อยู่ - เพิ่มขึ้นหลัง field test เจอบั๊กจริง (2026-08-25):
// Restart/Shutdown ตั้งเวลาดับเครื่องแบบ fire-and-forget "ก่อน" แล้วค่อย ack "ทีหลัง" ถ้า ack POST
// ไปไม่ถึง Server ทันเวลาที่เครื่องดับจริงพอดี (มีโอกาสสูงขึ้นมากตอนใช้ Tailscale fallback ที่ช้า/
// หลุดง่ายกว่า LAN) command จะค้าง Pending ตลอดกาล - เครื่องบูตกลับมา Agent (state ในหน่วยความจำ
// ล้างหมด) poll เจอ command เดิมที่ยังเป็น Pending ซ้ำ รันซ้ำ ดับซ้ำ วนไม่จบ endpoint นี้ให้ admin
// ตัด Pending ทิ้งตรงๆ จาก Server ได้ทันทีโดยไม่ต้องพึ่ง Agent เลย (Agent ที่ยังไม่ boot กลับมา
// poll จะไม่เจอ command นี้อีกต่อไป) - แก้ที่ต้นตอจริงคือ persist "เคยรันแล้ว" ให้ Restart/Shutdown
// รอดจาก reboot ด้วย (ดู CommandProcessor.cs) แต่เครื่องที่ยังไม่ได้อัปเดต Agent ตัวใหม่ต้องพึ่ง
// endpoint นี้เป็นทางออกเดียวที่หยุด loop ได้ทันที
app.MapPost("/api/commands/{commandId:int}/cancel", async (
    int commandId, HttpRequest request, AppDbContext db, IHubContext<LabHub> hub, IActivityLogService activityLog) =>
{
    var admin = await GetAuthorizedAdmin(request, db);
    if (admin is null)
        return Results.Unauthorized();

    var command = await db.ClientCommands.FindAsync(commandId);
    if (command is null) return Results.NotFound();

    // Status (delivery/poll filter) บังคับเป็น Failed เสมอไม่ว่าจะอยู่สถานะไหนอยู่ก่อน - นี่คือตัวที่
    // หยุด /api/poll ไม่ให้ส่ง command นี้อีกจริงๆ (ดู pendingCommands query ด้านบนที่ filter
    // Status == Pending เท่านั้น)
    command.Status = ClientCommandStatus.Failed;

    // ExecutionStatus แตะเฉพาะตอนยังเป็น Pending เท่านั้น - กันทับผลจริงที่ Agent อาจรายงานมาก่อนแล้ว
    // (เช่น กด cancel ช้าไปนิดหลัง Agent เพิ่ง report สำเร็จพอดี ไม่อยากให้ข้อความ "ยกเลิก" ไปทับ
    // ผลจริงที่ถูกต้องกว่า)
    if (command.ExecutionStatus == CommandExecutionStatus.Pending)
    {
        command.ExecutionStatus = CommandExecutionStatus.Failed;
        command.ExecutionMessage = $"ยกเลิกโดย {admin.Username}";
        command.ExecutedAt = DateTime.UtcNow;
    }

    await db.SaveChangesAsync();
    await hub.Clients.BroadcastCommandResult(command);

    var targetClient = await db.ClientMachines.FirstOrDefaultAsync(c => c.ClientGuid == command.ClientGuid);
    await activityLog.LogAdminActionAsync(admin, "CommandCancelled",
        $"{admin.Username} ยกเลิกคำสั่ง '{command.CommandType}' (id={command.Id}) ที่ค้างอยู่บนเครื่อง '{targetClient?.MachineName}'",
        command.ClientGuid, targetClient?.MachineName);

    return Results.Ok(CommandResultDto.From(command));
});

// client push log กลับมา - ตรวจ agent secret ตั้งแต่ 2026-08-31 (เดิมไม่ตรวจอะไรเลย ทำให้ใครใน
// LAN ก็ push log ปลอมใส่เครื่องคนอื่น หรือถมดิสก์ของ server ได้)
app.MapPost("/api/logs", async (
    LogEntry log,
    HttpRequest httpRequest,
    AppDbContext db,
    AgentAuthenticator agentAuth,
    IActivityLogService activityLog) =>
{
    var (allowed, authenticatedClient) = await agentAuth.AuthorizeByHeadersAsync(httpRequest, db, activityLog, "POST /api/logs");
    if (!allowed)
    {
        return Results.Unauthorized();
    }

    // บังคับให้ log ผูกกับเครื่องที่พิสูจน์ตัวตนได้จริง ไม่ใช่ค่าที่อยู่ใน body - ไม่งั้นเครื่องที่
    // auth ผ่านในนามตัวเองยังยัด ClientGuid/MachineName ของเครื่องอื่นลง body ได้อยู่ดี
    if (authenticatedClient is not null)
    {
        log.ClientGuid = authenticatedClient.ClientGuid;
        log.MachineName = authenticatedClient.MachineName;
    }

    log.Timestamp = DateTime.UtcNow;
    db.LogEntries.Add(log);
    await db.SaveChangesAsync();
    return Results.Created($"/api/logs/{log.Id}", log);
});

// ---- Tamper report จาก agent-watchdog.ps1 (installer/CustomActions/) ----
// ยิงมา "ก่อน" เครื่องจะ reboot ตัวเอง เป็น best-effort ล้วนๆ: watchdog รอแค่ไม่กี่วินาที
// แล้ว reboot ต่อไม่ว่าจะสำเร็จหรือไม่ - เป้าหมายเดียวคือให้ครูเห็นที่ Console ว่า "เครื่อง
// COM-xx หลุดไปเพราะถูก tamper" แทนที่จะเห็นแค่ "หลุดไปเฉยๆ" ซึ่งแยกจากเน็ตมีปัญหาไม่ออก
//
// ไม่ต้อง auth เหมือน /api/logs และ /api/poll (ข้อจำกัดเดิมของทั้งระบบ - ดู PROGRESS/
// SECURITY note: agent-facing endpoints ทั้งหมดยังไม่มี per-agent secret) endpoint นี้จึง
// "เขียนได้อย่างเดียว ไม่เปลี่ยน state ของ enforcement อะไรเลย" โดยตั้งใจ: ปลอมเข้ามาได้
// อย่างมากก็สร้าง log ปลอม ไม่สามารถใช้ปิด/เปิดกฎ หรือสั่งการเครื่องอื่นได้
app.MapPost("/api/tamper-report", async (
    TamperReportRequest req,
    AppDbContext db,
    IActivityLogService activityLog,
    IHubContext<LabHub> hub) =>
{
    // ตัดความยาวทุก field ก่อนเก็บ - ผู้ส่งไม่ได้ผ่าน auth จึงต้องถือว่าเป็น input ที่ควบคุมไม่ได้
    // (endpoint นี้เป็นจุดใหม่ ไม่อยากให้เพิ่มพื้นที่ disk-fill ให้กว้างกว่าที่ /api/logs มีอยู่แล้ว)
    static string Clamp(string? value, int max) =>
        string.IsNullOrEmpty(value) ? string.Empty : (value.Length <= max ? value : value[..max]);

    var machineName = Clamp(req.MachineName, 128);
    var reasonCode = Clamp(req.ReasonCode, 64);
    var detail = Clamp(req.Detail, 1024);

    var log = new LogEntry
    {
        ClientGuid = Clamp(req.ClientGuid, 64),
        MachineName = machineName,
        // ไม่มี process ที่เกี่ยวข้องจริง - ใส่ marker ที่ค้นหาเจอง่ายใน Console แทนการปล่อยว่าง
        ProcessPath = "[TAMPER]",
        DetectedPublisher = reasonCode,
        WasSignedMatch = false,
        MatchedRuleId = null,
        MatchedRuleName = "Tamper Protection",
        ActionTaken = $"TAMPER DETECTED ({reasonCode}) - เครื่องกำลังจะ restart อัตโนมัติ | {detail}",
        Timestamp = DateTime.UtcNow
    };

    db.LogEntries.Add(log);
    await db.SaveChangesAsync();

    await activityLog.LogAgentConnectivityAsync(
        "AgentTamperDetected",
        $"เครื่อง {machineName} ตรวจพบการแก้ไข/ถอน Agent ที่ไม่ได้รับอนุญาต ({reasonCode}) และกำลังรีสตาร์ทตัวเอง",
        log.ClientGuid,
        machineName);

    // push ให้ Console ที่เปิดอยู่เห็นทันที ไม่ต้องรอรอบ refresh ถัดไป - เครื่องกำลังจะ reboot
    // ในอีกไม่กี่วินาที ถ้ารอ poll ปกติครูจะเห็นตอนที่เครื่องหายไปแล้วซึ่งช้าเกินไปที่จะไปดูทัน
    await hub.Clients.Group("console").SendAsync("TamperDetected", new
    {
        clientGuid = log.ClientGuid,
        machineName,
        reasonCode,
        detail,
        detectedAtUtc = log.Timestamp
    });

    return Results.Ok(new { received = true });
});

// ดู log ล่าสุด (ไว้ให้ Console GUI แสดงผล)
app.MapGet("/api/logs", async (HttpRequest request, AppDbContext db, int take = 100) =>
{
    var admin = await GetAuthorizedAdmin(request, db);
    if (admin is null)
        return Results.Unauthorized();
    if (admin.HasDefaultPassword)
        return Results.Json(new { error = "Password change required before continuing." }, statusCode: 403);

    var logs = await db.LogEntries
        .OrderByDescending(l => l.Timestamp)
        .Take(take)
        .ToListAsync();
    return Results.Ok(logs);
});

// ดู Activity Log (audit การกระทำของครู + agent connectivity + system warning/error) พร้อม filter
// + pagination - ตั้งใจแยก endpoint จาก /api/logs (คนละเรื่องกัน คนละ DbContext) และคืนเป็นหน้าๆ
// เสมอ ไม่คืนทั้งหมดทีเดียว (volume คาดว่าสูงกว่า LogEntry มาก)
app.MapGet("/api/activitylog", async (
    HttpRequest request, AppDbContext db, ActivityLogDbContext activityDb,
    ActivityLogCategory? category, ActivityLogSeverity? minSeverity,
    DateTime? from, DateTime? to, string? clientGuid, int? actorAdminUserId,
    int page = 1, int pageSize = 100) =>
{
    var admin = await GetAuthorizedAdmin(request, db);
    if (admin is null)
        return Results.Unauthorized();
    if (admin.HasDefaultPassword)
        return Results.Json(new { error = "Password change required before continuing." }, statusCode: 403);

    page = Math.Max(page, 1);
    pageSize = Math.Clamp(pageSize, 1, 500);

    var query = activityDb.ActivityLogEntries.AsQueryable();
    if (category is not null) query = query.Where(e => e.Category == category);
    if (minSeverity is not null) query = query.Where(e => e.Severity >= minSeverity);
    if (from is not null) query = query.Where(e => e.Timestamp >= from);
    if (to is not null) query = query.Where(e => e.Timestamp <= to);
    if (!string.IsNullOrWhiteSpace(clientGuid)) query = query.Where(e => e.TargetClientGuid == clientGuid);
    if (actorAdminUserId is not null) query = query.Where(e => e.ActorAdminUserId == actorAdminUserId);

    var totalCount = await query.CountAsync();
    var items = await query
        .OrderByDescending(e => e.Timestamp)
        .Skip((page - 1) * pageSize)
        .Take(pageSize)
        .ToListAsync();

    // Category/Severity ส่งเป็น string ไม่ใช่ enum ตัวเลข (ไม่มี JsonStringEnumConverter ตั้งอยู่
    // ใน pipeline นี้ - pattern เดียวกับ CommandResultDto ที่แปลง enum เป็น string ก่อนส่งเสมอ
    // เพื่อให้ Console อ่านตรงๆ ได้โดยไม่ต้องก๊อป enum ไปไว้อีกฝั่ง)
    var dtoItems = items.Select(e => new
    {
        e.Id,
        e.Timestamp,
        Category = e.Category.ToString(),
        Severity = e.Severity.ToString(),
        e.EventType,
        e.Description,
        e.ActorAdminUserId,
        e.ActorUsername,
        e.TargetClientGuid,
        e.TargetMachineName,
        e.DetailJson
    });

    return Results.Ok(new { items = dtoItems, totalCount, page, pageSize });
});

// UninstallHelper.exe เรียกตรงนี้หลัง admin login สำเร็จ (แทนรหัสผ่าน uninstall แบบ local เดิม) ก่อน
// จะสั่ง msiexec /x จริง - บันทึกไว้เป็น audit trail ว่าใครอนุมัติถอด Agent ออกจากเครื่องไหนเมื่อไหร่
// (ดู installer/CustomActions/check-uninstall-password.ps1 สำหรับกลไก verify จริงฝั่ง MSI - endpoint
// นี้ไม่เกี่ยวกับการอนุญาต uninstall เลย แค่บันทึกผลหลังอนุญาตแล้วเท่านั้น)
app.MapPost("/api/activitylog/agent-uninstall-authorized", async (
    AgentUninstallAuthorizedRequest req, HttpRequest request, AppDbContext db, IActivityLogService activityLog) =>
{
    var admin = await GetAuthorizedAdmin(request, db);
    if (admin is null)
        return Results.Unauthorized();

    await activityLog.LogAdminActionAsync(admin, "AgentUninstallAuthorized",
        $"{admin.Username} ยืนยัน uninstall Agent บนเครื่อง '{req.MachineName}'",
        req.ClientGuid, req.MachineName);

    return Results.Ok();
});

// ---- User Management Endpoints ----

// list admin users ทั้งหมด (ไม่คืน hash/salt ออกไปเด็ดขาด)
app.MapGet("/api/admin/users", async (HttpRequest request, AppDbContext db) =>
{
    var admin = await GetAuthorizedAdmin(request, db);
    if (admin is null)
        return Results.Unauthorized();
    if (admin.HasDefaultPassword)
        return Results.Json(new { error = "Password change required before continuing." }, statusCode: 403);

    var users = await db.AdminUsers
        .OrderBy(u => u.Username)
        .Select(u => new
        {
            u.Id,
            u.Username,
            u.IsBuiltIn,
            u.HasDefaultPassword,
            u.CreatedAt,
            u.LastLoginAt
        })
        .ToListAsync();

    return Results.Ok(users);
});

// สร้าง admin user ใหม่ (ไม่ใช่ built-in)
app.MapPost("/api/admin/users", async (CreateUserRequest req, HttpRequest request, AppDbContext db, IActivityLogService activityLog) =>
{
    var admin = await GetAuthorizedAdmin(request, db);
    if (admin is null)
        return Results.Unauthorized();
    if (admin.HasDefaultPassword)
        return Results.Json(new { error = "Password change required before continuing." }, statusCode: 403);

    if (string.IsNullOrWhiteSpace(req.Username) || string.IsNullOrWhiteSpace(req.Password))
    {
        return Results.BadRequest(new { error = "Username and password are required." });
    }

    if (req.Password.Length < 8)
    {
        return Results.BadRequest(new { error = "Password must be at least 8 characters." });
    }

    var usernameTaken = await db.AdminUsers.AnyAsync(u => u.Username == req.Username);
    if (usernameTaken)
    {
        return Results.BadRequest(new { error = "Username already exists." });
    }

    var salt = PasswordHasher.GenerateSalt();
    var hash = PasswordHasher.HashPassword(req.Password, salt);

    var newUser = new AdminUser
    {
        Username = req.Username,
        PasswordSalt = salt,
        PasswordHash = hash,
        IsBuiltIn = false,
        HasDefaultPassword = false,
        CreatedAt = DateTime.UtcNow
    };

    db.AdminUsers.Add(newUser);
    await db.SaveChangesAsync();

    await activityLog.LogAdminActionAsync(admin, "AdminUserCreated", $"{admin.Username} สร้าง admin user '{newUser.Username}'");

    return Results.Created($"/api/admin/users/{newUser.Id}", new { newUser.Id, newUser.Username });
});

// ลบ admin user (ห้ามลบ built-in account - กัน lockout)
app.MapDelete("/api/admin/users/{id:int}", async (int id, HttpRequest request, AppDbContext db, IActivityLogService activityLog) =>
{
    var admin = await GetAuthorizedAdmin(request, db);
    if (admin is null)
        return Results.Unauthorized();
    if (admin.HasDefaultPassword)
        return Results.Json(new { error = "Password change required before continuing." }, statusCode: 403);

    var target = await db.AdminUsers.FindAsync(id);
    if (target is null) return Results.NotFound();

    if (target.IsBuiltIn)
    {
        return Results.BadRequest(new { error = "Cannot delete the built-in Administrator account." });
    }

    db.AdminUsers.Remove(target);
    await db.SaveChangesAsync();

    await activityLog.LogAdminActionAsync(admin, "AdminUserDeleted", $"{admin.Username} ลบ admin user '{target.Username}'");

    return Results.Ok(new { message = "User deleted." });
});

// เปลี่ยน password ของตัวเอง - endpoint เดียวที่ยกเว้นการเช็ค HasDefaultPassword
app.MapPost("/api/admin/users/{id:int}/change-password", async (int id, ChangePasswordRequest req, HttpRequest request, AppDbContext db, IActivityLogService activityLog) =>
{
    var admin = await GetAuthorizedAdmin(request, db);
    if (admin is null)
        return Results.Unauthorized();

    // ให้เปลี่ยนได้เฉพาะ account ของตัวเอง กันแอบเปลี่ยนของคนอื่นผ่าน id
    if (admin.Id != id)
    {
        return Results.Json(new { error = "You can only change your own password." }, statusCode: 403);
    }

    if (string.IsNullOrEmpty(req.NewPassword) || req.NewPassword.Length < 8)
    {
        return Results.BadRequest(new { error = "New password must be at least 8 characters." });
    }

    var salt = PasswordHasher.GenerateSalt();
    var hash = PasswordHasher.HashPassword(req.NewPassword, salt);

    admin.PasswordSalt = salt;
    admin.PasswordHash = hash;
    admin.HasDefaultPassword = false;

    // ---- เพิกถอน token เก่าทั้งหมดของ account นี้ (2026-08-31, จาก security audit) ----
    // เดิม GetAuthorizedAdmin ตรวจแค่ ExpiresAt อย่างเดียว และไม่มีโค้ดตรงไหนลบ AuthToken เลย
    // แปลว่า token ที่ถูกดักไว้ (ทั้งระบบยังเป็น HTTP ล้วน) ใช้ได้ต่อจนครบ 12 ชม. แม้เจ้าของจะรู้ตัว
    // และเปลี่ยนรหัสผ่านไปแล้ว - "เปลี่ยนรหัสผ่าน" จึงไม่ได้แปลว่า "ตัดคนอื่นออก" อย่างที่ควรเป็น
    //
    // เว้น token ของ request ปัจจุบันไว้ตัวเดียวโดยตั้งใจ (semantics แบบ "ออกจากระบบอุปกรณ์อื่น"):
    // flow บังคับเปลี่ยนรหัสผ่านครั้งแรกของ built-in Administrator เดินต่อด้วย ApiClient/token ตัวเดิม
    // ทันที (MainWindow -> ForceChangePasswordWindow -> DashboardWindow ใช้ instance เดียวกันตลอด)
    // ถ้าลบ token ปัจจุบันไปด้วย การ setup ครั้งแรกของทุกเครื่องจะพังทันทีด้วย 401 รัวๆ
    var currentToken = request.Headers.TryGetValue("X-Auth-Token", out var rawToken)
        ? rawToken.ToString()
        : null;

    var staleTokens = await db.AuthTokens
        .Where(t => t.AdminUserId == admin.Id && t.Token != currentToken)
        .ToListAsync();
    db.AuthTokens.RemoveRange(staleTokens);

    await db.SaveChangesAsync();

    await activityLog.LogAdminActionAsync(admin, "AdminPasswordChanged",
        $"{admin.Username} เปลี่ยน password ของตัวเอง (เพิกถอน session อื่น {staleTokens.Count} รายการ)");

    return Results.Ok(new { message = "Password changed successfully.", otherSessionsRevoked = staleTokens.Count });
});

// admin คนหนึ่ง reset password ให้ user คนอื่น (ต่างจาก change-password ที่จำกัดแค่ตัวเอง)
// ไม่ต้องรู้ password เดิมของเป้าหมายเลย เหมาะกับกรณีลืม password
app.MapPost("/api/admin/users/{id:int}/reset-password", async (int id, ChangePasswordRequest req, HttpRequest request, AppDbContext db, IActivityLogService activityLog) =>
{
    var admin = await GetAuthorizedAdmin(request, db);
    if (admin is null)
        return Results.Unauthorized();
    if (admin.HasDefaultPassword)
        return Results.Json(new { error = "Password change required before continuing." }, statusCode: 403);

    var target = await db.AdminUsers.FindAsync(id);
    if (target is null) return Results.NotFound();

    // บั๊กความปลอดภัยจริงที่เจอ (2026-08-25): endpoint นี้ไม่เคยกัน built-in "Administrator" account
    // เลย - admin คนอื่นที่ login อยู่ (ไม่ว่าจะเป็นใครก็ตาม) reset password ของ built-in account ได้
    // โดยไม่ต้อง login เป็น built-in เองเลย ขัดกับเจตนาที่อยากให้ built-in account เปลี่ยน password
    // ตัวเองได้ก็จริง แต่ต้อง login ด้วย built-in account เท่านั้นถึงจะทำได้ (self-service ผ่าน
    // change-password ด้านบนเท่านั้น ซึ่งบังคับ admin.Id == id อยู่แล้วโดยธรรมชาติ)
    if (target.IsBuiltIn)
    {
        return Results.BadRequest(new { error = "Cannot reset the built-in Administrator's password this way - log in as Administrator and change it from there instead." });
    }

    if (string.IsNullOrEmpty(req.NewPassword) || req.NewPassword.Length < 8)
    {
        return Results.BadRequest(new { error = "New password must be at least 8 characters." });
    }

    var salt = PasswordHasher.GenerateSalt();
    var hash = PasswordHasher.HashPassword(req.NewPassword, salt);

    target.PasswordSalt = salt;
    target.PasswordHash = hash;
    // สำคัญ: ตั้ง HasDefaultPassword = true เพื่อบังคับให้เจ้าของ account เปลี่ยน password
    // อีกครั้งตอน login ครั้งถัดไป (คนที่ reset ให้ไม่ควรรู้ password ถาวรของอีกคน)
    //target.HasDefaultPassword = true;

    // เพิกถอน session ที่ค้างอยู่ของ account เป้าหมายทั้งหมด (2026-08-31) - reset password ให้คนอื่น
    // แต่ปล่อยให้ token เดิมของเขาใช้ได้ต่ออีก 12 ชม. ทำให้การ reset ไม่มีผลอะไรจริงในเชิงตัดสิทธิ์
    // ต่างจาก change-password ด้านบนที่เว้น token ปัจจุบันไว้ (คนละคนกัน - ที่นี่ลบของเป้าหมายทั้งหมด)
    var targetTokens = await db.AuthTokens.Where(t => t.AdminUserId == target.Id).ToListAsync();
    db.AuthTokens.RemoveRange(targetTokens);

    await db.SaveChangesAsync();

    await activityLog.LogAdminActionAsync(admin, "AdminPasswordReset", $"{admin.Username} reset password ให้ '{target.Username}'");

    return Results.Ok(new { message = $"Password for '{target.Username}' has been reset. They must change it on next login." });
});


// ---- Helper: เช็คว่า request มี token ที่ valid ไหม คืน AdminUser ถ้า valid, null ถ้าไม่ ----
async Task<AdminUser?> GetAuthorizedAdmin(HttpRequest request, AppDbContext db)
{
    if (!request.Headers.TryGetValue("X-Auth-Token", out var tokenValue))
    {
        return null;
    }

    var token = await db.AuthTokens
        .FirstOrDefaultAsync(t => t.Token == tokenValue.ToString() && t.ExpiresAt > DateTime.UtcNow);

    if (token is null)
    {
        return null;
    }

    return await db.AdminUsers.FindAsync(token.AdminUserId);
}

app.Run();

/// <summary>
/// command นี้เป็นของเครื่องนั้นจริงไหม - แยกเป็น helper เพราะทั้ง /ack และ /result ต้องใช้เหมือนกัน
/// และเป็นเงื่อนไขที่ถ้าลืมใส่จุดใดจุดหนึ่งแล้วช่องโหว่จะกลับมาทันทีโดยไม่มีอาการอะไรให้เห็น
/// </summary>
static async Task<bool> OwnsCommandAsync(AppDbContext db, int commandId, string clientGuid)
{
    var owner = await db.ClientCommands
        .Where(c => c.Id == commandId)
        .Select(c => c.ClientGuid)
        .FirstOrDefaultAsync();

    // ไม่มี command นี้อยู่ = ปล่อยผ่านให้ endpoint ตอบ 404 ตามเดิม (ไม่ใช่ 401) - แยกให้ชัดว่า
    // "ไม่มีของ" กับ "มีของแต่ไม่ใช่ของคุณ" คนละเรื่อง
    return owner is null || owner == clientGuid;
}

// ---- Request DTOs ----
record CreateUserRequest(string Username, string Password);
record ChangePasswordRequest(string NewPassword);
record RuleOverrideRequest(bool IsEnabled);

/// <summary>BatchId เป็น optional - Console เวอร์ชันเก่าที่ไม่ส่งมาก็ยังใช้งานได้ตามปกติ (เป็น null)</summary>
record CommandRequest(string CommandType, string? Payload, string? BatchId = null);

/// <summary>ผลจริงที่ Agent รายงานกลับมาหลัง process command เสร็จ</summary>
record CommandResultRequest(bool Success, string? Message);
record AgentUninstallAuthorizedRequest(string ClientGuid, string MachineName);

/// <summary>
/// รายงานจาก agent-watchdog.ps1 ก่อนที่มันจะสั่ง reboot เครื่องตัวเอง
/// DetectedAtUtc มาจากฝั่ง client (นาฬิกาเครื่องนักเรียน) จึงไม่ถูกใช้เป็น Timestamp จริงของ
/// LogEntry - Server ประทับเวลาของตัวเองเสมอเหมือนทุก endpoint อื่น รับมาเพื่อดู clock skew เท่านั้น
/// </summary>
record TamperReportRequest(
    string? ClientGuid,
    string? MachineName,
    string? ReasonCode,
    string? Detail,
    DateTime? DetectedAtUtc);
