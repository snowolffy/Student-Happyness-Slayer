using Microsoft.AspNetCore.SignalR.Client;
using OnionProcOparetor.OnlineConsole.Models;

namespace OnionProcOparetor.OnlineConsole.Services;

/// <summary>
/// Ported from OnionProcOparetor.Console/Services/LabHubClient.cs - connects to the same LabHub SignalR
/// endpoint the WPF Console uses, so the web dashboard gets the same real-time push (client status,
/// command results, VNC session negotiation, process-list results) instead of only polling.
///
/// One difference from the WPF version: ConsoleDiagnostics.WriteLine (a static file writer tied to the
/// WPF app's %ProgramData% layout) is swapped for an injected ILogger - the idiomatic ASP.NET Core way
/// to get the same "what is this background connection doing" visibility.
///
/// Registered as scoped DI so one instance lives per Blazor circuit (= one instance per logged-in
/// browser tab), matching how the WPF Console created one instance per DashboardWindow.
/// </summary>
public class LabHubClient : IAsyncDisposable
{
    private readonly ILogger<LabHubClient> _logger;
    private HubConnection? _connection;

    public string SessionId { get; } = Guid.NewGuid().ToString();

    /// <summary>
    /// true = the real-time push channel is up, so the dashboard is seeing changes the moment the Server
    /// publishes them. false = the 10s REST poll in Home.razor is the only thing keeping the page fresh.
    ///
    /// Exposed (the WPF Console never needed this) purely so the UI can be honest about it: a dashboard
    /// that has silently lost its live feed and is showing minute-old state, while looking exactly like a
    /// live one, is more dangerous than a dashboard that is visibly stale - "no machines flagged" means
    /// something completely different depending on which of the two you're looking at.
    /// </summary>
    public bool IsConnected => _connection?.State == HubConnectionState.Connected;

    /// <summary>Fired whenever <see cref="IsConnected"/> may have flipped. Same threading caveat as below.</summary>
    public event Action? ConnectionStateChanged;

    /// <summary>
    /// Fired from the SignalR client's background thread, not from a Blazor component's render context -
    /// subscribers must marshal back via `await InvokeAsync(StateHasChanged)` on the owning component.
    /// </summary>
    public event Action<ClientMachineDto>? ClientStatusChanged;
    public event Action<string, int, string>? VncSessionReady;
    public event Action<string, string>? VncSessionFailed;
    public event Action<CommandResultDto>? CommandResultChanged;
    public event Action<string, string>? ProcessListReady;
    public event Action<string, string>? ProcessListFailed;

    public LabHubClient(ILogger<LabHubClient> logger)
    {
        _logger = logger;
    }

    public async Task StartAsync(string baseUrl, CancellationToken ct)
    {
        var hubUrl = baseUrl.TrimEnd('/') + "/hubs/lab";

        _connection = new HubConnectionBuilder()
            .WithUrl(hubUrl, options =>
            {
                // WP1 (2026-10-30): pin thumbprint เดียวกับ ApiClient (ดู ServerCertificatePinning) - ไม่
                // มีผลถ้า hubUrl เป็น http://
                options.HttpMessageHandlerFactory = _ => ServerCertificatePinning.CreateHandler();
            })
            .WithAutomaticReconnect(new ExponentialBackoffRetryPolicy())
            .Build();

        _connection.On<ClientMachineDto>("ClientStatusChanged", dto => ClientStatusChanged?.Invoke(dto));
        _connection.On<string, int, string>("VncSessionReady", (clientGuid, port, password) =>
        {
            _logger.LogDebug("VncSessionReady - ClientGuid: {ClientGuid}, Port: {Port} (SessionId: {SessionId})", clientGuid, port, SessionId);
            VncSessionReady?.Invoke(clientGuid, port, password);
        });
        _connection.On<string, string>("VncSessionFailed", (clientGuid, reason) =>
        {
            _logger.LogDebug("VncSessionFailed - ClientGuid: {ClientGuid}, Reason: {Reason}", clientGuid, reason);
            VncSessionFailed?.Invoke(clientGuid, reason);
        });
        _connection.On<CommandResultDto>("CommandResultChanged", dto => CommandResultChanged?.Invoke(dto));
        _connection.On<string, string>("ProcessListReady", (clientGuid, json) =>
        {
            _logger.LogDebug("ProcessListReady - ClientGuid: {ClientGuid}", clientGuid);
            ProcessListReady?.Invoke(clientGuid, json);
        });
        _connection.On<string, string>("ProcessListFailed", (clientGuid, reason) =>
        {
            _logger.LogDebug("ProcessListFailed - ClientGuid: {ClientGuid}, Reason: {Reason}", clientGuid, reason);
            ProcessListFailed?.Invoke(clientGuid, reason);
        });

        _connection.Reconnecting += ex =>
        {
            _logger.LogInformation(ex, "LabHub reconnecting");
            ConnectionStateChanged?.Invoke();
            return Task.CompletedTask;
        };
        _connection.Reconnected += async _ =>
        {
            _logger.LogInformation("LabHub reconnected - rejoining console groups");
            ConnectionStateChanged?.Invoke();
            await RegisterConsoleSafeAsync();
        };
        _connection.Closed += ex =>
        {
            _logger.LogInformation(ex, "LabHub connection closed - falling back to REST polling only");
            ConnectionStateChanged?.Invoke();
            return Task.CompletedTask;
        };

        await ConnectWithRetryAsync(ct);
    }

    private async Task ConnectWithRetryAsync(CancellationToken ct)
    {
        var attempt = 0;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await _connection!.StartAsync(ct);
                _logger.LogInformation("LabHub connected (ConnectionId: {ConnectionId})", _connection.ConnectionId);
                ConnectionStateChanged?.Invoke();
                await RegisterConsoleSafeAsync();
                return;
            }
            catch (Exception ex)
            {
                var delay = ExponentialBackoffRetryPolicy.GetDelay(attempt);
                _logger.LogWarning(ex, "LabHub connect failed - retry in {DelaySeconds}s", delay.TotalSeconds);
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

    private async Task RegisterConsoleSafeAsync()
    {
        try
        {
            await _connection!.InvokeAsync("RegisterConsole", SessionId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "RegisterConsole failed");
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

/// <summary>Retry policy แบบ exponential backoff: 0, 2, 5, 10, 30 วิ แล้ววนซ้ำที่ 30 วิไปเรื่อยๆ (เหมือนฝั่ง WPF Console/Agent ทุกจุด)</summary>
internal class ExponentialBackoffRetryPolicy : IRetryPolicy
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
