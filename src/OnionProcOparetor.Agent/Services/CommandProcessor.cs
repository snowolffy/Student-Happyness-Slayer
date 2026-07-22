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
    private readonly ILogger<CommandProcessor> _logger;
    private readonly ServerClient _serverClient;

    // เก็บ commandId ที่ process ไปแล้วล่าสุด กันประมวลผลซ้ำตอน network race
    // (เช่น SignalR ReceiveCommand กับ poll loop เห็น command เดียวกันเกือบพร้อมกัน หรือ ExecuteAsync
    // loop กับ RunPeriodicProcessScanAsync loop เห็น pendingCommands ตัวเดียวกันพร้อมกัน)
    // ใช้ commandId เดียวกันได้ทั้ง 2 path แล้ว (Server ส่ง commandId มาด้วยทาง SignalR ด้วย)
    private const int MaxProcessedCommandIds = 50;
    private readonly object _processedLock = new();
    private readonly Queue<int> _processedCommandIdOrder = new();
    private readonly HashSet<int> _processedCommandIds = new();

    public CommandProcessor(ILogger<CommandProcessor> logger, ServerClient serverClient)
    {
        _logger = logger;
        _serverClient = serverClient;
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
            // TODO Phase 3/4: ใส่ command จริงตรงนี้ เช่น "RefreshRules", "Kill", "RunCommand"
            default:
                _logger.LogWarning(
                    "ได้รับ command ชนิดที่ยังไม่รู้จัก: '{CommandType}' (payload: {Payload}) - ยังไม่ implement (Phase 3/4)",
                    commandType, payloadJson);
                break;
        }

        return Task.CompletedTask;
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
