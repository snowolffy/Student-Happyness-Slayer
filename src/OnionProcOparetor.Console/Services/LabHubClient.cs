using Microsoft.AspNetCore.SignalR.Client;
using OnionProcOparetor.Console.Models;

namespace OnionProcOparetor.Console.Services;

/// <summary>
/// เชื่อมต่อ SignalR Hub (LabHub) ที่ Server เพื่อรับ live status update ของ client แบบ real-time
/// เป็นชั้นเสริมคู่ขนานกับ DispatcherTimer polling เดิมใน DashboardWindow (ไม่แทนที่)
/// ถ้า connect ไม่ได้/หลุด Dashboard ยังอัปเดตข้อมูลได้ตามปกติผ่าน poll ทุก 10 วิ (fail-secure)
/// </summary>
public class LabHubClient : IAsyncDisposable
{
    private HubConnection? _connection;

    /// <summary>
    /// เรียกทุกครั้งที่ได้ status update ของ client เครื่องใดเครื่องหนึ่งจาก server
    /// หมายเหตุ: เรียกจาก SignalR background thread ไม่ใช่ UI thread - ผู้ subscribe ต้อง
    /// marshal เข้า UI thread เอง (เช่นผ่าน Dispatcher.Invoke)
    /// </summary>
    public event Action<ClientMachineDto>? ClientStatusChanged;

    /// <summary>
    /// เริ่มต่อ SignalR ไปยัง {baseUrl}/hubs/lab - ไม่ throw ออกไปแม้ server จะยังไม่พร้อม/offline
    /// มี retry loop ของตัวเองข้างใน เรียกแบบ fire-and-forget ได้เลย (ไม่ block caller)
    /// </summary>
    public async Task StartAsync(string baseUrl, CancellationToken ct)
    {
        var hubUrl = baseUrl.TrimEnd('/') + "/hubs/lab";

        _connection = new HubConnectionBuilder()
            .WithUrl(hubUrl)
            .WithAutomaticReconnect(new ExponentialBackoffRetryPolicy())
            .Build();

        _connection.On<ClientMachineDto>("ClientStatusChanged", dto => ClientStatusChanged?.Invoke(dto));

        _connection.Reconnecting += ex =>
        {
            System.Diagnostics.Debug.WriteLine($"[LabHubClient] Reconnecting: {ex?.Message}");
            return Task.CompletedTask;
        };

        _connection.Reconnected += async _ =>
        {
            System.Diagnostics.Debug.WriteLine("[LabHubClient] Reconnected - join group \"console\" อีกครั้ง");
            await RegisterConsoleSafeAsync();
        };

        _connection.Closed += ex =>
        {
            System.Diagnostics.Debug.WriteLine($"[LabHubClient] Closed: {ex?.Message} - Dashboard ยังอัปเดตผ่าน poll ตามปกติ");
            return Task.CompletedTask;
        };

        await ConnectWithRetryAsync(ct);
    }

    /// <summary>
    /// retry การ start เชื่อมต่อครั้งแรกเองไม่มีที่สิ้นสุด (จนกว่าจะสำเร็จหรือถูกยกเลิก) - กันกรณี
    /// Server ยังไม่พร้อมตอน Console connect (WithAutomaticReconnect ทำงานได้แค่หลัง connect สำเร็จครั้งแรกแล้วเท่านั้น)
    /// </summary>
    private async Task ConnectWithRetryAsync(CancellationToken ct)
    {
        var attempt = 0;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await _connection!.StartAsync(ct);
                System.Diagnostics.Debug.WriteLine("[LabHubClient] Connected");
                await RegisterConsoleSafeAsync();
                return;
            }
            catch (Exception ex)
            {
                var delay = ExponentialBackoffRetryPolicy.GetDelay(attempt);
                System.Diagnostics.Debug.WriteLine(
                    $"[LabHubClient] Connect failed ({ex.Message}) - retry in {delay.TotalSeconds}s");
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
            await _connection!.InvokeAsync("RegisterConsole");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LabHubClient] RegisterConsole failed: {ex.Message}");
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
/// (เหมือนฝั่ง Agent ทุกจุด - เครือข่ายโรงเรียนไม่เสถียร กันไม่ให้ retry ถี่เกินจนโหลด server)
/// </summary>
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
