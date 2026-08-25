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
            await _httpClient.PostAsJsonAsync("/vnc/session-result", new
            {
                requestingConsoleSessionId,
                success,
                pid,
                password,
                failureReason
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
            await _httpClient.PostAsJsonAsync("/processes/list-result", new
            {
                requestingConsoleSessionId,
                success,
                processes,
                failureReason
            });
        }
        catch
        {
            // Agent local API ไม่ตอบก็ปล่อยผ่าน - ไม่มีอะไรทำต่อได้อยู่ดี (Console จะเห็น timeout เอง)
        }
    }

    /// <summary>
    /// สั่ง Agent Service ให้ toggle "Bypass" (ตัด/ต่อการเชื่อมต่อกับ Server ทั้งหมด) - คืนค่า bypass
    /// state ที่ยืนยันแล้วจาก Agent จริงๆ (ไม่ใช่แค่ echo ค่าที่ส่งไป) คืน null ถ้า Agent local API
    /// ไม่ตอบ (เช่น service ไม่ได้รันอยู่) - caller ต้องไม่ถือว่า toggle สำเร็จถ้าได้ null กลับมา
    /// </summary>
    public async Task<bool?> SetBypassAsync(bool bypassed)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync("/bypass/set", new { bypassed });
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var result = await response.Content.ReadFromJsonAsync<BypassSetResponseDto>();
            return result?.Bypassed;
        }
        catch
        {
            return null;
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
    public async Task<(bool Success, string Message)> LoginToServerAsync(string primaryBaseUrl, string? fallbackBaseUrl, string username, string password)
    {
        var (success, reachable, message) = await TryLoginAsync(primaryBaseUrl, username, password);
        if (success || reachable || string.IsNullOrWhiteSpace(fallbackBaseUrl))
        {
            return (success, message);
        }

        var (fallbackSuccess, _, fallbackMessage) = await TryLoginAsync(fallbackBaseUrl, username, password);
        return (fallbackSuccess, fallbackMessage);
    }

    private static async Task<(bool Success, bool Reachable, string Message)> TryLoginAsync(string serverBaseUrl, string username, string password)
    {
        try
        {
            using var serverClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var url = serverBaseUrl.TrimEnd('/') + "/api/auth/login";
            var response = await serverClient.PostAsJsonAsync(url, new { username, password });

            if (!response.IsSuccessStatusCode)
            {
                // ต่อ server ได้จริง แค่ credentials ผิด - ไม่ใช่กรณี "ต่อไม่ได้" ที่ควร retry ไป fallback
                return (false, true, "Username หรือ Password ไม่ถูกต้อง");
            }

            return (true, true, "Login สำเร็จ");
        }
        catch (Exception)
        {
            // ไม่โชว์ ex.Message ตรงๆ เพราะ HttpRequestException ของ .NET มักยาวเกินกว่าจะแสดงใน
            // LoginWindow ที่เป็นหน้าต่างขนาดคงที่ (ResizeMode="NoResize") ทำให้ข้อความล้นออกนอกกรอบ
            return (false, false, "ต่อ Server ไม่ได้ - เช็คว่า Server เปิดอยู่และตั้งค่า IP:Port ถูกต้อง");
        }
    }
}
