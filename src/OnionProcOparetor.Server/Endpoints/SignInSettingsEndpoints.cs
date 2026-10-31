using Microsoft.EntityFrameworkCore;
using OnionProcOparetor.Server.Data;
using OnionProcOparetor.Server.Models;
using OnionProcOparetor.Server.Services;
using static OnionProcOparetor.Server.Endpoints.AuthHelpers;

namespace OnionProcOparetor.Server.Endpoints;

// ---- Request DTOs ของ SignInSettingsEndpoints (WP3-1, 2026-10-31) ----
record UpdateSignInSettingsRequest(
    int RequireLoginMode,
    string? RequireLoginRoomIdsCsv,
    bool GuestModeEnabled,
    bool EmergencyCodeEnabled,
    int PasswordMinLength,
    bool ForceChangePasswordOnFirstLogin,
    int LockoutFailedAttempts,
    int MultiDeviceMode,
    int AutoLogoutIdleMinutes,
    bool LogoutOnShutdown,
    bool TeacherCanResetPassword,
    bool ShowStudentNameInConsole,
    string? LoginScreenMessage,
    string? LockScreenDefaultMessage);

record UpdateBackupSettingsRequest(int Frequency, int RetentionCount, string? StoragePath);

/// <summary>
/// ตั้งค่า Sign-in นักเรียนทั้งระบบ + ตั้งค่า/สั่งสำรองข้อมูล DB (WP3-1, 2026-10-31) - ตรงตาม wireframe
/// docs/wireframes/nf-signin-settings.dc.html และ docs/wireframes/nf-settings-more.dc.html (เฉพาะหัวข้อ
/// "สำรองข้อมูล" - ส่วน Staff session/รหัสฉุกเฉิน/HTTPS ของหน้านั้นเป็นของ WP2/WP3-4/WP1 ไม่แก้ที่นี่)
///
/// ทุก endpoint ในไฟล์นี้เป็นสิทธิ์ Admin เท่านั้น (ตั้งค่าระบบ - ตารางสิทธิ์ Admin/Teacher ใน
/// docs/roadmap.md ระบุชัดว่า Teacher แก้ตั้งค่าระบบไม่ได้ ต่างจากรีเซ็ตรหัส/ปลดล็อกที่เปิดให้ Teacher
/// ได้ถ้าตั้งค่าไว้ - ดู RosterEndpoints.cs)
/// </summary>
public static class SignInSettingsEndpoints
{
    public static WebApplication MapSignInSettingsEndpoints(this WebApplication app)
    {
        // ---- Sign-in settings (nf-signin-settings) ----

        app.MapGet("/api/settings/signin", async (HttpRequest request, AppDbContext db) =>
        {
            var admin = await GetAuthorizedAdmin(request, db);
            if (admin is null) return Results.Unauthorized();
            if (admin.HasDefaultPassword) return Results.Json(new { error = "Password change required before continuing." }, statusCode: 403);
            if (RequireAdmin(admin) is { } forbidden) return forbidden;

            return Results.Ok(await GetOrCreateSignInSettingsAsync(db));
        });

        app.MapPut("/api/settings/signin", async (UpdateSignInSettingsRequest req, HttpRequest request, AppDbContext db, IActivityLogService activityLog) =>
        {
            var admin = await GetAuthorizedAdmin(request, db);
            if (admin is null) return Results.Unauthorized();
            if (admin.HasDefaultPassword) return Results.Json(new { error = "Password change required before continuing." }, statusCode: 403);
            if (RequireAdmin(admin) is { } forbidden) return forbidden;

            if (!Enum.IsDefined(typeof(RequireLoginMode), req.RequireLoginMode))
                return Results.BadRequest(new { error = "RequireLoginMode ไม่ถูกต้อง" });
            if (!Enum.IsDefined(typeof(MultiDeviceLoginMode), req.MultiDeviceMode))
                return Results.BadRequest(new { error = "MultiDeviceMode ไม่ถูกต้อง" });
            if (req.PasswordMinLength < 4 || req.PasswordMinLength > 32)
                return Results.BadRequest(new { error = "ความยาวรหัสผ่านขั้นต่ำต้องอยู่ระหว่าง 4-32" });
            if (req.LockoutFailedAttempts < 1 || req.LockoutFailedAttempts > 50)
                return Results.BadRequest(new { error = "จำนวนครั้งที่กรอกผิดต้องอยู่ระหว่าง 1-50" });
            if (req.AutoLogoutIdleMinutes < 1)
                return Results.BadRequest(new { error = "เวลาไม่ได้ใช้งานต้องมากกว่า 0 นาที" });

            var settings = await GetOrCreateSignInSettingsAsync(db);
            settings.RequireLoginMode = (RequireLoginMode)req.RequireLoginMode;
            settings.RequireLoginRoomIdsCsv = req.RequireLoginRoomIdsCsv;
            settings.GuestModeEnabled = req.GuestModeEnabled;
            settings.EmergencyCodeEnabled = req.EmergencyCodeEnabled;
            settings.PasswordMinLength = req.PasswordMinLength;
            settings.ForceChangePasswordOnFirstLogin = req.ForceChangePasswordOnFirstLogin;
            settings.LockoutFailedAttempts = req.LockoutFailedAttempts;
            settings.MultiDeviceMode = (MultiDeviceLoginMode)req.MultiDeviceMode;
            settings.AutoLogoutIdleMinutes = req.AutoLogoutIdleMinutes;
            settings.LogoutOnShutdown = req.LogoutOnShutdown;
            settings.TeacherCanResetPassword = req.TeacherCanResetPassword;
            settings.ShowStudentNameInConsole = req.ShowStudentNameInConsole;
            settings.LoginScreenMessage = req.LoginScreenMessage;
            settings.LockScreenDefaultMessage = string.IsNullOrWhiteSpace(req.LockScreenDefaultMessage)
                ? settings.LockScreenDefaultMessage
                : req.LockScreenDefaultMessage;
            settings.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();

            await activityLog.LogAdminActionAsync(admin, "SignInSettingsChanged", $"{admin.Username} แก้ตั้งค่า Sign-in นักเรียน");

            return Results.Ok(settings);
        });

        // ---- Backup settings + ปุ่ม "สำรองตอนนี้" (nf-settings-more > สำรองข้อมูล) ----

        app.MapGet("/api/settings/backup", async (HttpRequest request, AppDbContext db, DatabaseBackupService backupService) =>
        {
            var admin = await GetAuthorizedAdmin(request, db);
            if (admin is null) return Results.Unauthorized();
            if (admin.HasDefaultPassword) return Results.Json(new { error = "Password change required before continuing." }, statusCode: 403);
            if (RequireAdmin(admin) is { } forbidden) return forbidden;

            var settings = await backupService.GetOrCreateSettingsAsync();
            var files = await backupService.ListBackupsAsync();

            return Results.Ok(new
            {
                settings.Id,
                settings.Frequency,
                settings.RetentionCount,
                settings.StoragePath,
                settings.LastBackupAt,
                settings.LastBackupSizeBytes,
                settings.LastBackupFileName,
                settings.UpdatedAt,
                effectiveStoragePath = string.IsNullOrWhiteSpace(settings.StoragePath) ? DatabaseBackupService.DefaultBackupDirectory : settings.StoragePath,
                files = files.Select(f => new { f.FileName, f.SizeBytes, f.CreatedAtUtc })
            });
        });

        app.MapPut("/api/settings/backup", async (UpdateBackupSettingsRequest req, HttpRequest request, AppDbContext db, DatabaseBackupService backupService, IActivityLogService activityLog) =>
        {
            var admin = await GetAuthorizedAdmin(request, db);
            if (admin is null) return Results.Unauthorized();
            if (admin.HasDefaultPassword) return Results.Json(new { error = "Password change required before continuing." }, statusCode: 403);
            if (RequireAdmin(admin) is { } forbidden) return forbidden;

            if (!Enum.IsDefined(typeof(BackupFrequency), req.Frequency))
                return Results.BadRequest(new { error = "Frequency ไม่ถูกต้อง" });
            if (req.RetentionCount < 1 || req.RetentionCount > 365)
                return Results.BadRequest(new { error = "จำนวนชุดที่เก็บย้อนหลังต้องอยู่ระหว่าง 1-365" });

            var settings = await backupService.GetOrCreateSettingsAsync();
            settings.Frequency = (BackupFrequency)req.Frequency;
            settings.RetentionCount = req.RetentionCount;
            settings.StoragePath = string.IsNullOrWhiteSpace(req.StoragePath) ? null : req.StoragePath.Trim();
            settings.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();

            await activityLog.LogAdminActionAsync(admin, "BackupSettingsChanged", $"{admin.Username} แก้ตั้งค่าสำรองข้อมูล");

            return Results.Ok(settings);
        });

        app.MapPost("/api/settings/backup/now", async (HttpRequest request, AppDbContext db, DatabaseBackupService backupService, IActivityLogService activityLog) =>
        {
            var admin = await GetAuthorizedAdmin(request, db);
            if (admin is null) return Results.Unauthorized();
            if (admin.HasDefaultPassword) return Results.Json(new { error = "Password change required before continuing." }, statusCode: 403);
            if (RequireAdmin(admin) is { } forbidden) return forbidden;

            var result = await backupService.BackupNowAsync();

            await activityLog.LogAdminActionAsync(admin, "DatabaseBackedUp",
                $"{admin.Username} สำรองข้อมูลด้วยมือ ({result.MainFileName} + {result.ActivityLogFileName}, {result.TotalSizeBytes} bytes)");

            return Results.Ok(new { result.MainFileName, result.ActivityLogFileName, result.Directory, result.TotalSizeBytes, result.CreatedAtUtc });
        });

        return app;
    }

    /// <summary>
    /// อ่านแถว SignInSettings (Id = 1) - สร้างให้ถ้ายังไม่มี (pattern เดียวกับ
    /// GlobalStateEndpoints.GetOrCreateGlobalStateAsync) เป็น public เพราะ PollEndpoints.cs และ
    /// StudentAuthEndpoints.cs ต้องอ่านค่านี้ด้วย (requireLogin ของ poll, กติกา login ของนักเรียน)
    /// </summary>
    public static async Task<SignInSettings> GetOrCreateSignInSettingsAsync(AppDbContext db)
    {
        var settings = await db.SignInSettings.FirstOrDefaultAsync(s => s.Id == 1);
        if (settings is null)
        {
            settings = new SignInSettings { Id = 1 };
            db.SignInSettings.Add(settings);
            await db.SaveChangesAsync();
        }
        return settings;
    }
}
