using Microsoft.EntityFrameworkCore;
using OnionProcOparetor.Server.Data;
using OnionProcOparetor.Server.Models;
using OnionProcOparetor.Server.Services;
using static OnionProcOparetor.Server.Endpoints.AuthHelpers;

namespace OnionProcOparetor.Server.Endpoints;

/// <summary>CRUD กฎโปรแกรม (Rule)</summary>
public static class RuleEndpoints
{
    public static WebApplication MapRuleEndpoints(this WebApplication app)
    {
        // ดึงกฎทั้งหมด
        app.MapGet("/api/rules", async (HttpRequest request, AppDbContext db) =>
        {
            var admin = await GetAuthorizedAdmin(request, db);
            if (admin is null)
                return Results.Unauthorized();
            if (admin.HasDefaultPassword)
                return Results.Json(new { error = "Password change required before continuing." }, statusCode: 403);

            var rules = await db.Rules.OrderBy(r => r.Name).ToListAsync();
            return Results.Ok(rules);
        });

        // เพิ่มกฎใหม่
        app.MapPost("/api/rules", async (Rule rule, HttpRequest request, AppDbContext db, IActivityLogService activityLog) =>
        {
            var admin = await GetAuthorizedAdmin(request, db);
            if (admin is null)
                return Results.Unauthorized();
            if (admin.HasDefaultPassword)
                return Results.Json(new { error = "Password change required before continuing." }, statusCode: 403);
            // WP4: แก้กฎถาวรเป็นสิทธิ์ Admin เท่านั้น (ตาราง Admin/Teacher ใน docs/roadmap.md)
            if (RequireAdmin(admin) is { } forbidden) return forbidden;

            // WP2-3: RoomId ต้องเป็น null (ทุกห้อง) หรืออ้างถึงห้องที่มีอยู่จริงเท่านั้น
            if (rule.RoomId is not null && !await db.Rooms.AnyAsync(r => r.Id == rule.RoomId))
                return Results.BadRequest(new { error = "ไม่พบห้องที่ระบุ" });

            rule.CreatedAt = DateTime.UtcNow;
            rule.UpdatedAt = DateTime.UtcNow;
            db.Rules.Add(rule);
            await db.SaveChangesAsync();

            await activityLog.LogAdminActionAsync(admin, "RuleCreated", $"{admin.Username} สร้างกฎ '{rule.Name}'");

            return Results.Created($"/api/rules/{rule.Id}", rule);
        });

        // ลบกฎทิ้ง (ใช้เมื่อกฎตั้งผิดหรือกว้างเกินไป - สำคัญมากเพื่อความปลอดภัย)
        app.MapDelete("/api/rules/{id:int}", async (int id, HttpRequest request, AppDbContext db, IActivityLogService activityLog) =>
        {
            var admin = await GetAuthorizedAdmin(request, db);
            if (admin is null)
                return Results.Unauthorized();
            if (admin.HasDefaultPassword)
                return Results.Json(new { error = "Password change required before continuing." }, statusCode: 403);
            if (RequireAdmin(admin) is { } forbidden) return forbidden;

            var rule = await db.Rules.FindAsync(id);
            if (rule is null) return Results.NotFound();

            db.Rules.Remove(rule);
            await db.SaveChangesAsync();

            await activityLog.LogAdminActionAsync(admin, "RuleDeleted", $"{admin.Username} ลบกฎ '{rule.Name}'");

            return Results.Ok(new { message = "Rule deleted." });
        });

        // แก้ไขกฎแบบ in-place (ไม่ต้องลบแล้วสร้างใหม่ - เก็บ Id/CreatedAt/RuleOverride ของกฎเดิมไว้ครบ)
        app.MapPut("/api/rules/{id:int}", async (int id, Rule updated, HttpRequest request, AppDbContext db, IActivityLogService activityLog) =>
        {
            var admin = await GetAuthorizedAdmin(request, db);
            if (admin is null)
                return Results.Unauthorized();
            if (admin.HasDefaultPassword)
                return Results.Json(new { error = "Password change required before continuing." }, statusCode: 403);
            if (RequireAdmin(admin) is { } forbidden) return forbidden;

            var rule = await db.Rules.FindAsync(id);
            if (rule is null) return Results.NotFound();

            // WP2-3: RoomId ต้องเป็น null (ทุกห้อง) หรืออ้างถึงห้องที่มีอยู่จริงเท่านั้น
            if (updated.RoomId is not null && !await db.Rooms.AnyAsync(r => r.Id == updated.RoomId))
                return Results.BadRequest(new { error = "ไม่พบห้องที่ระบุ" });

            rule.Name = updated.Name;
            rule.PublisherName = updated.PublisherName;
            rule.ProcessNameContains = updated.ProcessNameContains;
            rule.RequireSignedMatch = updated.RequireSignedMatch;
            rule.KillProcess = updated.KillProcess;
            rule.ActionCommand = updated.ActionCommand;
            rule.ActionArguments = updated.ActionArguments;
            rule.IsEnabled = updated.IsEnabled;
            rule.RoomId = updated.RoomId;
            rule.UpdatedAt = DateTime.UtcNow;

            await db.SaveChangesAsync();

            await activityLog.LogAdminActionAsync(admin, "RuleUpdated", $"{admin.Username} แก้ไขกฎ '{rule.Name}'");

            return Results.Ok(rule);
        });

        // toggle เปิด/ปิดกฎ (ไม่ต้องลบทิ้งถาวร แค่ปิดชั่วคราวได้)
        app.MapPost("/api/rules/{id:int}/toggle", async (int id, HttpRequest request, AppDbContext db, IActivityLogService activityLog) =>
        {
            var admin = await GetAuthorizedAdmin(request, db);
            if (admin is null)
                return Results.Unauthorized();
            if (admin.HasDefaultPassword)
                return Results.Json(new { error = "Password change required before continuing." }, statusCode: 403);
            if (RequireAdmin(admin) is { } forbidden) return forbidden;

            var rule = await db.Rules.FindAsync(id);
            if (rule is null) return Results.NotFound();

            rule.IsEnabled = !rule.IsEnabled;
            rule.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();

            await activityLog.LogAdminActionAsync(admin, "RuleToggled",
                $"{admin.Username} {(rule.IsEnabled ? "เปิด" : "ปิด")} กฎ '{rule.Name}'");

            return Results.Ok(rule);
        });

        return app;
    }
}
