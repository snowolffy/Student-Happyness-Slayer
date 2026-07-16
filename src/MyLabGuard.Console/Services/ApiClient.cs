using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MyLabGuard.Console.Models;

namespace MyLabGuard.Console.Services;

/// <summary>
/// คุยกับ MyLabGuard.Server ผ่าน HTTP REST API
/// เก็บ token หลัง login ไว้ในตัวเอง แนบใน header ทุก request ที่ต้อง auth
/// </summary>
public class ApiClient
{
    private readonly HttpClient _httpClient;
    private string? _token;

    public string? BaseUrl { get; private set; }
    public bool IsLoggedIn => !string.IsNullOrEmpty(_token);

    public ApiClient()
    {
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10)
        };
    }

    /// <summary>ตั้ง base URL จาก IP:Port ที่ user กรอกในหน้า Connect</summary>
    public void SetServer(string ipAndPort)
    {
        BaseUrl = ipAndPort.StartsWith("http://") || ipAndPort.StartsWith("https://")
            ? ipAndPort
            : $"http://{ipAndPort}";
        _httpClient.BaseAddress = new Uri(BaseUrl);
    }

    public async Task<(bool Success, string Message)> LoginAsync(string username, string password)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync("/api/auth/login",
                new { username, password });

            if (!response.IsSuccessStatusCode)
            {
                return (false, $"Login failed: {response.StatusCode}");
            }

            var result = await response.Content.ReadFromJsonAsync<LoginResponse>();
            if (result is null || string.IsNullOrEmpty(result.Token))
            {
                return (false, "Login response ไม่ถูกต้อง");
            }

            _token = result.Token;
            _httpClient.DefaultRequestHeaders.Remove("X-Auth-Token");
            _httpClient.DefaultRequestHeaders.Add("X-Auth-Token", _token);

            return (true, "Login สำเร็จ");
        }
        catch (HttpRequestException ex)
        {
            return (false, $"ต่อ server ไม่ได้: {ex.Message}");
        }
        catch (TaskCanceledException)
        {
            return (false, "หมดเวลาเชื่อมต่อ (timeout) - เช็ค IP:Port อีกครั้ง");
        }
    }

    public async Task<List<ClientMachineDto>> GetClientsAsync()
    {
        var result = await _httpClient.GetFromJsonAsync<List<ClientMachineDto>>("/api/clients");
        return result ?? new List<ClientMachineDto>();
    }

    public async Task<bool> ToggleClientAsync(int clientId)
    {
        var response = await _httpClient.PostAsync($"/api/clients/{clientId}/toggle", null);
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> ToggleGlobalAsync()
    {
        var response = await _httpClient.PostAsync("/api/global/toggle", null);
        return response.IsSuccessStatusCode;
    }

    public async Task<List<RuleDto>> GetRulesAsync()
    {
        var result = await _httpClient.GetFromJsonAsync<List<RuleDto>>("/api/rules");
        return result ?? new List<RuleDto>();
    }

    public async Task<bool> AddRuleAsync(RuleDto rule)
    {
        var response = await _httpClient.PostAsJsonAsync("/api/rules", rule);
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> DeleteRuleAsync(int ruleId)
    {
        var response = await _httpClient.DeleteAsync($"/api/rules/{ruleId}");
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> ToggleRuleAsync(int ruleId)
    {
        var response = await _httpClient.PostAsync($"/api/rules/{ruleId}/toggle", null);
        return response.IsSuccessStatusCode;
    }

    public async Task<List<LogEntryDto>> GetLogsAsync(int take = 100)
    {
        var result = await _httpClient.GetFromJsonAsync<List<LogEntryDto>>($"/api/logs?take={take}");
        return result ?? new List<LogEntryDto>();
    }
}