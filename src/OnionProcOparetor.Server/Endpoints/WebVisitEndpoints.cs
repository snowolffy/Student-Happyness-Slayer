using Microsoft.EntityFrameworkCore;
using OnionProcOparetor.Server.Data;
using OnionProcOparetor.Server.Models;
using OnionProcOparetor.Server.Services;
using static OnionProcOparetor.Server.Endpoints.AuthHelpers;

namespace OnionProcOparetor.Server.Endpoints;

// ---- Request DTOs ของ WebVisitEndpoints (WP2-4, 2026-10-31) ----
record WebVisitUploadItem(DateTime VisitedAt, string Domain, string? PageTitle, bool WasBlocked);
record UploadWebVisitsRequest(List<WebVisitUploadItem>? Visits);

/// <summary>
/// ประวัติเว็บ (WP2-4, 2026-10-31) - Agent ส่งขึ้นมาเป็นชุดทุก 1 นาที (POST ไม่ auth แบบ admin เหมือน
/// /api/logs - ใช้ agent secret) + Console อ่าน/กรอง/สรุปเว็บที่เข้าบ่อย (GET auth แบบ admin ปกติ) ดู
/// Models/WebVisit.cs
/// </summary>
public static class WebVisitEndpoints
{
    // กันเครื่องเดียวส่งมาถี่/มากผิดปกติ (agent มีบั๊กจน loop ซ้ำ) ถมดิสก์ server - เดินตาม pattern
    // เดียวกับ MaxBulkRulesPerRequest ใน WebsiteRuleEndpoints (จำกัดต่อ request ไม่ใช่ต่อวัน)
    private const int MaxVisitsPerRequest = 500;

    public static WebApplication MapWebVisitEndpoints(this WebApplication app)
    {
        // Agent push ประวัติเว็บที่สะสมไว้ - ตรวจ agent secret เหมือน POST /api/logs (ดู LogEndpoints.cs)
        // บังคับ ClientGuid/MachineName มาจากเครื่องที่พิสูจน์ตัวตนได้จริงเสมอ ไม่ใช่ค่าที่ agent ส่งมาใน body
        app.MapPost("/api/web-visits", async (
            UploadWebVisitsRequest req,
            HttpRequest httpRequest,
            AppDbContext db,
            AgentAuthenticator agentAuth,
            IActivityLogService activityLog) =>
        {
            var (allowed, authenticatedClient) = await agentAuth.AuthorizeByHeadersAsync(httpRequest, db, activityLog, "POST /api/web-visits");
            if (!allowed)
            {
                return Results.Unauthorized();
            }

            if (req.Visits is null || req.Visits.Count == 0)
            {
                return Results.Ok(new { added = 0 });
            }

            // agent เวอร์ชันเก่าที่ไม่ส่ง header มาเลยตอนยังไม่บังคับใช้ (ดู AuthorizeByHeadersAsync) - ไม่มี
            // เครื่องให้ผูกประวัติด้วย ทิ้งไปเงียบๆ (ไม่ throw - ประวัติเว็บเป็นแค่ของดูประกอบ ไม่คุ้มที่จะ
            // ปฏิเสธ request ทั้งก้อนเพราะเหตุนี้)
            if (authenticatedClient is null)
            {
                return Results.Ok(new { added = 0 });
            }

            // หา session นักเรียนที่เปิดอยู่ ณ ตอนนี้ของเครื่องนี้ครั้งเดียว แล้ว snapshot ใส่ทุกแถวของ batch
            // นี้เหมือนกัน (ดู doc comment บน Models/WebVisit.cs) - แม่นพอสำหรับ batch ที่ส่งถี่ทุก 1 นาที
            var session = await db.StudentSessions
                .Where(s => s.ClientGuid == authenticatedClient.ClientGuid && s.EndedAt == null)
                .OrderByDescending(s => s.StartedAt)
                .FirstOrDefaultAsync();
            var student = session is null ? null : await db.Students.FindAsync(session.StudentId);

            var now = DateTime.UtcNow;
            var rows = req.Visits
                .Take(MaxVisitsPerRequest)
                .Where(v => !string.IsNullOrWhiteSpace(v.Domain))
                .Select(v => new WebVisit
                {
                    ClientGuid = authenticatedClient.ClientGuid,
                    MachineName = authenticatedClient.MachineName,
                    VisitedAt = v.VisitedAt,
                    Domain = v.Domain.Trim(),
                    PageTitle = v.PageTitle,
                    WasBlocked = v.WasBlocked,
                    StudentId = student?.Id,
                    StudentFullName = student?.FullName,
                    StudentClassName = student?.ClassName,
                    CreatedAt = now
                })
                .ToList();

            if (rows.Count > 0)
            {
                db.WebVisits.AddRange(rows);
                await db.SaveChangesAsync();
            }

            return Results.Ok(new { added = rows.Count });
        });

        // "ประวัติเว็บ" (con-website-history.dc.html) - Admin+Teacher ดูได้ทั้งคู่ (เป็นรายงาน ไม่ใช่
        // ตั้งค่าระบบ - เหตุผลเดียวกับ GET /api/students/sessions/history)
        app.MapGet("/api/web-visits", async (
            HttpRequest request,
            AppDbContext db,
            DateTime? from,
            DateTime? to,
            string? className,
            string? search,
            string? domain,
            bool blockedOnly = false,
            int page = 1,
            int pageSize = 200) =>
        {
            var admin = await GetAuthorizedAdmin(request, db);
            if (admin is null) return Results.Unauthorized();
            if (admin.HasDefaultPassword) return Results.Json(new { error = "Password change required before continuing." }, statusCode: 403);

            page = Math.Max(page, 1);
            pageSize = Math.Clamp(pageSize, 1, 1000);

            var query = db.WebVisits.AsQueryable();
            if (from is not null) query = query.Where(v => v.VisitedAt >= from);
            if (to is not null) query = query.Where(v => v.VisitedAt <= to);
            if (!string.IsNullOrWhiteSpace(className)) query = query.Where(v => v.StudentClassName == className);
            if (!string.IsNullOrWhiteSpace(domain)) query = query.Where(v => v.Domain.Contains(domain));
            if (blockedOnly) query = query.Where(v => v.WasBlocked);
            if (!string.IsNullOrWhiteSpace(search))
            {
                query = query.Where(v =>
                    v.MachineName.Contains(search) ||
                    (v.StudentFullName != null && v.StudentFullName.Contains(search)));
            }

            var totalCount = await query.CountAsync();
            var items = await query
                .OrderByDescending(v => v.VisitedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return Results.Ok(new { items, totalCount, page, pageSize });
        });

        // "เว็บที่เข้าบ่อยช่วงนี้" (aside ของ wireframe con-website-history.dc.html) - นับจำนวนครั้งต่อ
        // โดเมนในช่วงเวลาเดียวกับตัวกรองหลัก แต่ไม่กรองตาม className/search/domain/blockedOnly (สรุป
        // ทั้งห้องเสมอ - ใช้หาเว็บเกมใหม่ที่ยังไม่อยู่ใน blocklist ตามที่ wireframe ระบุไว้)
        app.MapGet("/api/web-visits/top-domains", async (
            HttpRequest request, AppDbContext db, DateTime? from, DateTime? to, int take = 6) =>
        {
            var admin = await GetAuthorizedAdmin(request, db);
            if (admin is null) return Results.Unauthorized();
            if (admin.HasDefaultPassword) return Results.Json(new { error = "Password change required before continuing." }, statusCode: 403);

            var query = db.WebVisits.AsQueryable();
            if (from is not null) query = query.Where(v => v.VisitedAt >= from);
            if (to is not null) query = query.Where(v => v.VisitedAt <= to);

            var top = await query
                .GroupBy(v => v.Domain)
                .Select(g => new { Domain = g.Key, Count = g.Count(), AnyBlocked = g.Any(x => x.WasBlocked) })
                .OrderByDescending(g => g.Count)
                .Take(Math.Clamp(take, 1, 50))
                .ToListAsync();

            return Results.Ok(top);
        });

        return app;
    }
}
