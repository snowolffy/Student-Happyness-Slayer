using Microsoft.EntityFrameworkCore;
using OnionProcOparetor.Server.Data;
using OnionProcOparetor.Server.Models;
using OnionProcOparetor.Server.Services;
using static OnionProcOparetor.Server.Endpoints.AuthHelpers;

namespace OnionProcOparetor.Server.Endpoints;

// ---- Request DTOs ของ LaunchableProgram (WP4-1, 2026-11-01) ----
record CreateLaunchableProgramRequest(string Name, string Path, string? Arguments);
record UpdateLaunchableProgramRequest(string Name, string Path, string? Arguments);

/// <summary>
/// รายการโปรแกรมที่ Admin อนุญาตให้เปิดทั้งห้องได้ (WP4-1, 2026-11-01) - ดู Models/LaunchableProgram.cs
///
/// CRUD เป็นสิทธิ์ Admin เท่านั้น (ตารางสิทธิ์ Admin/Teacher ใน docs/roadmap.md - "แก้กฎถาวร...ทำไม่ได้")
/// ส่วน GET รายการเฉยๆ ให้ Teacher อ่านได้ด้วย เพราะ Teacher ต้องเลือกโปรแกรมจาก dropdown ตอนสั่ง
/// "เปิดโปรแกรมทั้งห้อง" ได้ (แค่เลือกจากรายการที่ Admin ตั้งไว้ ไม่ใช่แก้รายการ)
///
/// endpoint /for-agent แยกออกมาต่างหากเพราะ Agent (รันบนเครื่องนักเรียน) ไม่มี admin token ให้แนบ -
/// ใช้ agent secret (AgentAuthenticator) แบบเดียวกับ /api/commands/{id}/result และ /api/web-visits
/// เป็นจุดที่ Agent re-validate ว่าโปรแกรมนี้ยังอยู่ในรายการที่ Admin อนุญาตจริงไหมก่อนจะรันคำสั่ง
/// LaunchProgram ที่มาจาก command queue (ดู Agent/Services/CommandProcessor.cs)
/// </summary>
public static class LaunchableProgramEndpoints
{
    public static WebApplication MapLaunchableProgramEndpoints(this WebApplication app)
    {
        app.MapGet("/api/launchable-programs", async (HttpRequest request, AppDbContext db) =>
        {
            var admin = await GetAuthorizedAdmin(request, db);
            if (admin is null)
                return Results.Unauthorized();
            if (admin.HasDefaultPassword)
                return Results.Json(new { error = "Password change required before continuing." }, statusCode: 403);

            var programs = await db.LaunchablePrograms.OrderBy(p => p.Name).ToListAsync();
            return Results.Ok(programs);
        });

        app.MapPost("/api/launchable-programs", async (CreateLaunchableProgramRequest req, HttpRequest request, AppDbContext db, IActivityLogService activityLog) =>
        {
            var admin = await GetAuthorizedAdmin(request, db);
            if (admin is null)
                return Results.Unauthorized();
            if (admin.HasDefaultPassword)
                return Results.Json(new { error = "Password change required before continuing." }, statusCode: 403);
            if (RequireAdmin(admin) is { } forbidden) return forbidden;

            var name = req.Name?.Trim();
            var path = req.Path?.Trim();
            if (string.IsNullOrEmpty(name))
                return Results.BadRequest(new { error = "ต้องระบุชื่อโปรแกรม" });
            if (string.IsNullOrEmpty(path))
                return Results.BadRequest(new { error = "ต้องระบุ path ของโปรแกรม" });

            if (await db.LaunchablePrograms.AnyAsync(p => p.Name == name))
                return Results.Json(new { error = $"มีโปรแกรมชื่อ '{name}' อยู่แล้ว" }, statusCode: 409);

            var program = new LaunchableProgram
            {
                Name = name,
                Path = path,
                Arguments = string.IsNullOrWhiteSpace(req.Arguments) ? null : req.Arguments.Trim(),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            db.LaunchablePrograms.Add(program);
            await db.SaveChangesAsync();

            await activityLog.LogAdminActionAsync(admin, "LaunchableProgramCreated",
                $"{admin.Username} เพิ่มโปรแกรมที่เปิดได้ทั้งห้อง '{program.Name}' ({program.Path})");

            return Results.Created($"/api/launchable-programs/{program.Id}", program);
        });

        app.MapPut("/api/launchable-programs/{id:int}", async (int id, UpdateLaunchableProgramRequest req, HttpRequest request, AppDbContext db, IActivityLogService activityLog) =>
        {
            var admin = await GetAuthorizedAdmin(request, db);
            if (admin is null)
                return Results.Unauthorized();
            if (admin.HasDefaultPassword)
                return Results.Json(new { error = "Password change required before continuing." }, statusCode: 403);
            if (RequireAdmin(admin) is { } forbidden) return forbidden;

            var program = await db.LaunchablePrograms.FindAsync(id);
            if (program is null) return Results.NotFound();

            var name = req.Name?.Trim();
            var path = req.Path?.Trim();
            if (string.IsNullOrEmpty(name))
                return Results.BadRequest(new { error = "ต้องระบุชื่อโปรแกรม" });
            if (string.IsNullOrEmpty(path))
                return Results.BadRequest(new { error = "ต้องระบุ path ของโปรแกรม" });

            if (await db.LaunchablePrograms.AnyAsync(p => p.Id != id && p.Name == name))
                return Results.Json(new { error = $"มีโปรแกรมชื่อ '{name}' อยู่แล้ว" }, statusCode: 409);

            program.Name = name;
            program.Path = path;
            program.Arguments = string.IsNullOrWhiteSpace(req.Arguments) ? null : req.Arguments.Trim();
            program.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();

            await activityLog.LogAdminActionAsync(admin, "LaunchableProgramUpdated",
                $"{admin.Username} แก้ไขโปรแกรม '{program.Name}' ({program.Path})");

            return Results.Ok(program);
        });

        app.MapDelete("/api/launchable-programs/{id:int}", async (int id, HttpRequest request, AppDbContext db, IActivityLogService activityLog) =>
        {
            var admin = await GetAuthorizedAdmin(request, db);
            if (admin is null)
                return Results.Unauthorized();
            if (admin.HasDefaultPassword)
                return Results.Json(new { error = "Password change required before continuing." }, statusCode: 403);
            if (RequireAdmin(admin) is { } forbidden) return forbidden;

            var program = await db.LaunchablePrograms.FindAsync(id);
            if (program is null) return Results.NotFound();

            db.LaunchablePrograms.Remove(program);
            await db.SaveChangesAsync();

            await activityLog.LogAdminActionAsync(admin, "LaunchableProgramDeleted",
                $"{admin.Username} ลบโปรแกรม '{program.Name}' ออกจากรายการที่เปิดได้ทั้งห้อง");

            return Results.Ok(new { message = "Program deleted." });
        });

        // Agent re-validate ก่อนรัน LaunchProgram command จริง - auth ด้วย agent secret (ไม่ใช่ admin
        // token เพราะ Agent ไม่มี) ดู doc comment บนคลาสนี้ คืน 404 ถ้าโปรแกรมถูกลบ/ไม่เคยมีอยู่ - Agent
        // ต้องปฏิเสธการรันทันทีเมื่อเจอ 404 (ดู CommandProcessor.HandleLaunchProgramAsync)
        app.MapGet("/api/launchable-programs/{id:int}/for-agent", async (
            int id, HttpRequest httpRequest, AppDbContext db, AgentAuthenticator agentAuth, IActivityLogService activityLog) =>
        {
            var (allowed, _) = await agentAuth.AuthorizeByHeadersAsync(httpRequest, db, activityLog, "GET /api/launchable-programs/{id}/for-agent");
            if (!allowed)
                return Results.Unauthorized();

            var program = await db.LaunchablePrograms.FindAsync(id);
            if (program is null) return Results.NotFound();

            return Results.Ok(new { program.Id, program.Name, program.Path, program.Arguments });
        });

        return app;
    }
}
