using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using OnionProcOparetor.Server.Data;
using OnionProcOparetor.Server.Models;

namespace OnionProcOparetor.Server.Hubs;

/// <summary>
/// Real-time push คู่ขนานกับ HTTP polling เดิม - เป็นชั้นเสริม ไม่ใช่ตัวแทนที่
/// ถ้า Agent/Console หลุดจาก hub นี้ ระบบยังทำงานได้ปกติผ่าน poll เดิมทุกอย่าง (ดู /api/poll/{clientGuid})
///
/// Group ที่ใช้:
/// - group ชื่อ = ClientGuid ของแต่ละเครื่อง agent (join ผ่าน RegisterAgent) ใช้ push command เจาะจงเครื่องได้
/// - group "console" (join ผ่าน RegisterConsole) ใช้ broadcast สถานะเครื่องให้ console admin ทุกตัวที่เปิดอยู่
/// </summary>
public class LabHub : Hub
{
    public const string ConsoleGroupName = "console";

    private readonly AppDbContext _db;

    public LabHub(AppDbContext db)
    {
        _db = db;
    }

    /// <summary>Agent เรียกทันทีหลัง connect เสร็จ เพื่อ join group เฉพาะเครื่องตัวเอง (ชื่อ group = clientGuid)</summary>
    public async Task RegisterAgent(string clientGuid)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, clientGuid);

        // อัปเดต LastSeenAt ทันทีที่ต่อ SignalR สำเร็จ (เสริมจาก poll เดิมที่อัปเดตอยู่แล้วทุกรอบ)
        // ถ้ายังไม่เคยมี ClientMachine ใน DB เลย (ยังไม่เคย poll มาก่อน) ข้ามไปก่อน - รอ poll รอบแรกสร้าง record ให้ตามเดิม
        var client = await _db.ClientMachines.FirstOrDefaultAsync(c => c.ClientGuid == clientGuid);
        if (client is not null)
        {
            client.LastSeenAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            await Clients.Group(ConsoleGroupName).SendAsync("ClientStatusChanged", client);
        }
    }

    /// <summary>
    /// Console เรียกทันทีหลัง connect เสร็จ เพื่อ join group "console" (broadcast status ปกติ) และ
    /// group ส่วนตัวชื่อ consoleSessionId (GUID สุ่มต่อ Console instance หนึ่งตัว ไม่ persist) - group
    /// ส่วนตัวนี้ใช้เป็นปลายทางตอบกลับแบบเจาะจง (เช่น VncSessionReady) ที่ต้องส่งกลับไปยัง Console
    /// ตัวที่ request เท่านั้น ไม่ broadcast ให้ Console ตัวอื่นเห็นข้อมูล session/password คนอื่น
    /// </summary>
    public Task RegisterConsole(string consoleSessionId)
    {
        var joinBroadcastGroup = Groups.AddToGroupAsync(Context.ConnectionId, ConsoleGroupName);
        var joinSessionGroup = Groups.AddToGroupAsync(Context.ConnectionId, consoleSessionId);
        return Task.WhenAll(joinBroadcastGroup, joinSessionGroup);
    }

    /// <summary>
    /// Agent เรียกหลัง VncSessionManager.HandleAgentTrayStartedAsync() สำเร็จ เพื่อส่ง port/password กลับไปยัง
    /// Console ตัวที่ request เท่านั้น (ผ่าน group ส่วนตัวที่ join ไว้ใน RegisterConsole) - ไม่ log
    /// password ฝั่ง server เลย (แค่ relay ผ่านมือ ไม่เก็บ ไม่ persist ลง DB)
    ///
    /// clientGuid บอกว่า "ready" นี้เป็นของเครื่องไหน - จำเป็นตั้งแต่ Step 4e เพราะ Console เดียวกันอาจมี
    /// StartVncSession ค้างรออยู่หลายเครื่องพร้อมกัน (VncSessionPool เปิดหลาย session พร้อมกันได้) ถ้าไม่มี
    /// clientGuid มาด้วย Console จะแยกไม่ออกว่า event ที่มาถึงเป็นของ request ไหนใน consoleSessionId group
    /// เดียวกัน (ทุก request จาก Console ตัวเดียวกันแชร์ group เดียวกันหมด)
    /// </summary>
    public Task RelayVncSessionReady(string consoleSessionId, string clientGuid, int port, string password) =>
        Clients.Group(consoleSessionId).SendAsync("VncSessionReady", clientGuid, port, password);

    /// <summary>เหมือน RelayVncSessionReady แต่สำหรับกรณี Agent เปิด VNC session ไม่สำเร็จ - ส่งเหตุผลกลับไปด้วย</summary>
    public Task RelayVncSessionFailed(string consoleSessionId, string clientGuid, string reason) =>
        Clients.Group(consoleSessionId).SendAsync("VncSessionFailed", clientGuid, reason);

    /// <summary>
    /// Agent รายงาน "ผลจริง" ของ command กลับมาหลัง process เสร็จ (ต่างจาก ack เดิมที่บอกแค่ว่า
    /// ได้รับแล้ว) - ทางนี้ใช้เมื่อ Agent connect SignalR อยู่ ถ้าไม่ได้ connect จะ fallback ไป
    /// POST /api/commands/{id}/result แทน ซึ่งทำงานเหมือนกันเป๊ะ (ดู CommandResultRecorder)
    ///
    /// pattern เดียวกับทั้งระบบ: SignalR เป็นชั้นเสริมความเร็ว ไม่ใช่ทางเดียว - ทุกอย่างต้อง
    /// ทำงานได้ครบผ่าน HTTP เสมอแม้ hub จะล่ม
    /// </summary>
    public async Task ReportCommandResult(int commandId, bool success, string? message)
    {
        var updated = await CommandResultRecorder.RecordAsync(_db, commandId, success, message);

        if (updated is not null)
        {
            await Clients.Group(ConsoleGroupName).SendAsync("CommandResultChanged", CommandResultDto.From(updated));
        }
    }
}

/// <summary>
/// Logic กลางสำหรับบันทึก "ผลจริง" ของ command ลง DB - ใช้ร่วมกันทั้งทาง SignalR
/// (LabHub.ReportCommandResult) และทาง HTTP (POST /api/commands/{id}/result) ไม่ให้ logic
/// แยกกันคนละที่ (หลักการเดียวกับ CommandProcessor.ProcessPendingCommandAsync ฝั่ง Agent
/// ที่รวม SignalR path กับ poll path ไว้จุดเดียว)
/// </summary>
public static class CommandResultRecorder
{
    /// <summary>
    /// คืน ClientCommand ที่อัปเดตแล้ว หรือ null ถ้าไม่เจอ command นั้น
    ///
    /// idempotent: ถ้ามีผลจริงบันทึกไว้แล้ว (ไม่ใช่ Pending) จะไม่เขียนทับ - กัน Agent ที่รายงาน
    /// ซ้ำทั้งทาง SignalR และ HTTP พร้อมกัน (race แบบเดียวกับที่ ack เดิมเจอ) มาทับผลเดิม
    /// ข้อยกเว้นเดียว: ผลจริงที่มาช้าหลังถูก mark TimedOut ไปแล้ว "ทับได้" เพราะข้อมูลจริงจาก
    /// เครื่องย่อมดีกว่าการเดาว่า timeout (เช่นเครือข่ายกระตุกแล้วผลตามมาทีหลัง)
    /// </summary>
    public static async Task<ClientCommand?> RecordAsync(AppDbContext db, int commandId, bool success, string? message)
    {
        var command = await db.ClientCommands.FindAsync(commandId);
        if (command is null)
        {
            return null;
        }

        if (command.ExecutionStatus is CommandExecutionStatus.Succeeded or CommandExecutionStatus.Failed)
        {
            return command; // มีผลจริงแล้ว - ไม่เขียนทับ แต่ยัง broadcast ค่าปัจจุบันกลับไปได้
        }

        command.ExecutionStatus = success ? CommandExecutionStatus.Succeeded : CommandExecutionStatus.Failed;
        command.ExecutionMessage = message;
        command.ExecutedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        return command;
    }
}

/// <summary>
/// รูปแบบสถานะ command ที่ส่งให้ Console (ทั้งทาง SignalR "CommandResultChanged" และทาง
/// GET /api/commands/batch/{batchId}) - ส่งเป็น string ไม่ใช่ตัวเลข enum เพื่อให้ Console
/// อ่านได้ตรงๆ ไม่ต้องก๊อป enum ไปไว้อีกฝั่งให้ค่าเพี้ยนกันทีหลัง
/// </summary>
public class CommandResultDto
{
    public int CommandId { get; set; }
    public string ClientGuid { get; set; } = string.Empty;
    public string CommandType { get; set; } = string.Empty;
    public string? BatchId { get; set; }

    /// <summary>"Pending" | "Succeeded" | "Failed" | "TimedOut"</summary>
    public string ExecutionStatus { get; set; } = nameof(CommandExecutionStatus.Pending);

    public string? ExecutionMessage { get; set; }
    public DateTime? ExecutedAt { get; set; }

    /// <summary>สถานะส่งมอบเดิม ส่งไปด้วยเผื่อ Console อยากแยกแยะ "ยังไปไม่ถึงเครื่อง" ออกจาก "ถึงแล้วแต่ยังไม่ตอบผล"</summary>
    public string DeliveryStatus { get; set; } = nameof(ClientCommandStatus.Pending);

    public static CommandResultDto From(ClientCommand command) => new()
    {
        CommandId = command.Id,
        ClientGuid = command.ClientGuid,
        CommandType = command.CommandType,
        BatchId = command.BatchId,
        ExecutionStatus = command.ExecutionStatus.ToString(),
        ExecutionMessage = command.ExecutionMessage,
        ExecutedAt = command.ExecutedAt,
        DeliveryStatus = command.Status.ToString()
    };
}

/// <summary>
/// Helper method สำหรับ push จากฝั่ง server - ใช้ได้ทั้งจาก Program.cs (ผ่าน IHubContext&lt;LabHub&gt;.Clients)
/// และจากในตัว LabHub เอง (ผ่าน Clients) เพราะทั้งคู่ implement IHubClients ร่วมกัน
/// </summary>
public static class LabHubClientsExtensions
{
    /// <summary>
    /// Push command ไปหา Agent เครื่องที่ระบุทันที ถ้า connected ผ่าน SignalR อยู่ขณะนี้ (ไม่มี guarantee การส่งถึง - ต้องพึ่ง poll fallback เสมอ)
    /// ส่ง commandId ไปด้วยเสมอ - Agent ใช้ตัวนี้ dedup + ack กลับมา (path เดียวกับที่ใช้ตอนเจอ command ผ่าน poll fallback)
    /// </summary>
    public static Task SendCommandToAgent(this IHubClients clients, string clientGuid, int commandId, string commandType, string? payloadJson)
        => clients.Group(clientGuid).SendAsync("ReceiveCommand", commandId, commandType, payloadJson);

    /// <summary>Broadcast สถานะเครื่อง client ล่าสุดไปให้ console admin ทุกตัวที่ join group "console" อยู่</summary>
    public static Task BroadcastClientStatus(this IHubClients clients, ClientMachine client)
        => clients.Group(LabHub.ConsoleGroupName).SendAsync("ClientStatusChanged", client);

    /// <summary>
    /// Broadcast "ผลจริง" ของ command ล่าสุดไปให้ console ทุกตัวที่เปิดอยู่ - หน้า report ฝั่ง
    /// Console ใช้ตัวนี้อัปเดตแถวแบบ real-time (มี poll ทุก 2 วิเป็น fallback อยู่แล้วเผื่อ hub หลุด)
    /// </summary>
    public static Task BroadcastCommandResult(this IHubClients clients, ClientCommand command)
        => clients.Group(LabHub.ConsoleGroupName).SendAsync("CommandResultChanged", CommandResultDto.From(command));
}
