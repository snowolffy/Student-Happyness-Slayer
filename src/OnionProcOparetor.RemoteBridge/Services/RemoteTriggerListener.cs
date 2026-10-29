using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace OnionProcOparetor.RemoteBridge.Services;

/// <summary>
/// local HTTP listener ที่ ESP32 (ผ่าน Tailscale Funnel) ยิงมาสั่ง lock/unlock ทั้งห้อง - ผูกที่
/// 127.0.0.1 เท่านั้น (ไม่ใช่ LAN/WiFi interface ใดๆ) เพราะสิ่งที่ทำให้มันเข้าถึงได้จากอินเทอร์เน็ต
/// คือ `tailscale funnel &lt;port&gt;` ที่รันแยกต่างหากบนเครื่องเดียวกัน ไม่ใช่ตัว listener นี้เอง
///
/// ใช้ Kestrel (WebApplication) แทน System.Net.HttpListener โดยตั้งใจ - ลองด้วย HttpListener ก่อน
/// แล้วพบจริงว่า Http.sys ปฏิเสธ request ที่ Host header ไม่ตรงกับ prefix เป๊ะๆ ด้วย 400 (Funnel proxy
/// forward Host header เป็นชื่อ .ts.net จริงมาเสมอ ไม่ใช่ "localhost") Kestrel ผูก IP:port ตรงๆ ไม่สน
/// Host header เลยไม่มีปัญหานี้
///
/// ทำแค่ GET /trigger?secret=xxx&amp;locked=true|false โดยตั้งใจ (ไม่ใช่ POST+JSON) เพราะฝั่ง ESP32
/// เขียนด้วย Arduino HTTPClient แบบ query string ง่ายกว่ามาก ไม่ต้องพึ่ง JSON library บนบอร์ด -
/// เป็น GET ที่มีผลข้างเคียง (ไม่ idempotent-safe ตามหลัก REST เป๊ะๆ) ยอมรับ tradeoff นี้เพราะ endpoint
/// นี้มีเจตนาเดียวคือ "overwrite ทับ" อยู่แล้ว ไม่ต้องแคร์ semantics ของ GET ตามหลักวิชาการ
/// </summary>
public sealed class RemoteTriggerListener
{
    private readonly ApiClient _apiClient;
    private readonly RemoteTriggerThrottle _throttle = new();
    private WebApplication? _app;

    public RemoteTriggerListener(ApiClient apiClient)
    {
        _apiClient = apiClient;
    }

    public async Task StartAsync(int port)
    {
        await StopAsync();

        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders(); // ไม่ต้องการ log ของ ASP.NET Core ปนกับของ RemoteBridge เอง
        builder.WebHost.ConfigureKestrel(options => options.ListenLocalhost(port));

        _app = builder.Build();
        _app.MapGet("/trigger", HandleTriggerAsync);

        await _app.StartAsync();
    }

    public async Task StopAsync()
    {
        if (_app is not null)
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
            _app = null;
        }
    }

    private async Task HandleTriggerAsync(HttpContext context)
    {
        if (_throttle.IsLockedOut())
        {
            await RespondAsync(context, StatusCodes.Status429TooManyRequests, "locked out - too many failed attempts, ลองใหม่อีกสักครู่");
            return;
        }

        var config = ConfigStore.Current;
        var providedSecret = context.Request.Query["secret"].ToString();
        if (config is null || string.IsNullOrEmpty(config.RemoteSecret) || !IsSecretValid(config.RemoteSecret, providedSecret))
        {
            _throttle.RecordFailure();
            await RespondAsync(context, StatusCodes.Status401Unauthorized, "invalid secret");
            return;
        }

        _throttle.RecordSuccess();

        if (!_apiClient.IsLoggedIn)
        {
            await RespondAsync(context, StatusCodes.Status503ServiceUnavailable, "bridge ยังไม่ได้ login กับ Server");
            return;
        }

        var lockedParam = context.Request.Query["locked"].ToString();
        if (!bool.TryParse(lockedParam, out var locked))
        {
            await RespondAsync(context, StatusCodes.Status400BadRequest, "ต้องมี query param locked=true หรือ locked=false");
            return;
        }

        var (clientsOk, machines, clientsMessage) = await _apiClient.GetClientsAsync();
        if (!clientsOk || machines is null)
        {
            await RespondAsync(context, StatusCodes.Status502BadGateway, clientsMessage);
            return;
        }

        var (successCount, failCount) = await _apiClient.BroadcastLockAsync(locked, machines.Select(m => m.ClientGuid));
        await RespondAsync(context, StatusCodes.Status200OK, $"{(locked ? "locked" : "unlocked")} success={successCount} fail={failCount}");
    }

    /// <summary>constant-time compare กัน timing attack เดา secret ทีละตัวอักษร</summary>
    private static bool IsSecretValid(string expected, string provided)
    {
        if (string.IsNullOrEmpty(provided))
        {
            return false;
        }

        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var providedBytes = Encoding.UTF8.GetBytes(provided);

        // ความยาวไม่เท่ากันคือผิดแน่นอนอยู่แล้ว แต่ต้องเช็คก่อนเรียก FixedTimeEquals เพราะมันบังคับ
        // ให้สอง buffer ยาวเท่ากัน (โยน exception ถ้าไม่เท่า) - เปิดเผยความยาวไม่ถือเป็นความลับสำคัญพอ
        return expectedBytes.Length == providedBytes.Length && CryptographicOperations.FixedTimeEquals(expectedBytes, providedBytes);
    }

    private static Task RespondAsync(HttpContext context, int status, string body)
    {
        context.Response.StatusCode = status;
        context.Response.ContentType = "text/plain; charset=utf-8";
        return context.Response.WriteAsync(body);
    }
}
