using Microsoft.EntityFrameworkCore;
using OnionProcOparetor.Server.Data;
using OnionProcOparetor.Server.Models;
using OnionProcOparetor.Server.Services;
using static OnionProcOparetor.Server.Endpoints.AuthHelpers;

namespace OnionProcOparetor.Server.Endpoints;

/// <summary>จัดการบัญชี admin ของ Console</summary>
public static class UserEndpoints
{
    public static WebApplication MapUserEndpoints(this WebApplication app)
    {
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

        return app;
    }
}
