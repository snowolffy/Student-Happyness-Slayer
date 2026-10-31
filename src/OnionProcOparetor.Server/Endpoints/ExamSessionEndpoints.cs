using Microsoft.EntityFrameworkCore;
using OnionProcOparetor.Server.Data;
using OnionProcOparetor.Server.Models;
using OnionProcOparetor.Server.Services;
using static OnionProcOparetor.Server.Endpoints.AuthHelpers;

namespace OnionProcOparetor.Server.Endpoints;

/// <summary>
/// "โหมดสอบ" (WP2-2, 2026-10-31) - allow-list ชั่วคราวที่ต้องมีเวลาหมดเสมอ ดู Models/ExamSession.cs
///
/// เปิด/ยกเลิกให้ Teacher ทำได้เหมือน Admin (ไม่เรียก RequireAdmin) - นี่คือเครื่องมือสอนประจำวัน
/// เหมือน Staff session/ส่งข้อความขึ้นจอ/สั่ง shutdown ไม่ใช่งานตั้งค่าถาวรแบบ CRUD กฎ/ชุด Allow-list
/// (ซึ่งเป็น Admin เท่านั้น - ดู AllowListPresetEndpoints) ตรงกับตาราง Admin/Teacher ใน docs/roadmap.md
/// ที่ระบุว่า Teacher คุมห้อง/ดูจอ/โหมดสอบได้
/// </summary>
public static class ExamSessionEndpoints
{
    public static WebApplication MapExamSessionEndpoints(this WebApplication app)
    {
        app.MapPost("/api/exam-sessions/start", async (StartExamSessionRequest req, HttpRequest request, AppDbContext db, IActivityLogService activityLog) =>
        {
            var admin = await GetAuthorizedAdmin(request, db);
            if (admin is null)
                return Results.Unauthorized();
            if (admin.HasDefaultPassword)
                return Results.Json(new { error = "Password change required before continuing." }, statusCode: 403);

            // ---- ต้องระบุเป้าหมายอย่างใดอย่างหนึ่งเท่านั้น (ไม่ใช่ทั้งคู่ ไม่ใช่ไม่มีเลย) ----
            var clientGuids = (req.ClientGuids ?? new List<string>())
                .Select(g => g?.Trim())
                .Where(g => !string.IsNullOrEmpty(g))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var hasRoom = req.RoomId is not null;
            var hasClients = clientGuids.Count > 0;

            if (hasRoom == hasClients)
                return Results.BadRequest(new { error = "ต้องเลือกเป้าหมายเป็นห้อง หรือเครื่องที่เลือกไว้ อย่างใดอย่างหนึ่งเท่านั้น" });

            if (hasRoom && !await db.Rooms.AnyAsync(r => r.Id == req.RoomId))
                return Results.BadRequest(new { error = "ไม่พบห้องที่ระบุ" });

            // ---- ต้องมีเวลาหมดอายุที่ชัดเจนเสมอ (ดู doc comment บน ExamSession) ----
            if (req.DurationMinutes <= 0)
                return Results.BadRequest(new { error = "ต้องระบุเวลาที่ใช้ (นาที) มากกว่า 0" });

            var presetIds = (req.PresetIds ?? new List<int>()).Distinct().ToList();
            var extraDomains = (req.ExtraDomains ?? new List<string>())
                .Select(d => d?.Trim())
                .Where(d => !string.IsNullOrEmpty(d))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (presetIds.Count > 0)
            {
                var validPresetIds = await db.AllowListPresets.Where(p => presetIds.Contains(p.Id)).Select(p => p.Id).ToListAsync();
                presetIds = presetIds.Where(id => validPresetIds.Contains(id)).ToList();
            }

            if (presetIds.Count == 0 && extraDomains.Count == 0)
                return Results.BadRequest(new { error = "ต้องเลือกชุด Allow-list หรือพิมพ์เว็บเพิ่มเติมอย่างน้อยหนึ่งรายการ" });

            var now = DateTime.UtcNow;
            var session = new ExamSession
            {
                RoomId = req.RoomId,
                ClientGuidsCsv = hasClients ? string.Join(",", clientGuids!) : null,
                PresetIdsCsv = string.Join(",", presetIds),
                ExtraDomainsCsv = extraDomains.Count > 0 ? string.Join(",", extraDomains) : null,
                StartedAt = now,
                ExpiresAt = now.AddMinutes(req.DurationMinutes),
                StartedByUsername = admin.Username,
                CreatedAt = now
            };

            db.ExamSessions.Add(session);
            await db.SaveChangesAsync();

            var targetDescription = await DescribeTargetAsync(db, session);
            await activityLog.LogAdminActionAsync(admin, "ExamSessionStarted",
                $"{admin.Username} เปิดโหมดสอบ ({targetDescription}) ถึง {session.ExpiresAt:HH:mm}");

            return Results.Ok(await BuildSummaryAsync(db, session));
        });

        app.MapPost("/api/exam-sessions/{id:int}/cancel", async (int id, HttpRequest request, AppDbContext db, IActivityLogService activityLog) =>
        {
            var admin = await GetAuthorizedAdmin(request, db);
            if (admin is null)
                return Results.Unauthorized();
            if (admin.HasDefaultPassword)
                return Results.Json(new { error = "Password change required before continuing." }, statusCode: 403);

            var session = await db.ExamSessions.FindAsync(id);
            if (session is null) return Results.NotFound();

            if (!session.IsActiveAt(DateTime.UtcNow))
                return Results.BadRequest(new { error = "โหมดสอบนี้ไม่ได้ทำงานอยู่แล้ว" });

            session.EndedAt = DateTime.UtcNow;
            session.EndedReason = "CancelledEarly";
            await db.SaveChangesAsync();

            var targetDescription = await DescribeTargetAsync(db, session);
            await activityLog.LogAdminActionAsync(admin, "ExamSessionCancelled", $"{admin.Username} ยกเลิกโหมดสอบ ({targetDescription}) ก่อนเวลา");

            return Results.Ok(new { message = "ยกเลิกโหมดสอบแล้ว" });
        });

        // สำหรับแถบประกาศ "โหมดสอบกำลังทำงาน" ของ Console (AnnouncementCenter) และหน้าเปิดโหมดสอบของ
        // OnlineConsole - คืนทุกเซสชันที่ "กำลังทำงานอยู่ ณ ตอนนี้" (ไม่ใช่ EndedAt==null ตรงๆ เพราะต้อง
        // เช็ค ExpiresAt ด้วย - เซสชันที่หมดเวลาไปแล้วไม่เคยถูกเขียน EndedAt ย้อนหลัง ดู ExamSession.IsActiveAt)
        app.MapGet("/api/exam-sessions/active", async (HttpRequest request, AppDbContext db) =>
        {
            var admin = await GetAuthorizedAdmin(request, db);
            if (admin is null)
                return Results.Unauthorized();
            if (admin.HasDefaultPassword)
                return Results.Json(new { error = "Password change required before continuing." }, statusCode: 403);

            var now = DateTime.UtcNow;
            var candidates = await db.ExamSessions.Where(e => e.EndedAt == null && e.ExpiresAt > now).ToListAsync();

            var summaries = new List<object>();
            foreach (var session in candidates)
            {
                summaries.Add(await BuildSummaryAsync(db, session));
            }

            return Results.Ok(summaries);
        });

        return app;
    }

    private static async Task<string> DescribeTargetAsync(AppDbContext db, ExamSession session)
    {
        if (session.RoomId is not null)
        {
            var room = await db.Rooms.FindAsync(session.RoomId.Value);
            var clientCount = await db.ClientMachines.CountAsync(c => c.RoomId == session.RoomId);
            return room is null ? "ห้อง (ลบไปแล้ว)" : $"ห้อง {room.Name} ({clientCount} เครื่อง)";
        }

        var count = (session.ClientGuidsCsv ?? string.Empty)
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Length;
        return $"เครื่องที่เลือกไว้ ({count} เครื่อง)";
    }

    private static async Task<object> BuildSummaryAsync(AppDbContext db, ExamSession session)
    {
        var presetIds = ParseIntCsv(session.PresetIdsCsv);
        var presetNames = presetIds.Count > 0
            ? await db.AllowListPresets.Where(p => presetIds.Contains(p.Id)).Select(p => p.Name).ToListAsync()
            : new List<string>();

        var extraDomains = ParseStringCsv(session.ExtraDomainsCsv);
        var presetDomains = await AllowListPresetEndpoints.GetDomainsForPresetIdsAsync(db, presetIds);
        var allowedDomains = presetDomains.Concat(extraDomains).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        string? roomName = null;
        int? targetClientCount = null;
        if (session.RoomId is not null)
        {
            roomName = (await db.Rooms.FindAsync(session.RoomId.Value))?.Name;
            targetClientCount = await db.ClientMachines.CountAsync(c => c.RoomId == session.RoomId);
        }
        else
        {
            targetClientCount = ParseStringCsv(session.ClientGuidsCsv).Count;
        }

        return new
        {
            session.Id,
            session.RoomId,
            RoomName = roomName,
            ClientGuids = ParseStringCsv(session.ClientGuidsCsv),
            TargetClientCount = targetClientCount,
            PresetIds = presetIds,
            PresetNames = presetNames,
            ExtraDomains = extraDomains,
            AllowedDomains = allowedDomains,
            session.StartedAt,
            session.ExpiresAt,
            session.StartedByUsername
        };
    }

    private static List<int> ParseIntCsv(string? csv) =>
        (csv ?? string.Empty)
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(s => int.TryParse(s, out var i) ? i : (int?)null)
            .Where(i => i is not null)
            .Select(i => i!.Value)
            .ToList();

    private static List<string> ParseStringCsv(string? csv) =>
        (csv ?? string.Empty)
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .ToList();
}
