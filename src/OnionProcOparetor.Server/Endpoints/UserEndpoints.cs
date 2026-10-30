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
            // WP4: จัดการบัญชี (ผู้ใช้) เป็นสิทธิ์ Admin เท่านั้น (ตาราง Admin/Teacher ใน docs/roadmap.md)
            if (RequireAdmin(admin) is { } forbidden) return forbidden;

            // materialize ก่อนแล้วค่อยแปลง Role เป็น string - enum.ToString() แปลงเป็น SQL ไม่ได้ตรงๆ
            // (pattern เดียวกับ Category/Severity ใน ActivityLogEndpoints.cs)
            var entities = await db.AdminUsers.OrderBy(u => u.Username).ToListAsync();
            var users = entities.Select(u => new
            {
                u.Id,
                u.Username,
                u.IsBuiltIn,
                u.HasDefaultPassword,
                u.CreatedAt,
                u.LastLoginAt,
                Role = u.Role.ToString(),
                u.DisplayName
            });

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
            if (RequireAdmin(admin) is { } forbidden) return forbidden;

            if (string.IsNullOrWhiteSpace(req.Username) || string.IsNullOrWhiteSpace(req.Password))
            {
                return Results.BadRequest(new { error = "Username and password are required." });
            }

            if (req.Password.Length < 8)
            {
                return Results.BadRequest(new { error = "Password must be at least 8 characters." });
            }

            if (!Enum.IsDefined(typeof(AdminRole), req.Role))
            {
                return Results.BadRequest(new { error = "Unknown role." });
            }

            var usernameTaken = await db.AdminUsers.AnyAsync(u => u.Username == req.Username);
            if (usernameTaken)
            {
                return Results.BadRequest(new { error = "Username already exists." });
            }

            var salt = PasswordHasher.GenerateSalt();
            var hash = PasswordHasher.HashPassword(req.Password, salt);
            var role = (AdminRole)req.Role;
            // DisplayName เริ่มต้น = Username เสมอถ้าไม่ได้ระบุมา (WP4, ตรงกับ default ของบัญชีเดิมตอน migrate)
            var displayName = string.IsNullOrWhiteSpace(req.DisplayName) ? req.Username : req.DisplayName.Trim();

            var newUser = new AdminUser
            {
                Username = req.Username,
                PasswordSalt = salt,
                PasswordHash = hash,
                IsBuiltIn = false,
                HasDefaultPassword = false,
                CreatedAt = DateTime.UtcNow,
                Role = role,
                DisplayName = displayName
            };

            db.AdminUsers.Add(newUser);
            await db.SaveChangesAsync();

            await activityLog.LogAdminActionAsync(admin, "AdminUserCreated",
                $"{admin.Username} สร้าง admin user '{newUser.Username}' ({newUser.DisplayName}, role={newUser.Role})");

            return Results.Created($"/api/admin/users/{newUser.Id}", new { newUser.Id, newUser.Username, newUser.DisplayName, Role = newUser.Role.ToString() });
        });

        // ลบ admin user (ห้ามลบ built-in account - กัน lockout)
        app.MapDelete("/api/admin/users/{id:int}", async (int id, HttpRequest request, AppDbContext db, IActivityLogService activityLog) =>
        {
            var admin = await GetAuthorizedAdmin(request, db);
            if (admin is null)
                return Results.Unauthorized();
            if (admin.HasDefaultPassword)
                return Results.Json(new { error = "Password change required before continuing." }, statusCode: 403);
            if (RequireAdmin(admin) is { } forbidden) return forbidden;

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
            if (RequireAdmin(admin) is { } forbidden) return forbidden;

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

        // แก้ชื่อที่แสดง (WP4, 2026-10-30) - เจ้าของบัญชีแก้เองได้ Admin แก้ให้คนอื่นได้ (ตามตาราง
        // Admin/Teacher ใน docs/roadmap.md) ต่างจาก change-password ข้างบนที่ล็อกเฉพาะตัวเองเท่านั้น
        // ไม่มี id ไหนที่ Teacher แก้ไม่ได้เลยนอกจาก id ของคนอื่น (RequireAdmin เช็คเฉพาะตอนไม่ใช่ตัวเอง)
        app.MapPut("/api/admin/users/{id:int}/display-name", async (int id, UpdateDisplayNameRequest req, HttpRequest request, AppDbContext db, IActivityLogService activityLog) =>
        {
            var admin = await GetAuthorizedAdmin(request, db);
            if (admin is null)
                return Results.Unauthorized();
            if (admin.HasDefaultPassword)
                return Results.Json(new { error = "Password change required before continuing." }, statusCode: 403);

            if (admin.Id != id && RequireAdmin(admin) is { } forbidden)
                return forbidden;

            var target = await db.AdminUsers.FindAsync(id);
            if (target is null) return Results.NotFound();

            var displayName = req.DisplayName?.Trim();
            if (string.IsNullOrEmpty(displayName))
            {
                return Results.BadRequest(new { error = "ต้องระบุชื่อที่แสดง" });
            }

            var oldName = target.DisplayName;
            target.DisplayName = displayName;
            await db.SaveChangesAsync();

            await activityLog.LogAdminActionAsync(admin, "AdminDisplayNameChanged",
                $"{admin.Username} เปลี่ยนชื่อที่แสดงของ '{target.Username}' จาก '{oldName}' เป็น '{target.DisplayName}'");

            return Results.Ok(new { target.Id, target.Username, target.DisplayName });
        });

        // เปลี่ยน role ของบัญชี (WP4b, 2026-10-30) - Admin เท่านั้น ต่างจาก display-name ข้างบนที่
        // เจ้าของบัญชีแก้เองได้ด้วย ตรงนี้ไม่มีข้อยกเว้น: การแก้ role เป็นการตัดสินใจเชิงสิทธิ์ที่
        // เจ้าของบัญชีเองไม่ควรเปลี่ยนให้ตัวเองได้
        // กันไม่ให้ระบบเหลือ Admin เป็นศูนย์คน 2 ทาง: ห้ามแก้บัญชี built-in เลย (ต้องเป็น Admin เสมอ
        // เหมือนที่ delete/reset-password ข้างบนกันไว้) และห้ามลด Admin คนสุดท้ายของระบบลงเป็น Teacher
        app.MapPut("/api/admin/users/{id:int}/role", async (int id, UpdateRoleRequest req, HttpRequest request, AppDbContext db, IActivityLogService activityLog) =>
        {
            var admin = await GetAuthorizedAdmin(request, db);
            if (admin is null)
                return Results.Unauthorized();
            if (admin.HasDefaultPassword)
                return Results.Json(new { error = "Password change required before continuing." }, statusCode: 403);
            if (RequireAdmin(admin) is { } forbidden) return forbidden;

            if (!Enum.IsDefined(typeof(AdminRole), req.Role))
            {
                return Results.BadRequest(new { error = "Unknown role." });
            }

            var target = await db.AdminUsers.FindAsync(id);
            if (target is null) return Results.NotFound();

            // เหตุผลเดียวกับ reset-password ด้านบน (บั๊กที่เจอจริง 2026-08-25): built-in account
            // ต้องเป็น Admin เสมอกัน lockout ไม่มีทางแก้ role ของมันได้เลยไม่ว่าใครจะ login อยู่
            if (target.IsBuiltIn)
            {
                return Results.BadRequest(new { error = "Cannot change the role of the built-in Administrator account." });
            }

            var newRole = (AdminRole)req.Role;

            if (target.Role == AdminRole.Admin && newRole != AdminRole.Admin)
            {
                // นับทุกบัญชีที่เป็น Admin ในระบบ (รวม built-in) ก่อนยอมให้ลดคนนี้ลง - ถ้าเหลือแค่
                // คนเดียว (คนนี้เอง) แปลว่าลดแล้วจะไม่มีใครจัดการผู้ใช้/ห้อง/กฎถาวร/ตั้งค่าระบบได้อีก
                var adminCount = await db.AdminUsers.CountAsync(u => u.Role == AdminRole.Admin);
                if (adminCount <= 1)
                {
                    return Results.BadRequest(new { error = "Cannot demote the last Admin account - the system must always have at least one Admin." });
                }
            }

            var oldRole = target.Role;
            target.Role = newRole;
            await db.SaveChangesAsync();

            await activityLog.LogAdminActionAsync(admin, "AdminRoleChanged",
                $"{admin.Username} เปลี่ยน role ของ '{target.Username}' จาก {oldRole} เป็น {target.Role}");

            return Results.Ok(new { target.Id, target.Username, Role = target.Role.ToString() });
        });

        return app;
    }
}
