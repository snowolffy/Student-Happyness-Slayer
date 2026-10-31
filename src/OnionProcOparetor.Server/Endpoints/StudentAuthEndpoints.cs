using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using OnionProcOparetor.Server.Data;
using OnionProcOparetor.Server.Models;
using OnionProcOparetor.Server.Services;

namespace OnionProcOparetor.Server.Endpoints;

// ---- Request DTOs ของ StudentAuthEndpoints (WP3-1, 2026-10-31) ----
record StudentLoginRequest(string ClientGuid, string? MachineName, string Identifier, string Password, bool Force = false);
record ChangeStudentPasswordRequest(string PendingToken, string NewPassword, string ConfirmPassword);
record StudentLogoutRequest(string ClientGuid, int? SessionId, int Reason = 0);

/// <summary>
/// Login/logout/เปลี่ยนรหัสผ่านครั้งแรกของนักเรียน - เรียกจาก AgentTray ที่จอล็อกอิน (WP3-3 เป็นคนทำ UI
/// จริง) ตรงตาม wireframe docs/wireframes/st-login.dc.html, st-login-states.dc.html
///
/// ไม่ต้องมี agent secret header เหมือน endpoint ฝั่ง Agent อื่นๆ (AgentAuthenticator) โดยตั้งใจ -
/// เดินตาม pattern เดียวกับ Endpoints/StaffSessionEndpoints.cs (WP2): AgentTray ยิงตรงมาที่ Server ด้วย
/// ClientGuid ใน body เท่านั้น ไม่ผ่าน Agent loopback API เลย ความปลอดภัยของ ClientGuid เองเหมือนกับ
/// ทุก endpoint ฝั่ง client อื่นที่ไม่ผ่าน AgentAuthenticator (เช่น /api/staff-session/*) - เครื่องนักเรียน
/// ไม่ใช่ผู้ใช้ที่ไม่น่าเชื่อถือในโมเดลภัยคุกคามของระบบนี้ (ดู docs/roadmap.md "Agent secret พักไว้")
///
/// ไม่มี IP-based throttle แบบ LoginThrottle ของ Admin - การป้องกันการเดารหัสผ่านของนักเรียนใช้
/// Student.IsLocked ต่อบัญชี (ครบ SignInSettings.LockoutFailedAttempts ครั้งก็ล็อกทันที ปลดด้วยมือเท่านั้น)
/// ตาม docs/roadmap.md ระบุไว้ตรงๆ ว่า "ล็อกบัญชีเมื่อกรอกผิด 5 ครั้ง (ครูปลดได้)" ไม่ได้พูดถึง throttle แยก
/// ต่างหากแบบของ admin - ถ้าต้องการเพิ่มทีหลังทำแบบ additive ได้
/// </summary>
public static class StudentAuthEndpoints
{
    public static WebApplication MapStudentAuthEndpoints(this WebApplication app)
    {
        app.MapPost("/api/students/login", async (StudentLoginRequest req, AppDbContext db, IActivityLogService activityLog) =>
        {
            if (string.IsNullOrWhiteSpace(req.ClientGuid) || string.IsNullOrWhiteSpace(req.Identifier) || req.Password is null)
            {
                return Results.BadRequest(new { error = "ClientGuid/Identifier/Password ต้องไม่ว่าง" });
            }

            var settings = await SignInSettingsEndpoints.GetOrCreateSignInSettingsAsync(db);
            var identifier = req.Identifier.Trim();

            // เมลโรงเรียนหรือรหัสนักเรียน - แยกด้วย "@" (ดู docs/roadmap.md "ล็อกอินด้วยเมลโรงเรียนหรือ
            // รหัสนักเรียน") เมลเทียบแบบไม่สนตัวพิมพ์เล็ก/ใหญ่ รหัสนักเรียนเทียบตรงๆ (ปกติเป็นตัวเลข)
            var student = identifier.Contains('@')
                ? await db.Students.FirstOrDefaultAsync(s => s.SchoolEmail != null && s.SchoolEmail.ToLower() == identifier.ToLower())
                : await db.Students.FirstOrDefaultAsync(s => s.StudentCode == identifier);

            // ไม่บอกว่าผิดช่องไหน (ดู wireframe st-login-states #1) - ไม่พบบัญชี/บัญชีปิดใช้งาน ตอบ
            // เหมือนกันหมดเป็น InvalidCredentials กันการเดาว่ารหัสนักเรียนนี้มีอยู่จริงไหม
            if (student is null || student.IsDisabled)
            {
                await activityLog.LogStudentActionAsync(null, "StudentLoginFailed",
                    $"พยายาม login ด้วย '{identifier}' ที่เครื่อง {req.ClientGuid} แต่ไม่พบบัญชี หรือบัญชีถูกปิดใช้งาน",
                    req.ClientGuid, req.MachineName, studentCodeOverride: identifier);
                return Results.Ok(new { status = "InvalidCredentials" });
            }

            // บัญชีถูกล็อกอยู่แล้ว - ไม่ต้องเสียเวลาคำนวณ PBKDF2 เลย (เหตุผลเดียวกับ LoginThrottle ของ admin)
            if (student.IsLocked)
            {
                return Results.Ok(new { status = "Locked" });
            }

            var passwordValid = PasswordHasher.VerifyPassword(req.Password, student.PasswordSalt, student.PasswordHash);
            if (!passwordValid)
            {
                student.FailedLoginCount++;
                var justLocked = student.FailedLoginCount >= settings.LockoutFailedAttempts;
                if (justLocked)
                {
                    student.IsLocked = true;
                }
                await db.SaveChangesAsync();

                await activityLog.LogStudentActionAsync(student,
                    justLocked ? "StudentAccountLocked" : "StudentLoginFailed",
                    justLocked
                        ? $"บัญชี '{student.StudentCode}' ({student.FullName}) ถูกล็อกเพราะกรอกรหัสผ่านผิดครบ {student.FailedLoginCount} ครั้ง ที่เครื่อง {req.ClientGuid}"
                        : $"บัญชี '{student.StudentCode}' กรอกรหัสผ่านผิด (ครั้งที่ {student.FailedLoginCount}) ที่เครื่อง {req.ClientGuid}",
                    req.ClientGuid, req.MachineName);

                // แจ้งทันทีว่าถูกล็อกแล้ว ดีกว่าตอบ "รหัสผิด เหลือ 0 ครั้ง" ที่กำกวม (ดู wireframe #1 vs #2)
                return Results.Ok(new { status = justLocked ? "Locked" : "InvalidCredentials" });
            }

            // ---- กติกา "บัญชีเดียวหลายเครื่อง" ----
            var otherOpenSessions = await db.StudentSessions
                .Where(s => s.StudentId == student.Id && s.EndedAt == null && s.ClientGuid != req.ClientGuid)
                .ToListAsync();

            if (otherOpenSessions.Count > 0 && settings.MultiDeviceMode != MultiDeviceLoginMode.Allow)
            {
                var canForce = settings.MultiDeviceMode == MultiDeviceLoginMode.KickOldSession;

                if (!req.Force || !canForce)
                {
                    return Results.Ok(new
                    {
                        status = "LoggedInElsewhere",
                        otherMachineName = otherOpenSessions[0].MachineName,
                        canForce
                    });
                }

                // Force=true และโหมดอนุญาตให้เตะได้ - จบ session เดิมทุกเครื่องของบัญชีนี้ทันที
                var now = DateTime.UtcNow;
                foreach (var other in otherOpenSessions)
                {
                    other.EndedAt = now;
                    other.EndReason = StudentSessionEndReason.KickedByOtherLogin;
                }
                await db.SaveChangesAsync();

                foreach (var other in otherOpenSessions)
                {
                    await activityLog.LogStudentActionAsync(student, "StudentKickedByOtherLogin",
                        $"บัญชี '{student.StudentCode}' ({student.FullName}) ถูกเตะออกจากเครื่อง '{other.MachineName}' เพราะ login ใหม่ที่เครื่อง {req.ClientGuid}",
                        other.ClientGuid, other.MachineName);
                }
            }

            // Re-login ที่เครื่องเดิมโดยไม่ได้ logout ก่อน (เช่น AgentTray restart กลางคัน) - ปิด session
            // เก่าของเครื่องนี้เองทิ้งก่อนเริ่มใหม่ ไม่ให้มีสองแถว "เปิดอยู่" ซ้อนกันบน ClientGuid เดียวกัน
            var existingSameClient = await db.StudentSessions
                .FirstOrDefaultAsync(s => s.ClientGuid == req.ClientGuid && s.EndedAt == null);
            if (existingSameClient is not null)
            {
                existingSameClient.EndedAt = DateTime.UtcNow;
                existingSameClient.EndReason = StudentSessionEndReason.ManualLogout;
            }

            if (student.MustChangePassword)
            {
                await db.SaveChangesAsync(); // บันทึกการปิด session เก่า (ถ้ามี) ก่อน - ยังไม่สร้าง session ใหม่

                var pendingToken = PendingPasswordChangeStore.Issue(student.Id, req.ClientGuid, req.MachineName ?? string.Empty);
                return Results.Ok(new
                {
                    status = "MustChangePassword",
                    pendingToken,
                    fullName = student.FullName,
                    passwordMinLength = settings.PasswordMinLength
                });
            }

            student.FailedLoginCount = 0;
            student.LastLoginAt = DateTime.UtcNow;

            var session = new StudentSession
            {
                StudentId = student.Id,
                ClientGuid = req.ClientGuid,
                MachineName = req.MachineName ?? string.Empty,
                RoomId = (await db.ClientMachines.FirstOrDefaultAsync(c => c.ClientGuid == req.ClientGuid))?.RoomId,
                StartedAt = DateTime.UtcNow
            };
            db.StudentSessions.Add(session);
            await db.SaveChangesAsync();

            await activityLog.LogStudentActionAsync(student, "StudentLoggedIn",
                $"'{student.StudentCode}' ({student.FullName}) เข้าสู่ระบบที่เครื่อง {req.MachineName ?? req.ClientGuid}",
                req.ClientGuid, req.MachineName);

            return Results.Ok(new
            {
                status = "Success",
                sessionId = session.Id,
                student = new { student.Id, student.StudentCode, student.FullName, student.ClassName }
            });
        });

        // ตั้งรหัสผ่านใหม่ตอนบังคับเปลี่ยน (ครั้งแรก/หลังรีเซ็ต) - ใช้ pendingToken จากขั้น login ด้านบน
        // แทนการขอรหัสเดิมซ้ำ (ตรวจผ่านไปแล้วตอน login ก่อนจะได้ pendingToken มา - ดู wireframe #4)
        app.MapPost("/api/students/change-password", async (ChangeStudentPasswordRequest req, AppDbContext db, IActivityLogService activityLog) =>
        {
            var pending = PendingPasswordChangeStore.Validate(req.PendingToken);
            if (pending is null)
            {
                return Results.BadRequest(new { error = "ลิงก์เปลี่ยนรหัสผ่านหมดอายุหรือไม่ถูกต้อง กรุณาล็อกอินใหม่" });
            }

            if (req.NewPassword != req.ConfirmPassword)
            {
                return Results.BadRequest(new { error = "รหัสผ่านใหม่ทั้งสองช่องไม่ตรงกัน" });
            }

            var settings = await SignInSettingsEndpoints.GetOrCreateSignInSettingsAsync(db);
            if (req.NewPassword.Length < settings.PasswordMinLength)
            {
                return Results.BadRequest(new { error = $"รหัสผ่านต้องมีอย่างน้อย {settings.PasswordMinLength} ตัว" });
            }

            var student = await db.Students.FindAsync(pending.Value.StudentId);
            if (student is null || student.IsDisabled)
            {
                return Results.BadRequest(new { error = "ไม่พบบัญชีนี้แล้ว" });
            }

            var salt = PasswordHasher.GenerateSalt();
            student.PasswordSalt = salt;
            student.PasswordHash = PasswordHasher.HashPassword(req.NewPassword, salt);
            student.MustChangePassword = false;
            student.FailedLoginCount = 0;
            student.LastLoginAt = DateTime.UtcNow;
            student.UpdatedAt = DateTime.UtcNow;

            var clientGuid = pending.Value.ClientGuid;
            var client = await db.ClientMachines.FirstOrDefaultAsync(c => c.ClientGuid == clientGuid);
            var session = new StudentSession
            {
                StudentId = student.Id,
                ClientGuid = clientGuid,
                // MachineName มาจาก request login เดิมเป็นหลัก (เก็บไว้ใน pending token ตั้งแต่ตอนขอ
                // เปลี่ยนรหัสผ่าน) - เครื่องอาจยังไม่เคย poll เข้ามาเลยก็ได้ (ClientMachine record ไม่มี) ดู
                // bug จริงที่เจอตอนทดสอบ: เดิมใช้ client?.MachineName อย่างเดียว กลายเป็นค่าว่างเสมอสำหรับ
                // เครื่องที่ยังไม่เคย poll เพราะ db.ClientMachines หาไม่เจอ
                MachineName = client?.MachineName ?? pending.Value.MachineName,
                RoomId = client?.RoomId,
                StartedAt = DateTime.UtcNow
            };
            db.StudentSessions.Add(session);
            await db.SaveChangesAsync();

            PendingPasswordChangeStore.Remove(req.PendingToken);

            await activityLog.LogStudentActionAsync(student, "StudentPasswordChanged",
                $"'{student.StudentCode}' ({student.FullName}) ตั้งรหัสผ่านใหม่สำเร็จ (บังคับเปลี่ยนครั้งแรก/หลังรีเซ็ต) ที่เครื่อง {clientGuid}",
                clientGuid, client?.MachineName);

            return Results.Ok(new
            {
                status = "Success",
                sessionId = session.Id,
                student = new { student.Id, student.StudentCode, student.FullName, student.ClassName }
            });
        });

        app.MapPost("/api/students/logout", async (StudentLogoutRequest req, AppDbContext db, IActivityLogService activityLog) =>
        {
            if (string.IsNullOrWhiteSpace(req.ClientGuid))
            {
                return Results.BadRequest(new { error = "ClientGuid ว่างเปล่า" });
            }

            var session = req.SessionId is { } sessionId
                ? await db.StudentSessions.FirstOrDefaultAsync(s => s.Id == sessionId && s.EndedAt == null)
                : await db.StudentSessions
                    .Where(s => s.ClientGuid == req.ClientGuid && s.EndedAt == null)
                    .OrderByDescending(s => s.StartedAt)
                    .FirstOrDefaultAsync();

            if (session is null)
            {
                // idempotent - AgentTray อาจเรียก logout ซ้ำ/เผื่อไว้โดยไม่รู้ว่ามี session เปิดอยู่จริงไหม
                return Results.Ok(new { message = "ไม่มี session ที่เปิดอยู่ให้ logout" });
            }

            var reason = Enum.IsDefined(typeof(StudentSessionEndReason), req.Reason)
                ? (StudentSessionEndReason)req.Reason
                : StudentSessionEndReason.ManualLogout;

            session.EndedAt = DateTime.UtcNow;
            session.EndReason = reason;
            await db.SaveChangesAsync();

            var student = await db.Students.FindAsync(session.StudentId);
            await activityLog.LogStudentActionAsync(student, "StudentLoggedOut",
                $"'{student?.StudentCode}' ออกจากระบบที่เครื่อง '{session.MachineName}' ({reason})",
                session.ClientGuid, session.MachineName);

            return Results.Ok(new { message = "Logged out.", endedAt = session.EndedAt, reason = reason.ToString() });
        });

        return app;
    }
}

/// <summary>
/// token ชั่วคราวระหว่าง "login ด้วยรหัสเดิมผ่านแล้ว" กับ "ตั้งรหัสใหม่สำเร็จ" (ขั้นบังคับเปลี่ยนรหัสผ่าน
/// ครั้งแรก/หลังรีเซ็ต) - เก็บใน memory เท่านั้น ไม่ลง DB (pattern เดียวกับ StaffSessionTokenStore ของ WP2)
/// เพราะอายุสั้นมาก (นาทีเดียว) Server restart กลางทางแปลว่าต้อง login ใหม่ ซึ่งไม่ใช่ความเสียหายจริง
/// </summary>
internal static class PendingPasswordChangeStore
{
    private static readonly TimeSpan TokenLifetime = TimeSpan.FromMinutes(5);

    private sealed record Entry(int StudentId, string ClientGuid, string MachineName, DateTime ExpiresAtUtc);

    private static readonly ConcurrentDictionary<string, Entry> Tokens = new();

    public static string Issue(int studentId, string clientGuid, string machineName)
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        Tokens[token] = new Entry(studentId, clientGuid, machineName, DateTime.UtcNow.Add(TokenLifetime));
        return token;
    }

    public static (int StudentId, string ClientGuid, string MachineName)? Validate(string? token)
    {
        if (string.IsNullOrWhiteSpace(token) || !Tokens.TryGetValue(token, out var entry))
        {
            return null;
        }

        if (entry.ExpiresAtUtc <= DateTime.UtcNow)
        {
            Tokens.TryRemove(token, out _);
            return null;
        }

        return (entry.StudentId, entry.ClientGuid, entry.MachineName);
    }

    public static void Remove(string token) => Tokens.TryRemove(token, out _);
}
