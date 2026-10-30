using Microsoft.EntityFrameworkCore;
using OnionProcOparetor.Server.Data;
using OnionProcOparetor.Server.Models;
using OnionProcOparetor.Server.Services;
using static OnionProcOparetor.Server.Endpoints.AuthHelpers;

namespace OnionProcOparetor.Server.Endpoints;

/// <summary>LogEntry ที่ Agent push ขึ้นมา + ให้ Console อ่าน</summary>
public static class LogEndpoints
{
    public static WebApplication MapLogEndpoints(this WebApplication app)
    {
        // client push log กลับมา - ตรวจ agent secret ตั้งแต่ 2026-08-31 (เดิมไม่ตรวจอะไรเลย ทำให้ใครใน
        // LAN ก็ push log ปลอมใส่เครื่องคนอื่น หรือถมดิสก์ของ server ได้)
        app.MapPost("/api/logs", async (
            LogEntry log,
            HttpRequest httpRequest,
            AppDbContext db,
            AgentAuthenticator agentAuth,
            IActivityLogService activityLog) =>
        {
            var (allowed, authenticatedClient) = await agentAuth.AuthorizeByHeadersAsync(httpRequest, db, activityLog, "POST /api/logs");
            if (!allowed)
            {
                return Results.Unauthorized();
            }

            // บังคับให้ log ผูกกับเครื่องที่พิสูจน์ตัวตนได้จริง ไม่ใช่ค่าที่อยู่ใน body - ไม่งั้นเครื่องที่
            // auth ผ่านในนามตัวเองยังยัด ClientGuid/MachineName ของเครื่องอื่นลง body ได้อยู่ดี
            if (authenticatedClient is not null)
            {
                log.ClientGuid = authenticatedClient.ClientGuid;
                log.MachineName = authenticatedClient.MachineName;
            }

            log.Timestamp = DateTime.UtcNow;
            db.LogEntries.Add(log);
            await db.SaveChangesAsync();
            return Results.Created($"/api/logs/{log.Id}", log);
        });

        // ดู log ล่าสุด (ไว้ให้ Console GUI แสดงผล)
        app.MapGet("/api/logs", async (HttpRequest request, AppDbContext db, int take = 100) =>
        {
            var admin = await GetAuthorizedAdmin(request, db);
            if (admin is null)
                return Results.Unauthorized();
            if (admin.HasDefaultPassword)
                return Results.Json(new { error = "Password change required before continuing." }, statusCode: 403);

            var logs = await db.LogEntries
                .OrderByDescending(l => l.Timestamp)
                .Take(take)
                .ToListAsync();
            return Results.Ok(logs);
        });

        return app;
    }
}
