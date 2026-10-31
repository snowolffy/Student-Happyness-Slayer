using Microsoft.EntityFrameworkCore;
using OnionProcOparetor.Server.Data;
using OnionProcOparetor.Server.Models;
using OnionProcOparetor.Server.Services;
using static OnionProcOparetor.Server.Endpoints.AuthHelpers;

namespace OnionProcOparetor.Server.Endpoints;

// ---- Request DTOs ของ StudentSessionEndpoints (WP3-1, 2026-10-31) ----
record ForceLogoutStudentRequest(int Reason = 3); // default = StudentSessionEndReason.TeacherForced (3)

/// <summary>
/// อ่าน session ที่เปิดอยู่ (ให้ AgentTray ถามตอนเริ่มต้นว่าใครล็อกอินอยู่บนเครื่องนี้ - disk freeze ทำให้
/// state ฝั่ง client หายทุกรีบูต ต้องถาม Server ใหม่เสมอ) + "ประวัติการใช้เครื่อง" ให้ Console/OnlineConsole
/// (WP3-5 ทำ UI) + ปุ่ม "บังคับ logout" จาก Console inspector ตรงตาม wireframe docs/wireframes/nf-attendance.dc.html
/// </summary>
public static class StudentSessionEndpoints
{
    public static WebApplication MapStudentSessionEndpoints(this WebApplication app)
    {
        // AgentTray เรียกตอนเริ่มทำงาน (boot/restart) เพื่อรู้ว่ามี session เปิดอยู่บนเครื่องนี้ไหม - ไม่ต้อง
        // auth เหมือน StudentAuthEndpoints (เดินตาม trust model เดียวกัน - ดู comment หัวไฟล์นั้น)
        app.MapGet("/api/students/sessions/active/{clientGuid}", async (string clientGuid, AppDbContext db) =>
        {
            var session = await db.StudentSessions
                .Where(s => s.ClientGuid == clientGuid && s.EndedAt == null)
                .OrderByDescending(s => s.StartedAt)
                .FirstOrDefaultAsync();

            if (session is null)
            {
                return Results.Ok(new { active = false });
            }

            var student = await db.Students.FindAsync(session.StudentId);
            return Results.Ok(new
            {
                active = true,
                sessionId = session.Id,
                startedAt = session.StartedAt,
                student = student is null ? null : new { student.Id, student.StudentCode, student.FullName, student.ClassName }
            });
        });

        // "ประวัติการใช้เครื่อง" (nf-attendance) - ใช้ดูประกอบเท่านั้น ไม่ใช่ใบเช็คชื่อทางการ (ดู
        // docs/roadmap.md "ไม่มีตาราง/คาบเรียน") Admin+Teacher ดูได้ทั้งคู่ (เป็นรายงาน ไม่ใช่ตั้งค่าระบบ)
        app.MapGet("/api/students/sessions/history", async (
            HttpRequest request,
            AppDbContext db,
            DateTime? from,
            DateTime? to,
            int? roomId,
            string? className,
            string? search,
            int take = 200) =>
        {
            var admin = await GetAuthorizedAdmin(request, db);
            if (admin is null) return Results.Unauthorized();
            if (admin.HasDefaultPassword) return Results.Json(new { error = "Password change required before continuing." }, statusCode: 403);

            var query = db.StudentSessions.AsQueryable();
            if (from is not null) query = query.Where(s => s.StartedAt >= from);
            if (to is not null) query = query.Where(s => s.StartedAt <= to);
            if (roomId is not null) query = query.Where(s => s.RoomId == roomId);

            var sessions = await query.OrderByDescending(s => s.StartedAt).Take(Math.Clamp(take, 1, 2000)).ToListAsync();

            var studentIds = sessions.Select(s => s.StudentId).Distinct().ToList();
            var students = await db.Students.Where(s => studentIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id);

            var rows = sessions
                .Select(s => new
                {
                    s.Id,
                    s.ClientGuid,
                    s.MachineName,
                    s.RoomId,
                    s.StartedAt,
                    s.EndedAt,
                    EndReason = s.EndReason?.ToString(),
                    Student = students.GetValueOrDefault(s.StudentId)
                })
                // ตัวกรอง "ห้องเรียนของนักเรียน" + ค้นหา ทำหลัง join กับ Student ใน memory เพราะ ClassName
                // อยู่ที่ Student ไม่ใช่ StudentSession (ดู Models/StudentSession.cs - ไม่ snapshot ClassName)
                .Where(r => className is null || string.Equals(r.Student?.ClassName, className, StringComparison.OrdinalIgnoreCase))
                .Where(r => string.IsNullOrWhiteSpace(search)
                    || (r.Student?.FullName.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false)
                    || (r.Student?.StudentCode.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false))
                .ToList();

            return Results.Ok(rows);
        });

        // ครู/Admin สั่ง logout นักเรียนจาก Console (inspector ของเครื่อง) - ดู docs/roadmap.md
        // "API สำหรับ Console: ... สั่ง logout นักเรียน" ไม่ได้ระบุว่าจำกัดแค่ Admin จึงเปิดให้ Teacher ด้วย
        // (คุมห้อง/ดูจอเป็นงานประจำวันของ Teacher ตามตาราง Admin/Teacher)
        app.MapPost("/api/students/sessions/{id:int}/force-logout", async (int id, ForceLogoutStudentRequest req, HttpRequest request, AppDbContext db, IActivityLogService activityLog) =>
        {
            var admin = await GetAuthorizedAdmin(request, db);
            if (admin is null) return Results.Unauthorized();
            if (admin.HasDefaultPassword) return Results.Json(new { error = "Password change required before continuing." }, statusCode: 403);

            var session = await db.StudentSessions.FindAsync(id);
            if (session is null || session.EndedAt is not null)
            {
                return Results.NotFound(new { error = "ไม่พบ session ที่เปิดอยู่" });
            }

            var reason = Enum.IsDefined(typeof(StudentSessionEndReason), req.Reason)
                ? (StudentSessionEndReason)req.Reason
                : StudentSessionEndReason.TeacherForced;

            session.EndedAt = DateTime.UtcNow;
            session.EndReason = reason;
            await db.SaveChangesAsync();

            var student = await db.Students.FindAsync(session.StudentId);
            await activityLog.LogAdminActionAsync(admin, "StudentForcedLogout",
                $"{admin.Username} สั่ง logout '{student?.StudentCode}' ({student?.FullName}) ที่เครื่อง '{session.MachineName}'",
                session.ClientGuid, session.MachineName);

            return Results.Ok(new { message = "Forced logout.", endedAt = session.EndedAt });
        });

        return app;
    }
}
