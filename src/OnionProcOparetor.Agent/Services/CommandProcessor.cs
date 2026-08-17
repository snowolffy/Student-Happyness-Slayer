using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using OnionProcOparetor.Agent.Models;

namespace OnionProcOparetor.Agent.Services;

/// <summary>
/// Logic กลางสำหรับประมวลผล command ที่ได้จาก server - ไม่ว่าจะมาทาง SignalR ReceiveCommand
/// (real-time) หรือทาง pendingCommands จาก poll response (fallback) ก็เรียกจุดนี้จุดเดียวกัน
/// ไม่ให้ logic แยกกันคนละที่
///
/// ตอนนี้ยังเป็นแค่โครง (Phase 3/4 ยังไม่เริ่ม implement command จริง) - รู้จัก command แล้ว log ไว้เฉยๆ
/// </summary>
public class CommandProcessor
{
    // หมายเหตุ: const PowerActionWarningDelaySeconds = 10 เดิมถูกย้ายไปเป็น
    // PowerActionPayload.DefaultDelaySeconds แล้ว - ตอนนี้ค่าหน่วงมาจาก payload ที่ครูตั้งจาก
    // Console เป็นหลัก (1-120 วิ) ค่า default เดิมใช้เฉพาะตอนไม่มี payload มาด้วยเท่านั้น

    private readonly ILogger<CommandProcessor> _logger;
    private readonly ServerClient _serverClient;
    private readonly AgentTrayNotifier _agentTrayNotifier;
    private readonly ClientState _clientState;
    private readonly VncSessionManager _vncSessionManager;

    // ดึง LabHubConnection แบบ lazy ผ่าน IServiceProvider แทน constructor injection ตรงๆ เพราะ
    // LabHubConnection เองก็ต้องพึ่ง CommandProcessor อยู่แล้ว (รับ "ReceiveCommand" แล้วส่งต่อมาที่นี่)
    // - inject ตรงๆ ทั้งคู่จะเกิด circular DI dependency ที่ container resolve ไม่ได้ตอน startup
    private readonly IServiceProvider _serviceProvider;

    // เก็บ commandId ที่ process ไปแล้วล่าสุด กันประมวลผลซ้ำตอน network race
    // (เช่น SignalR ReceiveCommand กับ poll loop เห็น command เดียวกันเกือบพร้อมกัน หรือ ExecuteAsync
    // loop กับ RunPeriodicProcessScanAsync loop เห็น pendingCommands ตัวเดียวกันพร้อมกัน)
    // ใช้ commandId เดียวกันได้ทั้ง 2 path แล้ว (Server ส่ง commandId มาด้วยทาง SignalR ด้วย)
    private const int MaxProcessedCommandIds = 50;
    private readonly object _processedLock = new();
    private readonly Queue<int> _processedCommandIdOrder = new();
    private readonly HashSet<int> _processedCommandIds = new();

    public CommandProcessor(
        ILogger<CommandProcessor> logger,
        ServerClient serverClient,
        AgentTrayNotifier agentTrayNotifier,
        ClientState clientState,
        VncSessionManager vncSessionManager,
        IServiceProvider serviceProvider)
    {
        _logger = logger;
        _serverClient = serverClient;
        _agentTrayNotifier = agentTrayNotifier;
        _clientState = clientState;
        _vncSessionManager = vncSessionManager;
        _serviceProvider = serviceProvider;
    }

    /// <summary>
    /// ประมวลผล command ที่ได้จาก pendingCommands (poll fallback) หรือจาก SignalR ReceiveCommand
    /// (ตอนนี้ทั้ง 2 ทางมี commandId เหมือนกันแล้ว เลยเรียก method เดียวกันนี้ได้ทั้งคู่)
    /// กัน process ซ้ำด้วย commandId ที่เคยเห็นแล้ว แล้ว ack กลับไปหา server เสมอ (ไม่ว่าจะเพิ่ง
    /// process จริงหรือข้ามเพราะซ้ำก็ตาม - เพื่อให้ server mark เป็น Delivered ไม่ส่งมาอีก)
    /// คืน true ถ้าเป็นครั้งแรกที่ process (ไม่ใช่ของซ้ำ)
    /// </summary>
    public async Task<bool> ProcessPendingCommandAsync(int commandId, string commandType, string? payloadJson, CancellationToken ct = default)
    {
        var isFirstTime = TryMarkAsProcessed(commandId);

        if (!isFirstTime)
        {
            _logger.LogDebug(
                "Command {CommandId} ({CommandType}) ถูก process ไปแล้วก่อนหน้านี้ - ข้าม (กัน duplicate จาก SignalR/poll race)",
                commandId, commandType);
        }
        else
        {
            await ProcessCommandAsync(commandType, payloadJson, ct);
        }

        // ack เสมอไม่ว่าจะเพิ่ง process จริงหรือข้ามเพราะซ้ำ - ฝั่ง server ทำ idempotent ไว้แล้ว เรียกซ้ำได้ปลอดภัย
        await _serverClient.AckCommandAsync(commandId, ct);

        return isFirstTime;
    }

    /// <summary>
    /// ประมวลผล command จริง (ไม่ dedup ไม่ ack) - เรียกจาก ProcessPendingCommandAsync เท่านั้น
    /// (ทั้ง SignalR ReceiveCommand และ poll fallback เข้าทาง ProcessPendingCommandAsync เหมือนกันหมดแล้ว)
    /// </summary>
    public Task ProcessCommandAsync(string commandType, string? payloadJson, CancellationToken ct = default)
    {
        switch (commandType)
        {
            case "BroadcastMessage":
                return HandleBroadcastMessageAsync(payloadJson, ct);

            case "LockWorkstation":
                return HandleShowLockScreenAsync(ct);

            case "UnlockWorkstation":
                return HandleHideLockScreenAsync(ct);

            case "Shutdown":
                return HandlePowerActionAsync(isRestart: false, payloadJson, ct);

            case "Restart":
                return HandlePowerActionAsync(isRestart: true, payloadJson, ct);

            case "UpdateSettings":
                return HandleUpdateSettingsAsync(payloadJson, ct);

            case "StartVncSession":
                return HandleStartVncSessionAsync(payloadJson, ct);

            case "BlockTargetInput":
                return HandleBlockTargetInputAsync(ct);

            case "UnblockTargetInput":
                return HandleUnblockTargetInputAsync(ct);

            // TODO Phase 3/4: ใส่ command อื่นๆ ตรงนี้เพิ่ม เช่น "Kill", "RunCommand"
            default:
                _logger.LogWarning(
                    "ได้รับ command ชนิดที่ยังไม่รู้จัก: '{CommandType}' (payload: {Payload}) - ยังไม่ implement (Phase 3/4)",
                    commandType, payloadJson);
                break;
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// ครูพิมพ์ข้อความจาก Console ส่งมาโชว์เป็น popup บนเครื่อง client - payload คาดว่าเป็น
    /// { "message": string, "title": string? } ส่งต่อให้ AgentTray แสดงผลจริง (ดู AgentTrayNotifier)
    /// </summary>
    private async Task HandleBroadcastMessageAsync(string? payloadJson, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            _logger.LogWarning("BroadcastMessage command ไม่มี payload มาด้วย - ข้าม");
            return;
        }

        BroadcastMessagePayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<BroadcastMessagePayload>(
                payloadJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "แปลง payload ของ BroadcastMessage ไม่สำเร็จ: {Payload}", payloadJson);
            return;
        }

        if (payload is null || string.IsNullOrWhiteSpace(payload.Message))
        {
            _logger.LogWarning("BroadcastMessage payload ไม่มีข้อความ - ข้าม");
            return;
        }

        _logger.LogInformation("ได้รับ BroadcastMessage: '{Message}' (title: {Title})", payload.Message, payload.Title);
        await _agentTrayNotifier.SendBroadcastMessageAsync(payload, ct);
    }

    /// <summary>
    /// สั่งแสดงหน้าจอล็อกเต็มจอ - ต้องส่งผ่าน AgentTray เสมอ (Agent เอง Session 0 ไม่มี desktop
    /// ให้แสดง window ได้เลย - ปัญหาเดียวกับตอน BroadcastMessage ที่ MessageBox.Show ตรงจาก Service
    /// ไม่โชว์ให้ใครเห็น) หน้าจอล็อกนี้ปลดได้ทางเดียวคือ UnlockWorkstation command จาก Console เท่านั้น
    /// (ไม่ใช่แค่ LockWorkStation() ธรรมดาเหมือนกด Win+L ที่ user ปลดเองด้วย password ตัวเองได้)
    /// </summary>
    private async Task HandleShowLockScreenAsync(CancellationToken ct)
    {
        _logger.LogInformation("ได้รับ LockWorkstation command - ส่งต่อให้ AgentTray แสดงหน้าจอล็อกเต็มจอ");
        await _agentTrayNotifier.SendShowLockScreenAsync(ct);
    }

    /// <summary>สั่งปิดหน้าจอล็อก - ครูกดปลดจาก Console เท่านั้น (ไม่มี local password/timeout ฝั่ง client)</summary>
    private async Task HandleHideLockScreenAsync(CancellationToken ct)
    {
        _logger.LogInformation("ได้รับ UnlockWorkstation command - ส่งต่อให้ AgentTray ปิดหน้าจอล็อก");
        await _agentTrayNotifier.SendHideLockScreenAsync(ct);
    }

    /// <summary>ครูกด "BLOCK STUDENT INPUT" ระหว่าง full-screen VNC - ส่งต่อให้ AgentTray ติดตั้ง input hook จริง (ดู PhysicalInputBlocker)</summary>
    private async Task HandleBlockTargetInputAsync(CancellationToken ct)
    {
        _logger.LogInformation("ได้รับ BlockTargetInput command - ส่งต่อให้ AgentTray บล็อก physical input");
        await _agentTrayNotifier.SendBlockTargetInputAsync(ct);
    }

    /// <summary>ครูกด "UNBLOCK STUDENT INPUT" หรือออกจาก full-screen - ส่งต่อให้ AgentTray คืนสิทธิ์ physical input</summary>
    private async Task HandleUnblockTargetInputAsync(CancellationToken ct)
    {
        _logger.LogInformation("ได้รับ UnblockTargetInput command - ส่งต่อให้ AgentTray คืนสิทธิ์ physical input");
        await _agentTrayNotifier.SendUnblockTargetInputAsync(ct);
    }

    /// <summary>
    /// ครู (Console) เปลี่ยนค่า setting ของเครื่องนี้ (ตอนนี้มีแค่ PollIntervalOverrideSeconds) แล้วอยาก
    /// ให้มีผลทันทีโดยไม่ต้องรอ poll รอบถัดไป - อัปเดต ClientState ตรงๆ (Worker อ่านค่านี้ไปคำนวณ
    /// interval รอบถัดไปทุกครั้ง) DB (ClientMachine.PollIntervalOverrideSeconds) ถูกเขียนแยกจาก
    /// Console ผ่าน endpoint เดิมอยู่แล้วเสมอ (เป็น poll safety-net เผื่อเครื่องนี้ไม่ได้ connect
    /// SignalR อยู่ตอนที่ครูกดส่ง - จะเห็นค่าใหม่ตอน poll รอบถัดไปเหมือนเดิม)
    /// </summary>
    private Task HandleUpdateSettingsAsync(string? payloadJson, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            _logger.LogWarning("UpdateSettings command ไม่มี payload มาด้วย - ข้าม");
            return Task.CompletedTask;
        }

        UpdateSettingsPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<UpdateSettingsPayload>(
                payloadJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "แปลง payload ของ UpdateSettings ไม่สำเร็จ: {Payload}", payloadJson);
            return Task.CompletedTask;
        }

        if (payload is null)
        {
            return Task.CompletedTask;
        }

        _logger.LogInformation(
            "ได้รับ UpdateSettings command - ตั้ง PollIntervalOverrideSeconds เป็น {Value} มีผลทันที (ไม่ต้องรอ poll รอบถัดไป)",
            payload.PollIntervalOverrideSeconds);
        _clientState.SetPollIntervalOverride(payload.PollIntervalOverrideSeconds);

        return Task.CompletedTask;
    }

    /// <summary>
    /// สั่ง AgentTray (ผ่าน named pipe) ให้ start winvnc.exe เอง - ต้องทำจาก AgentTray เท่านั้น (รันใน
    /// user session จริง) ไม่ใช่ Agent เอง (Session 0 - capture หน้าจอไม่ได้เลย ยืนยันจากการ debug จริง)
    /// method นี้แค่ "ยิงคำสั่งออกไป" เท่านั้น - ผลจริง (สำเร็จ/ไม่สำเร็จ) จะย้อนกลับมาทาง local HTTP
    /// callback /vnc/session-result (ดู Program.cs) แยกต่างหาก เพราะ AgentTray ต้อง start process จริง
    /// ซึ่งใช้เวลา ถ้า pipe ส่งไม่ถึง AgentTray เลย (เช่นไม่มี user login อยู่หน้าเครื่อง) ค่อย fail ทันที
    /// ตรงนี้เพราะรู้อยู่แล้วว่าไม่มีทางสำเร็จแน่นอน ไม่ต้องรอ callback ที่ไม่มีวันมา
    /// </summary>
    private async Task HandleStartVncSessionAsync(string? payloadJson, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            _logger.LogWarning("StartVncSession command ไม่มี payload มาด้วย (ต้องมี RequestingConsoleSessionId) - ข้าม");
            return;
        }

        StartVncSessionPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<StartVncSessionPayload>(
                payloadJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "แปลง payload ของ StartVncSession ไม่สำเร็จ: {Payload}", payloadJson);
            return;
        }

        if (payload is null || string.IsNullOrWhiteSpace(payload.RequestingConsoleSessionId))
        {
            _logger.LogWarning("StartVncSession payload ไม่มี RequestingConsoleSessionId - ข้าม (ไม่รู้จะตอบกลับใคร)");
            return;
        }

        _logger.LogInformation("ได้รับ StartVncSession command จาก Console session {SessionId} - กำลังสั่ง AgentTray ให้ start winvnc.exe", payload.RequestingConsoleSessionId);

        var sentToTray = await _agentTrayNotifier.SendStartVncSessionAsync(payload.RequestingConsoleSessionId, ct);
        if (!sentToTray)
        {
            var labHubConnection = _serviceProvider.GetRequiredService<LabHubConnection>();
            _logger.LogWarning("StartVncSession: ส่งคำสั่งให้ AgentTray ไม่สำเร็จ (อาจไม่มี user login อยู่หน้าเครื่อง) - แจ้งกลับ Console session {SessionId} ทันที", payload.RequestingConsoleSessionId);
            await labHubConnection.NotifyVncSessionFailedAsync(payload.RequestingConsoleSessionId, "ไม่สามารถเปิด VNC ได้ - ไม่มี user login อยู่หน้าเครื่องนี้ (ต้องมีคน login ถึงจะเปิด VNC ได้)", ct);
        }
    }

    /// <summary>
    /// เรียกจาก /vnc/session-result (local HTTP endpoint ใน Program.cs) หลัง AgentTray รายงานผลกลับมา
    /// - แยกจาก HandleStartVncSessionAsync เพราะผลจริงมาช้ากว่า command processing ปกติมาก (ต้องรอ
    /// AgentTray start process จริง) ไม่ผ่าน pipe/command queue เดิมเลย เป็น flow คู่ขนานที่ผูกกันด้วย
    /// RequestingConsoleSessionId เท่านั้น
    /// </summary>
    public async Task HandleVncSessionResultAsync(string requestingConsoleSessionId, bool success, int pid, string? password, string? failureReason, CancellationToken ct)
    {
        var labHubConnection = _serviceProvider.GetRequiredService<LabHubConnection>();

        if (!success || password is null)
        {
            _logger.LogWarning("VncSessionResult: AgentTray start winvnc.exe ไม่สำเร็จ ({Reason}) - แจ้งกลับ Console session {SessionId}", failureReason, requestingConsoleSessionId);
            await labHubConnection.NotifyVncSessionFailedAsync(requestingConsoleSessionId, failureReason ?? "เปิด VNC session บนเครื่องนี้ไม่สำเร็จ", ct);
            return;
        }

        var (portFound, port, portFailureReason) = await _vncSessionManager.HandleAgentTrayStartedAsync(pid, password, ct);
        if (!portFound)
        {
            await labHubConnection.NotifyVncSessionFailedAsync(requestingConsoleSessionId, portFailureReason ?? "เปิด VNC session บนเครื่องนี้ไม่สำเร็จ", ct);
            return;
        }

        await labHubConnection.NotifyVncSessionReadyAsync(requestingConsoleSessionId, port, password, ct);
    }

    /// <summary>
    /// Shutdown/Restart - เรียกจาก Agent (Session 0) ได้ตรงๆ เพราะเป็น system-level operation
    /// (ต่างจาก LockWorkstation ที่เป็น per-session) ส่งคำสั่งให้ AgentTray โชว์หน้าต่างนับถอยหลัง
    /// ก่อนเสมอ แล้วค่อย delay จริงแบบ fire-and-forget - method นี้ return ทันทีหลังส่งเสร็จ
    /// เพื่อไม่ block ProcessPendingCommandAsync จาก ack/ประมวลผล command อื่นที่เข้ามาระหว่างรอ
    ///
    /// เวลาหน่วงมาจาก payload { "delaySeconds": int } ที่ Console ส่งมา (validate ซ้ำที่นี่เสมอ)
    /// ถ้าไม่มี payload มาด้วย (Console เวอร์ชันเก่า / command เก่าค้างใน DB) ใช้ 10 วิเท่าเดิม
    /// </summary>
    private async Task HandlePowerActionAsync(bool isRestart, string? payloadJson, CancellationToken ct)
    {
        var actionName = isRestart ? "Restart" : "Shutdown";
        var delaySeconds = ResolvePowerActionDelaySeconds(payloadJson, actionName);

        _logger.LogInformation("ได้รับ {Action} command - เตือน user ก่อน {DelaySeconds} วินาที", actionName, delaySeconds);

        // ส่งให้ Tray "ก่อน" แล้วค่อยเริ่มจับเวลาจริง - ดู DelayThenExitWindowsAsync เรื่อง drift
        await _agentTrayNotifier.SendShutdownNotifyAsync(isRestart, delaySeconds, ct);

        // fire-and-forget โดยตั้งใจ - ไม่ await ตรงนี้ กัน command อื่นที่มาระหว่างรอถูก block
        _ = DelayThenExitWindowsAsync(isRestart, delaySeconds, ct);
    }

    /// <summary>
    /// อ่าน DelaySeconds จาก payload แล้ว clamp ให้อยู่ในช่วง 1-120 เสมอ (defense-in-depth ชั้นที่ 2
    /// ต่อจาก Console - POST /api/commands ไม่ validate payload shape เลย ใครยิง HTTP ตรงเข้ามา
    /// ก็ใส่ค่าอะไรก็ได้ เช่น 0 (ดับทันทีไม่เตือน) หรือ 86400 (ค้างรอทั้งวัน))
    /// payload ที่หาย/พังทุกกรณี → ใช้ค่า default เดิม 10 วิ ไม่ throw ไม่ยกเลิก command
    /// </summary>
    private int ResolvePowerActionDelaySeconds(string? payloadJson, string actionName)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            _logger.LogInformation(
                "{Action} command ไม่มี payload มาด้วย - ใช้เวลาหน่วง default {DelaySeconds} วิ (พฤติกรรมเดิมก่อนมี DelaySeconds)",
                actionName, PowerActionPayload.DefaultDelaySeconds);
            return PowerActionPayload.DefaultDelaySeconds;
        }

        PowerActionPayload? payload = null;
        try
        {
            payload = JsonSerializer.Deserialize<PowerActionPayload>(
                payloadJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "แปลง payload ของ {Action} ไม่สำเร็จ: {Payload} - ใช้เวลาหน่วง default {DelaySeconds} วิแทน",
                actionName, payloadJson, PowerActionPayload.DefaultDelaySeconds);
        }

        var requested = payload?.DelaySeconds ?? PowerActionPayload.DefaultDelaySeconds;
        var clamped = PowerActionPayload.Clamp(requested);

        if (clamped != requested)
        {
            _logger.LogWarning(
                "{Action} command ขอเวลาหน่วง {Requested} วิ ซึ่งอยู่นอกช่วงที่ยอมรับ ({Min}-{Max}) - บีบเป็น {Clamped} วิ",
                actionName, requested, PowerActionPayload.MinDelaySeconds, PowerActionPayload.MaxDelaySeconds, clamped);
        }

        return clamped;
    }

    /// <summary>
    /// รอจนถึงเวลาที่กำหนดแล้วสั่ง ExitWindowsEx จริง
    ///
    /// ใช้ "deadline" (เวลาเป้าหมายสัมบูรณ์) แทน Task.Delay(ทั้งก้อน) ครั้งเดียว เพราะ Task.Delay
    /// รับประกันแค่ "ไม่ตื่นก่อนเวลา" ไม่ได้รับประกันว่าจะตื่น "ตรงเวลา" - บนเครื่องที่ CPU ตัน
    /// หรือ thread pool starve อยู่ (เครื่อง lab ที่กำลังโดน enforcement scan อยู่พอดี) อาจคลาด
    /// ไปหลายวินาที ซึ่งจะเห็นเป็น drift ชัดเจนเทียบกับ progress bar ฝั่ง Tray ที่นับตามนาฬิกาจริง
    /// วนรอเป็นช่วงสั้นๆ แล้วเช็คนาฬิกาจริงทุกครั้ง ทำให้ error สะสมไม่เกินรอบสุดท้ายรอบเดียว
    ///
    /// deadline ถูกตั้ง "หลัง" ส่ง pipe เสร็จแล้ว (ดู HandlePowerActionAsync) ฝั่ง Tray เริ่มนับ
    /// ตอนรับ message ซึ่งเกิดก่อนบรรทัดนี้เสมอ - แปลว่า progress bar จะแตะ 0 ก่อนเครื่องดับเล็กน้อย
    /// (หลัก ms) ไม่มีทางที่ user เห็นเลข 0 ค้างแล้วเครื่องยังไม่ดับ ซึ่งเป็นทิศทางที่ถูกต้อง
    /// </summary>
    private async Task DelayThenExitWindowsAsync(bool isRestart, int delaySeconds, CancellationToken ct)
    {
        var deadlineUtc = DateTime.UtcNow.AddSeconds(delaySeconds);

        try
        {
            while (true)
            {
                var remaining = deadlineUtc - DateTime.UtcNow;
                if (remaining <= TimeSpan.Zero)
                {
                    break;
                }

                // ตื่นถี่พอที่จะไม่เลยเวลาเกิน ~250ms แต่ไม่ถี่จนเปลือง CPU ระหว่างรอนานสุด 120 วิ
                var step = remaining < TimeSpan.FromMilliseconds(250) ? remaining : TimeSpan.FromMilliseconds(250);
                await Task.Delay(step, ct);
            }
        }
        catch (OperationCanceledException)
        {
            return; // service กำลังหยุด - ไม่ต้อง shutdown เครื่องต่อ
        }

        var actionName = isRestart ? "Restart" : "Shutdown";
        var success = WindowsPowerControl.ExitWindows(isRestart);

        if (success)
        {
            _logger.LogInformation("เรียก ExitWindowsEx สำเร็จ ({Action})", actionName);
        }
        else
        {
            _logger.LogError("เรียก ExitWindowsEx ไม่สำเร็จ ({Action}) - อาจเปิด SE_SHUTDOWN_NAME privilege ไม่ได้", actionName);
        }
    }

    private bool TryMarkAsProcessed(int commandId)
    {
        lock (_processedLock)
        {
            if (!_processedCommandIds.Add(commandId))
            {
                return false; // เคย process ไปแล้ว
            }

            _processedCommandIdOrder.Enqueue(commandId);
            while (_processedCommandIdOrder.Count > MaxProcessedCommandIds)
            {
                var oldest = _processedCommandIdOrder.Dequeue();
                _processedCommandIds.Remove(oldest);
            }

            return true;
        }
    }
}
