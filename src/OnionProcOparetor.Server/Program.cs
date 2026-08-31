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
});

// ---- สร้างโฟลเดอร์เก็บ DB ถ้ายังไม่มี (เช่นตอน dev หรือเครื่องใหม่) ----
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? "Data Source=C:\\ProgramData\\OnionProcOparetor\\onionprocoparetor.db";
var dbDir = Path.GetDirectoryName(connectionString.Replace("Data Source=", ""));
if (!string.IsNullOrEmpty(dbDir) && !Directory.Exists(dbDir))
{
    Directory.CreateDirectory(dbDir);
}

// ---- ผูก EF Core + SQLite เข้ากับ Dependency Injection ----
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(connectionString));

// ---- Activity Log: DB แยกต่างหาก (คนละไฟล์ SQLite) จาก DB หลัก เพราะคาดว่า volume จะสูงกว่า
// (audit ทุก action ของครู + ทุก agent connect/disconnect + ทุก warning/error) และมี auto-cleanup
// ทุก 7 วัน (ดู ActivityLogCleanupService) ต่างจาก DB หลักที่ไม่มี retention ----
var activityLogConnectionString = builder.Configuration.GetConnectionString("ActivityLogConnection")
    ?? "Data Source=C:\\ProgramData\\OnionProcOparetor\\activitylog.db";
builder.Services.AddDbContext<ActivityLogDbContext>(options =>
    options.UseSqlite(activityLogConnectionString));
builder.Services.AddScoped<IActivityLogService, ActivityLogService>();
builder.Services.AddHostedService<ActivityLogCleanupService>();

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
}
StartupDiagnostics.WriteLine("Migrate เสร็จแล้ว กำลัง Run()...");

// ---- API Endpoints ----

app.MapGet("/", () => Results.Ok(new { service = "OnionProcOparetor.Server", status = "running" }));

// ---- SignalR hub: real-time command push + live status (ชั้นเสริมคู่ขนานกับ poll เดิม) ----
app.MapHub<LabHub>("/hubs/lab");

// ---- Auth Endpoints ----

// สร้าง admin คนแรก - built-in "Administrator" เสมอ, password ว่างเปล่า (ต้องบังคับเปลี่ยนหลัง login ครั้งแรก)
app.MapPost("/api/auth/setup", async (AppDbContext db) =>
{
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
app.MapPost("/api/auth/login", async (LoginRequest req, AppDbContext db, IActivityLogService activityLog) =>
{
    var admin = await db.AdminUsers.FirstOrDefaultAsync(u => u.Username == req.Username);
    if (admin is null)
    {
        return Results.Unauthorized();
    }

    var isValid = PasswordHasher.VerifyPassword(req.Password, admin.PasswordSalt, admin.PasswordHash);
    if (!isValid)
    {
        return Results.Unauthorized();
    }

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
// (endpoint สำคัญที่สุดสำหรับฝั่ง Client Service) - ไม่ต้อง auth เพราะใช้ clientGuid ยืนยันตัวเองแทน
app.MapGet("/api/poll/{clientGuid}", async (string clientGuid, string machineName, HttpContext httpContext, AppDbContext db, IHubContext<LabHub> hub, AgentConnectionRegistry connectionRegistry, bool usingFallback = false) =>
{
    var client = await db.ClientMachines.FirstOrDefaultAsync(c => c.ClientGuid == clientGuid);

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
// CommandResultRecorder ตัวเดียวกัน ไม่ต้อง auth เหมือน /api/poll และ /api/commands/{id}/ack
//
// ต่างจาก /ack ตรงที่ /ack บอกแค่ "ได้รับแล้ว" (delivery) ส่วนอันนี้บอก "ทำได้จริงไหม" (execution)
app.MapPost("/api/commands/{commandId:int}/result", async (
    int commandId, CommandResultRequest req, AppDbContext db, IHubContext<LabHub> hub) =>
{
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
// กัน command เดิมถูกส่งซ้ำไปเรื่อยๆ ทุกรอบ poll - ไม่ต้อง auth เหมือน /api/poll และ /api/logs (ใช้เครือข่ายปิดของโรงเรียนเท่านั้น)
app.MapPost("/api/commands/{commandId:int}/ack", async (int commandId, AppDbContext db) =>
{
    var command = await db.ClientCommands.FindAsync(commandId);
    if (command is null) return Results.NotFound();

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

// client push log กลับมา - ไม่ต้อง auth (ใช้ในเครือข่ายปิดของโรงเรียนเท่านั้น)
app.MapPost("/api/logs", async (LogEntry log, AppDbContext db) =>
{
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

    await db.SaveChangesAsync();

    await activityLog.LogAdminActionAsync(admin, "AdminPasswordChanged", $"{admin.Username} เปลี่ยน password ของตัวเอง");

    return Results.Ok(new { message = "Password changed successfully." });
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
