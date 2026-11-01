using System.Net.Http;
using System.Net.Http.Json;
using OnionProcOparetor.AgentTray.Models;

namespace OnionProcOparetor.AgentTray.Services;

/// <summary>
/// คุยกับ OnionProcOparetor.Agent (Windows Service ตัวจริง) ผ่าน local API ที่ localhost:8788
/// Tray เป็นแค่หน้าต่างแสดงผล ไม่มี logic ตรวจจับเอง
/// </summary>
public class ClientApiClient
{
    private readonly HttpClient _httpClient;

    // WP2 (2026-10-30): token ที่ Server ออกให้หลัง login สำเร็จ ผูกกับ clientGuid ของเครื่องนี้ +
    // บัญชีที่ login (ดู RequestStaffSessionTokenAsync) ต้องแนบไปกับ POST /bypass/set ทุกครั้งที่จะ
    // เปิด bypass - ไม่มี/หมดอายุ = Agent ปฏิเสธเอง (fail-secure, ดู BypassCoordinator ฝั่ง Agent)
    // เก็บไว้ใน instance นี้เท่านั้น (ไม่ persist) หายไปตอน AgentTray restart ซึ่งถูกต้องแล้ว - ต้อง
    // login ใหม่ทุกครั้งที่ AgentTray เริ่มทำงานใหม่
    private string? _staffSessionToken;

    /// <summary>WP3-3 (2026-10-31): เวลาหมดอายุของ staff session token ล่าสุดที่ขอมา (ถ้ามี) - ใช้โชว์
    /// นับถอยหลังบนป้าย StaffSessionBadgeWindow เท่านั้น ไม่ใช่แหล่งความจริงของการตัดสินใจ bypass จริง
    /// (Agent/BypassCoordinator เป็นคนตรวจ/บังคับหมดอายุเองอยู่แล้ว ดู ScheduleAutoRevert)</summary>
    public DateTime? StaffSessionExpiresAtUtc { get; private set; }

    /// <summary>ชื่อผู้ใช้ครูที่เริ่ม staff session ล่าสุด (ถ้ามี) - เก็บไว้โชว์บนป้ายเท่านั้น</summary>
    public string? StaffSessionUsername { get; private set; }

    public ClientApiClient()
    {
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri("http://localhost:8788"),
            Timeout = TimeSpan.FromSeconds(5)
        };
    }

    /// <summary>ดึงสถานะปัจจุบัน คืน null ถ้า Agent Service ไม่ได้รันอยู่ (เช่นถูก restart)</summary>
    public async Task<ClientStatusDto?> GetStatusAsync()
    {
        try
        {
            return await _httpClient.GetFromJsonAsync<ClientStatusDto>("/status");
        }
        catch
        {
            return null;
        }
    }

    public async Task<List<LogEntryDto>> GetRecentLogsAsync()
    {
        try
        {
            var result = await _httpClient.GetFromJsonAsync<List<LogEntryDto>>("/logs/recent");
            return result ?? new List<LogEntryDto>();
        }
        catch
        {
            return new List<LogEntryDto>();
        }
    }

    /// <summary>
    /// แจ้งผลกลับ Agent หลัง start winvnc.exe เอง (เพราะ AgentTray เป็นที่เดียวที่ capture หน้าจอได้
    /// - ดู VncProcessManager) - Agent (LocalSystem) เป็นคนทำ port discovery/firewall rule/relay ไป
    /// Console ต่อ เพราะมีสิทธิ์เต็ม ไม่เหมือน AgentTray ที่รันเป็น user ธรรมดา ยิงแบบ fire-and-forget
    /// (ไม่ throw ถ้า Agent local API ไม่ตอบ - ไม่มีอะไรทำต่อได้อยู่ดีถ้า Agent ไม่รับ)
    /// </summary>
    public async Task PostVncSessionResultAsync(string requestingConsoleSessionId, bool success, int pid, string? password, string? failureReason)
    {
        try
        {
            // WP2b: แนบ secret ล่าสุดที่ได้จาก Agent ผ่าน pipe (ดู LoopbackSecretStore) - Agent ปฏิเสธ
            // (401) ถ้าไม่มี/ผิด กัน process อื่นบนเครื่องเดียวกันยิง PID ปลอมเข้ามาที่ endpoint นี้ตรงๆ
            await _httpClient.PostAsJsonAsync("/vnc/session-result", new
            {
                requestingConsoleSessionId,
                success,
                pid,
                password,
                failureReason,
                secret = LoopbackSecretStore.Current
            });
        }
        catch
        {
            // Agent local API ไม่ตอบก็ปล่อยผ่าน - ไม่มีอะไรทำต่อได้อยู่ดี (Console จะเห็น timeout เอง)
        }
    }

    /// <summary>
    /// แจ้งผลกลับ Agent หลังสแกนโปรแกรมที่มีหน้าต่างบน desktop เสร็จ (เพราะ AgentTray เป็นที่เดียวที่
    /// เห็นหน้าต่างของ user session ได้จริง - ดู RunningProcessScanner) Agent เป็นคนส่งต่อไป Server/
    /// Console ผ่าน SignalR relay ต่อ ยิงแบบ fire-and-forget (ไม่ throw ถ้า Agent local API ไม่ตอบ)
    /// </summary>
    public async Task PostRunningProcessesResultAsync(string requestingConsoleSessionId, bool success, List<RunningProcessDto> processes, string? failureReason)
    {
        try
        {
            // WP2b: แนบ secret ล่าสุดเหมือนกับ PostVncSessionResultAsync ด้านบน (เหตุผลเดียวกัน)
            await _httpClient.PostAsJsonAsync("/processes/list-result", new
            {
                requestingConsoleSessionId,
                success,
                processes,
                failureReason,
                secret = LoopbackSecretStore.Current
            });
        }
        catch
        {
            // Agent local API ไม่ตอบก็ปล่อยผ่าน - ไม่มีอะไรทำต่อได้อยู่ดี (Console จะเห็น timeout เอง)
        }
    }

    /// <summary>
    /// สั่ง Agent Service ให้ toggle "Bypass" (ตัด/ต่อการเชื่อมต่อกับ Server ทั้งหมด) - คืนค่า bypass
    /// state ที่ยืนยันแล้วจาก Agent จริงๆ (ไม่ใช่แค่ echo ค่าที่ส่งไป) คืน Bypassed: null ถ้า Agent
    /// ปฏิเสธ (ไม่ว่าเพราะ service ไม่ตอบ หรือเพราะ token ไม่มี/หมดอายุ - ดู AgentReachable แยกว่า
    /// เป็นกรณีไหน) caller ต้องไม่ถือว่า toggle สำเร็จถ้า Bypassed เป็น null
    ///
    /// WP2 (2026-10-30): แนบ _staffSessionToken ที่ขอไว้ตอน login ไปด้วยทุกครั้ง - Agent เป็นคนตรวจ
    /// จริงกับ Server (ดู BypassCoordinator) ที่นี่แค่ส่งต่อเฉยๆ ไม่ validate เองฝั่ง AgentTray
    /// </summary>
    public async Task<(bool? Bypassed, bool AgentReachable)> SetBypassAsync(bool bypassed)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync("/bypass/set", new { bypassed, staffSessionToken = _staffSessionToken });
            if (!response.IsSuccessStatusCode)
            {
                // Agent ตอบจริง แค่ปฏิเสธ (เช่น token ไม่มี/หมดอายุ) - ต่างจากกรณี service ไม่ตอบเลย
                return (null, true);
            }

            var result = await response.Content.ReadFromJsonAsync<BypassSetResponseDto>();
            return (result?.Bypassed, true);
        }
        catch
        {
            return (null, false);
        }
    }

    /// <summary>
    /// Login ผ่าน Server กลาง (ใช้ account เดียวกับ Console GUI) - ลอง primaryBaseUrl ก่อนเสมอ
    /// ถ้า "ต่อไม่ได้เลย" (network unreachable - ไม่ใช่ username/password ผิด) และมี fallbackBaseUrl
    /// ตั้งค่าไว้ (ไม่ว่าง) ค่อยลองซ้ำที่ fallback (เช่น Tailscale) หนึ่งครั้ง เพื่อให้ครูล็อกอินดู status
    /// ได้แม้เครื่องกำลังใช้ fallback อยู่ (เดิมมีแค่ primaryBaseUrl ตัวเดียว ไม่มี fallback เลย - login
    /// จะพังตลอดเวลาที่เครื่อง fallback อยู่ ทั้งที่ poll/command channel ใช้งานได้ปกติผ่าน fallback)
    ///
    /// ไม่ retry ถ้า primary "ตอบกลับมา" แต่ username/password ผิด (reachable=true, success=false) -
    /// credentials เดียวกันจะผิดที่ fallback ด้วยเสมอ ลองซ้ำไปก็เสียเวลาเปล่าและทำให้ error message ช้าลง
    /// </summary>
    public Task<(bool Success, string Message)> LoginToServerAsync(string primaryBaseUrl, string? fallbackBaseUrl, string username, string password) =>
        LoginToServerAsync(primaryBaseUrl, fallbackBaseUrl, username, password, minutes: null);

    /// <summary>
    /// overload ที่รับ minutes (WP3-3, 2026-10-31) - ใช้จาก StaffSessionStartWindow ที่ครูเลือกเวลา
    /// 15/30/60 นาทีก่อนกด "เริ่ม Staff session" (ดู wireframe st-staff-start) null = ให้ Server ใช้
    /// default ของตัวเอง (เหมือนพฤติกรรมเดิมของ overload ข้างบนที่ LoginWindow ยังใช้อยู่)
    /// </summary>
    public async Task<(bool Success, string Message)> LoginToServerAsync(string primaryBaseUrl, string? fallbackBaseUrl, string username, string password, int? minutes)
    {
        var (success, reachable, message, adminToken) = await TryLoginAsync(primaryBaseUrl, username, password);
        var usedBaseUrl = primaryBaseUrl;

        if (!success && !reachable && !string.IsNullOrWhiteSpace(fallbackBaseUrl))
        {
            (success, reachable, message, adminToken) = await TryLoginAsync(fallbackBaseUrl, username, password);
            usedBaseUrl = fallbackBaseUrl;
        }

        // WP2 (2026-10-30): login สำเร็จแล้วขอ bypass token ต่อทันที (ผูกกับ clientGuid ของเครื่องนี้ +
        // บัญชีที่ login) เก็บไว้ใช้ตอนกดปุ่ม BYPASS ใน StatusWindow - ไม่ throw/ไม่ทำให้ login ล้มเหลว
        // ถ้าขอ token ไม่สำเร็จ (เช่น Agent local API ไม่ตอบตอนนั้น) ปุ่ม BYPASS จะถูก Agent ปฏิเสธเอง
        // ตอนกดจริงถ้าไม่มี token (fail-secure, ดู BypassCoordinator)
        if (success && adminToken is not null)
        {
            await RequestStaffSessionTokenAsync(usedBaseUrl, adminToken, minutes);
            StaffSessionUsername = username;
        }

        return (success, message);
    }

    /// <summary>
    /// ขอ bypass token จาก Server (POST /api/staff-session/start) ด้วย adminToken ที่ได้จาก login
    /// ข้างบน + clientGuid ของเครื่องนี้ (อ่านจาก GET /status ของ Agent) เก็บผลไว้ใน _staffSessionToken
    /// ให้ SetBypassAsync ใช้ต่อ ล้มเหลวแบบเงียบๆ ได้ทุกจุด (ไม่มีอะไรทำต่อได้ถ้าขอไม่สำเร็จ - ปุ่ม
    /// BYPASS จะโดน Agent ปฏิเสธเองพร้อมเหตุผลตอนกดจริง)
    /// </summary>
    private async Task RequestStaffSessionTokenAsync(string serverBaseUrl, string adminToken, int? minutes = null)
    {
        try
        {
            var status = await GetStatusAsync();
            if (status is null || string.IsNullOrWhiteSpace(status.ClientGuid))
            {
                return;
            }

            // WP1 (2026-10-30) - HTTPS: ไม่ override cert validation ที่นี่ (เหตุผลเดียวกับ Agent's
            // ServerClient - MSI เดียวกันติดตั้ง cert ของ Server ไว้ใน LocalMachine\Root ของเครื่องนี้
            // แล้ว) serverBaseUrl ใช้ https:// ได้ตรงๆ ถ้าตั้งไว้แบบนั้น
            using var serverClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var url = serverBaseUrl.TrimEnd('/') + "/api/staff-session/start";
            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = JsonContent.Create(new { clientGuid = status.ClientGuid, minutes })
            };
            request.Headers.Add("X-Auth-Token", adminToken);

            var response = await serverClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                _staffSessionToken = null;
                StaffSessionExpiresAtUtc = null;
                return;
            }

            var result = await response.Content.ReadFromJsonAsync<StaffSessionStartResponseDto>();
            _staffSessionToken = result?.Token;
            StaffSessionExpiresAtUtc = result?.ExpiresAt;
        }
        catch
        {
            _staffSessionToken = null;
            StaffSessionExpiresAtUtc = null;
        }
    }

    /// <summary>
    /// แจ้ง Agent (local loopback) ว่านักเรียนคนไหน login/logout อยู่ที่เครื่องนี้ตอนนี้ (WP3-3,
    /// 2026-10-31) - เรียกหลัง StudentApiClient.LoginAsync/LogoutAsync สำเร็จกับ Server ตรงๆ เท่านั้น
    /// (Agent ไม่รู้เรื่อง login/logout โดยตรงเลย - ดู doc comment บน endpoint ฝั่ง Agent) ยิงแบบ
    /// fire-and-forget (ไม่ throw ถ้า Agent local API ไม่ตอบ - แค่ poll/log ของ Agent จะไม่มี studentCode
    /// ติดไปด้วย ไม่กระทบ session จริงที่อยู่ฝั่ง Server)
    /// </summary>
    public async Task NotifyStudentSessionAsync(bool loggedIn, string? studentCode = null, string? studentName = null)
    {
        try
        {
            await _httpClient.PostAsJsonAsync("/student-session", new { loggedIn, studentCode, studentName });
        }
        catch
        {
            // Agent local API ไม่ตอบก็ปล่อยผ่าน - ไม่มีอะไรทำต่อได้อยู่ดี (ดู doc comment ด้านบน)
        }
    }

    /// <summary>
    /// ยกมือขอความช่วยเหลือจากครู (WP4-2, 2026-11-01) - ยิงไป Agent local API ตรงๆ (Agent เป็นคนส่งต่อ
    /// Server พร้อมแนบ agent secret ของเครื่องนี้ - ดู ServerClient.SendRaiseHandAsync) คืน null ถ้า Agent
    /// local API ไม่ตอบเลย (service ไม่ได้รันอยู่) ต่างจาก Success=false ที่แปลว่า Agent ตอบจริงแต่ถูก
    /// cooldown หรือมีรายการค้างอยู่แล้ว - caller ต้องแยกสองกรณีนี้ (ดู RaiseHandWindow.xaml.cs)
    /// </summary>
    public async Task<RaiseHandSendResponseDto?> RaiseHandAsync(string reason, string? note)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync("/raisehand/send", new { reason, note });
            return await response.Content.ReadFromJsonAsync<RaiseHandSendResponseDto>();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>ยกเลิกยกมือที่ค้างอยู่ของเครื่องนี้ (WP4-2, 2026-11-01) - เรียกจากปุ่ม "ยกเลิก" บนป้าย RaiseHandBadgeWindow</summary>
    public async Task<bool> CancelRaiseHandAsync()
    {
        try
        {
            var response = await _httpClient.PostAsync("/raisehand/cancel", null);
            if (!response.IsSuccessStatusCode)
            {
                return false;
            }

            var result = await response.Content.ReadFromJsonAsync<RaiseHandCancelResponseDto>();
            return result?.Success ?? false;
        }
        catch
        {
            return false;
        }
    }

    private sealed record RaiseHandCancelResponseDto(bool Success);

    /// <summary>ปิด staff session info ที่เก็บไว้ (เรียกตอนกด "จบ" หรือ session หมดอายุไปแล้ว) - ไม่กระทบ bypass จริงฝั่ง Agent (SetBypassAsync(false) เป็นคนจัดการนั้น)</summary>
    public void ClearStaffSessionInfo()
    {
        _staffSessionToken = null;
        StaffSessionExpiresAtUtc = null;
        StaffSessionUsername = null;
    }

    private static async Task<(bool Success, bool Reachable, string Message, string? AdminToken)> TryLoginAsync(string serverBaseUrl, string username, string password)
    {
        try
        {
            // WP1 (2026-10-30) - HTTPS: เหตุผลเดียวกับ RequestStaffSessionTokenAsync ด้านบน (เชื่อถือผ่าน
            // Windows trust store - MSI ติดตั้ง cert ของ Server ไว้แล้ว ไม่ override ที่นี่)
            using var serverClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var url = serverBaseUrl.TrimEnd('/') + "/api/auth/login";
            var response = await serverClient.PostAsJsonAsync(url, new { username, password });

            if (!response.IsSuccessStatusCode)
            {
                // ต่อ server ได้จริง แค่ credentials ผิด - ไม่ใช่กรณี "ต่อไม่ได้" ที่ควร retry ไป fallback
                return (false, true, "Username หรือ Password ไม่ถูกต้อง", null);
            }

            var loginResult = await response.Content.ReadFromJsonAsync<LoginResponseDto>();
            return (true, true, "Login สำเร็จ", loginResult?.Token);
        }
        catch (Exception)
        {
            // ไม่โชว์ ex.Message ตรงๆ เพราะ HttpRequestException ของ .NET มักยาวเกินกว่าจะแสดงใน
            // LoginWindow ที่เป็นหน้าต่างขนาดคงที่ (ResizeMode="NoResize") ทำให้ข้อความล้นออกนอกกรอบ
            return (false, false, "ต่อ Server ไม่ได้ - เช็คว่า Server เปิดอยู่และตั้งค่า IP:Port ถูกต้อง", null);
        }
    }

    /// <summary>
    /// ตรวจรหัสฉุกเฉิน (WP3-4, 2026-10-31) - ยิงไป Agent local API ตรงๆ (ไม่ผ่าน Server เลย - Agent
    /// ตรวจ TOTP แบบ offline ด้วย secret ที่ MSI patch เข้ามา) คืน null ถ้า Agent ไม่ตอบ (ไม่ได้รันอยู่)
    /// </summary>
    public async Task<bool?> VerifyEmergencyCodeAsync(string code)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync("/emergency/verify", new { code });
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var result = await response.Content.ReadFromJsonAsync<EmergencyVerifyResponseDto>();
            return result?.Unlocked;
        }
        catch
        {
            return null;
        }
    }

    private sealed record EmergencyVerifyResponseDto(bool Unlocked, DateTime? UnlockedAtUtc);

    /// <summary>รูปร่าง JSON ตอบกลับจาก POST /api/auth/login ฝั่ง Server (ดู AuthEndpoints.cs) - เอาแค่ Token ที่ใช้ต่อ</summary>
    private sealed record LoginResponseDto(string? Token);

    /// <summary>รูปร่าง JSON ตอบกลับจาก POST /api/staff-session/start ฝั่ง Server (ดู StaffSessionEndpoints.cs)</summary>
    private sealed record StaffSessionStartResponseDto(string? Token, DateTime? ExpiresAt);
}
