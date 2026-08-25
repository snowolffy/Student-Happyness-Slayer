using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Options;
using OnionProcOparetor.Agent;

namespace OnionProcOparetor.Agent.Services;

/// <summary>
/// เชื่อมต่อ SignalR Hub (LabHub) ที่ Server เป็นชั้นเสริมคู่ขนานกับ HTTP poll loop เดิม (ไม่แทนที่)
/// ออกแบบ fail-secure เหมือน ServerClient: ถ้า connect ไม่ได้/หลุด ต้องไม่ throw ออกไปจน Worker/service ตาย
/// - Agent ยังทำงานผ่าน poll ปกติได้เสมอไม่ว่า SignalR จะต่อติดอยู่หรือไม่
/// </summary>
public class LabHubConnection : IAsyncDisposable
{
    private readonly ILogger<LabHubConnection> _logger;
    private readonly ServerSettings _settings;
    private readonly CommandProcessor _commandProcessor;
    private readonly CommandResultReporter _resultReporter;
    private HubConnection? _connection;
    private string _clientGuid = string.Empty;
    private string _activeBaseUrl = string.Empty;
    private readonly SemaphoreSlim _switchLock = new(1, 1);

    public LabHubConnection(
        ILogger<LabHubConnection> logger,
        IOptions<ServerSettings> settings,
        CommandProcessor commandProcessor,
        CommandResultReporter resultReporter)
    {
        _logger = logger;
        _settings = settings.Value;
        _commandProcessor = commandProcessor;
        _resultReporter = resultReporter;
    }

    /// <summary>
    /// ส่ง "ผลจริง" ของ command กลับไปหา Server ผ่าน hub method ReportCommandResult
    /// คืน false ถ้า connection ยังไม่พร้อม (ผู้เรียกจะ fallback ไป HTTP เอง) - ไม่ throw
    /// ออกไปเอง แต่ปล่อยให้ exception จริงจาก InvokeAsync หลุดขึ้นไปให้ CommandResultReporter
    /// log แล้ว fallback ต่อ (แยกให้ชัดระหว่าง "ไม่ได้ต่ออยู่" กับ "ต่ออยู่แต่ส่งพัง")
    /// </summary>
    private async Task<bool> TrySendCommandResultAsync(int commandId, bool success, string? message, CancellationToken ct)
    {
        if (_connection is null || _connection.State != HubConnectionState.Connected)
        {
            return false;
        }

        await _connection.InvokeAsync("ReportCommandResult", commandId, success, message, ct);
        return true;
    }

    /// <summary>
    /// เริ่มต่อ SignalR - ไม่ throw ออกไปแม้ server จะยังไม่พร้อม (เช่น start ทีหลัง Agent)
    /// รัน retry loop ของตัวเองอยู่เบื้องหลัง ไม่ block caller (ควร fire-and-forget เรียกจาก Worker)
    /// </summary>
    public async Task StartAsync(string clientGuid, CancellationToken ct)
    {
        _clientGuid = clientGuid;
        await BuildAndConnectAsync(_settings.BaseUrl, ct);
    }

    /// <summary>
    /// เปลี่ยนไป base URL ใหม่ (primary&lt;-&gt;fallback) - เรียกจาก Worker คู่ขนานกับ ServerClient.SwitchTo
    /// เพื่อให้ HTTP poll กับ SignalR hub ชี้ไปที่เดียวกันเสมอ ปิด connection เดิมแบบมีเวลาจำกัด (เหมือน
    /// DisposeAsync/StopAsync ของ Worker) ก่อนสร้างใหม่ กัน mid-connect ค้างยาวถ้า URL เดิมกำลังมีปัญหาอยู่พอดี
    /// SemaphoreSlim กันสอง SwitchServerAsync ทับกันเอง (Worker เรียกแบบ fire-and-forget)
    /// </summary>
    public async Task SwitchServerAsync(string newBaseUrl, CancellationToken ct)
    {
        await _switchLock.WaitAsync(ct);
        try
        {
            if (string.Equals(_activeBaseUrl, newBaseUrl, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (_connection is not null)
            {
                using var disposeTimeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                try
                {
                    await _connection.DisposeAsync().AsTask().WaitAsync(disposeTimeoutCts.Token);
                }
                catch (OperationCanceledException)
                {
                    _logger.LogWarning("ปิด SignalR connection เดิมไม่เสร็จภายใน 5 วิ (อาจ mid-connect อยู่) - ข้ามไปสร้างใหม่เลย");
                }
            }

            await BuildAndConnectAsync(newBaseUrl, ct);
        }
        finally
        {
            _switchLock.Release();
        }
    }

    /// <summary>
    /// สร้าง HubConnection ใหม่ชี้ไป baseUrl ที่ให้มา ผูก handler ครบชุดเดิม แล้ว connect (พร้อม retry)
    /// - ใช้ร่วมกันทั้งตอน StartAsync ครั้งแรกและตอน SwitchServerAsync กัน handler-wiring ซ้ำกันคนละที่
    /// </summary>
    private async Task BuildAndConnectAsync(string baseUrl, CancellationToken ct)
    {
        _activeBaseUrl = baseUrl;
        var hubUrl = baseUrl.TrimEnd('/') + "/hubs/lab";

        _connection = new HubConnectionBuilder()
            .WithUrl(hubUrl)
            .WithAutomaticReconnect(new ExponentialBackoffRetryPolicy())
            .Build();

        // "เสียบ" ตัวส่งผลผ่าน SignalR ให้ CommandResultReporter ตรงนี้ แทนที่จะให้
        // CommandProcessor inject LabHubConnection ตรงๆ (ซึ่งจะเป็น circular dependency
        // เพราะ LabHubConnection inject CommandProcessor อยู่แล้ว) - ดู CommandResultReporter
        _resultReporter.SignalRSender = TrySendCommandResultAsync;

        // path เดียวกับที่ใช้ตอนเจอ pendingCommands จาก poll (ProcessPendingCommandAsync มี dedup + ack
        // ในตัวอยู่แล้ว ผ่าน commandId) - ไม่ให้ logic แยกกันคนละที่ กันกรณี execute ซ้ำถ้า poll เห็น
        // command เดียวกันตามมาทีหลัง (หรือมาก่อน) SignalR
        _connection.On<int, string, string>("ReceiveCommand", async (commandId, commandType, payloadJson) =>
        {
            try
            {
                await _commandProcessor.ProcessPendingCommandAsync(commandId, commandType, payloadJson);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "ประมวลผล command {CommandId} ('{CommandType}') จาก SignalR ไม่สำเร็จ", commandId, commandType);
            }
        });

        _connection.Reconnecting += ex =>
        {
            _logger.LogWarning(ex, "SignalR reconnecting - ระหว่างนี้ Agent ยังทำงานผ่าน poll ตามปกติ (fail-secure)");
            return Task.CompletedTask;
        };

        _connection.Reconnected += async connectionId =>
        {
            _logger.LogInformation("SignalR reconnected (ConnectionId: {ConnectionId}) - join group agent ใหม่อีกครั้ง", connectionId);
            await RegisterAgentSafeAsync();
        };

        _connection.Closed += ex =>
        {
            _logger.LogWarning(ex, "SignalR connection closed (หมด automatic reconnect attempts แล้ว) - Agent ยังทำงานผ่าน poll ตามปกติ (fail-secure)");
            return Task.CompletedTask;
        };

        await ConnectWithRetryAsync(ct);
    }

    /// <summary>
    /// retry การ start เชื่อมต่อครั้งแรกเองไม่มีที่สิ้นสุด (จนกว่าจะสำเร็จหรือถูกยกเลิก) - กันกรณี
    /// Server ยังไม่พร้อมตอน Agent start (WithAutomaticReconnect ทำงานได้แค่หลัง connect สำเร็จครั้งแรกแล้วเท่านั้น)
    /// ใช้ backoff sequence เดียวกับตอน reconnect เพื่อไม่ให้ retry ถี่เกินจนโหลด server
    /// </summary>
    private async Task ConnectWithRetryAsync(CancellationToken ct)
    {
        var attempt = 0;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await _connection!.StartAsync(ct);
                _logger.LogInformation("SignalR connected สำเร็จ ({HubUrl})", _connection.ConnectionId);
                await RegisterAgentSafeAsync();
                return;
            }
            catch (Exception ex)
            {
                var delay = ExponentialBackoffRetryPolicy.GetDelay(attempt);
                _logger.LogWarning(ex,
                    "SignalR connect ไม่สำเร็จ (Server อาจยังไม่พร้อม/offline) - จะ retry อีกใน {DelaySeconds} วิ (ระหว่างนี้ยังทำงานผ่าน poll ตามปกติ)",
                    delay.TotalSeconds);
                attempt++;

                try
                {
                    await Task.Delay(delay, ct);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }

    /// <summary>
    /// ส่ง port/password ของ VNC session ที่เพิ่ง start สำเร็จกลับไปยัง Console ตัวที่ request เท่านั้น
    /// (ผ่าน LabHub.RelayVncSessionReady -> group ส่วนตัวของ consoleSessionId นั้น) - ถ้า SignalR ไม่
    /// connected อยู่ตอนนี้ก็แค่ log warning ไม่ throw (ไม่มี poll fallback สำหรับ VNC เพราะเป็นข้อมูล
    /// ที่ต้องส่งสดๆ ตอนนั้นเท่านั้น ไม่ควร cache/persist password ไว้ที่ไหนเพิ่ม)
    ///
    /// ส่ง _clientGuid ของตัวเองไปด้วยเสมอ (ตั้งแต่ Step 4e) - Console เดียวกันอาจมี StartVncSession
    /// ค้างรออยู่หลายเครื่องพร้อมกัน (group consoleSessionId เดียวกันหมด) ถ้าไม่มี clientGuid มาด้วย
    /// Console แยกไม่ออกว่า "ready" นี้เป็นของเครื่องไหน
    /// </summary>
    public async Task NotifyVncSessionReadyAsync(string consoleSessionId, int port, string password, CancellationToken ct)
    {
        if (_connection is null || _connection.State != HubConnectionState.Connected)
        {
            _logger.LogWarning("VNC session พร้อมใช้งานแล้วแต่ SignalR ไม่ connected อยู่ - ส่งผลกลับ Console ไม่ได้ (ConsoleSessionId: {SessionId})", consoleSessionId);
            return;
        }

        try
        {
            await _connection.InvokeAsync("RelayVncSessionReady", consoleSessionId, _clientGuid, port, password, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ส่ง VncSessionReady กลับ Console ไม่สำเร็จ (ConsoleSessionId: {SessionId})", consoleSessionId);
        }
    }

    /// <summary>เหมือน NotifyVncSessionReadyAsync แต่สำหรับกรณี start VNC session ไม่สำเร็จ - ส่งเหตุผลกลับไปด้วย</summary>
    public async Task NotifyVncSessionFailedAsync(string consoleSessionId, string reason, CancellationToken ct)
    {
        if (_connection is null || _connection.State != HubConnectionState.Connected)
        {
            _logger.LogWarning("VNC session start ไม่สำเร็จ ({Reason}) แต่ SignalR ก็ไม่ connected อยู่ด้วย - ส่งผลกลับ Console ไม่ได้ (ConsoleSessionId: {SessionId})", reason, consoleSessionId);
            return;
        }

        try
        {
            await _connection.InvokeAsync("RelayVncSessionFailed", consoleSessionId, _clientGuid, reason, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ส่ง VncSessionFailed กลับ Console ไม่สำเร็จ (ConsoleSessionId: {SessionId})", consoleSessionId);
        }
    }

    /// <summary>
    /// ส่งผล scan process (JSON ของ List&lt;RunningProcessDto&gt;) กลับไปยัง Console ตัวที่ request เท่านั้น
    /// (ผ่าน LabHub.RelayProcessListReady -> group ส่วนตัวของ consoleSessionId นั้น) - ส่งเป็น opaque
    /// string เพราะ Agent ไม่จำเป็นต้องรู้จัก shape ของ RunningProcessDto เลย (แค่ relay ผ่านมือ)
    /// </summary>
    public async Task NotifyProcessListReadyAsync(string consoleSessionId, string processListJson, CancellationToken ct)
    {
        if (_connection is null || _connection.State != HubConnectionState.Connected)
        {
            _logger.LogWarning("Process list พร้อมแล้วแต่ SignalR ไม่ connected อยู่ - ส่งผลกลับ Console ไม่ได้ (ConsoleSessionId: {SessionId})", consoleSessionId);
            return;
        }

        try
        {
            await _connection.InvokeAsync("RelayProcessListReady", consoleSessionId, _clientGuid, processListJson, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ส่ง ProcessListReady กลับ Console ไม่สำเร็จ (ConsoleSessionId: {SessionId})", consoleSessionId);
        }
    }

    /// <summary>เหมือน NotifyProcessListReadyAsync แต่สำหรับกรณีสแกนไม่สำเร็จ - ส่งเหตุผลกลับไปด้วย</summary>
    public async Task NotifyProcessListFailedAsync(string consoleSessionId, string reason, CancellationToken ct)
    {
        if (_connection is null || _connection.State != HubConnectionState.Connected)
        {
            _logger.LogWarning("สแกน process ไม่สำเร็จ ({Reason}) แต่ SignalR ก็ไม่ connected อยู่ด้วย - ส่งผลกลับ Console ไม่ได้ (ConsoleSessionId: {SessionId})", reason, consoleSessionId);
            return;
        }

        try
        {
            await _connection.InvokeAsync("RelayProcessListFailed", consoleSessionId, _clientGuid, reason, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ส่ง ProcessListFailed กลับ Console ไม่สำเร็จ (ConsoleSessionId: {SessionId})", consoleSessionId);
        }
    }

    private async Task RegisterAgentSafeAsync()
    {
        try
        {
            await _connection!.InvokeAsync("RegisterAgent", _clientGuid);
            _logger.LogInformation("RegisterAgent สำเร็จ - เข้า group '{ClientGuid}' บน SignalR hub แล้ว", _clientGuid);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "RegisterAgent ผ่าน SignalR ไม่สำเร็จ - server จะยังไม่รู้ว่า connection นี้เป็นเครื่องไหน (จะลองใหม่ตอน reconnect)");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            await _connection.DisposeAsync();
        }
    }
}

/// <summary>
/// Retry policy แบบ exponential backoff: 0, 2, 5, 10, 30 วิ แล้ววนซ้ำที่ 30 วิไปเรื่อยๆ
/// (เครือข่ายโรงเรียนไม่เสถียร - กันไม่ให้ retry ถี่เกินจนโหลด server แต่ก็ไม่ยอมแพ้ ลองต่อไปเรื่อยๆ)
/// </summary>
public class ExponentialBackoffRetryPolicy : IRetryPolicy
{
    private static readonly TimeSpan[] Delays =
    {
        TimeSpan.Zero,
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(30)
    };

    public static TimeSpan GetDelay(long previousRetryCount) =>
        previousRetryCount < Delays.Length ? Delays[previousRetryCount] : Delays[^1];

    public TimeSpan? NextRetryDelay(RetryContext retryContext) => GetDelay(retryContext.PreviousRetryCount);
}
