using Microsoft.EntityFrameworkCore;
using OnionProcOparetor.Server.Data;
using OnionProcOparetor.Server.Models;
using OnionProcOparetor.Server.Services;
using System.Security.Cryptography;

static async Task<AdminUser?> GetAuthorizedAdmin(HttpRequest request, AppDbContext db)
{
    if (!request.Headers.TryGetValue("Authorization", out var authValues))
    {
        return null;
    }

    var token = authValues.ToString();
    if (string.IsNullOrWhiteSpace(token))
    {
        return null;
    }

    if (token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
    {
        token = token["Bearer ".Length..];
    }

    var authToken = await db.AuthTokens
        .AsNoTracking()
        .FirstOrDefaultAsync(t => t.Token == token && t.ExpiresAt > DateTime.UtcNow);

    if (authToken is null)
    {
        return null;
    }

    return await db.AdminUsers.AsNoTracking().FirstOrDefaultAsync(u => u.Id == authToken.AdminUserId);
}

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

// ---- Auth Endpoints ----

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

app.Run();
