using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using OnionProcOparetor.Server.Data;
using OnionProcOparetor.Server.Models;
using OnionProcOparetor.Server.Services;

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
    private readonly AgentConnectionRegistry _connectionRegistry;
    private readonly IActivityLogService _activityLog;
    private readonly AgentAuthenticator _agentAuth;
    private readonly ScreenBroadcastRelay _screenBroadcastRelay;

    public LabHub(
        AppDbContext db,
        AgentConnectionRegistry connectionRegistry,
        IActivityLogService activityLog,
        AgentAuthenticator agentAuth,
        ScreenBroadcastRelay screenBroadcastRelay)
    {
        _db = db;
        _connectionRegistry = connectionRegistry;
        _activityLog = activityLog;
        _agentAuth = agentAuth;
        _screenBroadcastRelay = screenBroadcastRelay;
    }

    /// <summary>
    /// ClientGuid ที่ connection นี้พิสูจน์ได้ว่าเป็นเจ้าของจริง - null ถ้าพิสูจน์ไม่ได้
    ///
    /// header ถูกแนบมาตั้งแต่ตอน negotiate/handshake (ดู LabHubConnection ฝั่ง Agent) จึงอ่านได้จาก
    /// HttpContext ของ connection นี้ตลอดอายุ connection โดยไม่ต้องส่งซ้ำทุก invocation
    /// </summary>
    private async Task<ClientMachine?> ResolveAuthenticatedAgentAsync(string claimedGuid, string hubMethodName)
    {
        var httpContext = Context.GetHttpContext();
        var providedSecret = httpContext?.Request.Headers[AgentAuthenticator.SecretHeaderName].ToString();
        var headerGuid = httpContext?.Request.Headers[AgentAuthenticator.GuidHeaderName].ToString();

        // GUID ใน header (ผูกกับ connection) ต้องตรงกับ GUID ที่ method อ้าง - ถ้าไม่ตรงคือกำลัง
        // พยายามลงทะเบียนเป็นเครื่องอื่นด้วย connection ของตัวเอง
        if (!string.IsNullOrWhiteSpace(headerGuid) &&
            !string.Equals(headerGuid, claimedGuid, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var client = await _db.ClientMachines.FirstOrDefaultAsync(c => c.ClientGuid == claimedGuid);
        if (client is null)
        {
            // ยังไม่เคย poll = ยังไม่มีแถวใน DB ให้เทียบ secret ด้วย - พฤติกรรมเดิมคือปล่อยผ่าน
            // (RegisterAgent จัดการเคสนี้อยู่แล้วด้วยการข้ามการอัปเดต DB) คงไว้เหมือนเดิม
            return null;
        }

        return await _agentAuth.AuthorizeAsync(client, providedSecret, _activityLog, $"hub:{hubMethodName}")
            ? client
            : null;
    }

    /// <summary>Agent เรียกทันทีหลัง connect เสร็จ เพื่อ join group เฉพาะเครื่องตัวเอง (ชื่อ group = clientGuid)</summary>
    public async Task RegisterAgent(string clientGuid)
    {
        // ---- 2026-08-31: ตรวจสิทธิ์ก่อน join group (จาก security audit) ----
        // เดิมใครก็ตามที่เปิด WebSocket มาที่ /hubs/lab เรียก RegisterAgent ด้วย GUID ของเครื่องอื่น
        // ได้ทันที แล้วจะเข้า group ของเครื่องนั้นและ "รับ command ที่ครูส่งไปหาเครื่องนั้น" แทนมันเลย
        // (ดู SendCommandToAgent ที่ push เข้า group ชื่อ = clientGuid)
        var client = await ResolveAuthenticatedAgentAsync(clientGuid, nameof(RegisterAgent));

        if (client is null && _agentAuth.IsEnforcing)
        {
            throw new HubException("Agent authentication failed.");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, clientGuid);
        _connectionRegistry.Register(Context.ConnectionId, clientGuid);

        // อัปเดต LastSeenAt ทันทีที่ต่อ SignalR สำเร็จ (เสริมจาก poll เดิมที่อัปเดตอยู่แล้วทุกรอบ)
        // ถ้ายังไม่เคยมี ClientMachine ใน DB เลย (ยังไม่เคย poll มาก่อน) ข้ามไปก่อน - รอ poll รอบแรกสร้าง record ให้ตามเดิม
        client ??= await _db.ClientMachines.FirstOrDefaultAsync(c => c.ClientGuid == clientGuid);
        if (client is not null)
        {
            client.LastSeenAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            client.IsConnectedViaSignalR = true;
            client.HasEverConnectedViaSignalR = true;
            await Clients.Group(ConsoleGroupName).SendAsync("ClientStatusChanged", client);

            await _activityLog.LogAgentConnectivityAsync(
                "AgentConnected", $"เครื่อง '{client.MachineName}' เชื่อมต่อ (SignalR)", clientGuid, client.MachineName);
        }
    }

    /// <summary>
    /// เรียกอัตโนมัติทุกครั้งที่ connection หลุดไม่ว่าเหตุผลอะไร (Agent ปิดตัว, network หลุด, Server
    /// restart) - เป็นสัญญาณ "offline" ที่เร็วและแม่นยำที่สุดที่มี (เร็วกว่ารอ poll timeout มาก) ต้องเช็ค
    /// ก่อนว่า connection ที่หลุดเป็นของ Agent จริง (ไม่ใช่ของ Console ที่ใช้ hub เดียวกันแต่ไม่เคยเรียก
    /// RegisterAgent) และต้องเหลือ 0 connection ของเครื่องนั้นจริงๆ ก่อนถึงจะถือว่า "offline" (กัน race
    /// ตอน reconnect ที่ connection ใหม่ต่อเข้ามาก่อน connection เก่าจะ dispose)
    /// </summary>
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        // "ส่งจอครู" (WP4-4) - เครื่องที่กำลังดูจอครูอยู่แล้วหลุด SignalR (ปิดเครื่อง/เน็ตขาด) ต้องเลิก
        // เป็น viewer ด้วย ไม่งั้น ScreenBroadcastRelay จะพยายามส่งเฟรมเข้า connectionId ที่ตายไปแล้วไป
        // เรื่อยๆ (SendAsync จะ throw เงียบๆ ทุกครั้งซึ่งไม่เสียหายอะไร แต่เสียทรัพยากรเปล่าๆ)
        _screenBroadcastRelay.RemoveViewer(Context.ConnectionId);

        var (clientGuid, isFullyDisconnected) = _connectionRegistry.Unregister(Context.ConnectionId);

        if (clientGuid is not null && isFullyDisconnected)
        {
            var client = await _db.ClientMachines.FirstOrDefaultAsync(c => c.ClientGuid == clientGuid);
            if (client is not null)
            {
                client.IsConnectedViaSignalR = false;
                client.HasEverConnectedViaSignalR = true; // แน่นอนว่า true - เพิ่งหลุดจากการที่เคย connect ได้จริง
                await Clients.Group(ConsoleGroupName).SendAsync("ClientStatusChanged", client);

                await _activityLog.LogAgentConnectivityAsync(
                    "AgentDisconnected", $"เครื่อง '{client.MachineName}' หลุดการเชื่อมต่อ (SignalR)", clientGuid, client.MachineName);
            }
        }

        await base.OnDisconnectedAsync(exception);
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
    /// Agent เรียกหลัง AgentTray สแกนโปรแกรมที่มีหน้าต่างอยู่บน desktop เสร็จ เพื่อส่งผลลัพธ์ (JSON
    /// ของ List&lt;RunningProcessDto&gt;) กลับไปยัง Console ตัวที่ request เท่านั้น (group ส่วนตัวเดียวกับ
    /// VNC relay) - Server ไม่ต้องรู้จัก shape ของ RunningProcessDto เลย แค่ relay JSON string ผ่านมือ
    /// เหมือนที่ port/password ของ VNC เป็น primitive ธรรมดา ไม่มี model ฝั่งนี้
    /// </summary>
    public Task RelayProcessListReady(string consoleSessionId, string clientGuid, string processListJson) =>
        Clients.Group(consoleSessionId).SendAsync("ProcessListReady", clientGuid, processListJson);

    /// <summary>เหมือน RelayProcessListReady แต่สำหรับกรณีสแกนไม่สำเร็จ (เช่น ไม่มี AgentTray รันอยู่) - ส่งเหตุผลกลับไปด้วย</summary>
    public Task RelayProcessListFailed(string consoleSessionId, string clientGuid, string reason) =>
        Clients.Group(consoleSessionId).SendAsync("ProcessListFailed", clientGuid, reason);

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
        // ผูกกับ connection ที่ลงทะเบียนไว้แล้วเท่านั้น (2026-08-31) - ต่างจาก RegisterAgent ตรงที่
        // ที่นี่ไม่ต้องเชื่อ argument ใดๆ เลย เพราะ AgentConnectionRegistry รู้อยู่แล้วว่า connection
        // นี้คือเครื่องไหน จึงตรวจ ownership ของ commandId ได้ตรงๆ โดยไม่ต้องอ่าน header ซ้ำ
        var ownerGuid = _connectionRegistry.GetClientGuid(Context.ConnectionId);
        if (ownerGuid is not null)
        {
            var commandOwner = await _db.ClientCommands
                .Where(c => c.Id == commandId)
                .Select(c => c.ClientGuid)
                .FirstOrDefaultAsync();

            if (commandOwner is not null && commandOwner != ownerGuid)
            {
                throw new HubException("Command does not belong to this agent.");
            }
        }
        else if (_agentAuth.IsEnforcing)
        {
            // connection ที่ไม่เคยเรียก RegisterAgent สำเร็จ ไม่มีสิทธิ์รายงานผลของ command ใดๆ
            throw new HubException("Agent is not registered on this connection.");
        }

        var updated = await CommandResultRecorder.RecordAsync(_db, commandId, success, message);

        if (updated is not null)
        {
            await Clients.Group(ConsoleGroupName).SendAsync("CommandResultChanged", CommandResultDto.From(updated));
        }
    }

    // ==================== "ส่งจอครู" (WP4-4, 2026-11-01) ====================
    // Console ครูเป็นคน capture+encode เฟรมเอง แล้วยิงเข้ามาที่ PushScreenFrame - Server ไม่รู้จัก/ไม่
    // ตรวจสอบเนื้อหาของเฟรมเลย (opaque byte[] ผ่านมือ เหมือน ProcessListReady) แค่รีเลย์ต่อให้ Agent ที่
    // join ไว้ (ScreenBroadcastRelay เป็นคนจัดการ drop stale frame ต่อ connection ช้า)
    //
    // ตั้งใจไม่ตรวจ auth เพิ่มที่นี่ (ต่างจาก RegisterAgent ที่ตรวจ secret): PushScreenFrame เรียกได้
    // จาก connection ที่ login Console สำเร็จแล้วเท่านั้นในทางปฏิบัติ (ครูเป็นคนกด "เริ่มส่งจอ" เอง) ส่วน
    // JoinScreenBroadcast/LeaveScreenBroadcast ผูกกับ connection ของ Agent โดยตรง (ไม่รับ guid จาก
    // argument ที่ปลอมได้ - ใช้ Context.ConnectionId ของ connection ที่เรียกเองเท่านั้น)

    /// <summary>Agent เรียกหลังได้รับ command "StartScreenBroadcast" - join เป็น viewer เพื่อรับ "ScreenFrame" ที่ครูส่งเข้ามา</summary>
    public Task JoinScreenBroadcast()
    {
        _screenBroadcastRelay.AddViewer(Context.ConnectionId);
        return Task.CompletedTask;
    }

    /// <summary>Agent เรียกหลังได้รับ command "StopScreenBroadcast" (หรือครูหยุดส่ง) - เลิกรับเฟรม</summary>
    public Task LeaveScreenBroadcast()
    {
        _screenBroadcastRelay.RemoveViewer(Context.ConnectionId);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Console (ครู) เรียกทุกครั้งที่ capture+encode เฟรมใหม่เสร็จ (5-10 ครั้ง/วิ) - รีเลย์ให้ Agent ที่
    /// join ไว้ทุกตัวทันที ไม่ await ให้ทุก connection ส่งจบก่อนจะ return (ดู ScreenBroadcastRelay.BroadcastFrame)
    /// </summary>
    public Task PushScreenFrame(byte[] jpegBytes)
    {
        _screenBroadcastRelay.BroadcastFrame(Clients, jpegBytes);
        return Task.CompletedTask;
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

        // การมีผลจริงกลับมา = พิสูจน์แล้วว่า command ถึงเครื่องปลายทางจริง ต้องปิดสถานะ "ส่งมอบ"
        // ด้วยเสมอ (2026-09-01)
        //
        // บั๊กที่แก้: เดิมมีแต่ ack (POST /api/commands/{id}/ack) เท่านั้นที่ปิด Status ให้ ถ้า ack
        // ตัวนั้นไม่ถึง Server ด้วยเหตุผลใดก็ตาม - และฝั่ง Agent ไม่เคยเช็ค status code ของ ack เลย
        // จึงไม่มีใครรู้ว่ามันหลุด - command จะค้างเป็น Pending ตลอดไป ทั้งที่ครูเห็นในหน้ารายงานว่า
        // "สำเร็จ" เรียบร้อยแล้ว ผลคือหน้า PENDING COMMANDS โชว์คำสั่งที่ทำเสร็จไปแล้วค้างไว้ไม่หาย
        // และ /api/poll ก็ส่ง command เดิมกลับไปให้ Agent ซ้ำทุก 30 วิไม่จบ
        //
        // วางไว้ก่อน early-return ด้านล่างโดยตั้งใจ: แถวที่ค้างอยู่แล้วในสนามจะได้ถูกซ่อมด้วยเมื่อมี
        // รายงานผลซ้ำเข้ามา ไม่ใช่ซ่อมเฉพาะแถวใหม่
        if (command.Status == ClientCommandStatus.Pending)
        {
            command.Status = ClientCommandStatus.Delivered;
            command.DeliveredAt ??= DateTime.UtcNow;
            await db.SaveChangesAsync();
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
