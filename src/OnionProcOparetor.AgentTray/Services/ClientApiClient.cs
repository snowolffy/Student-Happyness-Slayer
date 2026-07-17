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

    /// <summary>Login ผ่าน Server กลาง (ใช้ account เดียวกับ Console GUI)</summary>
    public async Task<(bool Success, string Message)> LoginToServerAsync(string serverBaseUrl, string username, string password)
    {
        try
        {
            using var serverClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var url = serverBaseUrl.TrimEnd('/') + "/api/auth/login";
            var response = await serverClient.PostAsJsonAsync(url, new { username, password });

            if (!response.IsSuccessStatusCode)
            {
                return (false, "Username หรือ Password ไม่ถูกต้อง");
            }

            return (true, "Login สำเร็จ");
        }
        catch (Exception ex)
        {
            return (false, $"ต่อ Server ไม่ได้: {ex.Message}");
        }
    }
}
