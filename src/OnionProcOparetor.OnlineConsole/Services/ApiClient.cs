using System.Net.Http.Json;
using OnionProcOparetor.OnlineConsole.Models;

namespace OnionProcOparetor.OnlineConsole.Services;

/// <summary>
/// Ported from OnionProcOparetor.Console/Services/ApiClient.cs - same REST calls against
/// OnionProcOparetor.Server, byte-for-byte where possible so the two clients don't drift apart.
///
/// Two deliberate differences from the WPF version:
///  1. The WPF Console builds a brand-new ApiClient per login attempt because HttpClient.BaseAddress
///     can't change after first use and the server address is typed by the user each time. Here the
///     Server address is a fixed deployment setting (Server:BaseUrl in appsettings.json), so this class
///     is registered once via AddHttpClient&lt;ApiClient&gt;() with that fixed BaseAddress, and one instance
///     lives per Blazor circuit (scoped DI) instead of per login click.
///  2. AttachSession() is new: a Blazor circuit doesn't necessarily start with a fresh LoginAsync call -
///     the browser may already hold a valid auth cookie from an earlier visit, so the token/username/
///     adminId/hasDefaultPassword need to be restored onto a freshly-constructed ApiClient from the
///     cookie's claims (see AuthEndpoints.cs) instead of only being set inside LoginAsync.
/// </summary>
public class ApiClient
{
    private readonly HttpClient _httpClient;

    public string? Username { get; private set; }

    /// <summary>
    /// Exposed (unlike the WPF Console's private _token field) because AuthEndpoints.cs needs it to pack
    /// into the auth cookie's claims after login/change-password - the cookie is what lets this token
    /// survive across requests/circuits for this browser instead of just living in one ApiClient instance.
    /// </summary>
    public string? Token { get; private set; }

    public bool IsLoggedIn => !string.IsNullOrEmpty(Token);

    /// <summary>true = admin ที่ login อยู่ตอนนี้ยังไม่เคยเปลี่ยน password จาก default</summary>
    public bool HasDefaultPassword { get; private set; }

    /// <summary>Id ของ admin ที่ login อยู่ตอนนี้ (ใช้เรียก change-password)</summary>
    public int AdminId { get; private set; }

    public ApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    /// <summary>Server's base URL, needed by callers that also open the LabHub SignalR connection directly (it isn't reachable through this HttpClient).</summary>
    public string ServerBaseUrl => _httpClient.BaseAddress!.ToString();

    /// <summary>เรียกหลัง login สำเร็จผ่าน /login endpoint (ดู AuthEndpoints.cs) - restore session ที่เก็บไว้ใน auth cookie claims กลับเข้า ApiClient instance ของ circuit นี้</summary>
    public void AttachSession(string token, string username, int adminId, bool hasDefaultPassword)
    {
        Token = token;
        Username = username;
        AdminId = adminId;
        HasDefaultPassword = hasDefaultPassword;

        _httpClient.DefaultRequestHeaders.Remove("X-Auth-Token");
        _httpClient.DefaultRequestHeaders.Add("X-Auth-Token", Token);
    }

    public async Task<(bool Success, string Message)> LoginAsync(string username, string password)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync("/api/auth/login", new { username, password });

            if (!response.IsSuccessStatusCode)
            {
                return (false, $"Login failed: {response.StatusCode}");
            }

            var result = await response.Content.ReadFromJsonAsync<LoginResponse>();
            if (result is null || string.IsNullOrEmpty(result.Token))
            {
                return (false, "Login response ไม่ถูกต้อง");
            }

            AttachSession(result.Token, username, result.AdminId, result.HasDefaultPassword);

            return (true, "Login สำเร็จ");
        }
        catch (HttpRequestException ex)
        {
            return (false, $"ต่อ server ไม่ได้: {ex.Message}");
        }
        catch (TaskCanceledException)
        {
            return (false, "หมดเวลาเชื่อมต่อ (timeout) - เช็คว่า Server ทำงานอยู่หรือไม่");
        }
    }

    public async Task<(bool Success, string Message)> ChangePasswordAsync(string newPassword)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync($"/api/admin/users/{AdminId}/change-password", new { newPassword });

            if (!response.IsSuccessStatusCode)
            {
                return (false, await ExtractErrorAsync(response, $"เปลี่ยน password ไม่สำเร็จ: {response.StatusCode}"));
            }

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

    // ---- Program Control ----

    public async Task<List<ProgramVerdictDto>> GetProgramVerdictsAsync(string filter = "flagged", int? category = null)
    {
        var url = $"/api/program-verdicts?filter={Uri.EscapeDataString(filter)}";
        if (category is not null)
        {
            url += $"&category={category.Value}";
        }

        var result = await _httpClient.GetFromJsonAsync<List<ProgramVerdictDto>>(url);
        return result ?? new List<ProgramVerdictDto>();
    }

    public async Task<ProgramVerdictSummaryDto> GetProgramVerdictSummaryAsync(string filter = "flagged")
    {
        var result = await _httpClient.GetFromJsonAsync<ProgramVerdictSummaryDto>($"/api/program-verdicts/summary?filter={Uri.EscapeDataString(filter)}");
        return result ?? new ProgramVerdictSummaryDto();
    }

    public async Task<bool> DecideProgramVerdictAsync(int verdictId, int action)
    {
        var response = await _httpClient.PostAsJsonAsync($"/api/program-verdicts/{verdictId}/decide", new { action });
        return response.IsSuccessStatusCode;
    }

    public async Task<(bool Success, string Message)> SetManualProgramVerdictAsync(RunningProcessDto process, string machineName, int action)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync("/api/program-verdicts/manual", new
            {
                sha256 = process.Sha256,
                fileName = process.ProcessName,
                productName = process.WindowTitle,
                publisher = process.PublisherName,
                isSignedPublisher = process.IsSignedMatch,
                machineName,
                action
            });

            return response.IsSuccessStatusCode
                ? (true, "สำเร็จ")
                : (false, await ExtractErrorAsync(response, $"server ตอบ {(int)response.StatusCode}"));
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

    public async Task<bool> DeleteClientAsync(int clientId)
    {
        var response = await _httpClient.DeleteAsync($"/api/clients/{clientId}");
        return response.IsSuccessStatusCode;
    }

    /// <summary>
    /// อ่านสถานะ enforcement ทั้งห้อง - คืน null ถ้าอ่านไม่ได้ (Server เก่าที่ยังไม่มี endpoint นี้
    /// ก็ตอบ 404 มาเช่นกัน) ผู้เรียกต้องแยกกรณี "ไม่รู้" ออกจาก "รู้ว่าปิดอยู่" ให้ชัด - การเดาว่า
    /// เปิดอยู่เวลาที่อ่านไม่ได้ คือการกลับไปสู่บั๊กเดิมที่ทำให้ค่านี้มองไม่เห็น
    /// </summary>
    public async Task<GlobalStateDto?> GetGlobalStateAsync()
    {
        try
        {
            return await _httpClient.GetFromJsonAsync<GlobalStateDto>("/api/global");
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    /// <summary>ตั้งค่า enforcement ทั้งห้องแบบระบุค่าตรงๆ (idempotent) - ใช้แทน ToggleGlobalAsync</summary>
    public async Task<bool> SetGlobalEnabledAsync(bool enabled)
    {
        var response = await _httpClient.PostAsJsonAsync("/api/global/set", new { enabled });
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

    public async Task<bool> UpdateRuleAsync(RuleDto rule)
    {
        var response = await _httpClient.PutAsJsonAsync($"/api/rules/{rule.Id}", rule);
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

    // ---- Website Control (2026-09-11) - mirror ของ Rule CRUD ข้างบนทุกอย่าง ----

    /// <summary>แบ่งหน้า + ค้นหา + กรองตาม catalog source (2026-09-12) - เดินตามแบบเดียวกับ GetActivityLogAsync</summary>
    public async Task<WebsiteRulesPageDto> GetWebsiteRulesAsync(
        string? search = null, int? sourceCatalogId = null, bool manualOnly = false,
        int page = 1, int pageSize = 100)
    {
        var query = $"page={page}&pageSize={pageSize}&manualOnly={manualOnly}";
        if (!string.IsNullOrWhiteSpace(search)) query += $"&search={Uri.EscapeDataString(search)}";
        if (sourceCatalogId is not null) query += $"&sourceCatalogId={sourceCatalogId}";

        var result = await _httpClient.GetFromJsonAsync<WebsiteRulesPageDto>($"/api/website-rules?{query}");
        return result ?? new WebsiteRulesPageDto();
    }

    public async Task<bool> AddWebsiteRuleAsync(WebsiteRuleDto rule)
    {
        var response = await _httpClient.PostAsJsonAsync("/api/website-rules", rule);
        return response.IsSuccessStatusCode;
    }

    public async Task<BulkAddResultDto?> AddWebsiteRulesBulkAsync(List<string> urlPatterns)
    {
        var response = await _httpClient.PostAsJsonAsync("/api/website-rules/bulk", new { UrlPatterns = urlPatterns });
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<BulkAddResultDto>()
            : null;
    }

    public async Task<bool> UpdateWebsiteRuleAsync(WebsiteRuleDto rule)
    {
        var response = await _httpClient.PutAsJsonAsync($"/api/website-rules/{rule.Id}", rule);
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> DeleteWebsiteRuleAsync(int ruleId)
    {
        var response = await _httpClient.DeleteAsync($"/api/website-rules/{ruleId}");
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> ToggleWebsiteRuleAsync(int ruleId)
    {
        var response = await _httpClient.PostAsync($"/api/website-rules/{ruleId}/toggle", null);
        return response.IsSuccessStatusCode;
    }

    // ---- Website Catalog Sources (2026-09-12) ----

    public async Task<List<WebsiteCatalogSourceDto>> GetWebsiteCatalogSourcesAsync()
    {
        var result = await _httpClient.GetFromJsonAsync<List<WebsiteCatalogSourceDto>>("/api/website-catalog-sources");
        return result ?? new List<WebsiteCatalogSourceDto>();
    }

    public async Task<bool> AddWebsiteCatalogSourceAsync(WebsiteCatalogSourceDto source)
    {
        var response = await _httpClient.PostAsJsonAsync("/api/website-catalog-sources", source);
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> UpdateWebsiteCatalogSourceAsync(WebsiteCatalogSourceDto source)
    {
        var response = await _httpClient.PutAsJsonAsync($"/api/website-catalog-sources/{source.Id}", source);
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> DeleteWebsiteCatalogSourceAsync(int id)
    {
        var response = await _httpClient.DeleteAsync($"/api/website-catalog-sources/{id}");
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> SyncWebsiteCatalogSourceNowAsync(int id)
    {
        var response = await _httpClient.PostAsync($"/api/website-catalog-sources/{id}/sync-now", null);
        return response.IsSuccessStatusCode;
    }

    public async Task<List<RuleOverrideDto>> GetRuleOverridesAsync(string clientGuid)
    {
        var result = await _httpClient.GetFromJsonAsync<List<RuleOverrideDto>>($"/api/clients/{clientGuid}/rule-overrides");
        return result ?? new List<RuleOverrideDto>();
    }

    public async Task<bool> SetRuleOverrideAsync(string clientGuid, int ruleId, bool isEnabled)
    {
        var response = await _httpClient.PutAsJsonAsync($"/api/clients/{clientGuid}/rule-overrides/{ruleId}", new { isEnabled });
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> ClearRuleOverrideAsync(string clientGuid, int ruleId)
    {
        var response = await _httpClient.DeleteAsync($"/api/clients/{clientGuid}/rule-overrides/{ruleId}");
        return response.IsSuccessStatusCode;
    }

    // ---- โหมดสอบ + ชุด Allow-list (WP2-2, 2026-10-31) - ported from OnionProcOparetor.Console/Services/ApiClient.cs ----

    /// <summary>คืน empty list ถ้าอ่านไม่ได้ (Server รุ่นเก่า/ไม่มีสิทธิ์) - หน้าเปิดโหมดสอบยังพิมพ์เว็บเพิ่มเติมเองได้ต่อ ดู ExamMode.razor</summary>
    public async Task<List<AllowListPresetDto>> GetAllowListPresetsAsync()
    {
        try
        {
            var result = await _httpClient.GetFromJsonAsync<List<AllowListPresetDto>>("/api/allowlist-presets");
            return result ?? new List<AllowListPresetDto>();
        }
        catch (Exception)
        {
            return new List<AllowListPresetDto>();
        }
    }

    /// <summary>คืน null ถ้าเริ่มไม่สำเร็จ (validation ไม่ผ่าน/ไม่มีสิทธิ์) - ผู้เรียกต้องการ summary กลับไปแสดงผลทันทีด้วย (ไม่ใช่แค่ bool)</summary>
    public async Task<ExamSessionDto?> StartExamSessionAsync(
        int? roomId, List<string>? clientGuids, List<int> presetIds, List<string> extraDomains, int durationMinutes)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync("/api/exam-sessions/start", new
            {
                roomId,
                clientGuids,
                presetIds,
                extraDomains,
                durationMinutes
            });

            return response.IsSuccessStatusCode
                ? await response.Content.ReadFromJsonAsync<ExamSessionDto>()
                : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public async Task<bool> CancelExamSessionAsync(int id)
    {
        try
        {
            var response = await _httpClient.PostAsync($"/api/exam-sessions/{id}/cancel", null);
            return response.IsSuccessStatusCode;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>คืน empty list ถ้าอ่านไม่ได้ - ใช้เช็คว่ามีโหมดสอบกำลังทำงานอยู่กับเป้าหมายนี้แล้วหรือไม่ (ดู ExamMode.razor)</summary>
    public async Task<List<ExamSessionDto>> GetActiveExamSessionsAsync()
    {
        try
        {
            var result = await _httpClient.GetFromJsonAsync<List<ExamSessionDto>>("/api/exam-sessions/active");
            return result ?? new List<ExamSessionDto>();
        }
        catch (Exception)
        {
            return new List<ExamSessionDto>();
        }
    }

    public async Task<List<LogEntryDto>> GetLogsAsync(int take = 100)
    {
        var result = await _httpClient.GetFromJsonAsync<List<LogEntryDto>>($"/api/logs?take={take}");
        return result ?? new List<LogEntryDto>();
    }

    /// <summary>"ประวัติการใช้เครื่อง" บนมือถือ (WP3-5, 2026-10-31) - ported from
    /// OnionProcOparetor.Console/Services/ApiClient.cs GetStudentSessionHistoryAsync</summary>
    public async Task<List<StudentSessionHistoryRowDto>> GetStudentSessionHistoryAsync(
        int? roomId = null, string? className = null, string? search = null, int take = 500)
    {
        var query = new List<string> { $"take={take}" };
        if (roomId is not null) query.Add($"roomId={roomId.Value}");
        if (!string.IsNullOrWhiteSpace(className)) query.Add($"className={Uri.EscapeDataString(className)}");
        if (!string.IsNullOrWhiteSpace(search)) query.Add($"search={Uri.EscapeDataString(search)}");

        try
        {
            var result = await _httpClient.GetFromJsonAsync<List<StudentSessionHistoryRowDto>>($"/api/students/sessions/history?{string.Join('&', query)}");
            return result ?? new List<StudentSessionHistoryRowDto>();
        }
        catch (Exception)
        {
            return new List<StudentSessionHistoryRowDto>();
        }
    }

    public async Task<ActivityLogPageDto> GetActivityLogAsync(
        string? category = null, string? minSeverity = null, string? clientGuid = null,
        int page = 1, int pageSize = 100)
    {
        var query = new List<string> { $"page={page}", $"pageSize={pageSize}" };
        if (!string.IsNullOrEmpty(category)) query.Add($"category={Uri.EscapeDataString(category)}");
        if (!string.IsNullOrEmpty(minSeverity)) query.Add($"minSeverity={Uri.EscapeDataString(minSeverity)}");
        if (!string.IsNullOrEmpty(clientGuid)) query.Add($"clientGuid={Uri.EscapeDataString(clientGuid)}");

        var result = await _httpClient.GetFromJsonAsync<ActivityLogPageDto>($"/api/activitylog?{string.Join('&', query)}");
        return result ?? new ActivityLogPageDto();
    }

    public async Task<List<UserDto>> GetUsersAsync()
    {
        var result = await _httpClient.GetFromJsonAsync<List<UserDto>>("/api/admin/users");
        return result ?? new List<UserDto>();
    }

    /// <summary>role: 0 = Admin, 1 = Teacher (ตรง enum AdminRole ฝั่ง Server - WP4, 2026-10-30)</summary>
    public async Task<(bool Success, string Message)> CreateUserAsync(string username, string password, string? displayName = null, int role = 0)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync("/api/admin/users", new { username, password, displayName, role });
            return response.IsSuccessStatusCode
                ? (true, "สร้าง user สำเร็จ")
                : (false, await ExtractErrorAsync(response, $"สร้าง user ไม่สำเร็จ: {response.StatusCode}"));
        }
        catch (Exception ex)
        {
            return (false, $"เกิดข้อผิดพลาด: {ex.Message}");
        }
    }

    /// <summary>แก้ชื่อที่แสดง (WP4, 2026-10-30) - เจ้าของบัญชีแก้เองได้ Admin แก้ให้คนอื่นได้</summary>
    public async Task<(bool Success, string Message)> UpdateDisplayNameAsync(int userId, string displayName)
    {
        try
        {
            var response = await _httpClient.PutAsJsonAsync($"/api/admin/users/{userId}/display-name", new { displayName });
            return response.IsSuccessStatusCode
                ? (true, "แก้ชื่อที่แสดงสำเร็จ")
                : (false, await ExtractErrorAsync(response, $"แก้ชื่อที่แสดงไม่สำเร็จ: {response.StatusCode}"));
        }
        catch (Exception ex)
        {
            return (false, $"เกิดข้อผิดพลาด: {ex.Message}");
        }
    }

    /// <summary>เปลี่ยน role ของบัญชี (WP4b, 2026-10-30) - Admin เท่านั้น Server กันบัญชี built-in และ
    /// การลด Admin คนสุดท้ายให้แล้ว (ดู UserEndpoints.cs ฝั่ง Server)</summary>
    public async Task<(bool Success, string Message)> UpdateUserRoleAsync(int userId, int role)
    {
        try
        {
            var response = await _httpClient.PutAsJsonAsync($"/api/admin/users/{userId}/role", new { role });
            return response.IsSuccessStatusCode
                ? (true, "เปลี่ยน role สำเร็จ")
                : (false, await ExtractErrorAsync(response, $"เปลี่ยน role ไม่สำเร็จ: {response.StatusCode}"));
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
            return response.IsSuccessStatusCode
                ? (true, "ลบ user สำเร็จ")
                : (false, await ExtractErrorAsync(response, $"ลบ user ไม่สำเร็จ: {response.StatusCode}"));
        }
        catch (Exception ex)
        {
            return (false, $"เกิดข้อผิดพลาด: {ex.Message}");
        }
    }

    public async Task<(bool Success, string Message)> ResetUserPasswordAsync(int userId, string newPassword)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync($"/api/admin/users/{userId}/reset-password", new { newPassword });
            return response.IsSuccessStatusCode
                ? (true, "Reset password สำเร็จ")
                : (false, await ExtractErrorAsync(response, $"Reset password ไม่สำเร็จ: {response.StatusCode}"));
        }
        catch (HttpRequestException ex)
        {
            return (false, $"ต่อ server ไม่ได้: {ex.Message}");
        }
    }

    public async Task<bool> UpdateClientSettingsAsync(int clientId, int? pollIntervalOverrideSeconds)
    {
        var response = await _httpClient.PutAsJsonAsync($"/api/clients/{clientId}/settings", new { pollIntervalOverrideSeconds });
        return response.IsSuccessStatusCode;
    }

    // ---- ห้อง + ผังที่นั่ง (WP3, 2026-10-30) ----

    public async Task<List<RoomDto>> GetRoomsAsync()
    {
        var result = await _httpClient.GetFromJsonAsync<List<RoomDto>>("/api/rooms");
        return result ?? new List<RoomDto>();
    }

    public async Task<bool> CreateRoomAsync(string name)
    {
        var response = await _httpClient.PostAsJsonAsync("/api/rooms", new { name });
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> RenameRoomAsync(int roomId, string name)
    {
        var response = await _httpClient.PutAsJsonAsync($"/api/rooms/{roomId}", new { name });
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> DeleteRoomAsync(int roomId)
    {
        var response = await _httpClient.DeleteAsync($"/api/rooms/{roomId}");
        return response.IsSuccessStatusCode;
    }

    /// <summary>ย้ายเครื่องเข้า/ออกห้อง - roomId = null คือเอาเครื่องออกจากห้อง (กลับไปเป็น "ยังไม่จัดห้อง")</summary>
    public async Task<bool> AssignClientRoomAsync(int clientId, int? roomId, int? seatRow = null, int? seatCol = null)
    {
        var response = await _httpClient.PutAsJsonAsync($"/api/clients/{clientId}/room", new { roomId, seatRow, seatCol });
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> SendCommandAsync(string clientGuid, string commandType, object? payload)
        => (await SendCommandTrackedAsync(clientGuid, commandType, payload, batchId: null)).CommandId is not null;

    public async Task<(int? CommandId, string? Error)> SendCommandTrackedAsync(
        string clientGuid, string commandType, object? payload, string? batchId)
    {
        try
        {
            var payloadJson = payload is null ? null : System.Text.Json.JsonSerializer.Serialize(payload);
            var response = await _httpClient.PostAsJsonAsync($"/api/commands/{clientGuid}", new { commandType, payload = payloadJson, batchId });

            if (!response.IsSuccessStatusCode)
            {
                return (null, $"Server ปฏิเสธคำสั่ง: {(int)response.StatusCode} {response.StatusCode}");
            }

            var created = await response.Content.ReadFromJsonAsync<CreatedCommandDto>();
            if (created is null || created.Id <= 0)
            {
                return (null, "ส่งคำสั่งสำเร็จ แต่ Server ไม่ได้คืน command id กลับมา (ติดตามผลไม่ได้)");
            }

            return (created.Id, null);
        }
        catch (HttpRequestException ex)
        {
            return (null, $"ต่อ server ไม่ได้: {ex.Message}");
        }
        catch (TaskCanceledException)
        {
            return (null, "หมดเวลาเชื่อมต่อ (timeout) ตอนส่งคำสั่ง");
        }
    }

    public async Task<List<CommandResultDto>> GetBatchStatusAsync(string batchId)
    {
        try
        {
            var result = await _httpClient.GetFromJsonAsync<List<CommandResultDto>>($"/api/commands/batch/{batchId}");
            return result ?? new List<CommandResultDto>();
        }
        catch
        {
            return new List<CommandResultDto>();
        }
    }

    public async Task<List<CommandResultDto>> GetPendingCommandsAsync(string clientGuid)
    {
        try
        {
            var result = await _httpClient.GetFromJsonAsync<List<CommandResultDto>>($"/api/commands/pending/{clientGuid}");
            return result ?? new List<CommandResultDto>();
        }
        catch
        {
            return new List<CommandResultDto>();
        }
    }

    public async Task<(bool Success, string? Message)> CancelCommandAsync(int commandId)
    {
        try
        {
            var response = await _httpClient.PostAsync($"/api/commands/{commandId}/cancel", null);
            return response.IsSuccessStatusCode
                ? (true, null)
                : (false, $"ยกเลิกไม่สำเร็จ: {(int)response.StatusCode} {response.StatusCode}");
        }
        catch (Exception ex)
        {
            return (false, $"ยกเลิกไม่สำเร็จ: {ex.Message}");
        }
    }

    private static async Task<string> ExtractErrorAsync(HttpResponseMessage response, string fallback)
    {
        try
        {
            var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
            return string.IsNullOrEmpty(error?.Error) ? fallback : error.Error;
        }
        catch
        {
            // response ไม่ใช่ JSON (เช่น dev exception page เป็น HTML) - ใช้ fallback แทน ไม่ throw ต่อ
            return fallback;
        }
    }
}

public class ErrorResponse
{
    public string? Error { get; set; }
}

public class CreatedCommandDto
{
    public int Id { get; set; }
}
