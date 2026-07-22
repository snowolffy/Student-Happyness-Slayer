using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using OnionProcOparetor.Server.Data;
using OnionProcOparetor.Server.Hubs;
using OnionProcOparetor.Server.Models;
using OnionProcOparetor.Server.Services;
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
    ?? "Data Source=C:\\ProgramData\\OnionProcOparetor\\onionprocoparetor.db";
var dbDir = Path.GetDirectoryName(connectionString.Replace("Data Source=", ""));
if (!string.IsNullOrEmpty(dbDir) && !Directory.Exists(dbDir))
{
    Directory.CreateDirectory(dbDir);
}

// ---- ผูก EF Core + SQLite เข้ากับ Dependency Injection ----
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(connectionString));

// ---- SignalR: real-time push คู่ขนานกับ HTTP polling เดิม (ไม่แทนที่ poll) ----
builder.Services.AddSignalR();

// ---- รองรับ Windows Service host (ให้ publish เป็น service ได้ทีหลัง) ----
builder.Host.UseWindowsService();

var app = builder.Build();

// ---- สร้าง/migrate DB อัตโนมัติตอน start ----
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
}

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
app.MapGet("/api/clients", async (HttpRequest request, AppDbContext db) =>
{
    var admin = await GetAuthorizedAdmin(request, db);
    if (admin is null)
        return Results.Unauthorized();
    if (admin.HasDefaultPassword)
        return Results.Json(new { error = "Password change required before continuing." }, statusCode: 403);

    var clients = await db.ClientMachines.OrderBy(c => c.MachineName).ToListAsync();
    return Results.Ok(clients);
});

// toggle เครื่องใดเครื่องหนึ่ง (per-machine)
app.MapPost("/api/clients/{id:int}/toggle", async (int id, HttpRequest request, AppDbContext db) =>
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
    return Results.Ok(client);
});

// ลบเครื่อง client ออกจากระบบ (LogEntry.ClientGuid ไม่มี FK ผูกกับ ClientMachine โดยตั้งใจ
// จึงลบ ClientMachine ได้โดย log เก่าของเครื่องนั้นยังอยู่ครบ ไม่ต้อง cascade)
app.MapDelete("/api/clients/{id:int}", async (int id, HttpRequest request, AppDbContext db) =>
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
    return Results.Ok(new { message = "Client deleted." });
});

// toggle ทั้งห้อง (global)
app.MapPost("/api/global/toggle", async (HttpRequest request, AppDbContext db) =>
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
app.MapPost("/api/rules", async (Rule rule, HttpRequest request, AppDbContext db) =>
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
    return Results.Created($"/api/rules/{rule.Id}", rule);
});

// ลบกฎทิ้ง (ใช้เมื่อกฎตั้งผิดหรือกว้างเกินไป - สำคัญมากเพื่อความปลอดภัย)
app.MapDelete("/api/rules/{id:int}", async (int id, HttpRequest request, AppDbContext db) =>
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
    return Results.Ok(new { message = "Rule deleted." });
});

// toggle เปิด/ปิดกฎ (ไม่ต้องลบทิ้งถาวร แค่ปิดชั่วคราวได้)
app.MapPost("/api/rules/{id:int}/toggle", async (int id, HttpRequest request, AppDbContext db) =>
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
    return Results.Ok(rule);
});

// แก้ setting ของเครื่องใดเครื่องหนึ่งจากศูนย์กลาง (ตอนนี้มีแค่ poll interval override)
app.MapPut("/api/clients/{id:int}/settings", async (int id, ClientSettingsRequest request, HttpRequest httpRequest, AppDbContext db) =>
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

    return Results.Ok(client);
});

// client เอาไว้ poll เพื่อดึง rules + สถานะ enabled/disabled ของตัวเอง
// (endpoint สำคัญที่สุดสำหรับฝั่ง Client Service) - ไม่ต้อง auth เพราะใช้ clientGuid ยืนยันตัวเองแทน
app.MapGet("/api/poll/{clientGuid}", async (string clientGuid, string machineName, AppDbContext db, IHubContext<LabHub> hub) =>
{
    var client = await db.ClientMachines.FirstOrDefaultAsync(c => c.ClientGuid == clientGuid);

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
        client.MachineName = machineName;
    }

    var globalState = await db.GlobalStates.FirstOrDefaultAsync(g => g.Id == 1);
    var globalEnabled = globalState?.IsEnabled ?? true;

    var rules = await db.Rules.Where(r => r.IsEnabled).ToListAsync();

    // fallback path สำหรับ command ที่ยังไม่ถึง Agent ผ่าน SignalR (เช่น เครื่อง disconnect อยู่ตอนที่ถูกสั่ง)
    // Agent จะเห็น command เหล่านี้ผ่าน poll รอบถัดไปเสมอ ไม่ว่า SignalR จะต่อติดอยู่หรือไม่ก็ตาม
    var pendingCommands = await db.ClientCommands
        .Where(c => c.ClientGuid == clientGuid && c.Status == ClientCommandStatus.Pending)
        .OrderBy(c => c.CreatedAt)
        .Select(c => new { c.Id, c.CommandType, c.PayloadJson, c.CreatedAt })
        .ToListAsync();

    await db.SaveChangesAsync();

    // เสริม real-time status ให้ console ที่เปิดอยู่ - ไม่กระทบ response ที่คืนให้ agent เลย
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
app.MapPost("/api/commands/{clientGuid}", async (string clientGuid, CommandRequest req, HttpRequest request, AppDbContext db, IHubContext<LabHub> hub) =>
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
        Status = ClientCommandStatus.Pending
    };
    db.ClientCommands.Add(command);
    await db.SaveChangesAsync(); // ต้อง save ก่อนถึงจะได้ command.Id จริง (SQLite autoincrement) เอาไปส่งต่อให้ SignalR ได้

    await hub.Clients.SendCommandToAgent(clientGuid, command.Id, command.CommandType, command.PayloadJson);

    return Results.Created($"/api/commands/{command.Id}", command);
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

// client push log กลับมา - ไม่ต้อง auth (ใช้ในเครือข่ายปิดของโรงเรียนเท่านั้น)
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
app.MapPost("/api/admin/users", async (CreateUserRequest req, HttpRequest request, AppDbContext db) =>
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

    return Results.Created($"/api/admin/users/{newUser.Id}", new { newUser.Id, newUser.Username });
});

// ลบ admin user (ห้ามลบ built-in account - กัน lockout)
app.MapDelete("/api/admin/users/{id:int}", async (int id, HttpRequest request, AppDbContext db) =>
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
    return Results.Ok(new { message = "User deleted." });
});

// เปลี่ยน password ของตัวเอง - endpoint เดียวที่ยกเว้นการเช็ค HasDefaultPassword
app.MapPost("/api/admin/users/{id:int}/change-password", async (int id, ChangePasswordRequest req, HttpRequest request, AppDbContext db) =>
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

    return Results.Ok(new { message = "Password changed successfully." });
});

// admin คนหนึ่ง reset password ให้ user คนอื่น (ต่างจาก change-password ที่จำกัดแค่ตัวเอง)
// ไม่ต้องรู้ password เดิมของเป้าหมายเลย เหมาะกับกรณีลืม password
app.MapPost("/api/admin/users/{id:int}/reset-password", async (int id, ChangePasswordRequest req, HttpRequest request, AppDbContext db) =>
{
    var admin = await GetAuthorizedAdmin(request, db);
    if (admin is null)
        return Results.Unauthorized();
    if (admin.HasDefaultPassword)
        return Results.Json(new { error = "Password change required before continuing." }, statusCode: 403);

    var target = await db.AdminUsers.FindAsync(id);
    if (target is null) return Results.NotFound();

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
record CommandRequest(string CommandType, string? Payload);
