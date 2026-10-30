using Microsoft.EntityFrameworkCore;
using OnionProcOparetor.Server.Data;
using OnionProcOparetor.Server.Models;
using OnionProcOparetor.Server.Services;
using static OnionProcOparetor.Server.Endpoints.AuthHelpers;

namespace OnionProcOparetor.Server.Endpoints;

/// <summary>รายการเครื่อง client, toggle/ลบ/ตั้งค่ารายเครื่อง, rule override ต่อเครื่อง และ reset agent secret</summary>
public static class ClientEndpoints
{
    public static WebApplication MapClientEndpoints(this WebApplication app)
    {
        // ดึงรายชื่อ client ทั้งหมด + สถานะ
        // roomId เป็น query param ใหม่ (WP3, 2026-10-30) - optional เสมอ ไม่ส่งมา = ได้ทุกเครื่องเหมือนเดิม
        // ทุกประการ (client เก่าที่ไม่รู้จัก param นี้ไม่พัง) ส่งมา = กรองเฉพาะเครื่องในห้องนั้น
        app.MapGet("/api/clients", async (HttpRequest request, AppDbContext db, AgentConnectionRegistry connectionRegistry, int? roomId) =>
        {
            var admin = await GetAuthorizedAdmin(request, db);
            if (admin is null)
                return Results.Unauthorized();
            if (admin.HasDefaultPassword)
                return Results.Json(new { error = "Password change required before continuing." }, statusCode: 403);

            var query = db.ClientMachines.AsQueryable();
            if (roomId is not null)
            {
                query = query.Where(c => c.RoomId == roomId);
            }

            var clients = await query.OrderBy(c => c.MachineName).ToListAsync();
            // เติม IsConnectedViaSignalR + HasEverConnectedViaSignalR ก่อน serialize เสมอ (ไม่ set = default
            // false ทั้งคู่ = IsOnline พึ่ง LastSeenAt อย่างเดียว ยังถูกอยู่แต่ไม่ได้สัญญาณเร็วจาก SignalR - ดู
            // ClientMachine.IsConnectedViaSignalR/HasEverConnectedViaSignalR)
            foreach (var client in clients)
            {
                client.IsConnectedViaSignalR = connectionRegistry.IsConnected(client.ClientGuid);
                client.HasEverConnectedViaSignalR = connectionRegistry.HasEverConnected(client.ClientGuid);
            }
            return Results.Ok(clients);
        });

        // toggle เครื่องใดเครื่องหนึ่ง (per-machine)
        app.MapPost("/api/clients/{id:int}/toggle", async (int id, HttpRequest request, AppDbContext db, AgentConnectionRegistry connectionRegistry, IActivityLogService activityLog) =>
        {
            var admin = await GetAuthorizedAdmin(request, db);
            if (admin is null)
                return Results.Unauthorized();
            if (admin.HasDefaultPassword)
                return Results.Json(new { error = "Password change required before continuing." }, statusCode: 403);

            var client = await db.ClientMachines.FindAsync(id);
            if (client is null) return Results.NotFound();

            client.IsEnabled = !client.IsEnabled;
            await db.SaveChangesAsync();
            client.IsConnectedViaSignalR = connectionRegistry.IsConnected(client.ClientGuid);
            client.HasEverConnectedViaSignalR = connectionRegistry.HasEverConnected(client.ClientGuid);

            await activityLog.LogAdminActionAsync(admin, "ClientEnforcementToggled",
                $"{admin.Username} {(client.IsEnabled ? "เปิด" : "ปิด")} enforcement ของเครื่อง '{client.MachineName}'",
                client.ClientGuid, client.MachineName);

            return Results.Ok(client);
        });

        // ลบเครื่อง client ออกจากระบบ (LogEntry.ClientGuid ไม่มี FK ผูกกับ ClientMachine โดยตั้งใจ
        // จึงลบ ClientMachine ได้โดย log เก่าของเครื่องนั้นยังอยู่ครบ ไม่ต้อง cascade)
        app.MapDelete("/api/clients/{id:int}", async (int id, HttpRequest request, AppDbContext db, IActivityLogService activityLog) =>
        {
            var admin = await GetAuthorizedAdmin(request, db);
            if (admin is null)
                return Results.Unauthorized();
            if (admin.HasDefaultPassword)
                return Results.Json(new { error = "Password change required before continuing." }, statusCode: 403);

            var client = await db.ClientMachines.FindAsync(id);
            if (client is null) return Results.NotFound();

            db.ClientMachines.Remove(client);
            await db.SaveChangesAsync();

            await activityLog.LogAdminActionAsync(admin, "ClientDeleted",
                $"{admin.Username} ลบเครื่อง '{client.MachineName}' ออกจากระบบ",
                client.ClientGuid, client.MachineName);

            return Results.Ok(new { message = "Client deleted." });
        });

        // ---- Per-agent rule override endpoints (Stable 1 เรื่องที่ 3) ----
        // override ชนะ global เสมอ 100% - ดู /api/poll/{clientGuid} สำหรับจุดที่ resolve ค่าจริง

        // ดึง override ทั้งหมดของเครื่องหนึ่ง (Console ใช้ merge กับ GET /api/rules เองฝั่ง client เพื่อโชว์
        // รายการกฎทั้งหมด + สถานะ effective ต่อเครื่องนี้)
        app.MapGet("/api/clients/{clientGuid}/rule-overrides", async (string clientGuid, HttpRequest request, AppDbContext db) =>
        {
            var admin = await GetAuthorizedAdmin(request, db);
            if (admin is null)
                return Results.Unauthorized();
            if (admin.HasDefaultPassword)
                return Results.Json(new { error = "Password change required before continuing." }, statusCode: 403);

            var overrides = await db.RuleOverrides.Where(o => o.ClientGuid == clientGuid).ToListAsync();
            return Results.Ok(overrides);
        });

        // ตั้ง/แก้ override ของกฎหนึ่งสำหรับเครื่องหนึ่ง (upsert - ไม่มีแล้วสร้างใหม่, มีอยู่แล้วแก้ IsEnabled)
        app.MapPut("/api/clients/{clientGuid}/rule-overrides/{ruleId:int}", async (string clientGuid, int ruleId, RuleOverrideRequest req, HttpRequest request, AppDbContext db, IActivityLogService activityLog) =>
        {
            var admin = await GetAuthorizedAdmin(request, db);
            if (admin is null)
                return Results.Unauthorized();
            if (admin.HasDefaultPassword)
                return Results.Json(new { error = "Password change required before continuing." }, statusCode: 403);

            var rule = await db.Rules.FindAsync(ruleId);
            if (rule is null) return Results.NotFound();

            var existing = await db.RuleOverrides.FirstOrDefaultAsync(o => o.ClientGuid == clientGuid && o.RuleId == ruleId);
            if (existing is null)
            {
                existing = new RuleOverride { ClientGuid = clientGuid, RuleId = ruleId, IsEnabled = req.IsEnabled };
                db.RuleOverrides.Add(existing);
            }
            else
            {
                existing.IsEnabled = req.IsEnabled;
                existing.UpdatedAt = DateTime.UtcNow;
            }

            await db.SaveChangesAsync();

            var client = await db.ClientMachines.FirstOrDefaultAsync(c => c.ClientGuid == clientGuid);
            await activityLog.LogAdminActionAsync(admin, "RuleOverrideSet",
                $"{admin.Username} ตั้ง override กฎ '{rule.Name}' = {(req.IsEnabled ? "เปิด" : "ปิด")} สำหรับเครื่อง '{client?.MachineName}'",
                clientGuid, client?.MachineName);

            return Results.Ok(existing);
        });

        // ลบ override ทิ้ง - เครื่องนี้กลับไปใช้ค่า global ของกฎนี้ตามปกติ (ไม่ error ถ้าไม่เคยมี override อยู่แล้ว)
        app.MapDelete("/api/clients/{clientGuid}/rule-overrides/{ruleId:int}", async (string clientGuid, int ruleId, HttpRequest request, AppDbContext db, IActivityLogService activityLog) =>
        {
            var admin = await GetAuthorizedAdmin(request, db);
            if (admin is null)
                return Results.Unauthorized();
            if (admin.HasDefaultPassword)
                return Results.Json(new { error = "Password change required before continuing." }, statusCode: 403);

            var existing = await db.RuleOverrides.FirstOrDefaultAsync(o => o.ClientGuid == clientGuid && o.RuleId == ruleId);
            if (existing is not null)
            {
                db.RuleOverrides.Remove(existing);
                await db.SaveChangesAsync();

                var client = await db.ClientMachines.FirstOrDefaultAsync(c => c.ClientGuid == clientGuid);
                var rule = await db.Rules.FindAsync(ruleId);
                await activityLog.LogAdminActionAsync(admin, "RuleOverrideCleared",
                    $"{admin.Username} ล้าง override กฎ '{rule?.Name}' ของเครื่อง '{client?.MachineName}' (กลับไปใช้ค่า global)",
                    clientGuid, client?.MachineName);
            }

            return Results.Ok(new { message = "Override cleared." });
        });

        // ล้าง agent secret ที่ enroll ไว้ของเครื่องหนึ่ง เพื่อให้ enroll ใหม่ในรอบ poll ถัดไป (2026-08-31)
        //
        // จำเป็นเพราะ agent secret ผูกกับไฟล์ agent.key ใน %ProgramData% ของเครื่องนั้น ซึ่งหายไปได้จริง
        // ในสถานการณ์ปกติของห้องนี้: ลง Windows ใหม่, Reboot Restore Rx restore ทับตอนที่ยังไม่ได้ commit
        // baseline, หรือติดตั้ง Agent ใหม่หลังล้างเครื่อง - ถ้าไม่มีทางล้างจากฝั่ง Server เครื่องนั้นจะติด
        // Mismatch ถาวรและ (เมื่อเปิดโหมดบังคับใช้) หลุดจากระบบไปเลยโดยที่ครูแก้เองไม่ได้
        app.MapPost("/api/clients/{id:int}/reset-agent-secret", async (int id, HttpRequest request, AppDbContext db, IActivityLogService activityLog) =>
        {
            var admin = await GetAuthorizedAdmin(request, db);
            if (admin is null)
                return Results.Unauthorized();
            if (admin.HasDefaultPassword)
                return Results.Json(new { error = "Password change required before continuing." }, statusCode: 403);

            var client = await db.ClientMachines.FindAsync(id);
            if (client is null) return Results.NotFound();

            client.AgentSecretHash = null;
            client.AgentSecretSalt = null;
            client.AgentSecretEnrolledAt = null;
            await db.SaveChangesAsync();

            await activityLog.LogAdminActionAsync(admin, "AgentSecretReset",
                $"{admin.Username} ล้าง agent secret ของเครื่อง '{client.MachineName}' - เครื่องนี้จะ enroll ใหม่ในรอบ poll ถัดไป",
                client.ClientGuid, client.MachineName);

            return Results.Ok(new { message = "Agent secret cleared. The machine will re-enroll on its next poll." });
        });

        // แก้ setting ของเครื่องใดเครื่องหนึ่งจากศูนย์กลาง (ตอนนี้มีแค่ poll interval override)
        app.MapPut("/api/clients/{id:int}/settings", async (int id, ClientSettingsRequest request, HttpRequest httpRequest, AppDbContext db, AgentConnectionRegistry connectionRegistry, IActivityLogService activityLog) =>
        {
            var admin = await GetAuthorizedAdmin(httpRequest, db);
            if (admin is null)
                return Results.Unauthorized();
            if (admin.HasDefaultPassword)
                return Results.Json(new { error = "Password change required before continuing." }, statusCode: 403);

            var client = await db.ClientMachines.FindAsync(id);
            if (client is null) return Results.NotFound();

            client.PollIntervalOverrideSeconds = request.PollIntervalOverrideSeconds;
            await db.SaveChangesAsync();

            client.IsConnectedViaSignalR = connectionRegistry.IsConnected(client.ClientGuid);
            client.HasEverConnectedViaSignalR = connectionRegistry.HasEverConnected(client.ClientGuid);

            await activityLog.LogAdminActionAsync(admin, "ClientSettingsChanged",
                $"{admin.Username} เปลี่ยน poll interval override ของเครื่อง '{client.MachineName}' เป็น {(client.PollIntervalOverrideSeconds?.ToString() ?? "ค่า default")}",
                client.ClientGuid, client.MachineName);

            return Results.Ok(client);
        });

        return app;
    }
}
