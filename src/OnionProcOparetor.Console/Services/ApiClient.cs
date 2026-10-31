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

    /// <summary>"Admin" หรือ "Teacher" ของบัญชีที่ login อยู่ตอนนี้ (WP4b, 2026-10-30)</summary>
    public string Role { get; private set; } = "Admin";

    /// <summary>true = บัญชีที่ login อยู่เป็น Admin - ใช้ซ่อน/ปิดปุ่มที่ Teacher ใช้ไม่ได้ (Server ตอบ
    /// 403 อยู่แล้วถ้ากดได้ นี่แค่เพื่อ UX ไม่ใช่การกันจริง)</summary>
    public bool IsAdmin => Role != "Teacher";

    /// <summary>ชื่อที่แสดงของบัญชีที่ login อยู่ตอนนี้ (WP4b, 2026-10-30) - ใช้แทน Username ในแถบล่าง Console</summary>
    public string DisplayName { get; private set; } = string.Empty;

    public ApiClient()
    {
        // WP1 (2026-10-30): ผูก ServerCertificatePinning ไว้ตั้งแต่สร้าง HttpClient เสมอ (ไม่ใช่แค่ตอน
        // BaseUrl เป็น https://) - ไม่มีผลถ้าต่อ http:// (callback ไม่ถูกเรียกเลยเพราะไม่มี TLS handshake)
        // และถ้าไม่ได้ pin thumbprint ไว้ที่เครื่องนี้ มันจะ fallback ไปใช้ default validation ของ .NET
        // เองอยู่แล้ว (ดู comment เต็มใน ServerCertificatePinning)
        _httpClient = new HttpClient(ServerCertificatePinning.CreateHandler())
        {
            Timeout = TimeSpan.FromSeconds(10)
        };
    }

    /// <summary>
    /// ตั้ง base URL จาก IP:Port ที่ user กรอกในหน้า Connect
    /// useHttps (WP1b, 2026-10-30): เลือก scheme เมื่อ ipAndPort ไม่ได้พิมพ์ scheme มาเอง (กรณีปกติ -
    /// ผู้ใช้พิมพ์แค่ IP:Port ไม่ใช่ URL เต็ม) - ค่า default เป็น false (HTTP) เสมอที่ชั้นนี้ ส่วนค่าจริงที่
    /// ใช้มาจาก checkbox บนหน้าล็อกอิน (ดู MainWindow.xaml.cs) ถ้า ipAndPort มี scheme ระบุมาแล้วตรงๆ
    /// (เผื่อโค้ดอื่นเรียกแบบเดิม) scheme ที่พิมพ์มาชนะเสมอ ไม่ถูก useHttps ทับ
    /// </summary>
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
            // ค่าว่าง = login response เก่า (Server ยังไม่มี WP4b) - fallback เป็น Admin/username เดิม
            // กันไม่ให้ Console ซ่อนปุ่มผิดๆ ทั้งที่ Server ไม่ได้เช็ค role อะไรเลย
            Role = string.IsNullOrWhiteSpace(result.Role) ? "Admin" : result.Role;
            DisplayName = string.IsNullOrWhiteSpace(result.DisplayName) ? username : result.DisplayName;

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

    // ---- Program Control (2026-08-31) - ดู ProgramReviewWindow ----

    /// <summary>
    /// ดึงคิวตรวจสอบ - filter: flagged (ค่าเริ่มต้น) / blocked / allowed / manual / all
    /// ไม่ throw ถ้า deserialize ได้ null (เช่น server เวอร์ชันเก่าที่ยังไม่มี endpoint นี้ ตอบ 404
    /// แล้ว GetFromJsonAsync โยน HttpRequestException) - ผู้เรียกจับ exception แสดงใน StatusText เอง
    /// </summary>
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

    /// <summary>
    /// จำนวนแถวแยกตามหมวดของ filter ที่เลือกอยู่ - ใช้เติมตัวเลขในกล่องเลือกหมวดและเตือนเมื่อยอดรวม
    /// เกินจำนวนที่ Server ส่งมาให้ (2026-09-01)
    /// </summary>
    public async Task<ProgramVerdictSummaryDto> GetProgramVerdictSummaryAsync(string filter = "flagged")
    {
        var result = await _httpClient.GetFromJsonAsync<ProgramVerdictSummaryDto>(
            $"/api/program-verdicts/summary?filter={Uri.EscapeDataString(filter)}");
        return result ?? new ProgramVerdictSummaryDto();
    }

    /// <summary>ครูตัดสินโปรแกรมหนึ่งด้วยมือ - ทับผลของอัลกอริทึมถาวร (0=Allow, 1=Flag, 2=Block)</summary>
    public async Task<bool> DecideProgramVerdictAsync(int verdictId, int action)
    {
        var response = await _httpClient.PostAsJsonAsync(
            $"/api/program-verdicts/{verdictId}/decide", new { action });
        return response.IsSuccessStatusCode;
    }

    /// <summary>
    /// ครูเพิ่ม/แก้คำตัดสินของโปรแกรมด้วยตัวเอง ผูกกับ SHA-256 (2026-08-31)
    ///
    /// ใช้จากรายการ "โปรแกรมที่กำลังรันอยู่" ซึ่งครอบคลุมกรณีที่ปุ่ม CREATE RULE เดิมทำไม่ได้:
    /// โปรแกรมที่ไม่มี publisher เลย และโปรแกรมที่ Program Control เคยเห็นแล้วแต่ตัดสินไว้ไม่ตรงใจ
    /// </summary>
    public async Task<(bool Success, string Message)> SetManualProgramVerdictAsync(
        RunningProcessDto process, string machineName, int action)
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

            if (response.IsSuccessStatusCode)
            {
                return (true, "สำเร็จ");
            }

            var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
            return (false, string.IsNullOrEmpty(error?.Error) ? $"server ตอบ {(int)response.StatusCode}" : error.Error);
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

    /// <summary>ห้องทั้งหมด + จำนวนเครื่องในห้องนั้น (WP3, 2026-10-30) - ใช้เติมตัวเลือกห้องบนหัวแท็บ
    /// Clients (ดู DashboardWindow.LoadRoomsAsync) - pattern เดียวกับ OnlineConsole's ApiClient.GetRoomsAsync</summary>
    public async Task<List<RoomDto>> GetRoomsAsync()
    {
        var result = await _httpClient.GetFromJsonAsync<List<RoomDto>>("/api/rooms");
        return result ?? new List<RoomDto>();
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
    /// อ่านสถานะ enforcement ทั้งห้อง - คืน null ถ้าอ่านไม่ได้ ผู้เรียกต้องแยก "ไม่รู้" ออกจาก
    /// "รู้ว่าเปิดอยู่" ให้ชัด (การเดาว่าเปิดอยู่เวลาที่อ่านไม่ได้ คือการกลับไปสู่บั๊กเดิมที่ทำให้
    /// ค่านี้มองไม่เห็น - ดู DashboardWindow.RefreshGlobalStateUi)
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

    /// <summary>
    /// ตั้งค่า enforcement ทั้งห้องแบบระบุค่าตรงๆ (idempotent) - แทน ToggleGlobalAsync เดิมที่พลิกค่า
    /// จากของเดิม ซึ่งต้องรู้ค่าปัจจุบันก่อนถึงจะรู้ว่ากดแล้วได้อะไร และแพ้ lost-update ถ้าครู 2 คน
    /// กดพร้อมกัน (กดคนละครั้ง = กลับมาที่เดิม)
    /// </summary>
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

    /// <summary>แก้ไขกฎแบบ in-place - ไม่ต้องลบแล้วสร้างใหม่ (เก็บ Id/RuleOverride ของกฎเดิมไว้ครบ)</summary>
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

    /// <summary>
    /// แบ่งหน้า + ค้นหา + กรองตาม catalog source (2026-09-12) - เดินตามแบบเดียวกับ audit log
    /// page/pageSize (ดู DashboardWindow.xaml.cs) sourceCatalogId=null และ manualOnly=false คือ
    /// "ทั้งหมด" (ค่า default)
    /// </summary>
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

    // ---- ป้องกันการเลี่ยง (WP2-1, 2026-10-31) ----

    /// <summary>
    /// คืน null ถ้าอ่านไม่ได้ - pattern เดียวกับ GetGlobalStateAsync (แยก "ไม่รู้" ออกจาก "รู้ว่าปิดอยู่")
    /// จับ Exception แบบกว้าง (ไม่ใช่แค่ HttpRequestException เหมือน GetGlobalStateAsync) เพราะเจอจริง
    /// ตอนทดสอบ: ApiClient ที่ยังไม่ได้ตั้ง BaseAddress (เช่นโหมดตรวจ UI ที่ไม่ได้ login จริง) โยน
    /// InvalidOperationException ไม่ใช่ HttpRequestException - ถ้าจับแค่ตัวเดียวจะหลุดไปโดน global
    /// exception handler จนเด้ง MessageBox ค้างทุกครั้งที่สลับมาแท็บนี้โดยไม่มี Server ต่ออยู่จริง
    /// </summary>
    public async Task<BrowserHardeningSettingsDto?> GetBrowserHardeningSettingsAsync()
    {
        try
        {
            return await _httpClient.GetFromJsonAsync<BrowserHardeningSettingsDto>("/api/browser-hardening");
        }
        catch (Exception)
        {
            return null;
        }
    }

    public async Task<bool> SetBrowserHardeningSettingsAsync(BrowserHardeningSettingsDto settings)
    {
        var response = await _httpClient.PostAsJsonAsync("/api/browser-hardening/set", new
        {
            settings.BlockExtensions,
            settings.BlockGuestAndIncognito,
            settings.BlockDevTools,
            settings.LockDnsAndProxy,
            settings.BlockOtherBrowsers
        });
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

    // ---- ชุด Allow-list ของ "โหมดสอบ" (WP2-2, 2026-10-31) ----

    public async Task<List<AllowListPresetDto>> GetAllowListPresetsAsync()
    {
        var result = await _httpClient.GetFromJsonAsync<List<AllowListPresetDto>>("/api/allowlist-presets");
        return result ?? new List<AllowListPresetDto>();
    }

    public async Task<bool> AddAllowListPresetAsync(string name)
    {
        var response = await _httpClient.PostAsJsonAsync("/api/allowlist-presets", new { name });
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> RenameAllowListPresetAsync(int id, string name)
    {
        var response = await _httpClient.PutAsJsonAsync($"/api/allowlist-presets/{id}", new { name });
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> DeleteAllowListPresetAsync(int id)
    {
        var response = await _httpClient.DeleteAsync($"/api/allowlist-presets/{id}");
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> DuplicateAllowListPresetAsync(int id)
    {
        var response = await _httpClient.PostAsync($"/api/allowlist-presets/{id}/duplicate", null);
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> AddAllowListPresetDomainAsync(int presetId, string domain)
    {
        var response = await _httpClient.PostAsJsonAsync($"/api/allowlist-presets/{presetId}/domains", new { domain });
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> DeleteAllowListPresetDomainAsync(int presetId, int domainId)
    {
        var response = await _httpClient.DeleteAsync($"/api/allowlist-presets/{presetId}/domains/{domainId}");
        return response.IsSuccessStatusCode;
    }

    // ---- โหมดสอบ (WP2-2, 2026-10-31) ----

    /// <summary>คืน null ถ้าเริ่มไม่สำเร็จ (validation ไม่ผ่าน/ไม่มีสิทธิ์) - ต่างจาก false/true ตรงๆ เพราะผู้เรียกต้องการ summary กลับไปแสดงผลทันทีด้วย (ไม่ใช่แค่ bool)</summary>
    public async Task<ExamSessionDto?> StartExamSessionAsync(
        int? roomId, List<string>? clientGuids, List<int> presetIds, List<string> extraDomains, int durationMinutes)
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

    public async Task<bool> CancelExamSessionAsync(int id)
    {
        var response = await _httpClient.PostAsync($"/api/exam-sessions/{id}/cancel", null);
        return response.IsSuccessStatusCode;
    }

    /// <summary>คืน empty list ถ้าอ่านไม่ได้ (Server รุ่นเก่า/ไม่มีสิทธิ์) - ใช้ขึ้นประกาศ "โหมดสอบกำลังทำงาน" บนแถบหัว (ดู DashboardWindow.RefreshExamModeAnnouncement)</summary>
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

    // ---- ประวัติเว็บ (WP2-4, 2026-10-31) ----

    /// <summary>คืน empty page ถ้าอ่านไม่ได้ (เหตุผลเดียวกับ GetBrowserHardeningSettingsAsync - โหมดตรวจ
    /// UI ที่ยังไม่ login จริงโยน InvalidOperationException ไม่ใช่ HttpRequestException)</summary>
    public async Task<WebVisitsPageDto> GetWebVisitsAsync(
        DateTime? from = null, DateTime? to = null, string? className = null, string? search = null,
        string? domain = null, bool blockedOnly = false, int page = 1, int pageSize = 200)
    {
        var query = $"page={page}&pageSize={pageSize}&blockedOnly={blockedOnly}";
        if (from is not null) query += $"&from={Uri.EscapeDataString(from.Value.ToString("o"))}";
        if (to is not null) query += $"&to={Uri.EscapeDataString(to.Value.ToString("o"))}";
        if (!string.IsNullOrWhiteSpace(className)) query += $"&className={Uri.EscapeDataString(className)}";
        if (!string.IsNullOrWhiteSpace(search)) query += $"&search={Uri.EscapeDataString(search)}";
        if (!string.IsNullOrWhiteSpace(domain)) query += $"&domain={Uri.EscapeDataString(domain)}";

        try
        {
            var result = await _httpClient.GetFromJsonAsync<WebVisitsPageDto>($"/api/web-visits?{query}");
            return result ?? new WebVisitsPageDto();
        }
        catch (Exception)
        {
            return new WebVisitsPageDto();
        }
    }

    /// <summary>"เว็บที่เข้าบ่อยช่วงนี้" (aside ของ con-website-history.dc.html) - คืน empty list ถ้าอ่านไม่ได้</summary>
    public async Task<List<TopWebDomainDto>> GetTopWebDomainsAsync(DateTime? from = null, DateTime? to = null, int take = 6)
    {
        var query = $"take={take}";
        if (from is not null) query += $"&from={Uri.EscapeDataString(from.Value.ToString("o"))}";
        if (to is not null) query += $"&to={Uri.EscapeDataString(to.Value.ToString("o"))}";

        try
        {
            var result = await _httpClient.GetFromJsonAsync<List<TopWebDomainDto>>($"/api/web-visits/top-domains?{query}");
            return result ?? new List<TopWebDomainDto>();
        }
        catch (Exception)
        {
            return new List<TopWebDomainDto>();
        }
    }

    /// <summary>Override เฉพาะเครื่องเดียว - ดึง override ทั้งหมดของเครื่องนี้ (Config Rule dialog เอาไปเทียบกับ GetRulesAsync() เพื่อโชว์สถานะ effective ต่อกฎ)</summary>
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

    /// <summary>ลบ override ทิ้ง - เครื่องนี้กลับไปใช้ค่า global ของกฎนี้ตามปกติ</summary>
    public async Task<bool> ClearRuleOverrideAsync(string clientGuid, int ruleId)
    {
        var response = await _httpClient.DeleteAsync($"/api/clients/{clientGuid}/rule-overrides/{ruleId}");
        return response.IsSuccessStatusCode;
    }

    public async Task<List<LogEntryDto>> GetLogsAsync(int take = 100)
    {
        var result = await _httpClient.GetFromJsonAsync<List<LogEntryDto>>($"/api/logs?take={take}");
        return result ?? new List<LogEntryDto>();
    }

    /// <summary>
    /// ดึง Activity Log แบบแบ่งหน้า (ไม่โหลดทั้งหมดทีเดียว - volume คาดว่าสูงกว่า LogEntry มาก)
    /// category/minSeverity เป็น null ได้ = ไม่กรองแกนนั้น
    /// </summary>
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

    /// <summary>role: 0 = Admin, 1 = Teacher (ตรง enum AdminRole ฝั่ง Server - WP4b, 2026-10-30)
    /// displayName ว่างไว้ได้ - Server fallback เป็น username เอง</summary>
    public async Task<(bool Success, string Message)> CreateUserAsync(string username, string password, string? displayName = null, int role = 0)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync("/api/admin/users", new { username, password, displayName, role });
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

    /// <summary>
    /// สั่ง command ไปยัง Agent เครื่องใดเครื่องหนึ่งผ่าน POST /api/commands/{clientGuid}
    /// (Server จะ push ทันทีผ่าน SignalR ถ้า Agent เครื่องนั้น connected อยู่ ไม่งั้น Agent จะเจอผ่าน poll fallback)
    /// payload ถูก serialize เป็น JSON string ก่อนส่ง เพราะ Server เก็บเป็น PayloadJson (string) ไม่ใช่ object ดิบ
    /// </summary>
    public async Task<bool> SendCommandAsync(string clientGuid, string commandType, object? payload)
        => (await SendCommandTrackedAsync(clientGuid, commandType, payload, batchId: null)).CommandId is not null;

    /// <summary>
    /// เหมือน SendCommandAsync แต่ "เก็บ" commandId ที่ Server สร้างให้กลับมาด้วย (เดิมถูกทิ้งไป
    /// ทั้งที่ Server คืนมาให้อยู่แล้วใน response body) - Console ใช้ id นี้ track ผลจริงของแต่ละ
    /// เครื่องในหน้า report ได้
    ///
    /// batchId ผูกทุกเครื่องที่ครูกดพร้อมกันครั้งเดียวไว้เป็นชุดเดียว เพื่อดึงสถานะทั้งชุดทีเดียว
    /// ผ่าน GET /api/commands/batch/{batchId}
    ///
    /// คืน CommandId = null ถ้าส่งไม่สำเร็จ พร้อม Error อธิบายสาเหตุ (เอาไปแสดงในหน้า report
    /// ได้เลยว่าเครื่องนี้ "ส่งคำสั่งไม่ออก" ซึ่งต่างจาก "ส่งออกแล้วแต่เครื่องไม่ตอบ")
    /// </summary>
    public async Task<(int? CommandId, string? Error)> SendCommandTrackedAsync(
        string clientGuid, string commandType, object? payload, string? batchId)
    {
        try
        {
            var payloadJson = payload is null ? null : System.Text.Json.JsonSerializer.Serialize(payload);
            var response = await _httpClient.PostAsJsonAsync(
                $"/api/commands/{clientGuid}",
                new { commandType, payload = payloadJson, batchId });

            if (!response.IsSuccessStatusCode)
            {
                return (null, $"Server ปฏิเสธคำสั่ง: {(int)response.StatusCode} {response.StatusCode}");
            }

            var created = await response.Content.ReadFromJsonAsync<CreatedCommandDto>();

            if (created is null || created.Id <= 0)
            {
                // ส่งสำเร็จแต่ไม่ได้ id กลับมา (เช่น Server เวอร์ชันเก่า) - ยังถือว่าสั่งไปแล้ว
                // แค่ track ผลไม่ได้ ผู้เรียกต้องรับมือกับ CommandId = null ให้ถูกต้อง
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

    /// <summary>
    /// ดึงสถานะปัจจุบันของ command ทั้งชุด - ใช้ 2 กรณี: (1) ตอนเปิดหน้า report ครั้งแรกเพื่อ
    /// sync สถานะที่อาจมาถึงก่อน subscribe ทัน (2) เป็น fallback poll ระหว่างที่หน้าเปิดอยู่
    /// เผื่อ SignalR หลุด (pattern เดียวกับ DashboardWindow ที่ poll คู่กับ SignalR อยู่แล้ว)
    /// </summary>
    public async Task<List<CommandResultDto>> GetBatchStatusAsync(string batchId)
    {
        try
        {
            var result = await _httpClient.GetFromJsonAsync<List<CommandResultDto>>($"/api/commands/batch/{batchId}");
            return result ?? new List<CommandResultDto>();
        }
        catch
        {
            // fail เงียบๆ โดยตั้งใจ - นี่เป็นแค่ fallback ของ SignalR ถ้าดึงไม่ได้รอบนี้
            // รอบหน้า (อีก 2 วิ) ลองใหม่เอง ไม่ควรเด้ง error ใส่ครูระหว่างรอผล
            return new List<CommandResultDto>();
        }
    }

    /// <summary>
    /// ดึงรายการ command ที่ยังค้าง Pending อยู่ของเครื่องหนึ่งๆ ผ่าน GET /api/commands/pending/{clientGuid}
    /// ใช้โดย PendingCommandsWindow (เปิดจาก ActionPanelWindow's PENDING COMMANDS...) - ต่างจาก
    /// GetBatchStatusAsync ตรงที่ไม่ผูกกับ BatchId เลย ดึงทุก Pending command ของเครื่องนี้ตรงๆ
    /// ไม่ว่าจะมาจาก batch ไหนหรือหน้าต่างที่สั่งไปปิดไปแล้วหรือไม่ก็ตาม
    /// </summary>
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

    /// <summary>
    /// ยกเลิก command ที่ยังค้าง Pending อยู่ ผ่าน POST /api/commands/{commandId}/cancel - เพิ่มขึ้น
    /// หลังบั๊กจริง (2026-08-25): Restart/Shutdown ที่ ack ไปไม่ถึง Server ทันเวลาที่เครื่องดับจริง
    /// (มีโอกาสสูงขึ้นตอนใช้ Tailscale fallback) จะค้าง Pending แล้วถูกส่งซ้ำทุกรอบ poll หลังเครื่อง
    /// บูตกลับมา วนไม่จบ ปุ่มนี้ใน ActionReportWindow ให้ครูกดยกเลิกได้ทันทีถ้าเห็นว่ายังค้างอยู่ -
    /// ประโยชน์คู่: (1) หยุด loop ได้จริงถ้าเจอ (2) ตัวช่วยกู้คืนถ้ากดสั่งปิด/ล็อกเครื่องผิดพลาด
    /// แล้วยังทัน (เครื่องยังไม่ตอบ Succeeded/Failed)
    /// </summary>
    public async Task<(bool Success, string? Message)> CancelCommandAsync(int commandId)
    {
        try
        {
            var response = await _httpClient.PostAsync($"/api/commands/{commandId}/cancel", null);

            if (!response.IsSuccessStatusCode)
            {
                return (false, $"ยกเลิกไม่สำเร็จ: {(int)response.StatusCode} {response.StatusCode}");
            }

            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, $"ยกเลิกไม่สำเร็จ: {ex.Message}");
        }
    }

    /// <summary>แก้ชื่อที่แสดง (WP4b, 2026-10-30) - เจ้าของบัญชีแก้เองได้ Admin แก้ให้คนอื่นได้ (Server ตรวจสิทธิ์)</summary>
    public async Task<(bool Success, string Message)> UpdateDisplayNameAsync(int userId, string displayName)
    {
        try
        {
            var response = await _httpClient.PutAsJsonAsync($"/api/admin/users/{userId}/display-name", new { displayName });
            if (response.IsSuccessStatusCode)
            {
                return (true, "แก้ชื่อที่แสดงสำเร็จ");
            }

            string errorMessage = $"แก้ชื่อที่แสดงไม่สำเร็จ: {response.StatusCode}";
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

    /// <summary>เปลี่ยน role ของบัญชี (WP4b, 2026-10-30) - Admin เท่านั้น Server กันบัญชี built-in และ
    /// การลด Admin คนสุดท้ายให้แล้ว (ดู UserEndpoints.cs ฝั่ง Server)</summary>
    public async Task<(bool Success, string Message)> UpdateUserRoleAsync(int userId, int role)
    {
        try
        {
            var response = await _httpClient.PutAsJsonAsync($"/api/admin/users/{userId}/role", new { role });
            if (response.IsSuccessStatusCode)
            {
                return (true, "เปลี่ยน role สำเร็จ");
            }

            string errorMessage = $"เปลี่ยน role ไม่สำเร็จ: {response.StatusCode}";
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

    // ==================== WP3-5 (2026-10-31) - Sign-in ส่วน Console ====================
    // Server ของ WP3-1 ทำ API ไว้ครบแล้ว (SignInSettingsEndpoints.cs, StudentSessionEndpoints.cs,
    // SecurityEndpoints.cs) ส่วนนี้แค่เพิ่มเมธอดเรียกฝั่ง Console - ไม่แก้ Server

    /// <summary>คืน null ถ้าอ่านไม่ได้ (Admin เท่านั้น - Teacher ที่เรียกจะได้ 403 แล้ว GetFromJsonAsync throw)</summary>
    public async Task<SignInSettingsDto?> GetSignInSettingsAsync()
    {
        try
        {
            return await _httpClient.GetFromJsonAsync<SignInSettingsDto>("/api/settings/signin");
        }
        catch (Exception)
        {
            return null;
        }
    }

    public async Task<(bool Success, string Message)> UpdateSignInSettingsAsync(SignInSettingsDto settings)
    {
        try
        {
            var response = await _httpClient.PutAsJsonAsync("/api/settings/signin", new
            {
                settings.RequireLoginMode,
                settings.RequireLoginRoomIdsCsv,
                settings.GuestModeEnabled,
                settings.EmergencyCodeEnabled,
                settings.PasswordMinLength,
                settings.ForceChangePasswordOnFirstLogin,
                settings.LockoutFailedAttempts,
                settings.MultiDeviceMode,
                settings.AutoLogoutIdleMinutes,
                settings.LogoutOnShutdown,
                settings.TeacherCanResetPassword,
                settings.ShowStudentNameInConsole,
                settings.LoginScreenMessage,
                settings.LockScreenDefaultMessage
            });

            if (response.IsSuccessStatusCode)
            {
                return (true, "บันทึกตั้งค่า Sign-in แล้ว");
            }

            return (false, await ExtractErrorAsync(response, $"บันทึกไม่สำเร็จ: {response.StatusCode}"));
        }
        catch (HttpRequestException ex)
        {
            return (false, $"ต่อ server ไม่ได้: {ex.Message}");
        }
    }

    public async Task<BackupSettingsDto?> GetBackupSettingsAsync()
    {
        try
        {
            return await _httpClient.GetFromJsonAsync<BackupSettingsDto>("/api/settings/backup");
        }
        catch (Exception)
        {
            return null;
        }
    }

    public async Task<(bool Success, string Message)> UpdateBackupSettingsAsync(int frequency, int retentionCount, string? storagePath)
    {
        try
        {
            var response = await _httpClient.PutAsJsonAsync("/api/settings/backup", new { frequency, retentionCount, storagePath });
            return response.IsSuccessStatusCode
                ? (true, "บันทึกตั้งค่าสำรองข้อมูลแล้ว")
                : (false, await ExtractErrorAsync(response, $"บันทึกไม่สำเร็จ: {response.StatusCode}"));
        }
        catch (HttpRequestException ex)
        {
            return (false, $"ต่อ server ไม่ได้: {ex.Message}");
        }
    }

    /// <summary>ปุ่ม "สำรองตอนนี้" - ไม่มี endpoint กู้คืน (restore) ใน API ตั้งใจไม่ทำปุ่มนั้น (ดู BackupSettingsDto)</summary>
    public async Task<(bool Success, string Message)> BackupNowAsync()
    {
        try
        {
            var response = await _httpClient.PostAsync("/api/settings/backup/now", null);
            return response.IsSuccessStatusCode
                ? (true, "สำรองข้อมูลสำเร็จ")
                : (false, await ExtractErrorAsync(response, $"สำรองไม่สำเร็จ: {response.StatusCode}"));
        }
        catch (HttpRequestException ex)
        {
            return (false, $"ต่อ server ไม่ได้: {ex.Message}");
        }
    }

    /// <summary>thumbprint ของ cert Server ใช้งานอยู่ (GET /security/certificate/thumbprint - ไม่ต้อง auth, text/plain ธรรมดา)</summary>
    public async Task<string?> GetCertificateThumbprintAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync("/security/certificate/thumbprint");
            return response.IsSuccessStatusCode ? (await response.Content.ReadAsStringAsync()).Trim() : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>ไฟล์ .cer ดิบของ Server (ไม่ต้อง auth) - ใช้กับปุ่ม "ส่งออก .cer" ในหน้าตั้งค่า</summary>
    public async Task<byte[]?> DownloadCertificateAsync()
    {
        try
        {
            return await _httpClient.GetByteArrayAsync("/security/certificate");
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>session ที่เปิดอยู่ของเครื่องหนึ่ง - ใช้ดึง sessionId มาสั่ง force-logout (GET /api/clients ไม่มี sessionId ติดมาด้วย)</summary>
    public async Task<ActiveStudentSessionDto?> GetActiveStudentSessionAsync(string clientGuid)
    {
        try
        {
            return await _httpClient.GetFromJsonAsync<ActiveStudentSessionDto>($"/api/students/sessions/active/{clientGuid}");
        }
        catch (Exception)
        {
            return null;
        }
    }

    public async Task<(bool Success, string Message)> ForceLogoutStudentSessionAsync(int sessionId)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync($"/api/students/sessions/{sessionId}/force-logout", new { });
            return response.IsSuccessStatusCode
                ? (true, "บังคับ logout นักเรียนแล้ว")
                : (false, await ExtractErrorAsync(response, $"บังคับ logout ไม่สำเร็จ: {response.StatusCode}"));
        }
        catch (HttpRequestException ex)
        {
            return (false, $"ต่อ server ไม่ได้: {ex.Message}");
        }
    }

    /// <summary>"ประวัติการใช้เครื่อง" (nf-attendance) - from/to เป็น UTC เสมอ (caller แปลงจากเวลาไทยก่อนส่งถ้าจำเป็น)</summary>
    public async Task<List<StudentSessionHistoryRowDto>> GetStudentSessionHistoryAsync(
        DateTime? from = null, DateTime? to = null, int? roomId = null, string? className = null, string? search = null, int take = 500)
    {
        var query = new List<string> { $"take={take}" };
        if (from is not null) query.Add($"from={Uri.EscapeDataString(from.Value.ToString("O"))}");
        if (to is not null) query.Add($"to={Uri.EscapeDataString(to.Value.ToString("O"))}");
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

    /// <summary>ใช้ร่วมกันทุกเมธอดใน WP3-5 ที่ต้องอ่าน { "error": "..." } จาก response ที่ไม่สำเร็จ (pattern เดิมของไฟล์นี้แต่แยกเป็น helper กันก๊อปซ้ำ 6 ที่)</summary>
    private static async Task<string> ExtractErrorAsync(HttpResponseMessage response, string fallback)
    {
        try
        {
            var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
            return !string.IsNullOrEmpty(error?.Error) ? error.Error : fallback;
        }
        catch
        {
            return fallback;
        }
    }
}

/// <summary>โครงสร้าง error response ทั่วไปที่ server คืนมา เช่น { "error": "..." }</summary>
public class ErrorResponse
{
    public string? Error { get; set; }
}

/// <summary>
/// ส่วนที่ Console สนใจจาก response ของ POST /api/commands/{clientGuid} - Server คืน
/// ClientCommand ทั้งก้อนมาให้อยู่แล้ว แต่ตรงนี้ต้องการแค่ Id ไปใช้ track ผลจริง
/// </summary>
public class CreatedCommandDto
{
    public int Id { get; set; }
}
