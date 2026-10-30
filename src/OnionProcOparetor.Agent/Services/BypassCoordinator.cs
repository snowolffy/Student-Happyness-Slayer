using System.Net.Http.Json;

namespace OnionProcOparetor.Agent.Services;

/// <summary>
/// จุดเดียวที่ toggle "Bypass" จริง - เรียกจาก POST /bypass/set (local loopback API, ดู Program.cs)
///
/// WP2 (2026-10-30): เดิม endpoint นี้ไม่มี auth เลย - loopback bind ไม่ได้แปลว่ามีแค่ AgentTray เรียกได้
/// process อื่นบนเครื่องเดียวกัน (เช่นนักเรียนเขียน script เอง) ยิง curl ตรงมาที่ 127.0.0.1:8788/bypass/set
/// ก็ตัดการเชื่อมต่อกับ Server ได้เลยโดยไม่ต้องผ่าน login ของ AgentTray ตอนนี้ตั้งเป็น true ต้องแนบ
/// staffSessionToken ที่ Server ออกให้ (ผูกกับ clientGuid เครื่องนี้ + บัญชีที่ login) เสมอ - AgentTray
/// ขอ token นี้เองหลัง login สำเร็จ (ดู ClientApiClient.RequestStaffSessionTokenAsync) ถ้าไม่มี/ผิด/
/// หมดอายุ/ตรวจกับ Server ไม่ได้ (Server ล่ม) = ปฏิเสธเสมอ (fail-secure ตามที่ roadmap ระบุ)
///
/// ตั้งเป็น false (reconnect) ไม่ต้องมี token เพราะเป็นทิศทางที่ปลอดภัยกว่าเดิมอยู่แล้ว (คืนการควบคุม
/// กลับให้ Server) - เหมือน endpoint อื่นๆ ที่ loopback + login gate ของ AgentTray เองพอสำหรับทิศทางนี้
///
/// ตัดขาด/ต่อกลับผ่าน LabHubConnection.DisposeAsync()/StartAsync() ที่มีอยู่แล้ว (ไม่ต้องเพิ่ม method
/// ใหม่ฝั่งนั้นเลย) - DisposeAsync ตัด SignalR ทันที, StartAsync สร้าง connection ใหม่ + retry loop
/// เดิมที่ Worker ใช้ตอน start เองอยู่แล้ว ส่วน HTTP poll loop ฝั่ง Worker.cs เช็ค AgentBypassState.IsBypassed
/// เองทุกรอบ - ไม่ต้องมีอะไรพิเศษเพิ่มที่นี่สำหรับ poll
///
/// SemaphoreSlim กัน race ถ้ามีการยิง toggle ซ้อนกันเร็วๆ (ไม่ควรเกิดจริงเพราะมาจาก UI เดียว แต่กันไว้)
/// </summary>
public class BypassCoordinator
{
    private readonly ILogger<BypassCoordinator> _logger;
    private readonly AgentBypassState _bypassState;
    private readonly LabHubConnection _labHubConnection;
    private readonly ClientState _clientState;
    private readonly ServerClient _serverClient;
    private readonly HttpClient _httpClient;
    private readonly IHostApplicationLifetime _appLifetime;
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    // token ที่กำลัง active อยู่ตอนนี้ (ถ้ามี) - ใช้กัน timer auto-revert ของ session เก่าไปปิด bypass ของ
    // session ใหม่กว่าโดยไม่ตั้งใจ (ดู ScheduleAutoRevert) ไม่ได้ผูกกับ AgentBypassState เพราะ field นั้น
    // เก็บแค่ true/false ไม่พอสำหรับเช็คว่า "เป็น session เดียวกันกับที่ตั้ง timer ไว้หรือเปล่า"
    private string? _activeStaffSessionToken;

    public BypassCoordinator(
        ILogger<BypassCoordinator> logger,
        AgentBypassState bypassState,
        LabHubConnection labHubConnection,
        ClientState clientState,
        ServerClient serverClient,
        IHttpClientFactory httpClientFactory,
        IHostApplicationLifetime appLifetime)
    {
        _logger = logger;
        _bypassState = bypassState;
        _labHubConnection = labHubConnection;
        _clientState = clientState;
        _serverClient = serverClient;
        // HttpClient แยกจากของ ServerClient โดยตั้งใจ (ไม่แตะ ServerClient.cs เลย - เป็นไฟล์ของ WP5) แค่
        // อ่าน ServerClient.ActiveBaseUrl แบบ public property เพื่อรู้ว่า Server ตัวที่ใช้งานจริงอยู่ที่ไหน
        _httpClient = httpClientFactory.CreateClient(nameof(BypassCoordinator));
        _httpClient.Timeout = TimeSpan.FromSeconds(10);
        _appLifetime = appLifetime;
    }

    /// <summary>คืนค่า bypass state ที่ยืนยันแล้วหลัง toggle + error message ถ้าถูกปฏิเสธ (null = สำเร็จ)</summary>
    public async Task<BypassSetResult> SetBypassedAsync(bool bypassed, string? staffSessionToken)
    {
        await _semaphore.WaitAsync();
        try
        {
            if (bypassed)
            {
                if (string.IsNullOrWhiteSpace(staffSessionToken))
                {
                    _logger.LogWarning("ปฏิเสธ /bypass/set - ไม่มี staff session token แนบมา");
                    return new BypassSetResult(_bypassState.IsBypassed, "ต้อง login ผ่าน AgentTray ก่อนจึงใช้ Bypass ได้");
                }

                if (_bypassState.IsBypassed && _activeStaffSessionToken == staffSessionToken)
                {
                    return new BypassSetResult(true, null); // ตั้งไว้แบบนี้อยู่แล้วด้วย token เดิม - ไม่ต้องทำอะไรซ้ำ
                }

                var (valid, reason, expiresAtUtc) = await ValidateStaffSessionTokenAsync(staffSessionToken);
                if (!valid)
                {
                    _logger.LogWarning("ปฏิเสธ /bypass/set - token ไม่ผ่านการตรวจกับ Server ({Reason})", reason);
                    return new BypassSetResult(_bypassState.IsBypassed, reason);
                }

                _activeStaffSessionToken = staffSessionToken;
                _bypassState.Set(true);
                _logger.LogInformation(
                    "Bypass เปิดใช้งาน (token ผ่านการตรวจกับ Server แล้ว หมดอายุ {ExpiresAtUtc:o}) - ตัดการเชื่อมต่อจาก Server ทั้งหมด",
                    expiresAtUtc);
                await _labHubConnection.DisposeAsync();

                if (expiresAtUtc is { } expiry)
                {
                    ScheduleAutoRevert(staffSessionToken, expiry);
                }

                return new BypassSetResult(true, null);
            }
            else
            {
                if (!_bypassState.IsBypassed)
                {
                    return new BypassSetResult(false, null); // ไม่ได้ bypass อยู่แล้ว - ไม่ต้องทำอะไรซ้ำ
                }

                _activeStaffSessionToken = null;
                _bypassState.Set(false);
                _logger.LogInformation("Bypass ปิดใช้งาน - เชื่อมต่อ Server ใหม่ (SignalR reconnect, HTTP poll กลับมาทำงานรอบถัดไป)");
                _ = _labHubConnection.StartAsync(_clientState.ClientGuid, _appLifetime.ApplicationStopping);

                return new BypassSetResult(false, null);
            }
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <summary>
    /// ถาม Server ว่า token นี้ valid กับ clientGuid เครื่องนี้ไหม - ติดต่อไม่ได้/Server ล่ม = ปฏิเสธเสมอ
    /// (fail-secure ตามที่ roadmap ระบุไว้ชัดเจนสำหรับ WP2 - รหัสฉุกเฉินทดแทนกรณี Server ล่มเป็นงานขั้น 3)
    /// </summary>
    private async Task<(bool Valid, string? Reason, DateTime? ExpiresAtUtc)> ValidateStaffSessionTokenAsync(string token)
    {
        try
        {
            var url = _serverClient.ActiveBaseUrl.TrimEnd('/') + "/api/staff-session/validate";
            var response = await _httpClient.PostAsJsonAsync(url, new { clientGuid = _clientState.ClientGuid, token });
            if (!response.IsSuccessStatusCode)
            {
                return (false, "Server ปฏิเสธ token (HTTP ไม่สำเร็จ)", null);
            }

            var result = await response.Content.ReadFromJsonAsync<StaffSessionValidateResponse>();
            if (result is null || !result.Valid)
            {
                return (false, "Token ไม่ถูกต้องหรือหมดอายุแล้ว - login ใหม่ผ่าน AgentTray", null);
            }

            return (true, null, result.ExpiresAt);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ตรวจ staff session token กับ Server ไม่สำเร็จ - ปฏิเสธ bypass (fail-secure)");
            return (false, "ติดต่อ Server เพื่อตรวจ token ไม่ได้ - ปฏิเสธ bypass", null);
        }
    }

    /// <summary>
    /// ตั้งเวลาปิด bypass อัตโนมัติตอน token หมดอายุ - ไม่พึ่ง Worker.cs/poll loop เลย (ไฟล์นั้นเป็นของ
    /// WP5) ทำงานอิสระในตัวเองด้วย Task.Delay ธรรมดา เช็ค token ที่ active อยู่ตอนนั้นซ้ำอีกครั้งก่อน
    /// revert จริง กัน race ที่ session ใหม่ (login/ขอ token ใหม่ หรือครูกด reconnect เองไปแล้ว) มาทับ
    /// ก่อนหมดเวลาของ session เก่า
    /// </summary>
    private void ScheduleAutoRevert(string token, DateTime expiresAtUtc)
    {
        var delay = expiresAtUtc - DateTime.UtcNow;
        if (delay < TimeSpan.Zero)
        {
            delay = TimeSpan.Zero;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(delay, _appLifetime.ApplicationStopping);
            }
            catch (OperationCanceledException)
            {
                return; // service กำลังปิดตัว - ไม่ต้องทำอะไรต่อ
            }

            await _semaphore.WaitAsync();
            try
            {
                if (_bypassState.IsBypassed && _activeStaffSessionToken == token)
                {
                    _activeStaffSessionToken = null;
                    _bypassState.Set(false);
                    _logger.LogInformation("Staff session token หมดอายุแล้ว - ปิด Bypass อัตโนมัติ เชื่อมต่อ Server กลับ");
                    _ = _labHubConnection.StartAsync(_clientState.ClientGuid, _appLifetime.ApplicationStopping);
                }
            }
            finally
            {
                _semaphore.Release();
            }
        });
    }
}

/// <summary>ผลลัพธ์ของ SetBypassedAsync - ErrorMessage ไม่ว่าง = ถูกปฏิเสธ (Bypassed คือ state จริงปัจจุบัน ไม่ใช่ค่าที่ขอ)</summary>
public readonly record struct BypassSetResult(bool Bypassed, string? ErrorMessage);

/// <summary>รูปร่าง JSON ตอบกลับจาก POST /api/staff-session/validate ฝั่ง Server</summary>
internal sealed record StaffSessionValidateResponse(bool Valid, DateTime? ExpiresAt);
