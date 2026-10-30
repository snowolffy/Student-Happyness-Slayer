using System.Net;
using System.Net.Http;
using System.Net.Http.Json;

namespace OnionProcOparetor.RemoteBridge.Services;

/// <summary>เท่าที่ RemoteBridge ต้องรู้จากเครื่อง client หนึ่งเครื่อง - ดึงจาก GET /api/clients</summary>
public record ClientMachineDto(int Id, string ClientGuid, string MachineName);

/// <summary>
/// คุยกับ Server ผ่าน HTTP REST API เดิมที่มีอยู่แล้ว (เหมือนที่ Console ใช้ทุกวัน) -
/// RemoteBridge ไม่มี endpoint ของตัวเองฝั่ง Server เลยสักตัว เป็นแค่ "Console อีกตัวที่ไม่มีหน้าตา"
/// ล็อกอินด้วยบัญชี admin ปกติผ่าน /api/auth/login แล้วยิง /api/commands/{clientGuid} วนทุกเครื่อง
/// เหมือนตอนครูกด "เลือกทุกเครื่อง + Lock" ใน ActionPanelWindow
/// </summary>
public class ApiClient
{
    // WP1 (2026-10-30): ผูก ServerCertificatePinning ไว้ตั้งแต่สร้าง (ดู comment เต็มในคลาสนั้น) - ไม่มี
    // ผลถ้าต่อ http:// และ fallback ไปใช้ default .NET validation เองถ้าไม่ได้ pin thumbprint ไว้
    private readonly HttpClient _httpClient = new(ServerCertificatePinning.CreateHandler()) { Timeout = TimeSpan.FromSeconds(10) };
    private string? _token;
    private string? _lastUsername;
    private string? _lastPassword;

    public bool IsLoggedIn => !string.IsNullOrEmpty(_token);

    /// <summary>
    /// false = รูปแบบที่กรอกมาไม่ใช่ URI ที่ถูกต้อง (เช่น พิมพ์ผิด/มีอักขระแปลกปน) - เดิม `new Uri()`
    /// โยน UriFormatException ตรงๆ แล้วไม่มีใครจับ ทำให้กด Connect ด้วยค่าที่พิมพ์ผิดครั้งเดียว
    /// ล้มทั้งโปรแกรม (พบจริงตอนทดสอบ 2026-09-13) ใช้ TryCreate แทนเพื่อคืนค่า false ให้ผู้เรียกจัดการเอง
    /// </summary>
    public bool SetServer(string urlOrIpPort)
    {
        var trimmed = urlOrIpPort.Trim();
        var baseUrl = trimmed.StartsWith("http://") || trimmed.StartsWith("https://")
            ? trimmed
            : $"http://{trimmed}";

        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
        {
            return false;
        }

        _httpClient.BaseAddress = uri;
        return true;
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

            _token = result.Token;
            _lastUsername = username;
            _lastPassword = password;

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
            return (false, "หมดเวลาเชื่อมต่อ (timeout) - เช็ค Server อีกครั้ง");
        }
    }

    public async Task<(bool Success, List<ClientMachineDto>? Machines, string Message)> GetClientsAsync()
    {
        var response = await SendWithRetryAsync(() => _httpClient.GetAsync("/api/clients"));
        if (!response.IsSuccessStatusCode)
        {
            return (false, null, $"ดึงรายชื่อเครื่องไม่สำเร็จ: {response.StatusCode}");
        }

        var machines = await response.Content.ReadFromJsonAsync<List<ClientMachineDto>>();
        return (true, machines, "OK");
    }

    /// <summary>
    /// ยิง LockWorkstation/UnlockWorkstation ไปทุกเครื่องในรายชื่อที่ให้มา - overwrite ทับตรงๆ
    /// ไม่เช็คสถานะเดิมของแต่ละเครื่องก่อน (ตามที่ตั้งใจไว้ตั้งแต่ต้น - remote คุมทิศทางเดียวข้ามทั้งห้อง)
    /// ใช้ endpoint เดิม POST /api/commands/{clientGuid} เดียวกับที่ ActionPanelWindow ใช้ทุกวัน
    /// </summary>
    public async Task<(int SuccessCount, int FailCount)> BroadcastLockAsync(bool locked, IEnumerable<string> clientGuids)
    {
        var commandType = locked ? "LockWorkstation" : "UnlockWorkstation";
        var batchId = Guid.NewGuid().ToString("N");
        var successCount = 0;
        var failCount = 0;

        foreach (var clientGuid in clientGuids)
        {
            var response = await SendWithRetryAsync(() => _httpClient.PostAsJsonAsync(
                $"/api/commands/{clientGuid}",
                new { commandType, payload = (string?)null, batchId }));

            if (response.IsSuccessStatusCode)
            {
                successCount++;
            }
            else
            {
                failCount++;
            }
        }

        return (successCount, failCount);
    }

    /// <summary>
    /// ยิง request ครั้งแรกก่อน ถ้าเจอ 401 (token หมดอายุ 12 ชม. - RemoteBridge ต้องรันค้างข้ามวัน
    /// โดยไม่มีคนเฝ้ากรอก login ใหม่) ลอง re-login ด้วย credential ล่าสุดแล้วยิงซ้ำอีกครั้งเดียว -
    /// ไม่ retry วนไม่รู้จบ เพราะถ้า credential ผิดจริง (เช่นรหัสผ่านถูกเปลี่ยนที่ Server) จะวนถามไม่เลิก
    /// </summary>
    private async Task<HttpResponseMessage> SendWithRetryAsync(Func<Task<HttpResponseMessage>> send)
    {
        var response = await send();
        if (response.StatusCode != HttpStatusCode.Unauthorized || _lastUsername is null || _lastPassword is null)
        {
            return response;
        }

        var (relSuccess, _) = await LoginAsync(_lastUsername, _lastPassword);
        return relSuccess ? await send() : response;
    }

    private class LoginResponse
    {
        public string Message { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string Token { get; set; } = string.Empty;
        public bool HasDefaultPassword { get; set; }
        public int AdminId { get; set; }
    }
}
