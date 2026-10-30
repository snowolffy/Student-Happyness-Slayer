using Microsoft.EntityFrameworkCore;
using OnionProcOparetor.Server.Data;
using OnionProcOparetor.Server.Models;
using OnionProcOparetor.Server.Services;
using System.Security.Cryptography;

namespace OnionProcOparetor.Server.Endpoints;

/// <summary>setup ครั้งแรก + login ของ Console</summary>
public static class AuthEndpoints
{
    public static WebApplication MapAuthEndpoints(this WebApplication app)
    {
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
                CreatedAt = DateTime.UtcNow,
                // WP4 (2026-10-30): บัญชี built-in ต้องเป็น Admin เสมอ (กัน lockout) DisplayName เริ่มต้น
                // = Username เหมือนบัญชีอื่นทุกบัญชี
                Role = AdminRole.Admin,
                DisplayName = "Administrator"
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
                adminId = admin.Id,
                // WP4 (2026-10-30): Console/OnlineConsole ใช้ role ซ่อนปุ่มที่ Teacher ใช้ไม่ได้ และใช้
                // displayName แสดงแทน username ตามจุดที่ roadmap ระบุ (แถบล่าง Console ฯลฯ)
                role = admin.Role.ToString(),
                displayName = string.IsNullOrWhiteSpace(admin.DisplayName) ? admin.Username : admin.DisplayName
            });
        });

        return app;
    }
}
