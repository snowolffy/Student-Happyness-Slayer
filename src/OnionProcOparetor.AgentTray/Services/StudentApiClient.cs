using System.Net.Http;
using System.Net.Http.Json;
using OnionProcOparetor.AgentTray.Models;

namespace OnionProcOparetor.AgentTray.Services;

/// <summary>
/// คุยกับ Server ตรงๆ สำหรับ login/logout/เปลี่ยนรหัสผ่านของนักเรียน (WP3-3, 2026-10-31) - ตามที่
/// Server/Endpoints/StudentAuthEndpoints.cs (WP3-1) ตั้งใจออกแบบไว้: "AgentTray ยิงตรงมาที่ Server ด้วย
/// ClientGuid ใน body เท่านั้น ไม่ผ่าน Agent loopback API เลย" - ต่างจาก ClientApiClient ที่คุยกับ Agent
/// (localhost:8788) คลาสนี้คุยกับ Server ตัวจริงโดยตรง (เหมือน ClientApiClient.TryLoginAsync ตอน admin
/// login) ไม่ต้องมี auth token เพราะ endpoint ฝั่ง Server ไม่บังคับ (ดู doc comment บนคลาสนั้น)
///
/// primaryBaseUrl/fallbackBaseUrl มาจาก appsettings.json เดียวกับที่ LoginWindow ใช้ (ServerSettings:BaseUrl/
/// FallbackBaseUrl) - ลอง primary ก่อนเสมอ ถ้า "ต่อไม่ได้เลย" (ไม่ใช่รหัสผิด) ค่อยลอง fallback
/// </summary>
public class StudentApiClient
{
    private readonly string _primaryBaseUrl;
    private readonly string? _fallbackBaseUrl;

    public StudentApiClient(string primaryBaseUrl, string? fallbackBaseUrl)
    {
        _primaryBaseUrl = primaryBaseUrl;
        _fallbackBaseUrl = fallbackBaseUrl;
    }

    public Task<StudentLoginResultDto> LoginAsync(string clientGuid, string? machineName, string identifier, string password, bool force = false) =>
        PostViaAsync<StudentLoginResultDto>("/api/students/login", new { clientGuid, machineName, identifier, password, force });

    public Task<StudentLoginResultDto> ChangePasswordAsync(string pendingToken, string newPassword, string confirmPassword) =>
        PostViaAsync<StudentLoginResultDto>("/api/students/change-password", new { pendingToken, newPassword, confirmPassword });

    public async Task<bool> LogoutAsync(string clientGuid, int? sessionId, StudentSessionEndReason reason)
    {
        if (await PostLogoutViaAsync(_primaryBaseUrl, clientGuid, sessionId, reason))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(_fallbackBaseUrl) &&
               await PostLogoutViaAsync(_fallbackBaseUrl!, clientGuid, sessionId, reason);
    }

    private async Task<bool> PostLogoutViaAsync(string baseUrl, string clientGuid, int? sessionId, StudentSessionEndReason reason)
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var url = baseUrl.TrimEnd('/') + "/api/students/logout";
            var response = await client.PostAsJsonAsync(url, new { clientGuid, sessionId, reason = (int)reason });
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private async Task<T?> PostViaAsync<T>(string path, object body) where T : class
    {
        var (result, reachable) = await TryPostAsync<T>(_primaryBaseUrl, path, body);
        if (result is not null || reachable)
        {
            return result;
        }

        if (!string.IsNullOrWhiteSpace(_fallbackBaseUrl))
        {
            (result, _) = await TryPostAsync<T>(_fallbackBaseUrl!, path, body);
        }

        return result;
    }

    private static async Task<(T? Result, bool Reachable)> TryPostAsync<T>(string baseUrl, string path, object body) where T : class
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var url = baseUrl.TrimEnd('/') + path;
            var response = await client.PostAsJsonAsync(url, body);

            // ต่อ Server ได้จริง (ไม่ว่า status code อะไร) = reachable=true กัน retry ไป fallback ที่ไม่
            // มีประโยชน์ (เช่น 400 BadRequest ที่ credentials เดียวกันจะผิดที่ fallback เหมือนกัน)
            var text = await response.Content.ReadAsStringAsync();
            if (string.IsNullOrWhiteSpace(text))
            {
                return (null, true);
            }

            var result = System.Text.Json.JsonSerializer.Deserialize<T>(text,
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return (result, true);
        }
        catch
        {
            return (null, false);
        }
    }
}
