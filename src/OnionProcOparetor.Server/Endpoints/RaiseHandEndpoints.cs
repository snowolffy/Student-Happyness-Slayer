using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using OnionProcOparetor.Server.Data;
using OnionProcOparetor.Server.Hubs;
using OnionProcOparetor.Server.Services;
using static OnionProcOparetor.Server.Endpoints.AuthHelpers;

namespace OnionProcOparetor.Server.Endpoints;

/// <summary>
/// ยกมือขอความช่วยเหลือจากครู (WP4-2, 2026-11-01 - ขั้น 4 เครื่องมือสอน ดู docs/wireframes/tc-raisehand.dc.html)
///
/// เส้นทาง: AgentTray (ปุ่มยกมือ) -> Agent loopback (POST /raisehand/send, /raisehand/cancel) -> endpoint
/// ที่นี่ (Agent เป็นคนแนบ X-Agent-Guid/X-Agent-Secret ให้เองทุก request อยู่แล้ว - ดู ServerClient) ->
/// push คิวล่าสุดไป Console ทุกตัวผ่าน SignalR group "console" (เหมือน TamperReportEndpoints)
///
/// คิวเก็บในหน่วยความจำล้วนๆ ผ่าน RaiseHandQueue (singleton) - "ตัวจริง" ของนักเรียนที่ผูกกับแต่ละรายการ
/// มาจาก StudentSession ที่เปิดอยู่ของเครื่องนั้น ณ เวลาที่ยกมือ (join เดียวกับที่ ClientEndpoints.cs ใช้เติม
/// ClientMachine.CurrentStudentName) - snapshot ไว้ตอนยกมือ ไม่ใช่ live join ตลอดเวลา เพราะนักเรียนอาจ
/// logout ไปแล้วระหว่างที่ยังรอครูอยู่ในคิว
/// </summary>
public static class RaiseHandEndpoints
{
    public static WebApplication MapRaiseHandEndpoints(this WebApplication app)
    {
        // ---- Agent เรียกตอน AgentTray กดปุ่ม "ยกมือ" ----
        app.MapPost("/api/raisehand", async (
            RaiseHandSendRequest req,
            HttpRequest httpRequest,
            AppDbContext db,
            AgentAuthenticator agentAuth,
            IActivityLogService activityLog,
            RaiseHandQueue queue,
            IHubContext<LabHub> hub) =>
        {
            var (allowed, authenticatedClient) = await agentAuth.AuthorizeByHeadersAsync(httpRequest, db, activityLog, "POST /api/raisehand");
            if (!allowed)
            {
                return Results.Unauthorized();
            }

            // agent เก่าที่ยังไม่ส่ง header มา (ได้ผ่านมาเพราะยังไม่บังคับใช้) ไม่มี GUID ให้ผูกคิวด้วยเลย
            var clientGuid = authenticatedClient?.ClientGuid
                ?? httpRequest.Headers[AgentAuthenticator.GuidHeaderName].ToString();
            if (string.IsNullOrWhiteSpace(clientGuid))
            {
                return Results.BadRequest(new { success = false, error = "ไม่พบ ClientGuid ของเครื่องนี้" });
            }

            var client = authenticatedClient ?? await db.ClientMachines.FirstOrDefaultAsync(c => c.ClientGuid == clientGuid);
            var machineName = client?.MachineName ?? clientGuid;

            // นักเรียนที่ login อยู่ตอนนี้ (ถ้ามี) - join เดียวกับ GET /api/clients (ดู ClientEndpoints.cs)
            var activeSession = await db.StudentSessions
                .FirstOrDefaultAsync(s => s.EndedAt == null && s.ClientGuid == clientGuid);
            var student = activeSession is null
                ? null
                : await db.Students.FindAsync(activeSession.StudentId);

            static string Clamp(string? value, int max) =>
                string.IsNullOrEmpty(value) ? string.Empty : (value.Length <= max ? value : value[..max]);

            var reason = Clamp(req.Reason, 64);
            var note = string.IsNullOrWhiteSpace(req.Note) ? null : Clamp(req.Note, 200);
            if (string.IsNullOrWhiteSpace(reason))
            {
                reason = "ขอความช่วยเหลือ";
            }

            var (entry, cooldownSecondsRemaining) = queue.TryRaise(clientGuid, machineName, student?.FullName, reason, note);
            if (entry is null)
            {
                return Results.Ok(new
                {
                    success = false,
                    error = cooldownSecondsRemaining is not null
                        ? $"ยกมือได้อีกใน {cooldownSecondsRemaining} วินาที"
                        : "เครื่องนี้ยกมืออยู่แล้ว - รอครูตอบก่อน",
                    cooldownSecondsRemaining
                });
            }

            await activityLog.LogStudentActionAsync(
                student,
                "RaiseHandRequested",
                $"เครื่อง '{machineName}' ยกมือขอความช่วยเหลือ ({reason})" + (student is null ? " - ไม่พบนักเรียน login อยู่" : string.Empty),
                clientGuid,
                machineName,
                studentCodeOverride: student is null ? machineName : null);

            await hub.Clients.Group(LabHub.ConsoleGroupName).SendAsync("RaiseHandQueueChanged", RaiseHandQueueEntryResponse.FromQueue(queue));

            return Results.Ok(new
            {
                success = true,
                entryId = entry.Id,
                queuePosition = queue.GetQueuePosition(entry.Id)
            });
        });

        // ---- Agent เรียกตอนนักเรียนกด "ยกเลิก" บนป้ายที่ค้างอยู่ ----
        app.MapPost("/api/raisehand/cancel", async (
            HttpRequest httpRequest,
            AppDbContext db,
            AgentAuthenticator agentAuth,
            IActivityLogService activityLog,
            RaiseHandQueue queue,
            IHubContext<LabHub> hub) =>
        {
            var (allowed, authenticatedClient) = await agentAuth.AuthorizeByHeadersAsync(httpRequest, db, activityLog, "POST /api/raisehand/cancel");
            if (!allowed)
            {
                return Results.Unauthorized();
            }

            var clientGuid = authenticatedClient?.ClientGuid
                ?? httpRequest.Headers[AgentAuthenticator.GuidHeaderName].ToString();
            if (string.IsNullOrWhiteSpace(clientGuid))
            {
                return Results.BadRequest(new { success = false });
            }

            var removed = queue.CancelByClient(clientGuid);
            if (removed is not null)
            {
                await hub.Clients.Group(LabHub.ConsoleGroupName).SendAsync("RaiseHandQueueChanged", RaiseHandQueueEntryResponse.FromQueue(queue));
            }

            return Results.Ok(new { success = removed is not null });
        });

        // ---- Console/OnlineConsole ดึงคิวปัจจุบัน (ใช้ตอนเปิดหน้าครั้งแรก/refresh - ค่าจริงหลังจากนั้น push ผ่าน SignalR) ----
        app.MapGet("/api/raisehand", async (HttpRequest request, AppDbContext db, RaiseHandQueue queue) =>
        {
            var admin = await GetAuthorizedAdmin(request, db);
            if (admin is null)
                return Results.Unauthorized();

            return Results.Ok(RaiseHandQueueEntryResponse.FromQueue(queue));
        });

        // ---- ครูกด "ไปช่วยแล้ว" ----
        app.MapPost("/api/raisehand/{id:int}/resolve", async (
            int id,
            HttpRequest request,
            AppDbContext db,
            IActivityLogService activityLog,
            RaiseHandQueue queue,
            IHubContext<LabHub> hub) =>
        {
            var admin = await GetAuthorizedAdmin(request, db);
            if (admin is null)
                return Results.Unauthorized();

            var resolved = queue.Resolve(id);
            if (resolved is null)
                return Results.NotFound();

            await activityLog.LogAdminActionAsync(admin, "RaiseHandResolved",
                $"{admin.Username} ปิดคิวยกมือของเครื่อง '{resolved.MachineName}' ({resolved.Reason})",
                resolved.ClientGuid, resolved.MachineName);

            await hub.Clients.Group(LabHub.ConsoleGroupName).SendAsync("RaiseHandQueueChanged", RaiseHandQueueEntryResponse.FromQueue(queue));

            return Results.Ok(new { success = true });
        });

        // ---- ครูกด "ล้างคิวทั้งหมด" ----
        app.MapPost("/api/raisehand/clear", async (
            HttpRequest request,
            AppDbContext db,
            IActivityLogService activityLog,
            RaiseHandQueue queue,
            IHubContext<LabHub> hub) =>
        {
            var admin = await GetAuthorizedAdmin(request, db);
            if (admin is null)
                return Results.Unauthorized();

            var cleared = queue.ClearAll();
            if (cleared > 0)
            {
                await activityLog.LogAdminActionAsync(admin, "RaiseHandQueueCleared",
                    $"{admin.Username} ล้างคิวยกมือทั้งหมด ({cleared} รายการ)");

                await hub.Clients.Group(LabHub.ConsoleGroupName).SendAsync("RaiseHandQueueChanged", RaiseHandQueueEntryResponse.FromQueue(queue));
            }

            return Results.Ok(new { cleared });
        });

        return app;
    }
}

/// <summary>รูปแบบ JSON ที่ส่งให้ Console/OnlineConsole ทั้งทาง GET และ SignalR "RaiseHandQueueChanged" - ส่ง QueuePosition ไปด้วยเลยกันต้องคำนวณซ้ำฝั่ง client</summary>
public sealed record RaiseHandQueueEntryResponse(
    int Id, string ClientGuid, string MachineName, string? StudentName,
    string Reason, string? Note, DateTime RaisedAt, int QueuePosition)
{
    public static List<RaiseHandQueueEntryResponse> FromQueue(RaiseHandQueue queue)
    {
        var active = queue.GetActiveQueue();
        return active
            .Select((e, index) => new RaiseHandQueueEntryResponse(
                e.Id, e.ClientGuid, e.MachineName, e.StudentName, e.Reason, e.Note, e.RaisedAt, index + 1))
            .ToList();
    }
}
