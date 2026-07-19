using System.Net.Http;
using System.Net.Http.Json;
using OnionProcOparetor.Console.Models;

namespace OnionProcOparetor.Console.Services;

/// <summary>
/// คุยกับ OnionProcOparetor.Server ผ่าน HTTP REST API
/// เก็บ token หลัง login ไว้ในตัวเอง แนบใน header ทุก request ที่ต้อง auth
/// </summary>
public class ApiClient
{
    private readonly HttpClient _httpClient;
    private string? _token;

    public string? BaseUrl { get; private set; }
    public string? Username { get; private set; }
    public bool IsLoggedIn => !string.IsNullOrEmpty(_token);

    /// <summary>true = admin ที่ login อยู่ตอนนี้ยังไม่เคยเปลี่ยน password จาก default</summary>
    public bool HasDefaultPassword { get; private set; }

    /// <summary>Id ของ admin ที่ login อยู่ตอนนี้ (ใช้เรียก change-password)</summary>
    public int AdminId { get; private set; }

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
            HasDefaultPassword = result.HasDefaultPassword;
            AdminId = result.AdminId;
            Username = username;

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

    /// <summary>เปลี่ยน password ของ admin ที่ login อยู่ตอนนี้ (ใช้ตอนบังคับเปลี่ยนจาก default หรือเปลี่ยนเองทีหลัง)</summary>
    public async Task<(bool Success, string Message)> ChangePasswordAsync(string newPassword)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync(
                $"/api/admin/users/{AdminId}/change-password",
                new { newPassword });

            if (!response.IsSuccessStatusCode)
            {
                string errorMessage = $"เปลี่ยน password ไม่สำเร็จ: {response.StatusCode}";
                try
                {
                    var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
                    if (!string.IsNullOrEmpty(error?.Error))
                    {
                        errorMessage = error.Error;
                    }
                }
                catch
                {
                    // response ไม่ใช่ JSON (เช่น dev exception page เป็น HTML) - ใช้ default message ข้างบนแทน ไม่ throw ต่อ
                }
                return (false, errorMessage);
            }

            // เปลี่ยนสำเร็จแล้ว - อัพเดต flag ในตัวเองด้วย จะได้ไม่ติด 403 ซ้ำใน request ถัดไป
            HasDefaultPassword = false;
            return (true, "เปลี่ยน password สำเร็จ");
        }
        catch (HttpRequestException ex)
        {
            return (false, $"ต่อ server ไม่ได้: {ex.Message}");
        }
        catch (TaskCanceledException)
        {
            return (false, "หมดเวลาเชื่อมต่อ (timeout)");
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

    public async Task<List<UserDto>> GetUsersAsync()
    {
        var result = await _httpClient.GetFromJsonAsync<List<UserDto>>("/api/admin/users");
        return result ?? new List<UserDto>();
    }

    public async Task<(bool Success, string Message)> CreateUserAsync(string username, string password)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync("/api/admin/users", new { username, password });
            if (response.IsSuccessStatusCode)
            {
                return (true, "สร้าง user สำเร็จ");
            }

            string errorMessage = $"สร้าง user ไม่สำเร็จ: {response.StatusCode}";
            try
            {
                var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
                if (!string.IsNullOrEmpty(error?.Error))
                {
                    errorMessage = error.Error;
                }
            }
            catch
            {
                // response ไม่ใช่ JSON - ใช้ default message
            }
            return (false, errorMessage);
        }
        catch (Exception ex)
        {
            return (false, $"เกิดข้อผิดพลาด: {ex.Message}");
        }
    }

    public async Task<(bool Success, string Message)> DeleteUserAsync(int userId)
    {
        try
        {
            var response = await _httpClient.DeleteAsync($"/api/admin/users/{userId}");
            if (response.IsSuccessStatusCode)
            {
                return (true, "ลบ user สำเร็จ");
            }

            string errorMessage = $"ลบ user ไม่สำเร็จ: {response.StatusCode}";
            try
            {
                var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
                if (!string.IsNullOrEmpty(error?.Error))
                {
                    errorMessage = error.Error;
                }
            }
            catch
            {
                // response ไม่ใช่ JSON - ใช้ default message
            }
            return (false, errorMessage);
        }
        catch (Exception ex)
        {
            return (false, $"เกิดข้อผิดพลาด: {ex.Message}");
        }
    }

    public async Task<bool> UpdateClientSettingsAsync(int clientId, int? pollIntervalOverrideSeconds)
    {
        var response = await _httpClient.PutAsJsonAsync(
            $"/api/clients/{clientId}/settings",
            new { pollIntervalOverrideSeconds });
        return response.IsSuccessStatusCode;
    }

    public async Task<(bool Success, string Message)> ResetUserPasswordAsync(int userId, string newPassword)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync(
                $"/api/admin/users/{userId}/reset-password",
                new { newPassword });

            if (!response.IsSuccessStatusCode)
            {
                string errorMessage = $"Reset password ไม่สำเร็จ: {response.StatusCode}";
                try
                {
                    var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
                    if (!string.IsNullOrEmpty(error?.Error))
                    {
                        errorMessage = error.Error;
                    }
                }
                catch { }
                return (false, errorMessage);
            }

            return (true, "Reset password สำเร็จ");
        }
        catch (HttpRequestException ex)
        {
            return (false, $"ต่อ server ไม่ได้: {ex.Message}");
        }
    }

}

/// <summary>โครงสร้าง error response ทั่วไปที่ server คืนมา เช่น { "error": "..." }</summary>
public class ErrorResponse
{
    public string? Error { get; set; }
}
