using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using OnionProcOparetor.Server.Data;
using OnionProcOparetor.Server.Hubs;
using OnionProcOparetor.Server.Models;
using OnionProcOparetor.Server.Services;
using static OnionProcOparetor.Server.Endpoints.AuthHelpers;

namespace OnionProcOparetor.Server.Endpoints;

/// <summary>สั่งคำสั่งไปยัง Agent + ack/result/cancel/batch/pending</summary>
public static class CommandEndpoints
{
    public static WebApplication MapCommandEndpoints(this WebApplication app, TimeSpan eventCommandLifetime)
    {
        // Console สั่ง command ไปยัง Agent เครื่องใดเครื่องหนึ่ง - บันทึกลง DB เป็น pending เสมอ (fallback ผ่าน poll)
        // แล้ว push ทันทีผ่าน SignalR ถ้า Agent เครื่องนั้น connected อยู่ตอนนี้
        app.MapPost("/api/commands/{clientGuid}", async (string clientGuid, CommandRequest req, HttpRequest request, AppDbContext db, IHubContext<LabHub> hub, IActivityLogService activityLog) =>
        {
            var admin = await GetAuthorizedAdmin(request, db);
            if (admin is null)
                return Results.Unauthorized();
            if (admin.HasDefaultPassword)
                return Results.Json(new { error = "Password change required before continuing." }, statusCode: 403);

            var commandCreatedAt = DateTime.UtcNow;
            var command = new ClientCommand
            {
                ClientGuid = clientGuid,
                CommandType = req.CommandType,
                PayloadJson = req.Payload,
                CreatedAt = commandCreatedAt,
                Status = ClientCommandStatus.Pending,
                // อายุคำสั่ง - คำสั่งประเภท "เหตุการณ์" (Shutdown/Restart/Lock/...) หมดอายุใน 3 นาที ส่วน
                // คำสั่งประเภท "สถานะ" (UpdateSettings/...) ไม่หมดอายุ ดูเหตุผลเต็มใน CommandExpiry
                ExpiresAt = CommandExpiry.ComputeExpiry(req.CommandType, commandCreatedAt, eventCommandLifetime),
                // BatchId ผูกคำสั่งทุกเครื่องที่ครูกดพร้อมกันครั้งเดียวไว้ด้วยกัน - Console สร้างค่านี้เอง
                // แล้วส่งมาให้เหมือนกันทุก request ในชุดนั้น (null ได้ถ้ายิง API ตรงโดยไม่ผ่าน Console)
                BatchId = string.IsNullOrWhiteSpace(req.BatchId) ? null : req.BatchId,
                ExecutionStatus = CommandExecutionStatus.Pending
            };
            db.ClientCommands.Add(command);
            await db.SaveChangesAsync(); // ต้อง save ก่อนถึงจะได้ command.Id จริง (SQLite autoincrement) เอาไปส่งต่อให้ SignalR ได้

            await hub.Clients.SendCommandToAgent(clientGuid, command.Id, command.CommandType, command.PayloadJson);

            var targetClient = await db.ClientMachines.FirstOrDefaultAsync(c => c.ClientGuid == clientGuid);
            await activityLog.LogAdminActionAsync(admin, "CommandSent",
                $"{admin.Username} สั่ง '{command.CommandType}' ไปยังเครื่อง '{targetClient?.MachineName}'",
                clientGuid, targetClient?.MachineName);

            return Results.Created($"/api/commands/{command.Id}", command);
        });

        // Agent รายงาน "ผลจริง" ของ command กลับมาหลัง process เสร็จ - fallback ของ SignalR hub method
        // LabHub.ReportCommandResult (ใช้เมื่อ Agent ไม่ได้ connect SignalR อยู่) ทั้ง 2 ทางเรียก
        // CommandResultRecorder ตัวเดียวกัน
        //
        // ต่างจาก /ack ตรงที่ /ack บอกแค่ "ได้รับแล้ว" (delivery) ส่วนอันนี้บอก "ทำได้จริงไหม" (execution)
        //
        // 2026-08-31: เพิ่ม agent auth + ตรวจว่า command เป็นของเครื่องที่เรียกมาจริง - เดิมไม่มีทั้งคู่ และ
        // commandId เป็นเลขรันนิ่งที่เดาได้ ประกอบกับ CommandResultRecorder เป็นแบบ first-write-wins
        // (ผลจริงที่มาทีหลังถูกทิ้ง) = ยิง {"success":true} ใส่เลขถัดๆ ไปทันทีหลังครูสั่งงาน แล้วผลปลอมจะ
        // ค้างเป็นผลถาวรทับผลจริง ทั้งฝั่ง enforcement และฝั่ง audit trail
        app.MapPost("/api/commands/{commandId:int}/result", async (
            int commandId,
            CommandResultRequest req,
            HttpRequest httpRequest,
            AppDbContext db,
            IHubContext<LabHub> hub,
            AgentAuthenticator agentAuth,
            IActivityLogService activityLog) =>
        {
            var (allowed, authenticatedClient) = await agentAuth.AuthorizeByHeadersAsync(httpRequest, db, activityLog, "POST /api/commands/{id}/result");
            if (!allowed)
            {
                return Results.Unauthorized();
            }

            if (authenticatedClient is not null && !await OwnsCommandAsync(db, commandId, authenticatedClient.ClientGuid))
            {
                return Results.Unauthorized();
            }

            var command = await CommandResultRecorder.RecordAsync(db, commandId, req.Success, req.Message);
            if (command is null) return Results.NotFound();

            await hub.Clients.BroadcastCommandResult(command);

            return Results.Ok(CommandResultDto.From(command));
        });

        // Console ดึงสถานะทั้งชุดของ action ที่กดไปครั้งเดียว (หลายเครื่อง = หลาย command แต่ BatchId
        // เดียวกัน) - ใช้เป็น fallback ของ SignalR "CommandResultChanged" และใช้ตอนเปิดหน้า report
        // ครั้งแรกเพื่อดึงสถานะปัจจุบันทั้งหมดทีเดียว (บาง result อาจมาถึงก่อนหน้าต่างจะ subscribe ทัน)
        app.MapGet("/api/commands/batch/{batchId}", async (string batchId, HttpRequest request, AppDbContext db) =>
        {
            var admin = await GetAuthorizedAdmin(request, db);
            if (admin is null)
                return Results.Unauthorized();
            if (admin.HasDefaultPassword)
                return Results.Json(new { error = "Password change required before continuing." }, statusCode: 403);

            var commands = await db.ClientCommands
                .Where(c => c.BatchId == batchId)
                .OrderBy(c => c.Id)
                .ToListAsync();

            return Results.Ok(commands.Select(CommandResultDto.From).ToList());
        });

        // Agent เรียก ack กลับมาหลังได้รับ command แล้ว (ไม่ว่าจะรับผ่าน SignalR หรือเจอผ่าน poll fallback ก็ตาม)
        // กัน command เดิมถูกส่งซ้ำไปเรื่อยๆ ทุกรอบ poll
        //
        // 2026-08-31: เพิ่ม agent auth + ตรวจ ownership ด้วยเหตุผลเดียวกับ /result ด้านบน แต่ผลกระทบของ
        // ช่องนี้ตรงกว่า: Status เป็นสิ่งเดียวที่ตัดสินว่า /api/poll จะส่ง command นั้นซ้ำอีกไหม
        // (filter Status == Pending) นักเรียนที่วน POST ack ใส่เลข 1..N รัวๆ จึงทำให้คำสั่งของครูที่ส่งมา
        // หาเครื่องตัวเองถูก mark ว่า Delivered แล้วไม่ถูกส่งซ้ำอีกเลย ทั้งที่ไม่เคยถูกรันจริงสักครั้ง
        app.MapPost("/api/commands/{commandId:int}/ack", async (
            int commandId,
            HttpRequest httpRequest,
            AppDbContext db,
            AgentAuthenticator agentAuth,
            IActivityLogService activityLog) =>
        {
            var (allowed, authenticatedClient) = await agentAuth.AuthorizeByHeadersAsync(httpRequest, db, activityLog, "POST /api/commands/{id}/ack");
            if (!allowed)
            {
                return Results.Unauthorized();
            }

            var command = await db.ClientCommands.FindAsync(commandId);
            if (command is null) return Results.NotFound();

            // ตรวจ ownership แยกจาก auth: แม้ agent จะพิสูจน์ตัวตนของ "ตัวเอง" ได้ ก็ยังไม่ควร ack
            // command ของเครื่องอื่น - เป็นการกัน lateral movement ในกรณีที่ secret ของเครื่องหนึ่งหลุด
            if (authenticatedClient is not null && command.ClientGuid != authenticatedClient.ClientGuid)
            {
                return Results.Unauthorized();
            }

            // idempotent โดยตั้งใจ: ต้องเรียกซ้ำได้ปลอดภัย เพราะ Agent อาจ ack command เดียวกันมาทั้งจาก
            // SignalR path และ poll path พร้อมกัน (race) - ถ้า deliver ไปแล้วไม่ต้อง overwrite DeliveredAt ซ้ำ
            // (ไม่งั้น DeliveredAt จะขยับเวลาไปเรื่อยๆ ทุกครั้งที่ ack ซ้ำ ทั้งที่จริงควรเป็นเวลาส่งมอบครั้งแรก)
            if (command.Status != ClientCommandStatus.Delivered)
            {
                command.Status = ClientCommandStatus.Delivered;
                command.DeliveredAt = DateTime.UtcNow;
                await db.SaveChangesAsync();
            }

            return Results.Ok(command);
        });

        // Console ดึงรายการ command ที่ยังค้าง Pending อยู่ของเครื่องหนึ่งๆ - คู่กับ /cancel ด้านล่าง
        // (ActionReportWindow ยกเลิกได้แค่ตอนหน้าต่างนั้นยังเปิดอยู่เท่านั้น แต่ command ที่ค้างจริงอาจมา
        // จากรอบก่อน เช่น หน้าต่างปิดไปแล้วหรือ Console เปิดใหม่ทับ - endpoint นี้ให้ ActionPanelWindow's
        // PENDING COMMANDS... ดึงสถานะปัจจุบันจริงจาก Server ได้เสมอ ไม่ต้องพึ่งว่าเคยเห็นหน้า report
        // ตอนสั่งหรือเปล่า)
        app.MapGet("/api/commands/pending/{clientGuid}", async (string clientGuid, HttpRequest request, AppDbContext db) =>
        {
            var admin = await GetAuthorizedAdmin(request, db);
            if (admin is null)
                return Results.Unauthorized();

            // กรองด้วยอายุเหมือน /api/poll เป๊ะ ๆ - ถ้าไม่กรอง หน้านี้จะโชว์คำสั่งที่ Server ไม่มีวันส่งให้
            // เครื่องอีกแล้วว่า "ค้างอยู่" ซึ่งหลอกครูให้ไปกดยกเลิกของที่ตายไปแล้ว
            var pendingNowUtc = DateTime.UtcNow;
            var pending = await db.ClientCommands
                .Where(c => c.ClientGuid == clientGuid
                            && c.Status == ClientCommandStatus.Pending
                            && (c.ExpiresAt == null || c.ExpiresAt >= pendingNowUtc))
                .OrderBy(c => c.CreatedAt)
                .ToListAsync();

            return Results.Ok(pending.Select(CommandResultDto.From).ToList());
        });

        // Console ยกเลิก command ที่ยังค้าง Pending อยู่ - เพิ่มขึ้นหลัง field test เจอบั๊กจริง (2026-08-25):
        // Restart/Shutdown ตั้งเวลาดับเครื่องแบบ fire-and-forget "ก่อน" แล้วค่อย ack "ทีหลัง" ถ้า ack POST
        // ไปไม่ถึง Server ทันเวลาที่เครื่องดับจริงพอดี (มีโอกาสสูงขึ้นมากตอนใช้ Tailscale fallback ที่ช้า/
        // หลุดง่ายกว่า LAN) command จะค้าง Pending ตลอดกาล - เครื่องบูตกลับมา Agent (state ในหน่วยความจำ
        // ล้างหมด) poll เจอ command เดิมที่ยังเป็น Pending ซ้ำ รันซ้ำ ดับซ้ำ วนไม่จบ endpoint นี้ให้ admin
        // ตัด Pending ทิ้งตรงๆ จาก Server ได้ทันทีโดยไม่ต้องพึ่ง Agent เลย (Agent ที่ยังไม่ boot กลับมา
        // poll จะไม่เจอ command นี้อีกต่อไป) - แก้ที่ต้นตอจริงคือ persist "เคยรันแล้ว" ให้ Restart/Shutdown
        // รอดจาก reboot ด้วย (ดู CommandProcessor.cs) แต่เครื่องที่ยังไม่ได้อัปเดต Agent ตัวใหม่ต้องพึ่ง
        // endpoint นี้เป็นทางออกเดียวที่หยุด loop ได้ทันที
        app.MapPost("/api/commands/{commandId:int}/cancel", async (
            int commandId, HttpRequest request, AppDbContext db, IHubContext<LabHub> hub, IActivityLogService activityLog) =>
        {
            var admin = await GetAuthorizedAdmin(request, db);
            if (admin is null)
                return Results.Unauthorized();

            var command = await db.ClientCommands.FindAsync(commandId);
            if (command is null) return Results.NotFound();

            // Status (delivery/poll filter) บังคับเป็น Failed เสมอไม่ว่าจะอยู่สถานะไหนอยู่ก่อน - นี่คือตัวที่
            // หยุด /api/poll ไม่ให้ส่ง command นี้อีกจริงๆ (ดู pendingCommands query ด้านบนที่ filter
            // Status == Pending เท่านั้น)
            command.Status = ClientCommandStatus.Failed;

            // ExecutionStatus แตะเฉพาะตอนยังเป็น Pending เท่านั้น - กันทับผลจริงที่ Agent อาจรายงานมาก่อนแล้ว
            // (เช่น กด cancel ช้าไปนิดหลัง Agent เพิ่ง report สำเร็จพอดี ไม่อยากให้ข้อความ "ยกเลิก" ไปทับ
            // ผลจริงที่ถูกต้องกว่า)
            if (command.ExecutionStatus == CommandExecutionStatus.Pending)
            {
                command.ExecutionStatus = CommandExecutionStatus.Failed;
                command.ExecutionMessage = $"ยกเลิกโดย {admin.Username}";
                command.ExecutedAt = DateTime.UtcNow;
            }

            await db.SaveChangesAsync();
            await hub.Clients.BroadcastCommandResult(command);

            var targetClient = await db.ClientMachines.FirstOrDefaultAsync(c => c.ClientGuid == command.ClientGuid);
            await activityLog.LogAdminActionAsync(admin, "CommandCancelled",
                $"{admin.Username} ยกเลิกคำสั่ง '{command.CommandType}' (id={command.Id}) ที่ค้างอยู่บนเครื่อง '{targetClient?.MachineName}'",
                command.ClientGuid, targetClient?.MachineName);

            return Results.Ok(CommandResultDto.From(command));
        });

        return app;
    }

    /// <summary>
    /// command นี้เป็นของเครื่องนั้นจริงไหม - แยกเป็น helper เพราะทั้ง /ack และ /result ต้องใช้เหมือนกัน
    /// และเป็นเงื่อนไขที่ถ้าลืมใส่จุดใดจุดหนึ่งแล้วช่องโหว่จะกลับมาทันทีโดยไม่มีอาการอะไรให้เห็น
    /// </summary>
    private static async Task<bool> OwnsCommandAsync(AppDbContext db, int commandId, string clientGuid)
    {
        var owner = await db.ClientCommands
            .Where(c => c.Id == commandId)
            .Select(c => c.ClientGuid)
            .FirstOrDefaultAsync();

        // ไม่มี command นี้อยู่ = ปล่อยผ่านให้ endpoint ตอบ 404 ตามเดิม (ไม่ใช่ 401) - แยกให้ชัดว่า
        // "ไม่มีของ" กับ "มีของแต่ไม่ใช่ของคุณ" คนละเรื่อง
        return owner is null || owner == clientGuid;
    }
}
