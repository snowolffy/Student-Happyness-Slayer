using Microsoft.EntityFrameworkCore;
using OnionProcOparetor.Server.Data;
using OnionProcOparetor.Server.Models;
using OnionProcOparetor.Server.Services;
using static OnionProcOparetor.Server.Endpoints.AuthHelpers;

namespace OnionProcOparetor.Server.Endpoints;

// ---- Request DTOs ของ EmergencyCodeEndpoints (WP3-4, 2026-10-31) ----
record TestEmergencyCodeRequest(string Code);
record EmergencyUsageUploadItem(DateTime UsedAtUtc);
record UploadEmergencyUsageRequest(List<EmergencyUsageUploadItem>? Usages);

/// <summary>
/// รหัสฉุกเฉิน (TOTP) ทั้งระบบ (WP3-4, 2026-10-31) - ตรงตาม docs/wireframes/nf-settings-more.dc.html
/// (หัวข้อ "รหัสฉุกเฉิน") + docs/wireframes/oc-emergency.dc.html
///
/// 3 endpoint แรกเป็นของ Console (Admin เท่านั้น - ตั้งค่า/ทดสอบ/rotate secret) endpoint สุดท้าย
/// เป็นของ Agent (push ประวัติการใช้ขึ้นมาตอนกลับมาออนไลน์ - auth แบบ agent secret เหมือน
/// POST /api/web-visits ดู WebVisitEndpoints.cs)
///
/// ตั้งใจไม่มี endpoint ให้ Apps Script ดึง secret สดๆ จาก Server (ต้องเปิด public ไม่มี auth เพื่อให้
/// หน้าเว็บ Apps Script เรียกได้ ซึ่งเท่ากับเปิดช่องขโมย secret ตรงๆ) - ตามที่ roadmap ตัดสินใจไว้แล้ว
/// ว่า secret ของ Apps Script อยู่ใน Script Properties ที่ Admin copy ไปวางเองตอนตั้งค่าครั้งเดียว
/// </summary>
public static class EmergencyCodeEndpoints
{
    private const string Issuer = "Onion ProcOparetor";

    public static WebApplication MapEmergencyCodeEndpoints(this WebApplication app)
    {
        // ---- อ่านค่า secret/QR/สถานะปัจจุบัน (การ์ด "รหัสฉุกเฉิน" ในหน้าตั้งค่า) ----
        app.MapGet("/api/settings/emergency-code", async (HttpRequest request, AppDbContext db) =>
        {
            var admin = await GetAuthorizedAdmin(request, db);
            if (admin is null) return Results.Unauthorized();
            if (admin.HasDefaultPassword) return Results.Json(new { error = "Password change required before continuing." }, statusCode: 403);
            if (RequireAdmin(admin) is { } forbidden) return forbidden;

            var settings = await GetOrCreateEmergencyCodeSettingsAsync(db);
            var usageCount = await db.EmergencyCodeUsages.CountAsync();
            var lastUsage = await db.EmergencyCodeUsages.OrderByDescending(u => u.UsedAtUtc).FirstOrDefaultAsync();

            return Results.Ok(new
            {
                settings.SecretBase32,
                otpAuthUri = TotpService.BuildOtpAuthUri(settings.SecretBase32, Issuer, "ครูผู้ดูแล"),
                settings.CreatedAt,
                settings.RotatedAt,
                settings.RotatedByUsername,
                usageCount,
                lastUsageAt = lastUsage?.UsedAtUtc,
                lastUsageMachineName = lastUsage?.MachineName
            });
        });

        // ---- ทดสอบรหัส 6 หลักจากแอป Authenticator/Apps Script ก่อนเอาไปใช้จริง ----
        app.MapPost("/api/settings/emergency-code/test", async (TestEmergencyCodeRequest req, HttpRequest request, AppDbContext db) =>
        {
            var admin = await GetAuthorizedAdmin(request, db);
            if (admin is null) return Results.Unauthorized();
            if (admin.HasDefaultPassword) return Results.Json(new { error = "Password change required before continuing." }, statusCode: 403);
            if (RequireAdmin(admin) is { } forbidden) return forbidden;

            var settings = await GetOrCreateEmergencyCodeSettingsAsync(db);
            var valid = TotpService.Verify(settings.SecretBase32, req.Code, DateTimeOffset.UtcNow);

            return Results.Ok(new { valid });
        });

        // ---- เปลี่ยน secret ใหม่ทั้งหมด (ใช้เมื่อสงสัยว่ารั่ว) - ต้องลง Agent MSI ใหม่ทุกเครื่องถึงจะใช้ secret ใหม่จริง ----
        app.MapPost("/api/settings/emergency-code/rotate", async (HttpRequest request, AppDbContext db, IActivityLogService activityLog) =>
        {
            var admin = await GetAuthorizedAdmin(request, db);
            if (admin is null) return Results.Unauthorized();
            if (admin.HasDefaultPassword) return Results.Json(new { error = "Password change required before continuing." }, statusCode: 403);
            if (RequireAdmin(admin) is { } forbidden) return forbidden;

            var settings = await GetOrCreateEmergencyCodeSettingsAsync(db);
            settings.SecretBase32 = TotpService.GenerateSecretBase32();
            settings.RotatedAt = DateTime.UtcNow;
            settings.RotatedByUsername = admin.Username;
            await db.SaveChangesAsync();

            await activityLog.LogAdminActionAsync(admin, "EmergencyCodeRotated",
                $"{admin.Username} เปลี่ยน secret รหัสฉุกเฉินใหม่ - ต้องลง Agent MSI ใหม่ทุกเครื่องและแก้ Apps Script Properties ด้วย");

            return Results.Ok(new
            {
                settings.SecretBase32,
                otpAuthUri = TotpService.BuildOtpAuthUri(settings.SecretBase32, Issuer, "ครูผู้ดูแล")
            });
        });

        // ---- ประวัติการใช้ (ปุ่ม "ดูทั้งหมด") ----
        app.MapGet("/api/settings/emergency-code/usage", async (HttpRequest request, AppDbContext db, int take = 100) =>
        {
            var admin = await GetAuthorizedAdmin(request, db);
            if (admin is null) return Results.Unauthorized();
            if (admin.HasDefaultPassword) return Results.Json(new { error = "Password change required before continuing." }, statusCode: 403);
            if (RequireAdmin(admin) is { } forbidden) return forbidden;

            var items = await db.EmergencyCodeUsages
                .OrderByDescending(u => u.UsedAtUtc)
                .Take(Math.Clamp(take, 1, 500))
                .ToListAsync();

            return Results.Ok(items);
        });

        // ---- Agent ส่งประวัติการใช้ที่สะสมไว้ตอน Server ล่มขึ้นมา (ตอนกลับมาออนไลน์แล้ว) ----
        app.MapPost("/api/emergency-code/usage", async (
            UploadEmergencyUsageRequest req,
            HttpRequest httpRequest,
            AppDbContext db,
            AgentAuthenticator agentAuth,
            IActivityLogService activityLog) =>
        {
            var (allowed, authenticatedClient) = await agentAuth.AuthorizeByHeadersAsync(httpRequest, db, activityLog, "POST /api/emergency-code/usage");
            if (!allowed)
            {
                return Results.Unauthorized();
            }

            if (req.Usages is null || req.Usages.Count == 0 || authenticatedClient is null)
            {
                return Results.Ok(new { added = 0 });
            }

            var now = DateTime.UtcNow;
            var rows = req.Usages
                .Take(50)
                .Select(u => new EmergencyCodeUsage
                {
                    ClientGuid = authenticatedClient.ClientGuid,
                    MachineName = authenticatedClient.MachineName,
                    UsedAtUtc = u.UsedAtUtc,
                    ReceivedAtUtc = now
                })
                .ToList();

            db.EmergencyCodeUsages.AddRange(rows);
            await db.SaveChangesAsync();

            await activityLog.LogAgentConnectivityAsync(
                "EmergencyCodeUsed",
                $"เครื่อง '{authenticatedClient.MachineName}' ถูกปลดด้วยรหัสฉุกเฉิน {rows.Count} ครั้ง (รายงานย้อนหลังตอนกลับมาออนไลน์)",
                authenticatedClient.ClientGuid,
                authenticatedClient.MachineName);

            return Results.Ok(new { added = rows.Count });
        });

        return app;
    }

    /// <summary>อ่านแถว EmergencyCodeSettings (Id = 1) - สร้างพร้อม secret สุ่มใหม่ถ้ายังไม่มี (pattern เดียวกับ GetOrCreateSignInSettingsAsync)</summary>
    public static async Task<EmergencyCodeSettings> GetOrCreateEmergencyCodeSettingsAsync(AppDbContext db)
    {
        var settings = await db.EmergencyCodeSettings.FirstOrDefaultAsync(s => s.Id == 1);
        if (settings is null)
        {
            settings = new EmergencyCodeSettings { Id = 1, SecretBase32 = TotpService.GenerateSecretBase32() };
            db.EmergencyCodeSettings.Add(settings);
            await db.SaveChangesAsync();
        }

        return settings;
    }
}
