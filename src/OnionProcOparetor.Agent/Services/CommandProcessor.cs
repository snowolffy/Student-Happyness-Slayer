using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
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
    private readonly CommandResultReporter _resultReporter;

    /// <summary>ใช้เปิด URL/โปรแกรมใน session ของ user ที่ login อยู่หน้าเครื่อง (WP4-1, 2026-11-01) - ดู HandleOpenUrlAsync/HandleLaunchProgramAsync</summary>
    private readonly UserSessionLauncher _userSessionLauncher;

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
        IServiceProvider serviceProvider,
        CommandResultReporter resultReporter,
        UserSessionLauncher userSessionLauncher)
    {
        _logger = logger;
        _serverClient = serverClient;
        _agentTrayNotifier = agentTrayNotifier;
        _clientState = clientState;
        _vncSessionManager = vncSessionManager;
        _serviceProvider = serviceProvider;
        _resultReporter = resultReporter;
        _userSessionLauncher = userSessionLauncher;
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
        CommandExecutionResult result;

        if (!isFirstTime)
        {
            _logger.LogDebug(
                "Command {CommandId} ({CommandType}) ถูก process ไปแล้วก่อนหน้านี้ - ข้าม (กัน duplicate จาก SignalR/poll race)",
                commandId, commandType);

            // ของซ้ำ = เคยรายงานผลจริงไปแล้วรอบก่อน ฝั่ง Server ก็ไม่เขียนทับผลเดิมอยู่แล้ว
            // (CommandResultRecorder เป็น idempotent) เลยไม่ต้องรายงานซ้ำ - แค่ ack ให้ครบ
            result = CommandExecutionResult.Ok("ข้ามเพราะเป็น command ซ้ำ (รายงานผลไปแล้วก่อนหน้านี้)");
        }
        else
        {
            result = await ExecuteAndCaptureResultAsync(commandId, commandType, payloadJson, ct);
        }

        // ack เสมอไม่ว่าจะเพิ่ง process จริงหรือข้ามเพราะซ้ำ - ฝั่ง server ทำ idempotent ไว้แล้ว เรียกซ้ำได้ปลอดภัย
        // (ack = "ได้รับแล้ว" คนละเรื่องกับผลจริงด้านล่าง - ตัวนี้เท่านั้นที่หยุดไม่ให้ poll ส่ง command ซ้ำ)
        await _serverClient.AckCommandAsync(commandId, ct);

        if (isFirstTime)
        {
            await _resultReporter.ReportAsync(commandId, result.Success, result.Message, ct);
        }

        return isFirstTime;
    }

    /// <summary>
    /// เรียก handler จริงแล้วดักทุก exception ให้กลายเป็นผลลัพธ์ Failed พร้อมข้อความ - สำคัญมาก
    /// เพราะถ้าปล่อย exception หลุดออกไป Agent จะไม่ได้รายงานผลใดๆ กลับไปเลย แล้ว Server จะ
    /// mark เป็น TimedOut ทั้งที่จริงๆ เรารู้สาเหตุที่ล้มเหลวอยู่แล้ว (ครูควรได้เห็นข้อความนั้น)
    /// </summary>
    private async Task<CommandExecutionResult> ExecuteAndCaptureResultAsync(
        int commandId, string commandType, string? payloadJson, CancellationToken ct)
    {
        try
        {
            return await ProcessCommandAsync(commandType, payloadJson, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "ประมวลผล command {CommandId} ('{CommandType}') ล้มเหลวด้วย exception", commandId, commandType);

            return CommandExecutionResult.Fail($"{ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// ประมวลผล command จริง (ไม่ dedup ไม่ ack) - เรียกจาก ProcessPendingCommandAsync เท่านั้น
    /// (ทั้ง SignalR ReceiveCommand และ poll fallback เข้าทาง ProcessPendingCommandAsync เหมือนกันหมดแล้ว)
    /// คืนผลจริงของ action เพื่อให้ผู้เรียกรายงานกลับ Server ได้
    /// </summary>
    public Task<CommandExecutionResult> ProcessCommandAsync(string commandType, string? payloadJson, CancellationToken ct = default)
    {
        switch (commandType)
        {
            case "BroadcastMessage":
                return HandleBroadcastMessageAsync(payloadJson, ct);

            case "LockWorkstation":
                return HandleShowLockScreenAsync(payloadJson, ct);

            case "UnlockWorkstation":
                return HandleHideLockScreenAsync(ct);

            case "Shutdown":
                return HandlePowerActionAsync(isRestart: false, payloadJson: payloadJson, ct);

            case "Restart":
                return HandlePowerActionAsync(isRestart: true, payloadJson: payloadJson, ct);

            case "UpdateSettings":
                return HandleUpdateSettingsAsync(payloadJson, ct);

            case "StartVncSession":
                return HandleStartVncSessionAsync(payloadJson, ct);

            case "BlockTargetInput":
                return HandleBlockTargetInputAsync(ct);

            case "UnblockTargetInput":
                return HandleUnblockTargetInputAsync(ct);

            case "GetRunningProcesses":
                return HandleGetRunningProcessesAsync(payloadJson, ct);

            case "SetTailscaleLockdown":
                return HandleSetTailscaleLockdownAsync(payloadJson, ct);

            case "RunTailscaleLogin":
                return HandleRunTailscaleLoginAsync(payloadJson, ct);

            case "GetTailscaleStatus":
                return HandleGetTailscaleStatusAsync(ct);

            case "OpenUrl":
                return HandleOpenUrlAsync(payloadJson, ct);

            case "LaunchProgram":
                return HandleLaunchProgramAsync(payloadJson, ct);

            // TODO Phase 3/4: ใส่ command อื่นๆ ตรงนี้เพิ่ม เช่น "Kill", "RunCommand"
            default:
                _logger.LogWarning(
                    "ได้รับ command ชนิดที่ยังไม่รู้จัก: '{CommandType}' (payload: {Payload}) - ยังไม่ implement (Phase 3/4)",
                    commandType, payloadJson);

                // รายงานเป็น Failed ไม่ใช่ Ok - ครูสั่งอะไรบางอย่างไปแล้วเครื่องนี้ทำไม่ได้จริงๆ
                // (เช่น Console เวอร์ชันใหม่กว่า Agent) ต้องเห็นในหน้า report ไม่ใช่เงียบหายไป
                return Task.FromResult(CommandExecutionResult.Fail(
                    $"Agent เวอร์ชันนี้ไม่รู้จัก command '{commandType}' (อาจต้องอัปเดต Agent ให้ตรงกับ Console)"));
        }
    }

    /// <summary>
    /// ครูพิมพ์ข้อความจาก Console ส่งมาโชว์เป็น popup บนเครื่อง client - payload คาดว่าเป็น
    /// { "message": string, "title": string? } ส่งต่อให้ AgentTray แสดงผลจริง (ดู AgentTrayNotifier)
    /// </summary>
    private async Task<CommandExecutionResult> HandleBroadcastMessageAsync(string? payloadJson, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            _logger.LogWarning("BroadcastMessage command ไม่มี payload มาด้วย - ข้าม");
            return CommandExecutionResult.Fail("BroadcastMessage command ไม่มี payload มาด้วย");
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
            return CommandExecutionResult.Fail($"payload ของ BroadcastMessage ไม่ถูกต้อง: {ex.Message}");
        }

        if (payload is null || string.IsNullOrWhiteSpace(payload.Message))
        {
            _logger.LogWarning("BroadcastMessage payload ไม่มีข้อความ - ข้าม");
            return CommandExecutionResult.Fail("BroadcastMessage payload ไม่มีข้อความ");
        }

        _logger.LogInformation("ได้รับ BroadcastMessage: '{Message}' (title: {Title})", payload.Message, payload.Title);

        var sent = await _agentTrayNotifier.SendBroadcastMessageAsync(payload, ct);

        return sent
            ? CommandExecutionResult.Ok()
            : CommandExecutionResult.Fail(TrayUnreachableMessage("แสดงข้อความ"));
    }

    /// <summary>
    /// ข้อความอธิบายกรณีที่ส่งให้ AgentTray ไม่สำเร็จ - รวมไว้ที่เดียวเพราะครูจะเห็นข้อความนี้
    /// ในหน้า report บ่อยที่สุด (เป็นสาเหตุความล้มเหลวที่พบบ่อยที่สุดจริงๆ: เครื่องเปิดอยู่
    /// Agent ทำงานปกติ แต่ไม่มีใคร login อยู่หน้าเครื่อง เลยไม่มี Tray ให้แสดงผล)
    /// </summary>
    private static string TrayUnreachableMessage(string whatFailed) =>
        $"ติดต่อ AgentTray บนเครื่องนี้ไม่ได้ จึง{whatFailed}ไม่ได้ (อาจไม่มี user login อยู่หน้าเครื่อง หรือ Tray ถูกปิดไป)";

    /// <summary>
    /// สั่งแสดงหน้าจอล็อกเต็มจอ - ต้องส่งผ่าน AgentTray เสมอ (Agent เอง Session 0 ไม่มี desktop
    /// ให้แสดง window ได้เลย - ปัญหาเดียวกับตอน BroadcastMessage ที่ MessageBox.Show ตรงจาก Service
    /// ไม่โชว์ให้ใครเห็น) หน้าจอล็อกนี้ปลดได้ทางเดียวคือ UnlockWorkstation command จาก Console เท่านั้น
    /// (ไม่ใช่แค่ LockWorkStation() ธรรมดาเหมือนกด Win+L ที่ user ปลดเองด้วย password ตัวเองได้)
    ///
    /// payload (WP3-3, 2026-10-31) - คาดว่าเป็น { "message": string? } ที่ Console ส่งมาจาก
    /// LockScreenMessageWindow (WP3-5) ครูเว้นว่าง = ใช้ข้อความเริ่มต้นจากหน้าตั้งค่า Sign-in แทน
    /// (ClientState.LockScreenDefaultMessage ที่ cache มาจาก poll - ดู SignInSettings.LockScreenDefaultMessage
    /// ฝั่ง Server) ตัดสินใจ "ข้อความสุดท้าย" ที่นี่จุดเดียว ก่อนส่งต่อให้ AgentTray แสดงผลตรงๆ
    /// </summary>
    private async Task<CommandExecutionResult> HandleShowLockScreenAsync(string? payloadJson, CancellationToken ct)
    {
        string? overrideMessage = null;
        if (!string.IsNullOrWhiteSpace(payloadJson))
        {
            try
            {
                var payload = JsonSerializer.Deserialize<LockWorkstationPayload>(
                    payloadJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                overrideMessage = payload?.Message;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "แปลง payload ของ LockWorkstation ไม่สำเร็จ: {Payload} - ใช้ข้อความเริ่มต้นแทน", payloadJson);
            }
        }

        var resolvedMessage = string.IsNullOrWhiteSpace(overrideMessage) ? _clientState.LockScreenDefaultMessage : overrideMessage;

        _logger.LogInformation("ได้รับ LockWorkstation command - ส่งต่อให้ AgentTray แสดงหน้าจอล็อกเต็มจอ (ข้อความ{Source})",
            string.IsNullOrWhiteSpace(overrideMessage) ? "เริ่มต้นจากหน้าตั้งค่า" : "ที่ครูพิมพ์ทับเฉพาะครั้งนี้");

        var sent = await _agentTrayNotifier.SendShowLockScreenAsync(resolvedMessage, ct);

        return sent
            ? CommandExecutionResult.Ok()
            : CommandExecutionResult.Fail(TrayUnreachableMessage("ล็อกหน้าจอ"));
    }

    /// <summary>สั่งปิดหน้าจอล็อก - ครูกดปลดจาก Console เท่านั้น (ไม่มี local password/timeout ฝั่ง client)</summary>
    private async Task<CommandExecutionResult> HandleHideLockScreenAsync(CancellationToken ct)
    {
        _logger.LogInformation("ได้รับ UnlockWorkstation command - ส่งต่อให้ AgentTray ปิดหน้าจอล็อก");

        var sent = await _agentTrayNotifier.SendHideLockScreenAsync(ct);

        return sent
            ? CommandExecutionResult.Ok()
            : CommandExecutionResult.Fail(TrayUnreachableMessage("ปลดล็อกหน้าจอ"));
    }

    /// <summary>ครูกด "BLOCK STUDENT INPUT" ระหว่าง full-screen VNC - ส่งต่อให้ AgentTray ติดตั้ง input hook จริง (ดู PhysicalInputBlocker)</summary>
    private async Task<CommandExecutionResult> HandleBlockTargetInputAsync(CancellationToken ct)
    {
        _logger.LogInformation("ได้รับ BlockTargetInput command - ส่งต่อให้ AgentTray บล็อก physical input");

        var sent = await _agentTrayNotifier.SendBlockTargetInputAsync(ct);

        return sent
            ? CommandExecutionResult.Ok()
            : CommandExecutionResult.Fail(TrayUnreachableMessage("บล็อก physical input"));
    }

    /// <summary>ครูกด "UNBLOCK STUDENT INPUT" หรือออกจาก full-screen - ส่งต่อให้ AgentTray คืนสิทธิ์ physical input</summary>
    private async Task<CommandExecutionResult> HandleUnblockTargetInputAsync(CancellationToken ct)
    {
        _logger.LogInformation("ได้รับ UnblockTargetInput command - ส่งต่อให้ AgentTray คืนสิทธิ์ physical input");

        var sent = await _agentTrayNotifier.SendUnblockTargetInputAsync(ct);

        return sent
            ? CommandExecutionResult.Ok()
            : CommandExecutionResult.Fail(TrayUnreachableMessage("คืนสิทธิ์ physical input"));
    }

    /// <summary>
    /// ครู (Console) เปลี่ยนค่า setting ของเครื่องนี้ (ตอนนี้มีแค่ PollIntervalOverrideSeconds) แล้วอยาก
    /// ให้มีผลทันทีโดยไม่ต้องรอ poll รอบถัดไป - อัปเดต ClientState ตรงๆ (Worker อ่านค่านี้ไปคำนวณ
    /// interval รอบถัดไปทุกครั้ง) DB (ClientMachine.PollIntervalOverrideSeconds) ถูกเขียนแยกจาก
    /// Console ผ่าน endpoint เดิมอยู่แล้วเสมอ (เป็น poll safety-net เผื่อเครื่องนี้ไม่ได้ connect
    /// SignalR อยู่ตอนที่ครูกดส่ง - จะเห็นค่าใหม่ตอน poll รอบถัดไปเหมือนเดิม)
    /// </summary>
    private Task<CommandExecutionResult> HandleUpdateSettingsAsync(string? payloadJson, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            _logger.LogWarning("UpdateSettings command ไม่มี payload มาด้วย - ข้าม");
            return Task.FromResult(CommandExecutionResult.Fail("UpdateSettings command ไม่มี payload มาด้วย"));
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
            return Task.FromResult(CommandExecutionResult.Fail($"payload ของ UpdateSettings ไม่ถูกต้อง: {ex.Message}"));
        }

        if (payload is null)
        {
            return Task.FromResult(CommandExecutionResult.Fail("payload ของ UpdateSettings ว่างเปล่า"));
        }

        _logger.LogInformation(
            "ได้รับ UpdateSettings command - ตั้ง PollIntervalOverrideSeconds เป็น {Value} มีผลทันที (ไม่ต้องรอ poll รอบถัดไป)",
            payload.PollIntervalOverrideSeconds);
        _clientState.SetPollIntervalOverride(payload.PollIntervalOverrideSeconds);

        return Task.FromResult(CommandExecutionResult.Ok());
    }

    /// <summary>
    /// สั่ง AgentTray (ผ่าน named pipe) ให้ start winvnc.exe เอง - ต้องทำจาก AgentTray เท่านั้น (รันใน
    /// user session จริง) ไม่ใช่ Agent เอง (Session 0 - capture หน้าจอไม่ได้เลย ยืนยันจากการ debug จริง)
    /// method นี้แค่ "ยิงคำสั่งออกไป" เท่านั้น - ผลจริง (สำเร็จ/ไม่สำเร็จ) จะย้อนกลับมาทาง local HTTP
    /// callback /vnc/session-result (ดู Program.cs) แยกต่างหาก เพราะ AgentTray ต้อง start process จริง
    /// ซึ่งใช้เวลา ถ้า pipe ส่งไม่ถึง AgentTray เลย (เช่นไม่มี user login อยู่หน้าเครื่อง) ค่อย fail ทันที
    /// ตรงนี้เพราะรู้อยู่แล้วว่าไม่มีทางสำเร็จแน่นอน ไม่ต้องรอ callback ที่ไม่มีวันมา
    /// </summary>
    private async Task<CommandExecutionResult> HandleStartVncSessionAsync(string? payloadJson, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            _logger.LogWarning("StartVncSession command ไม่มี payload มาด้วย (ต้องมี RequestingConsoleSessionId) - ข้าม");
            return CommandExecutionResult.Fail("StartVncSession command ไม่มี payload มาด้วย");
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
            return CommandExecutionResult.Fail($"payload ของ StartVncSession ไม่ถูกต้อง: {ex.Message}");
        }

        if (payload is null || string.IsNullOrWhiteSpace(payload.RequestingConsoleSessionId))
        {
            _logger.LogWarning("StartVncSession payload ไม่มี RequestingConsoleSessionId - ข้าม (ไม่รู้จะตอบกลับใคร)");
            return CommandExecutionResult.Fail("StartVncSession payload ไม่มี RequestingConsoleSessionId");
        }

        _logger.LogInformation("ได้รับ StartVncSession command จาก Console session {SessionId} - กำลังสั่ง AgentTray ให้ start winvnc.exe", payload.RequestingConsoleSessionId);

        var sentToTray = await _agentTrayNotifier.SendStartVncSessionAsync(payload.RequestingConsoleSessionId, ct);
        if (!sentToTray)
        {
            var labHubConnection = _serviceProvider.GetRequiredService<LabHubConnection>();
            _logger.LogWarning("StartVncSession: ส่งคำสั่งให้ AgentTray ไม่สำเร็จ (อาจไม่มี user login อยู่หน้าเครื่อง) - แจ้งกลับ Console session {SessionId} ทันที", payload.RequestingConsoleSessionId);
            await labHubConnection.NotifyVncSessionFailedAsync(payload.RequestingConsoleSessionId, "ไม่สามารถเปิด VNC ได้ - ไม่มี user login อยู่หน้าเครื่องนี้ (ต้องมีคน login ถึงจะเปิด VNC ได้)", ct);
            return CommandExecutionResult.Fail(TrayUnreachableMessage("เปิด VNC session"));
        }

        // ผลจริงของ VNC (สำเร็จ/ไม่สำเร็จ) มาทาง flow คู่ขนาน HandleVncSessionResultAsync แยกต่างหาก
        // (รอ AgentTray start process จริง ซึ่งช้ากว่า command processing ปกติมาก - ดู doc comment ด้านบน)
        // Ok ตรงนี้แปลว่า "ส่งคำสั่งถึง AgentTray แล้ว" เท่านั้น เหมือน pattern ของ HandlePowerActionAsync
        return CommandExecutionResult.Ok("ส่งคำสั่งเปิด VNC session ให้ AgentTray แล้ว (รอผลจริงแยกต่างหาก ผ่าน SignalR VncSession ready/failed)");
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
    /// สั่ง AgentTray (ผ่าน named pipe) ให้สแกนโปรแกรมที่มีหน้าต่างอยู่บน desktop จริง - ต้องทำจาก
    /// AgentTray เท่านั้น (รันใน user session จริง) เพราะ Process.MainWindowHandle/MainWindowTitle
    /// ของ Agent เอง (Session 0) มองไม่เห็นหน้าต่างของ user session เลย เหตุผลเดียวกับ StartVncSession
    /// (ดู comment เต็มใน AgentTrayPipeMessage) ผลจริง (list ของ process) ย้อนกลับมาทาง local HTTP
    /// callback /processes/list-result แยกต่างหาก เพราะสแกน+เช็ค publisher ทุก process ใช้เวลาสักครู่
    /// </summary>
    private async Task<CommandExecutionResult> HandleGetRunningProcessesAsync(string? payloadJson, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            _logger.LogWarning("GetRunningProcesses command ไม่มี payload มาด้วย (ต้องมี RequestingConsoleSessionId) - ข้าม");
            return CommandExecutionResult.Fail("GetRunningProcesses command ไม่มี payload มาด้วย");
        }

        StartVncSessionPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<StartVncSessionPayload>(
                payloadJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "แปลง payload ของ GetRunningProcesses ไม่สำเร็จ: {Payload}", payloadJson);
            return CommandExecutionResult.Fail($"payload ของ GetRunningProcesses ไม่ถูกต้อง: {ex.Message}");
        }

        if (payload is null || string.IsNullOrWhiteSpace(payload.RequestingConsoleSessionId))
        {
            _logger.LogWarning("GetRunningProcesses payload ไม่มี RequestingConsoleSessionId - ข้าม (ไม่รู้จะตอบกลับใคร)");
            return CommandExecutionResult.Fail("GetRunningProcesses payload ไม่มี RequestingConsoleSessionId");
        }

        _logger.LogInformation("ได้รับ GetRunningProcesses command จาก Console session {SessionId} - กำลังสั่ง AgentTray ให้สแกน process", payload.RequestingConsoleSessionId);

        var sentToTray = await _agentTrayNotifier.SendGetRunningProcessesAsync(payload.RequestingConsoleSessionId, ct);
        if (!sentToTray)
        {
            var labHubConnection = _serviceProvider.GetRequiredService<LabHubConnection>();
            _logger.LogWarning("GetRunningProcesses: ส่งคำสั่งให้ AgentTray ไม่สำเร็จ (อาจไม่มี user login อยู่หน้าเครื่อง) - แจ้งกลับ Console session {SessionId} ทันที", payload.RequestingConsoleSessionId);
            await labHubConnection.NotifyProcessListFailedAsync(payload.RequestingConsoleSessionId, "ไม่สามารถดู process ได้ - ไม่มี user login อยู่หน้าเครื่องนี้", ct);
            return CommandExecutionResult.Fail(TrayUnreachableMessage("ดู process ที่รันอยู่"));
        }

        // ผลจริง (list ของ process) มาทาง flow คู่ขนาน HandleRunningProcessesResultAsync แยกต่างหาก
        return CommandExecutionResult.Ok("ส่งคำสั่งขอ process list ให้ AgentTray แล้ว (รอผลจริงแยกต่างหาก ผ่าน SignalR ProcessList ready/failed)");
    }

    /// <summary>
    /// เรียกจาก /processes/list-result (local HTTP endpoint ใน Program.cs) หลัง AgentTray สแกนเสร็จ
    /// - เป็น flow คู่ขนานผูกกันด้วย RequestingConsoleSessionId เหมือน HandleVncSessionResultAsync
    /// </summary>
    public async Task HandleRunningProcessesResultAsync(string requestingConsoleSessionId, bool success, string? processListJson, string? failureReason, CancellationToken ct)
    {
        var labHubConnection = _serviceProvider.GetRequiredService<LabHubConnection>();

        if (!success || processListJson is null)
        {
            _logger.LogWarning("RunningProcessesResult: AgentTray สแกน process ไม่สำเร็จ ({Reason}) - แจ้งกลับ Console session {SessionId}", failureReason, requestingConsoleSessionId);
            await labHubConnection.NotifyProcessListFailedAsync(requestingConsoleSessionId, failureReason ?? "สแกน process บนเครื่องนี้ไม่สำเร็จ", ct);
            return;
        }

        await labHubConnection.NotifyProcessListReadyAsync(requestingConsoleSessionId, processListJson, ct);
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
    private async Task<CommandExecutionResult> HandlePowerActionAsync(bool isRestart, string? payloadJson, CancellationToken ct)
    {
        var actionName = isRestart ? "Restart" : "Shutdown";
        var delaySeconds = ResolvePowerActionDelaySeconds(payloadJson, actionName);

        _logger.LogInformation("ได้รับ {Action} command - เตือน user ก่อน {DelaySeconds} วินาที", actionName, delaySeconds);

        // ส่งให้ Tray "ก่อน" แล้วค่อยเริ่มจับเวลาจริง - ดู DelayThenExitWindowsAsync เรื่อง drift
        var notifyShown = await _agentTrayNotifier.SendShutdownNotifyAsync(isRestart, delaySeconds, ct);

        // fire-and-forget โดยตั้งใจ - ไม่ await ตรงนี้ กัน command อื่นที่มาระหว่างรอถูก block
        _ = DelayThenExitWindowsAsync(isRestart, delaySeconds, ct);

        // **ความหมายของ Success สำหรับ Shutdown/Restart โดยเฉพาะ:** = "รับคำสั่งและตั้งเวลาดับ
        // เรียบร้อยแล้ว" ไม่ใช่ "เครื่องดับไปแล้วจริง" - รายงานว่าดับสำเร็จจริงเป็นไปไม่ได้ตาม
        // ธรรมชาติของ action นี้ เพราะตอนที่เครื่องดับ process นี้ก็ตายไปพร้อมกัน ไม่มีใครเหลือ
        // ให้รายงานอะไรกลับไปได้อีก (ครูดูจากสถานะ offline ของเครื่องใน Dashboard แทน)
        //
        // ยังคงส่งคำสั่งดับต่อแม้ Tray จะติดต่อไม่ได้ (fail-secure ตามดีไซน์เดิม - คำสั่งของครู
        // ต้องมีผลเสมอ ไม่ใช่ถูกยกเลิกเพราะไม่มีใคร login อยู่) แต่บอกครูให้รู้ว่าคำเตือนไม่ได้
        // ถูกแสดงให้ user เห็น ซึ่งเป็นข้อมูลที่ต่างกันมากเวลามีคนนั่งใช้เครื่องอยู่
        return notifyShown
            ? CommandExecutionResult.Ok($"ตั้งเวลา{actionName}ใน {delaySeconds} วินาที และแสดงคำเตือนให้ user แล้ว")
            : CommandExecutionResult.Ok(
                $"ตั้งเวลา{actionName}ใน {delaySeconds} วินาทีแล้ว แต่แสดงคำเตือนบนหน้าจอไม่ได้ " +
                "(ติดต่อ AgentTray ไม่ได้ - อาจไม่มี user login อยู่หน้าเครื่อง)");
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

    /// <summary>
    /// สลับ lock/unlock ของ Tailscale (AlwaysOn policy) ระยะไกลจาก Console - รันสคริปต์เดิม
    /// (set-tailscale-lockdown.ps1) แทนที่จะ implement registry logic ซ้ำที่นี่ - single source of
    /// truth เดียว รันได้ทั้งตอน admin เรียกเองแบบ elevated PowerShell และตอน Agent (LocalSystem)
    /// เรียกแทน (ดู script's ตัวเช็คสิทธิ์ที่รองรับทั้ง Administrator และ SYSTEM แล้ว)
    /// </summary>
    private async Task<CommandExecutionResult> HandleSetTailscaleLockdownAsync(string? payloadJson, CancellationToken ct)
    {
        TailscaleLockdownPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<TailscaleLockdownPayload>(
                payloadJson ?? string.Empty, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "แปลง payload ของ SetTailscaleLockdown ไม่สำเร็จ: {Payload}", payloadJson);
            return CommandExecutionResult.Fail($"payload ของ SetTailscaleLockdown ไม่ถูกต้อง: {ex.Message}");
        }

        if (payload is null)
        {
            return CommandExecutionResult.Fail("payload ของ SetTailscaleLockdown ว่างเปล่า");
        }

        var scriptPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "CustomActions", "set-tailscale-lockdown.ps1"));
        if (!File.Exists(scriptPath))
        {
            return CommandExecutionResult.Fail($"หาสคริปต์ set-tailscale-lockdown.ps1 ไม่เจอที่ {scriptPath} (install อาจไม่สมบูรณ์)");
        }

        var arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{scriptPath}\"" + (payload.Locked ? string.Empty : " -Unlock");

        var (exitCode, stdOut, stdErr) = await RunProcessAsync("powershell.exe", arguments, TimeSpan.FromSeconds(30), ct);

        var action = payload.Locked ? "ล็อก" : "ปลดล็อก";
        if (exitCode == 0)
        {
            _logger.LogInformation("{Action} Tailscale สำเร็จผ่าน SetTailscaleLockdown command", action);
            return CommandExecutionResult.Ok($"{action} Tailscale สำเร็จ");
        }

        _logger.LogWarning("{Action} Tailscale ไม่สำเร็จ (exit code {ExitCode}): {StdErr} {StdOut}", action, exitCode, stdErr, stdOut);
        return CommandExecutionResult.Fail($"{action} Tailscale ไม่สำเร็จ (exit code {exitCode}): {(string.IsNullOrWhiteSpace(stdErr) ? stdOut : stdErr)}");
    }

    /// <summary>
    /// สั่ง tailscale.exe เข้าร่วม tailnet ด้วย authkey ที่ครูคัดลอกมาจาก Tailscale admin console เอง
    /// ผ่าน Console (RunTailscaleLogin command) - ตั้งใจไม่ baked authkey ไว้ใน installer เลย (ความเสี่ยง
    /// ด้านความปลอดภัยถ้า key เดียวกันหลุดไปทุกเครื่อง) ให้ส่งมาทีละครั้งตอนต้องการ enroll จริงแทน
    ///
    /// สำคัญมาก: ห้าม log payloadJson/authKey ที่ไหนทั้งสิ้น (ต่างจาก handler อื่นๆ ในไฟล์นี้ที่ log
    /// payload ตรงๆ ตอน parse ไม่ผ่าน - ที่นี่ต้อง log แค่ข้อความทั่วไปเท่านั้น)
    /// </summary>
    private async Task<CommandExecutionResult> HandleRunTailscaleLoginAsync(string? payloadJson, CancellationToken ct)
    {
        TailscaleLoginPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<TailscaleLoginPayload>(
                payloadJson ?? string.Empty, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (Exception)
        {
            _logger.LogWarning("แปลง payload ของ RunTailscaleLogin ไม่สำเร็จ (รูปแบบไม่ถูกต้อง)");
            return CommandExecutionResult.Fail("payload ของ RunTailscaleLogin ไม่ถูกต้อง");
        }

        if (string.IsNullOrWhiteSpace(payload?.AuthKey))
        {
            return CommandExecutionResult.Fail("ไม่ได้ระบุ authkey มาด้วย");
        }

        var tailscaleExePath = GetTailscaleExePath();
        if (!File.Exists(tailscaleExePath))
        {
            return CommandExecutionResult.Fail($"หา tailscale.exe ไม่เจอที่ {tailscaleExePath} (Tailscale ยังไม่ได้ติดตั้งบนเครื่องนี้)");
        }

        // authkey อยู่ใน command-line argument (ไม่มีทางเลี่ยงได้ - tailscale CLI ไม่รองรับรับ authkey
        // ผ่าน stdin/env var) ทำให้มีช่วงเวลาสั้นๆ ที่ process listing (Task Manager/Process Explorer/
        // event log ที่เปิด command-line auditing) เห็น argument นี้ได้ระหว่าง process ยังรันอยู่ -
        // ยอมรับความเสี่ยงนี้ (เหมือนกับทุก CLI tool ที่รับ secret ผ่าน argument) แต่ตัว Agent เองต้อง
        // ไม่ทำให้แย่ลงไปกว่านี้ด้วยการ log ค่านี้ซ้ำเองที่ไหนอีก
        var arguments = $"up --authkey={payload.AuthKey} --unattended";
        var (exitCode, stdOut, stdErr) = await RunProcessAsync(tailscaleExePath, arguments, TimeSpan.FromSeconds(30), ct);

        if (exitCode == 0)
        {
            _logger.LogInformation("tailscale up --unattended สำเร็จผ่าน RunTailscaleLogin command");
            return CommandExecutionResult.Ok("เข้าร่วม tailnet สำเร็จ");
        }

        _logger.LogWarning("tailscale up ไม่สำเร็จ (exit code {ExitCode}): {StdErr} {StdOut}", exitCode, stdErr, stdOut);
        return CommandExecutionResult.Fail($"เข้าร่วม tailnet ไม่สำเร็จ (exit code {exitCode}): {(string.IsNullOrWhiteSpace(stdErr) ? stdOut : stdErr)}");
    }

    /// <summary>
    /// อ่านสถานะ Tailscale ปัจจุบัน (ล็อกอยู่ไหม/ติดตั้งหรือยัง/tailnet IP อะไร) ให้ Console แสดงแบบ
    /// on-demand ตอนครูเปิดหน้าต่างที่เกี่ยวกับ Tailscale ของเครื่องนี้ - ไม่มี state เก็บฝั่ง Server เลย
    /// (ตั้งใจไม่ทำ persistent badge บน dashboard รอบนี้) อาศัย ack/result pipeline เดิมเป็นช่องทาง
    /// request/response แทนที่จะเพิ่ม transport ใหม่
    /// </summary>
    private async Task<CommandExecutionResult> HandleGetTailscaleStatusAsync(CancellationToken ct)
    {
        var tailscaleExePath = GetTailscaleExePath();
        var installed = File.Exists(tailscaleExePath);

        bool locked;
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Policies\Tailscale");
            var value = key?.GetValue("AlwaysOn.Enabled");
            locked = value is int i && i == 1;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "อ่านค่า Tailscale AlwaysOn.Enabled จาก registry ไม่สำเร็จ");
            locked = false;
        }

        if (!installed)
        {
            return CommandExecutionResult.Ok("ยังไม่ได้ติดตั้ง Tailscale บนเครื่องนี้");
        }

        var tailnetIp = string.Empty;
        try
        {
            var (exitCode, stdOut, _) = await RunProcessAsync(tailscaleExePath, "ip -4", TimeSpan.FromSeconds(5), ct);
            if (exitCode == 0)
            {
                tailnetIp = stdOut.Trim();
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "เรียก tailscale ip -4 ไม่สำเร็จ (ยังไม่ login/tailnet ไม่พร้อม)");
        }

        var connectedText = string.IsNullOrWhiteSpace(tailnetIp) ? "ยังไม่ได้ login เข้า tailnet" : $"tailnet IP: {tailnetIp}";
        return CommandExecutionResult.Ok($"ติดตั้งแล้ว, {(locked ? "ล็อกอยู่ (AlwaysOn)" : "ไม่ได้ล็อก")}, {connectedText}");
    }

    private static string GetTailscaleExePath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Tailscale", "tailscale.exe");

    /// <summary>
    /// "เปิด URL ทั้งห้อง" (WP4-1, 2026-11-01) - ตรวจซ้ำว่า Url เป็น http/https เท่านั้นก่อนเปิดเสมอ (ห้าม
    /// เชื่อ validation ฝั่ง Console อย่างเดียว - ดู doc comment บน OpenUrlPayload) แล้วเปิดใน session ของ
    /// user ที่ login อยู่หน้าเครื่องผ่าน UserSessionLauncher (Agent เอง Session 0 ไม่มี desktop ให้เปิด
    /// เบราว์เซอร์เลย - เหตุผลเดียวกับ LockWorkstation/BroadcastMessage) ใช้ "explorer.exe <url>" แทนการรัน
    /// เบราว์เซอร์ตรงๆ เพราะเป็นวิธีให้ Windows เปิดด้วย default browser ของ user คนนั้นเองโดยไม่ต้องรู้ว่า
    /// ติดตั้งเบราว์เซอร์ตัวไหนไว้ - ปลอดภัยเพราะ scheme ถูกบังคับให้เป็น http/https เท่านั้นแล้วด้านบน
    /// (ไม่ใช่ ShellExecute ของ scheme handler อื่นที่อาจรันโปรแกรมได้)
    ///
    /// โหมด Fullscreen (wireframe tc-openurl: "เต็มจอ (โหมดตั้งใจ)") เป็น best-effort เท่านั้นในรอบนี้ -
    /// ลองเปิดแบบ kiosk ถ้าหาเบราว์เซอร์ที่รองรับ (Edge/Chrome) เจอ ไม่เจอก็เปิดแบบปกติแทน ยังไม่ได้ทำ
    /// "ออกได้ด้วยคำสั่งจากครูเท่านั้น" (ต้อง lock/track process เพิ่ม - ทิ้งไว้เป็นงานต่อ ดู CommandProcessor
    /// comment ในรายงานของ WP4-1)
    /// </summary>
    private async Task<CommandExecutionResult> HandleOpenUrlAsync(string? payloadJson, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            _logger.LogWarning("OpenUrl command ไม่มี payload มาด้วย - ข้าม");
            return CommandExecutionResult.Fail("OpenUrl command ไม่มี payload มาด้วย");
        }

        OpenUrlPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<OpenUrlPayload>(
                payloadJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "แปลง payload ของ OpenUrl ไม่สำเร็จ: {Payload}", payloadJson);
            return CommandExecutionResult.Fail($"payload ของ OpenUrl ไม่ถูกต้อง: {ex.Message}");
        }

        if (payload is null || string.IsNullOrWhiteSpace(payload.Url))
        {
            return CommandExecutionResult.Fail("OpenUrl payload ไม่มี Url");
        }

        // ตรวจซ้ำที่นี่เสมอ - ด่านนี้คือสิ่งที่กันไม่ให้ใครที่มี admin token ยิง POST /api/commands ตรงๆ
        // สั่งเปิด scheme อื่น (เช่น file://, custom protocol handler ที่อาจรันโปรแกรมได้) บน 40 เครื่องพร้อมกัน
        // logic จริงอยู่ใน OpenUrlValidation.TryGetAllowedUri (แยกเป็น static ล้วนๆ เพื่อ unit test ได้
        // โดยไม่ต้องสร้าง CommandProcessor ทั้งก้อน)
        if (!OpenUrlValidation.TryGetAllowedUri(payload.Url, out var uri) || uri is null)
        {
            _logger.LogWarning("OpenUrl ปฏิเสธ Url ที่ไม่ใช่ http/https: '{Url}'", payload.Url);
            return CommandExecutionResult.Fail($"ปฏิเสธ - รองรับเฉพาะ URL แบบ http/https เท่านั้น (ได้รับ '{payload.Url}')");
        }

        var explorerPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
        var launched = payload.Mode == OpenUrlMode.Fullscreen
            ? TryLaunchFullscreen(uri)
            : _userSessionLauncher.TryLaunchInActiveUserSession(explorerPath, $"\"{uri.AbsoluteUri}\"");

        return launched
            ? CommandExecutionResult.Ok($"เปิด {uri.AbsoluteUri} แล้ว")
            : CommandExecutionResult.Fail("เปิด URL ไม่สำเร็จ (อาจไม่มี user login อยู่หน้าเครื่องนี้)");
    }

    /// <summary>
    /// best-effort kiosk mode สำหรับ "เต็มจอ (โหมดตั้งใจ)" - ลอง Edge แล้ว Chrome ตามลำดับที่ path เริ่มต้น
    /// ของ MSI/installer ทั่วไป ถ้าไม่เจอทั้งคู่ fallback เป็นเปิดแบบ NewTab ธรรมดาผ่าน explorer.exe
    /// (ยังเปิดเว็บได้อยู่ แค่ไม่เต็มจอ - ดีกว่ารายงาน Fail ทั้งที่เปิดได้จริง)
    /// </summary>
    private bool TryLaunchFullscreen(Uri uri)
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

        var kioskCandidates = new[]
        {
            Path.Combine(programFilesX86, "Microsoft", "Edge", "Application", "msedge.exe"),
            Path.Combine(programFiles, "Microsoft", "Edge", "Application", "msedge.exe"),
            Path.Combine(programFilesX86, "Google", "Chrome", "Application", "chrome.exe"),
            Path.Combine(programFiles, "Google", "Chrome", "Application", "chrome.exe")
        };

        var browserPath = kioskCandidates.FirstOrDefault(File.Exists);
        if (browserPath is null)
        {
            _logger.LogInformation("OpenUrl Fullscreen: ไม่พบ Edge/Chrome ที่ path เริ่มต้น - เปิดแบบ NewTab ปกติแทน");
            var explorerPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
            return _userSessionLauncher.TryLaunchInActiveUserSession(explorerPath, $"\"{uri.AbsoluteUri}\"");
        }

        return _userSessionLauncher.TryLaunchInActiveUserSession(browserPath, $"--start-fullscreen \"{uri.AbsoluteUri}\"");
    }

    /// <summary>
    /// "เปิดโปรแกรมทั้งห้อง" (WP4-1, 2026-11-01) - payload มีแค่ ProgramId ตั้งใจ (ดู doc comment บน
    /// LaunchProgramPayload) ต้องขอ Path/Arguments จริงจาก Server ก่อนรันเสมอ เพื่อตรวจซ้ำว่าโปรแกรมนี้ยัง
    /// อยู่ในรายการที่ Admin อนุญาตจริง ณ เวลาที่รัน ไม่ใช่เชื่อ path ที่อาจติดมากับ payload เก่า/ถูกแก้ไข
    /// </summary>
    private async Task<CommandExecutionResult> HandleLaunchProgramAsync(string? payloadJson, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            _logger.LogWarning("LaunchProgram command ไม่มี payload มาด้วย - ข้าม");
            return CommandExecutionResult.Fail("LaunchProgram command ไม่มี payload มาด้วย");
        }

        LaunchProgramPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<LaunchProgramPayload>(
                payloadJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "แปลง payload ของ LaunchProgram ไม่สำเร็จ: {Payload}", payloadJson);
            return CommandExecutionResult.Fail($"payload ของ LaunchProgram ไม่ถูกต้อง: {ex.Message}");
        }

        if (payload is null || payload.ProgramId <= 0)
        {
            return CommandExecutionResult.Fail("LaunchProgram payload ไม่มี ProgramId ที่ถูกต้อง");
        }

        // ด่านตรวจซ้ำตัวจริง - ไม่เชื่อ path ใดๆ ที่มาทาง payload เลย ขอจาก Server ใหม่เสมอ (fail-secure:
        // ติดต่อ Server ไม่ได้ก็ไม่รัน ไม่ fallback ไปรันด้วยข้อมูลเก่า) ตัดสินใจ "อนุญาตไหม" ด้วย
        // LaunchProgramValidation.IsAllowedToLaunch (static ล้วนๆ เพื่อ unit test ได้โดยไม่ต้องยิง HTTP จริง)
        var program = await _serverClient.GetLaunchableProgramAsync(payload.ProgramId, ct);
        if (!LaunchProgramValidation.IsAllowedToLaunch(program))
        {
            _logger.LogWarning("LaunchProgram ปฏิเสธ ProgramId {ProgramId} - ไม่อยู่ในรายการที่ Admin อนุญาตแล้ว (อาจถูกลบ/ไม่ติดต่อ Server ได้)", payload.ProgramId);
            return CommandExecutionResult.Fail("ปฏิเสธ - โปรแกรมนี้ไม่อยู่ในรายการที่ Admin อนุญาตแล้ว (อาจถูกลบ/ปิดใช้งาน หรือติดต่อ Server ไม่ได้)");
        }

        var launched = _userSessionLauncher.TryLaunchInActiveUserSession(program.Path, program.Arguments);

        return launched
            ? CommandExecutionResult.Ok($"เปิดโปรแกรม '{program.Name}' แล้ว")
            : CommandExecutionResult.Fail(
                $"เปิดโปรแกรม '{program.Name}' ไม่สำเร็จ (อาจไม่มี user login อยู่หน้าเครื่องนี้ หรือหาไฟล์ที่ '{program.Path}' บนเครื่องนี้ไม่พบ)");
    }

    /// <summary>
    /// รัน external process แบบ async พร้อม timeout - ใช้ร่วมกันทั้ง 3 handler ของ Tailscale ด้านบน
    /// (set-tailscale-lockdown.ps1 / tailscale.exe up / tailscale.exe ip) กัน process ค้าง (เช่น
    /// Restart-Service ที่อาจ hang) ด้วยการ kill ทั้ง process tree ถ้าเกิน timeout ที่กำหนด
    /// </summary>
    private async Task<(int ExitCode, string StdOut, string StdErr)> RunProcessAsync(
        string fileName, string arguments, TimeSpan timeout, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        using var process = new Process { StartInfo = psi };
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);

        process.Start();
        var stdOutTask = process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
        var stdErrTask = process.StandardError.ReadToEndAsync(timeoutCts.Token);

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // process อาจปิดตัวเองไปพอดีระหว่างที่กำลังจะ kill - ไม่ถือเป็น error
            }

            throw new TimeoutException($"'{fileName}' ไม่จบภายใน {timeout.TotalSeconds} วิ - kill process แล้ว");
        }

        var stdOut = await stdOutTask;
        var stdErr = await stdErrTask;
        return (process.ExitCode, stdOut, stdErr);
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
