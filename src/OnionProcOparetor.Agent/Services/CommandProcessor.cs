using System.Text.Json;
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
    /// <summary>วินาทีที่เตือน user ก่อน shutdown/restart จริง - ให้เวลา save งานก่อนเครื่องดับ</summary>
    private const int PowerActionWarningDelaySeconds = 10;

    private readonly ILogger<CommandProcessor> _logger;
    private readonly ServerClient _serverClient;
    private readonly AgentTrayNotifier _agentTrayNotifier;
    private readonly ClientState _clientState;

    // เก็บ commandId ที่ process ไปแล้วล่าสุด กันประมวลผลซ้ำตอน network race
    // (เช่น SignalR ReceiveCommand กับ poll loop เห็น command เดียวกันเกือบพร้อมกัน หรือ ExecuteAsync
    // loop กับ RunPeriodicProcessScanAsync loop เห็น pendingCommands ตัวเดียวกันพร้อมกัน)
    // ใช้ commandId เดียวกันได้ทั้ง 2 path แล้ว (Server ส่ง commandId มาด้วยทาง SignalR ด้วย)
    private const int MaxProcessedCommandIds = 50;
    private readonly object _processedLock = new();
    private readonly Queue<int> _processedCommandIdOrder = new();
    private readonly HashSet<int> _processedCommandIds = new();

    public CommandProcessor(ILogger<CommandProcessor> logger, ServerClient serverClient, AgentTrayNotifier agentTrayNotifier, ClientState clientState)
    {
        _logger = logger;
        _serverClient = serverClient;
        _agentTrayNotifier = agentTrayNotifier;
        _clientState = clientState;
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
                return HandlePowerActionAsync(isRestart: false, ct);

            case "Restart":
                return HandlePowerActionAsync(isRestart: true, ct);

            case "UpdateSettings":
                return HandleUpdateSettingsAsync(payloadJson, ct);

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
    /// Shutdown/Restart - เรียกจาก Agent (Session 0) ได้ตรงๆ เพราะเป็น system-level operation
    /// (ต่างจาก LockWorkstation ที่เป็น per-session) ส่ง broadcast เตือน user ล่วงหน้าก่อนเสมอ
    /// แล้วค่อย delay จริงแบบ fire-and-forget - method นี้ return ทันทีหลังส่ง broadcast สำเร็จ
    /// เพื่อไม่ block ProcessPendingCommandAsync จาก ack/ประมวลผล command อื่นที่เข้ามาระหว่างรอ
    /// </summary>
    private async Task HandlePowerActionAsync(bool isRestart, CancellationToken ct)
    {
        var actionName = isRestart ? "Restart" : "Shutdown";
        _logger.LogInformation("ได้รับ {Action} command - เตือน user ก่อน {DelaySeconds} วินาที", actionName, PowerActionWarningDelaySeconds);

        var warningPayload = new BroadcastMessagePayload
        {
            Title = "แจ้งเตือนจากผู้ดูแลระบบ",
            Message = isRestart
                ? $"เครื่องนี้จะรีสตาร์ทใน {PowerActionWarningDelaySeconds} วินาที โดยผู้ดูแลระบบ กรุณาบันทึกงานของท่าน"
                : $"เครื่องนี้จะปิดใน {PowerActionWarningDelaySeconds} วินาที โดยผู้ดูแลระบบ กรุณาบันทึกงานของท่าน"
        };
        await _agentTrayNotifier.SendBroadcastMessageAsync(warningPayload, ct);

        // fire-and-forget โดยตั้งใจ - ไม่ await ตรงนี้ กัน command อื่นที่มาระหว่างรอ 10 วิ ถูก block
        _ = DelayThenExitWindowsAsync(isRestart, ct);
    }

    private async Task DelayThenExitWindowsAsync(bool isRestart, CancellationToken ct)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(PowerActionWarningDelaySeconds), ct);
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
