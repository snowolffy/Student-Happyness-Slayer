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
    private HubConnection? _connection;
    private string _clientGuid = string.Empty;

    public LabHubConnection(ILogger<LabHubConnection> logger, IOptions<ServerSettings> settings, CommandProcessor commandProcessor)
    {
        _logger = logger;
        _settings = settings.Value;
        _commandProcessor = commandProcessor;
    }

    /// <summary>
    /// เริ่มต่อ SignalR - ไม่ throw ออกไปแม้ server จะยังไม่พร้อม (เช่น start ทีหลัง Agent)
    /// รัน retry loop ของตัวเองอยู่เบื้องหลัง ไม่ block caller (ควร fire-and-forget เรียกจาก Worker)
    /// </summary>
    public async Task StartAsync(string clientGuid, CancellationToken ct)
    {
        _clientGuid = clientGuid;
        var hubUrl = _settings.BaseUrl.TrimEnd('/') + "/hubs/lab";

        _connection = new HubConnectionBuilder()
            .WithUrl(hubUrl)
            .WithAutomaticReconnect(new ExponentialBackoffRetryPolicy())
            .Build();

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
