using Microsoft.EntityFrameworkCore;
using OnionProcOparetor.Server.Data;
using OnionProcOparetor.Server.Models;
using OnionProcOparetor.Server.Services;
using static OnionProcOparetor.Server.Endpoints.AuthHelpers;

namespace OnionProcOparetor.Server.Endpoints;

/// <summary>
/// ตั้งค่า "ป้องกันการเลี่ยง" ของ Website Control (WP2-1, 2026-10-31) - ตั้งเปิด/ปิดได้ทีละข้อ
/// (ห้าม extension, ปิด Guest/InPrivate, ปิด DevTools, ล็อก DoH/proxy, บล็อกเบราว์เซอร์อื่น)
/// Agent อ่านค่าชุดนี้จาก /api/poll (ดู PollEndpoints.cs) แล้วเขียน Chrome/Edge policy เอง
/// (ดู Agent/Services/BrowserHardeningService.cs) ยกเว้นข้อ "บล็อกเบราว์เซอร์อื่น" ซึ่งไปสร้าง/เปิดกฎ
/// ใน Program Control (ตาราง Rule) แทน เพราะใช้ pipeline การ kill process ที่มีอยู่แล้ว ไม่ต้องเขียนใหม่
/// </summary>
public static class BrowserHardeningEndpoints
{
    /// <summary>
    /// รายชื่อเบราว์เซอร์อื่นที่ "บล็อกเบราว์เซอร์อื่น" สร้างกฎให้อัตโนมัติ - จับคู่ด้วย Name คงที่
    /// (ไม่ใช่ Id) เพื่อให้ toggle ซ้ำได้แบบ idempotent: เปิดอีกรอบแล้วเจอกฎเดิมอยู่แล้ว ก็แค่เปิดกลับ
    /// ไม่สร้างซ้ำ ครูแก้ PublisherName/ProcessNameContains ของกฎพวกนี้เพิ่มเองได้ตามปกติจากหน้ากฎโปรแกรม
    /// หลังจากถูกสร้างครั้งแรกแล้ว (endpoint นี้แค่ upsert IsEnabled ไม่เขียนทับฟิลด์อื่นซ้ำ)
    /// </summary>
    private static readonly (string Name, string PublisherName, string ProcessNameContains)[] OtherBrowserTargets =
    {
        ("ป้องกันการเลี่ยง: บล็อก Firefox", "Mozilla Corporation", "firefox"),
        ("ป้องกันการเลี่ยง: บล็อก Opera", "Opera Norway AS", "opera"),
        ("ป้องกันการเลี่ยง: บล็อก Brave", "Brave Software, Inc.", "brave"),
    };

    public static WebApplication MapBrowserHardeningEndpoints(this WebApplication app)
    {
        // GET เปิดให้ Teacher อ่านได้ด้วย (เหมือน GET /api/website-rules) - แก้ค่าเท่านั้นที่ต้อง Admin
        app.MapGet("/api/browser-hardening", async (HttpRequest request, AppDbContext db) =>
        {
            var admin = await GetAuthorizedAdmin(request, db);
            if (admin is null)
                return Results.Unauthorized();
            if (admin.HasDefaultPassword)
                return Results.Json(new { error = "Password change required before continuing." }, statusCode: 403);

            return Results.Ok(await GetOrCreateSettingsAsync(db));
        });

        app.MapPost("/api/browser-hardening/set", async (BrowserHardeningRequest req, HttpRequest request, AppDbContext db, IActivityLogService activityLog) =>
        {
            var admin = await GetAuthorizedAdmin(request, db);
            if (admin is null)
                return Results.Unauthorized();
            if (admin.HasDefaultPassword)
                return Results.Json(new { error = "Password change required before continuing." }, statusCode: 403);
            if (RequireAdmin(admin) is { } forbidden) return forbidden;

            var settings = await GetOrCreateSettingsAsync(db);
            var changed = settings.BlockExtensions != req.BlockExtensions
                || settings.BlockGuestAndIncognito != req.BlockGuestAndIncognito
                || settings.BlockDevTools != req.BlockDevTools
                || settings.LockDnsAndProxy != req.LockDnsAndProxy
                || settings.BlockOtherBrowsers != req.BlockOtherBrowsers;

            settings.BlockExtensions = req.BlockExtensions;
            settings.BlockGuestAndIncognito = req.BlockGuestAndIncognito;
            settings.BlockDevTools = req.BlockDevTools;
            settings.LockDnsAndProxy = req.LockDnsAndProxy;
            settings.BlockOtherBrowsers = req.BlockOtherBrowsers;
            settings.UpdatedAt = DateTime.UtcNow;

            // "บล็อกเบราว์เซอร์อื่น" ไม่เขียน registry ของตัวเอง - แค่ upsert กฎ Program Control ให้ตรงกับ
            // ค่าที่ตั้งไว้ (เปิด = สร้าง/เปิดกฎ, ปิด = ปิดกฎไว้เฉยๆ ไม่ลบทิ้ง เผื่อครูแก้เพิ่มเองไว้แล้ว)
            await ApplyOtherBrowserRulesAsync(db, req.BlockOtherBrowsers);

            await db.SaveChangesAsync();

            if (changed)
            {
                await activityLog.LogAdminActionAsync(admin, "BrowserHardeningSettingsUpdated",
                    $"{admin.Username} แก้ค่าป้องกันการเลี่ยง (ห้าม extension={req.BlockExtensions}, " +
                    $"ปิด Guest/InPrivate={req.BlockGuestAndIncognito}, ปิด DevTools={req.BlockDevTools}, " +
                    $"ล็อก DoH/proxy={req.LockDnsAndProxy}, บล็อกเบราว์เซอร์อื่น={req.BlockOtherBrowsers})");
            }

            return Results.Ok(settings);
        });

        return app;
    }

    /// <summary>อ่านแถว BrowserHardeningSettings (Id = 1) - สร้างให้ถ้ายังไม่มี (safety net เหมือน GlobalState)</summary>
    private static async Task<BrowserHardeningSettings> GetOrCreateSettingsAsync(AppDbContext db)
    {
        var settings = await db.BrowserHardeningSettings.FirstOrDefaultAsync(s => s.Id == 1);
        if (settings is null)
        {
            settings = new BrowserHardeningSettings { Id = 1 };
            db.BrowserHardeningSettings.Add(settings);
        }

        return settings;
    }

    /// <summary>
    /// สร้างกฎ Program Control ของ Firefox/Opera/Brave ถ้ายังไม่มี แล้วตั้ง IsEnabled ให้ตรงกับ
    /// enabled - ไม่แก้ฟิลด์อื่นของกฎที่มีอยู่แล้ว (ครูอาจปรับ ProcessNameContains/RequireSignedMatch
    /// เองไว้) ไม่ SaveChangesAsync ที่นี่ - ผู้เรียก (endpoint ด้านบน) เป็นคน save รวมกับ settings ทีเดียว
    /// </summary>
    private static async Task ApplyOtherBrowserRulesAsync(AppDbContext db, bool enabled)
    {
        foreach (var (name, publisherName, processNameContains) in OtherBrowserTargets)
        {
            var rule = await db.Rules.FirstOrDefaultAsync(r => r.Name == name);
            if (rule is null)
            {
                db.Rules.Add(new Rule
                {
                    Name = name,
                    PublisherName = publisherName,
                    ProcessNameContains = processNameContains,
                    RequireSignedMatch = false,
                    KillProcess = true,
                    IsEnabled = enabled,
                    RoomId = null,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                });
            }
            else if (rule.IsEnabled != enabled)
            {
                rule.IsEnabled = enabled;
                rule.UpdatedAt = DateTime.UtcNow;
            }
        }
    }
}
