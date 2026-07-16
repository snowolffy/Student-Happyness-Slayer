using Microsoft.EntityFrameworkCore;
using MyLabGuard.Server.Data;
using MyLabGuard.Server.Models;
using MyLabGuard.Server.Services;
using System.Security.Cryptography;

var builder = WebApplication.CreateBuilder(args);

// ---- ตั้งค่า Kestrel ให้ฟัง port ตามที่กำหนดใน appsettings.json ----
var serverPort = builder.Configuration.GetValue<int>("Server:Port", 8787);
builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(serverPort); // ฟังทุก network interface เพื่อให้ client/console เครื่องอื่นต่อเข้ามาได้
});

// ---- สร้างโฟลเดอร์เก็บ DB ถ้ายังไม่มี (เช่นตอน dev หรือเครื่องใหม่) ----
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? "Data Source=mylabguard.db";
var dbDir = Path.GetDirectoryName(connectionString.Replace("Data Source=", ""));
if (!string.IsNullOrEmpty(dbDir) && !Directory.Exists(dbDir))
{
    Directory.CreateDirectory(dbDir);
}

// ---- ผูก EF Core + SQLite เข้ากับ Dependency Injection ----
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(connectionString));

// ---- รองรับ Windows Service host (ให้ publish เป็น service ได้ทีหลัง) ----
builder.Host.UseWindowsService();

var app = builder.Build();

// ---- สร้าง/migrate DB อัตโนมัติตอน start (สะดวกสำหรับ dev, ทีหลังอาจเปลี่ยนเป็น migration แบบเต็ม) ----
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
}

// ---- API Endpoints ----

app.MapGet("/", () => Results.Ok(new { service = "MyLabGuard.Server", status = "running" }));

// ---- Auth Endpoints ----

// สร้าง admin คนแรก - ใช้ได้แค่ตอนยังไม่มี admin เลยในระบบ (กันคนอื่นมาสร้างซ้ำ/แย่งสิทธิ์)
app.MapPost("/api/auth/setup", async (AdminSetupRequest req, AppDbContext db) =>
{
    var existingAdmin = await db.AdminUsers.AnyAsync();
    if (existingAdmin)
    {
        return Results.BadRequest(new { error = "Admin already exists. Setup can only run once." });
    }

    if (string.IsNullOrWhiteSpace(req.Username) || string.IsNullOrWhiteSpace(req.Password))
    {
        return Results.BadRequest(new { error = "Username and password are required." });
    }

    if (req.Password.Length < 8)
    {
        return Results.BadRequest(new { error = "Password must be at least 8 characters." });
    }

    var salt = PasswordHasher.GenerateSalt();
    var hash = PasswordHasher.HashPassword(req.Password, salt);

    var admin = new AdminUser
    {
        Username = req.Username,
        PasswordSalt = salt,
        PasswordHash = hash,
        CreatedAt = DateTime.UtcNow
    };

    db.AdminUsers.Add(admin);
    await db.SaveChangesAsync();

    return Results.Ok(new { message = "Admin created successfully." });
});

// login - ตรวจ username/password
app.MapPost("/api/auth/login", async (LoginRequest req, AppDbContext db) =>
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

    return Results.Ok(new { message = "Login successful.", username = admin.Username, token = tokenString });
});

// ดึงรายชื่อ client ทั้งหมด + สถานะ
app.MapGet("/api/clients", async (HttpRequest request, AppDbContext db) =>
{
    if (!await IsAuthorized(request, db)) return Results.Unauthorized();

    var clients = await db.ClientMachines.OrderBy(c => c.MachineName).ToListAsync();
    return Results.Ok(clients);
});

// toggle เครื่องใดเครื่องหนึ่ง (per-machine)
app.MapPost("/api/clients/{id:int}/toggle", async (int id, HttpRequest request, AppDbContext db) =>
{
    if (!await IsAuthorized(request, db)) return Results.Unauthorized();
    var client = await db.ClientMachines.FindAsync(id);
    if (client is null) return Results.NotFound();

    client.IsEnabled = !client.IsEnabled;
    await db.SaveChangesAsync();
    return Results.Ok(client);
});

// toggle ทั้งห้อง (global)
app.MapPost("/api/global/toggle", async (HttpRequest request, AppDbContext db) =>
{
    if (!await IsAuthorized(request, db)) return Results.Unauthorized();
    var state = await db.GlobalStates.FirstOrDefaultAsync(g => g.Id == 1);
    if (state is null)
    {
        state = new GlobalState { Id = 1, IsEnabled = true };
        db.GlobalStates.Add(state);
    }

    state.IsEnabled = !state.IsEnabled;
    state.UpdatedAt = DateTime.UtcNow;
    await db.SaveChangesAsync();
    return Results.Ok(state);
});

// ดึงกฎทั้งหมด
app.MapGet("/api/rules", async (HttpRequest request, AppDbContext db) =>
{
    if (!await IsAuthorized(request, db)) return Results.Unauthorized();
    var rules = await db.Rules.OrderBy(r => r.Name).ToListAsync();
    return Results.Ok(rules);
});

// เพิ่มกฎใหม่
app.MapPost("/api/rules", async (Rule rule, HttpRequest request, AppDbContext db) =>
{
    if (!await IsAuthorized(request, db)) return Results.Unauthorized();
    rule.CreatedAt = DateTime.UtcNow;
    rule.UpdatedAt = DateTime.UtcNow;
    db.Rules.Add(rule);
    await db.SaveChangesAsync();
    return Results.Created($"/api/rules/{rule.Id}", rule);
});

// ลบกฎทิ้ง (ใช้เมื่อกฎตั้งผิดหรือกว้างเกินไป - สำคัญมากเพื่อความปลอดภัย)
app.MapDelete("/api/rules/{id:int}", async (int id, HttpRequest request, AppDbContext db) =>
{
    if (!await IsAuthorized(request, db)) return Results.Unauthorized();

    var rule = await db.Rules.FindAsync(id);
    if (rule is null) return Results.NotFound();

    db.Rules.Remove(rule);
    await db.SaveChangesAsync();
    return Results.Ok(new { message = "Rule deleted." });
});

// toggle เปิด/ปิดกฎ (ไม่ต้องลบทิ้งถาวร แค่ปิดชั่วคราวได้)
app.MapPost("/api/rules/{id:int}/toggle", async (int id, HttpRequest request, AppDbContext db) =>
{
    if (!await IsAuthorized(request, db)) return Results.Unauthorized();

    var rule = await db.Rules.FindAsync(id);
    if (rule is null) return Results.NotFound();

    rule.IsEnabled = !rule.IsEnabled;
    rule.UpdatedAt = DateTime.UtcNow;
    await db.SaveChangesAsync();
    return Results.Ok(rule);
});

// client เอาไว้ poll เพื่อดึง rules + สถานะ enabled/disabled ของตัวเอง
// (endpoint สำคัญที่สุดสำหรับฝั่ง Client Service)
app.MapGet("/api/poll/{clientGuid}", async (string clientGuid, string machineName, AppDbContext db) =>
{
    var client = await db.ClientMachines.FirstOrDefaultAsync(c => c.ClientGuid == clientGuid);

    // ถ้ายังไม่เคยเห็นเครื่องนี้มาก่อน ให้ register อัตโนมัติ (ค่าเริ่มต้น = enabled)
    if (client is null)
    {
        client = new ClientMachine
        {
            ClientGuid = clientGuid,
            MachineName = machineName,
            IsEnabled = true,
            RegisteredAt = DateTime.UtcNow,
            LastSeenAt = DateTime.UtcNow
        };
        db.ClientMachines.Add(client);
    }
    else
    {
        client.LastSeenAt = DateTime.UtcNow;
        client.MachineName = machineName; // เผื่อเปลี่ยนชื่อเครื่อง
    }

    var globalState = await db.GlobalStates.FirstOrDefaultAsync(g => g.Id == 1);
    var globalEnabled = globalState?.IsEnabled ?? true;

    var rules = await db.Rules.Where(r => r.IsEnabled).ToListAsync();

    await db.SaveChangesAsync();

    return Results.Ok(new
    {
        // enforcement เปิดจริงๆ ก็ต่อเมื่อทั้ง global และ per-machine เปิดพร้อมกัน
        enabled = globalEnabled && client.IsEnabled,
        rules
    });
});

// client push log กลับมา
app.MapPost("/api/logs", async (LogEntry log, AppDbContext db) =>
{
    log.Timestamp = DateTime.UtcNow;
    db.LogEntries.Add(log);
    await db.SaveChangesAsync();
    return Results.Created($"/api/logs/{log.Id}", log);
});

// ดู log ล่าสุด (ไว้ให้ Console GUI แสดงผล)
app.MapGet("/api/logs", async (HttpRequest request, AppDbContext db, int take = 100) =>
{
    if (!await IsAuthorized(request, db)) return Results.Unauthorized();
    var logs = await db.LogEntries
        .OrderByDescending(l => l.Timestamp)
        .Take(take)
        .ToListAsync();
    return Results.Ok(logs);
});

// ---- Helper: เช็คว่า request มี token ที่ valid ไหม ----
async Task<bool> IsAuthorized(HttpRequest request, AppDbContext db)
{
    if (!request.Headers.TryGetValue("X-Auth-Token", out var tokenValue))
    {
        return false;
    }

    var token = await db.AuthTokens
        .FirstOrDefaultAsync(t => t.Token == tokenValue.ToString() && t.ExpiresAt > DateTime.UtcNow);

    return token is not null;
}

app.Run();
// ---- Request DTOs ----
record AdminSetupRequest(string Username, string Password);
record LoginRequest(string Username, string Password);