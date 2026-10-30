using Microsoft.EntityFrameworkCore;
using OnionProcOparetor.Server.Data;
using OnionProcOparetor.Server.Models;
using OnionProcOparetor.Server.Services;
using static OnionProcOparetor.Server.Endpoints.AuthHelpers;

namespace OnionProcOparetor.Server.Endpoints;

/// <summary>Activity Log (audit) + บันทึก uninstall ที่ได้รับอนุญาต</summary>
public static class ActivityLogEndpoints
{
    public static WebApplication MapActivityLogEndpoints(this WebApplication app)
    {
        // ดู Activity Log (audit การกระทำของครู + agent connectivity + system warning/error) พร้อม filter
        // + pagination - ตั้งใจแยก endpoint จาก /api/logs (คนละเรื่องกัน คนละ DbContext) และคืนเป็นหน้าๆ
        // เสมอ ไม่คืนทั้งหมดทีเดียว (volume คาดว่าสูงกว่า LogEntry มาก)
        app.MapGet("/api/activitylog", async (
            HttpRequest request, AppDbContext db, ActivityLogDbContext activityDb,
            ActivityLogCategory? category, ActivityLogSeverity? minSeverity,
            DateTime? from, DateTime? to, string? clientGuid, int? actorAdminUserId,
            int page = 1, int pageSize = 100) =>
        {
            var admin = await GetAuthorizedAdmin(request, db);
            if (admin is null)
                return Results.Unauthorized();
            if (admin.HasDefaultPassword)
                return Results.Json(new { error = "Password change required before continuing." }, statusCode: 403);

            page = Math.Max(page, 1);
            pageSize = Math.Clamp(pageSize, 1, 500);

            var query = activityDb.ActivityLogEntries.AsQueryable();
            if (category is not null) query = query.Where(e => e.Category == category);
            if (minSeverity is not null) query = query.Where(e => e.Severity >= minSeverity);
            if (from is not null) query = query.Where(e => e.Timestamp >= from);
            if (to is not null) query = query.Where(e => e.Timestamp <= to);
            if (!string.IsNullOrWhiteSpace(clientGuid)) query = query.Where(e => e.TargetClientGuid == clientGuid);
            if (actorAdminUserId is not null) query = query.Where(e => e.ActorAdminUserId == actorAdminUserId);

            var totalCount = await query.CountAsync();
            var items = await query
                .OrderByDescending(e => e.Timestamp)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            // Category/Severity ส่งเป็น string ไม่ใช่ enum ตัวเลข (ไม่มี JsonStringEnumConverter ตั้งอยู่
            // ใน pipeline นี้ - pattern เดียวกับ CommandResultDto ที่แปลง enum เป็น string ก่อนส่งเสมอ
            // เพื่อให้ Console อ่านตรงๆ ได้โดยไม่ต้องก๊อป enum ไปไว้อีกฝั่ง)
            var dtoItems = items.Select(e => new
            {
                e.Id,
                e.Timestamp,
                Category = e.Category.ToString(),
                Severity = e.Severity.ToString(),
                e.EventType,
                e.Description,
                e.ActorAdminUserId,
                e.ActorUsername,
                e.ActorDisplayName,
                e.TargetClientGuid,
                e.TargetMachineName,
                e.DetailJson
            });

            return Results.Ok(new { items = dtoItems, totalCount, page, pageSize });
        });

        // UninstallHelper.exe เรียกตรงนี้หลัง admin login สำเร็จ (แทนรหัสผ่าน uninstall แบบ local เดิม) ก่อน
        // จะสั่ง msiexec /x จริง - บันทึกไว้เป็น audit trail ว่าใครอนุมัติถอด Agent ออกจากเครื่องไหนเมื่อไหร่
        // (ดู installer/CustomActions/check-uninstall-password.ps1 สำหรับกลไก verify จริงฝั่ง MSI - endpoint
        // นี้ไม่เกี่ยวกับการอนุญาต uninstall เลย แค่บันทึกผลหลังอนุญาตแล้วเท่านั้น)
        app.MapPost("/api/activitylog/agent-uninstall-authorized", async (
            AgentUninstallAuthorizedRequest req, HttpRequest request, AppDbContext db, IActivityLogService activityLog) =>
        {
            var admin = await GetAuthorizedAdmin(request, db);
            if (admin is null)
                return Results.Unauthorized();

            await activityLog.LogAdminActionAsync(admin, "AgentUninstallAuthorized",
                $"{admin.Username} ยืนยัน uninstall Agent บนเครื่อง '{req.MachineName}'",
                req.ClientGuid, req.MachineName);

            return Results.Ok();
        });

        return app;
    }
}
