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
    /// GUID สุ่มต่อ Console instance หนึ่งตัว (ไม่ persist ข้าม run) ใช้เป็นชื่อ group ส่วนตัวบน
    /// SignalR hub (ดู LabHub.RegisterConsole) เพื่อให้ Agent ตอบกลับข้อมูลที่ต้องเจาะจง Console
    /// ตัวนี้เท่านั้น (เช่น VncSessionReady) โดยไม่ broadcast ให้ Console เครื่องอื่นเห็น
    /// </summary>
    public string SessionId { get; } = Guid.NewGuid().ToString();

    /// <summary>
    /// เรียกทุกครั้งที่ได้ status update ของ client เครื่องใดเครื่องหนึ่งจาก server
    /// หมายเหตุ: เรียกจาก SignalR background thread ไม่ใช่ UI thread - ผู้ subscribe ต้อง
    /// marshal เข้า UI thread เอง (เช่นผ่าน Dispatcher.Invoke)
    /// </summary>
    public event Action<ClientMachineDto>? ClientStatusChanged;

    /// <summary>
    /// เรียกตอน Agent เปิด VNC session สำเร็จ (clientGuid, port, password) - background thread เหมือนกัน
    /// ต้อง marshal เข้า UI thread เอง clientGuid บอกว่าเป็นของเครื่องไหน (ตั้งแต่ Step 4e) เพราะ Console
    /// เดียวกันอาจมี StartVncSession ค้างรออยู่หลายเครื่องพร้อมกัน (ดู VncSessionPool) - ผู้ subscribe
    /// ต้องกรองด้วย clientGuid ที่ตัวเอง request ไปเองเสมอ ห้ามสมมติว่า event ที่มาถึงเป็นของ request
    /// ล่าสุดที่ส่งไป (อาจเป็นของ request อื่นที่ค้างรออยู่ก่อนหน้าก็ได้)
    /// </summary>
    public event Action<string, int, string>? VncSessionReady;

    /// <summary>เรียกตอน Agent เปิด VNC session ไม่สำเร็จ (clientGuid, reason) - background thread เหมือนกัน (ดู comment VncSessionReady เรื่อง clientGuid)</summary>
    public event Action<string, string>? VncSessionFailed;

    /// <summary>
    /// เรียกทุกครั้งที่ Server แจ้ง "ผลจริง" ของ command ตัวใดตัวหนึ่ง (Agent รายงานผลกลับมา
    /// หรือ Server mark เป็น TimedOut เอง) - หน้า ActionReportWindow ใช้ตัวนี้อัปเดตแถวแบบ real-time
    ///
    /// หมายเหตุเดียวกับ ClientStatusChanged: เรียกจาก SignalR background thread ไม่ใช่ UI thread
    /// ผู้ subscribe ต้อง marshal เข้า UI thread เอง
    /// </summary>
    public event Action<CommandResultDto>? CommandResultChanged;

    /// <summary>
    /// เรียกตอน Agent สแกนโปรแกรมที่มีหน้าต่างบน desktop เสร็จ (clientGuid, processListJson) -
    /// background thread เหมือนกัน ต้อง marshal เข้า UI thread เอง และกรองด้วย clientGuid เอง
    /// (เหตุผลเดียวกับ VncSessionReady - Console ตัวเดียวอาจมี request ค้างรออยู่หลายเครื่องพร้อมกัน)
    /// </summary>
    public event Action<string, string>? ProcessListReady;

    /// <summary>เรียกตอน Agent สแกนไม่สำเร็จ (clientGuid, reason) - background thread เหมือนกัน</summary>
    public event Action<string, string>? ProcessListFailed;

    /// <summary>
    /// เรียกทุกครั้งที่คิวยกมือเปลี่ยน (มีคนยกมือ/ครูกด "ไปช่วยแล้ว"/ล้างคิว) - Server ส่งคิวทั้งก้อนมาเสมอ
    /// ไม่ใช่ diff (ดู RaiseHandQueueEntryResponse.FromQueue ฝั่ง Server) ง่ายกว่าและพอสำหรับคิวที่ไม่น่าจะ
    /// ยาวเกินหลักสิบรายการพร้อมกัน - background thread เหมือน event อื่นในคลาสนี้ ต้อง marshal เข้า UI thread เอง
    /// </summary>
    public event Action<List<RaiseHandQueueEntryDto>>? RaiseHandQueueChanged;

    /// <summary>
    /// สถานะการเชื่อมต่อ SignalR เปลี่ยน (true = ต่ออยู่) - Dashboard ใช้ขึ้นประกาศ "การอัปเดตสด
    /// กำลังเชื่อมต่อใหม่" บนแถบหัว (WP6) ยิงจาก background thread เหมือน event อื่นในคลาสนี้
    /// ยิงซ้ำค่าเดิมได้ (เช่นทุกรอบ retry ที่ต่อไม่ติด) ผู้ฟังต้องรับมือเอง
    /// </summary>
    public event Action<bool>? ConnectionStateChanged;

    /// <summary>
    /// เริ่มต่อ SignalR ไปยัง {baseUrl}/hubs/lab - ไม่ throw ออกไปแม้ server จะยังไม่พร้อม/offline
    /// มี retry loop ของตัวเองข้างใน เรียกแบบ fire-and-forget ได้เลย (ไม่ block caller)
    /// </summary>
    public async Task StartAsync(string baseUrl, CancellationToken ct)
    {
        var hubUrl = baseUrl.TrimEnd('/') + "/hubs/lab";

        _connection = new HubConnectionBuilder()
            .WithUrl(hubUrl, options =>
            {
                // WP1 (2026-10-30): pin thumbprint เดียวกับที่ ApiClient ใช้ (ดู ServerCertificatePinning)
                // ไม่มีผลถ้า hubUrl เป็น http:// (ไม่มี TLS handshake ให้ callback นี้ถูกเรียก)
                options.HttpMessageHandlerFactory = _ => ServerCertificatePinning.CreateHandler();
            })
            .WithAutomaticReconnect(new ExponentialBackoffRetryPolicy())
            .Build();

        _connection.On<ClientMachineDto>("ClientStatusChanged", dto => ClientStatusChanged?.Invoke(dto));
        _connection.On<string, int, string>("VncSessionReady", (clientGuid, port, password) =>
        {
            ConsoleDiagnostics.WriteLine($"[LabHubClient] ได้รับ VncSessionReady จาก hub - ClientGuid: {clientGuid}, Port: {port} (SessionId ของเรา: {SessionId})");
            VncSessionReady?.Invoke(clientGuid, port, password);
        });
        _connection.On<string, string>("VncSessionFailed", (clientGuid, reason) =>
        {
            ConsoleDiagnostics.WriteLine($"[LabHubClient] ได้รับ VncSessionFailed จาก hub - ClientGuid: {clientGuid}, Reason: {reason} (SessionId ของเรา: {SessionId})");
            VncSessionFailed?.Invoke(clientGuid, reason);
        });
        _connection.On<CommandResultDto>("CommandResultChanged", dto => CommandResultChanged?.Invoke(dto));
        _connection.On<string, string>("ProcessListReady", (clientGuid, json) =>
        {
            ConsoleDiagnostics.WriteLine($"[LabHubClient] ได้รับ ProcessListReady จาก hub - ClientGuid: {clientGuid} (SessionId ของเรา: {SessionId})");
            ProcessListReady?.Invoke(clientGuid, json);
        });
        _connection.On<string, string>("ProcessListFailed", (clientGuid, reason) =>
        {
            ConsoleDiagnostics.WriteLine($"[LabHubClient] ได้รับ ProcessListFailed จาก hub - ClientGuid: {clientGuid}, Reason: {reason} (SessionId ของเรา: {SessionId})");
            ProcessListFailed?.Invoke(clientGuid, reason);
        });
        _connection.On<List<RaiseHandQueueEntryDto>>("RaiseHandQueueChanged", queue => RaiseHandQueueChanged?.Invoke(queue));

        _connection.Reconnecting += ex =>
        {
            ConsoleDiagnostics.WriteLine($"[LabHubClient] Reconnecting: {ex?.Message}");
            ConnectionStateChanged?.Invoke(false);
            return Task.CompletedTask;
        };

        _connection.Reconnected += async _ =>
        {
            ConsoleDiagnostics.WriteLine("[LabHubClient] Reconnected - join group \"console\" + session group อีกครั้ง");
            await RegisterConsoleSafeAsync();
            ConnectionStateChanged?.Invoke(true);
        };

        _connection.Closed += ex =>
        {
            ConsoleDiagnostics.WriteLine($"[LabHubClient] Closed: {ex?.Message} - Dashboard ยังอัปเดตผ่าน poll ตามปกติ");
            ConnectionStateChanged?.Invoke(false);
            return Task.CompletedTask;
        };

        ConsoleDiagnostics.WriteLine($"[LabHubClient] กำลังเชื่อมต่อ {hubUrl} (SessionId: {SessionId})");
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
                ConsoleDiagnostics.WriteLine($"[LabHubClient] Connected (ConnectionId: {_connection.ConnectionId})");
                await RegisterConsoleSafeAsync();
                ConnectionStateChanged?.Invoke(true);
                return;
            }
            catch (Exception ex)
            {
                var delay = ExponentialBackoffRetryPolicy.GetDelay(attempt);
                ConsoleDiagnostics.WriteLine(
                    $"[LabHubClient] Connect failed ({ex.Message}) - retry in {delay.TotalSeconds}s");
                ConnectionStateChanged?.Invoke(false);
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
            ConsoleDiagnostics.WriteLine($"[LabHubClient] RegisterConsole สำเร็จ - เข้า group \"console\" + group '{SessionId}' บน SignalR hub แล้ว");
        }
        catch (Exception ex)
        {
            ConsoleDiagnostics.WriteLine($"[LabHubClient] RegisterConsole failed: {ex.Message}");
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
