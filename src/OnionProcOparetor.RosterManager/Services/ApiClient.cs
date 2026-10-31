using System.Net.Http;
using System.Net.Http.Json;
using OnionProcOparetor.RosterManager.Models;

namespace OnionProcOparetor.RosterManager.Services;

/// <summary>
/// คุยกับ OnionProcOparetor.Server ผ่าน HTTP REST API เดียวกับ Console (ใช้ Server/DB ตัวเดียวกัน -
/// ดู docs/roadmap.md WP3-2 "Roster Manager เป็นโปรแกรมแยก (WPF) แต่ใช้ Server และ DB ตัวเดียวกัน")
/// เก็บ token หลัง login ไว้ในตัวเอง แนบใน header ทุก request ที่ต้อง auth
///
/// endpoint ที่ใช้ทั้งหมดอยู่ใน Server/Endpoints/AuthEndpoints.cs, RosterEndpoints.cs และ
/// StudentSessionEndpoints.cs (WP3-1) - ไม่มี endpoint ใหม่ถูกเพิ่มจาก WP3-2 เลย
///
/// ไม่มี cert pinning/trust-cert UI แบบ Console (ServerCertificatePinning + TrustCertificateWindow) -
/// นอกขอบเขตของ WP3-2 ตาม roadmap (WP1 ระบุรายชื่อ client ที่ต้องทำไว้ชัดเจนตอนที่ Roster Manager
/// ยังไม่มีอยู่) ถ้าเลือกต่อ HTTPS จะพึ่ง OS certificate store ปกติ - ใช้ได้ทันทีบนเครื่อง Server เอง
/// เพราะ MSI ของ WP1 ติดตั้ง cert ลง LocalMachine\Root ของเครื่องนั้นอยู่แล้ว
/// </summary>
public class ApiClient
{
    private readonly HttpClient _httpClient;
    private string? _token;

    public string? BaseUrl { get; private set; }
    public string? Username { get; private set; }
    public bool IsLoggedIn => !string.IsNullOrEmpty(_token);
    public bool HasDefaultPassword { get; private set; }
    public int AdminId { get; private set; }
    public string Role { get; private set; } = "Admin";
    public bool IsAdmin => Role != "Teacher";
    public string DisplayName { get; private set; } = string.Empty;

    public ApiClient()
    {
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
    }

    public void SetServer(string ipAndPort, bool useHttps = false)
    {
        BaseUrl = ipAndPort.StartsWith("http://") || ipAndPort.StartsWith("https://")
            ? ipAndPort
            : $"{(useHttps ? "https" : "http")}://{ipAndPort}";
        _httpClient.BaseAddress = new Uri(BaseUrl);
    }

    public async Task<(bool Success, string Message)> LoginAsync(string username, string password)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync("/api/auth/login", new { username, password });

            if (!response.IsSuccessStatusCode)
            {
                return (false, $"เข้าสู่ระบบไม่สำเร็จ: {response.StatusCode}");
            }

            var result = await response.Content.ReadFromJsonAsync<LoginResponse>();
            if (result is null || string.IsNullOrEmpty(result.Token))
            {
                return (false, "คำตอบจาก Server ไม่ถูกต้อง");
            }

            _token = result.Token;
            HasDefaultPassword = result.HasDefaultPassword;
            AdminId = result.AdminId;
            Username = username;
            Role = string.IsNullOrWhiteSpace(result.Role) ? "Admin" : result.Role;
            DisplayName = string.IsNullOrWhiteSpace(result.DisplayName) ? username : result.DisplayName;

            _httpClient.DefaultRequestHeaders.Remove("X-Auth-Token");
            _httpClient.DefaultRequestHeaders.Add("X-Auth-Token", _token);

            return (true, "เข้าสู่ระบบสำเร็จ");
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

    /// <summary>เปลี่ยน password ของ admin ที่ login อยู่ตอนนี้ - ใช้ตอนบังคับเปลี่ยนจาก default (เหมือน Console)</summary>
    public async Task<(bool Success, string Message)> ChangeMyPasswordAsync(string newPassword)
    {
        var result = await PostForErrorAsync($"/api/admin/users/{AdminId}/change-password", new { newPassword });
        if (result.Success)
        {
            HasDefaultPassword = false;
        }
        return result;
    }

    // ---- Roster (RosterEndpoints.cs) ----

    public async Task<List<StudentDto>> GetStudentsAsync(string? className = null, string? search = null, string? status = null)
    {
        var query = new List<string>();
        if (!string.IsNullOrWhiteSpace(className)) query.Add($"className={Uri.EscapeDataString(className)}");
        if (!string.IsNullOrWhiteSpace(search)) query.Add($"search={Uri.EscapeDataString(search)}");
        if (!string.IsNullOrWhiteSpace(status)) query.Add($"status={Uri.EscapeDataString(status)}");

        var url = "/api/students" + (query.Count > 0 ? "?" + string.Join('&', query) : string.Empty);
        var result = await _httpClient.GetFromJsonAsync<List<StudentDto>>(url);
        return result ?? new List<StudentDto>();
    }

    /// <summary>จำนวนคนต่อชั้น/ห้อง (เฉพาะที่ยังไม่ปิดใช้งาน) - ใช้เติม sidebar "ชั้น/ห้อง" ของ nf-roster
    /// และตารางของ nf-promote (endpoint เดียวกันทั้งสองหน้า)</summary>
    public async Task<List<ClassCountDto>> GetClassCountsAsync()
    {
        var result = await _httpClient.GetFromJsonAsync<List<ClassCountDto>>("/api/students/promote/preview");
        return result ?? new List<ClassCountDto>();
    }

    public async Task<(bool Success, string Message, StudentDto? Student, string? GeneratedPassword)> CreateStudentAsync(
        string studentCode, string fullName, string? schoolEmail, string className, string? password, bool forceChangePasswordOnFirstLogin)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync("/api/students", new
            {
                studentCode,
                fullName,
                schoolEmail,
                className,
                password,
                forceChangePasswordOnFirstLogin
            });

            if (!response.IsSuccessStatusCode)
            {
                return (false, await ReadErrorAsync(response), null, null);
            }

            var created = await response.Content.ReadFromJsonAsync<CreateStudentResultDto>();
            return (true, "เพิ่มนักเรียนสำเร็จ", created?.Student, created?.GeneratedPassword);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return (false, $"ต่อ server ไม่ได้: {ex.Message}", null, null);
        }
    }

    public async Task<(bool Success, string Message)> UpdateStudentAsync(int id, string studentCode, string fullName, string? schoolEmail, string className) =>
        await PutForErrorAsync($"/api/students/{id}", new { studentCode, fullName, schoolEmail, className });

    public async Task<(bool Success, string Message)> SetDisabledAsync(int id, bool disabled) =>
        await PostForErrorAsync($"/api/students/{id}/{(disabled ? "disable" : "enable")}", null);

    public async Task<(bool Success, string Message)> MoveClassAsync(int id, string className) =>
        await PutForErrorAsync($"/api/students/{id}/class", new { className });

    public async Task<(bool Success, string Message, string? GeneratedPassword)> ResetPasswordAsync(int id)
    {
        try
        {
            var response = await _httpClient.PostAsync($"/api/students/{id}/reset-password", null);
            if (!response.IsSuccessStatusCode)
            {
                return (false, await ReadErrorAsync(response), null);
            }

            var result = await response.Content.ReadFromJsonAsync<ResetPasswordResultDto>();
            return (true, "รีเซ็ตรหัสผ่านสำเร็จ", result?.GeneratedPassword);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return (false, $"ต่อ server ไม่ได้: {ex.Message}", null);
        }
    }

    public async Task<(bool Success, string Message)> UnlockAsync(int id) =>
        await PostForErrorAsync($"/api/students/{id}/unlock", null);

    public async Task<(bool Success, ImportSummaryDto? Summary, string? Error)> ImportDryRunAsync(ImportStudentsRequest request) =>
        await ImportAsync("/api/students/import/dry-run", request);

    public async Task<(bool Success, ImportSummaryDto? Summary, string? Error)> ImportCommitAsync(ImportStudentsRequest request) =>
        await ImportAsync("/api/students/import/commit", request);

    private async Task<(bool Success, ImportSummaryDto? Summary, string? Error)> ImportAsync(string url, ImportStudentsRequest request)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync(url, request);
            if (!response.IsSuccessStatusCode)
            {
                return (false, null, await ReadErrorAsync(response));
            }

            var summary = await response.Content.ReadFromJsonAsync<ImportSummaryDto>();
            return (true, summary, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return (false, null, $"ต่อ server ไม่ได้: {ex.Message}");
        }
    }

    public async Task<(bool Success, string Message, int PromotedCount, int GraduatedCount, string? BackupFileName)> PromoteAsync(
        List<(string FromClassName, string? ToClassName)> mappings, int graduateAction)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync("/api/students/promote", new
            {
                mappings = mappings.Select(m => new { fromClassName = m.FromClassName, toClassName = m.ToClassName }),
                graduateAction
            });

            if (!response.IsSuccessStatusCode)
            {
                return (false, await ReadErrorAsync(response), 0, 0, null);
            }

            var result = await response.Content.ReadFromJsonAsync<PromoteResultDto>();
            return (true, "เลื่อนชั้นสำเร็จ", result?.PromotedCount ?? 0, result?.GraduatedCount ?? 0, result?.BackupMainFileName);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return (false, $"ต่อ server ไม่ได้: {ex.Message}", 0, 0, null);
        }
    }

    // ---- StudentSessionEndpoints.cs ----

    /// <summary>"ใช้งานล่าสุด" ของนักเรียนคนหนึ่ง - ใช้ search=รหัสนักเรียน เพื่อกรองเฉพาะคนนั้น (endpoint
    /// นี้ไม่มี studentId ตรงๆ - ใช้ search ของมันตามที่ StudentSessionEndpoints.cs รองรับ)</summary>
    public async Task<List<SessionHistoryRowDto>> GetSessionHistoryAsync(string? search = null, int take = 20)
    {
        try
        {
            var url = $"/api/students/sessions/history?take={take}";
            if (!string.IsNullOrWhiteSpace(search)) url += $"&search={Uri.EscapeDataString(search)}";

            var result = await _httpClient.GetFromJsonAsync<List<SessionHistoryRowDto>>(url);
            return result ?? new List<SessionHistoryRowDto>();
        }
        catch
        {
            return new List<SessionHistoryRowDto>();
        }
    }

    // ---- helpers ----

    private async Task<(bool Success, string Message)> PostForErrorAsync(string url, object? body)
    {
        try
        {
            var response = body is null ? await _httpClient.PostAsync(url, null) : await _httpClient.PostAsJsonAsync(url, body);
            return response.IsSuccessStatusCode ? (true, "สำเร็จ") : (false, await ReadErrorAsync(response));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return (false, $"ต่อ server ไม่ได้: {ex.Message}");
        }
    }

    private async Task<(bool Success, string Message)> PutForErrorAsync(string url, object body)
    {
        try
        {
            var response = await _httpClient.PutAsJsonAsync(url, body);
            return response.IsSuccessStatusCode ? (true, "สำเร็จ") : (false, await ReadErrorAsync(response));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return (false, $"ต่อ server ไม่ได้: {ex.Message}");
        }
    }

    private static async Task<string> ReadErrorAsync(HttpResponseMessage response)
    {
        try
        {
            var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
            if (!string.IsNullOrEmpty(error?.Error))
            {
                return error.Error;
            }
        }
        catch
        {
            // response ไม่ใช่ JSON (เช่น dev exception page) - ใช้ default message ด้านล่างแทน
        }

        return $"Server ตอบ {(int)response.StatusCode} {response.StatusCode}";
    }

    private sealed class CreateStudentResultDto
    {
        public StudentDto? Student { get; set; }
        public string? GeneratedPassword { get; set; }
    }

    private sealed class ResetPasswordResultDto
    {
        public string? GeneratedPassword { get; set; }
    }

    private sealed class PromoteResultDto
    {
        public int PromotedCount { get; set; }
        public int GraduatedCount { get; set; }
        public string? BackupMainFileName { get; set; }
    }
}

public class ErrorResponse
{
    public string? Error { get; set; }
}
