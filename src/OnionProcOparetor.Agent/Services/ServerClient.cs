using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using OnionProcOparetor.Agent.Models;

namespace OnionProcOparetor.Agent.Services;

/// <summary>
/// คุยกับ Server ผ่าน HTTP - poll เพื่อดึง rules/enabled state และ push log กลับ
/// ออกแบบให้ fail เงียบๆ ถ้า server ติดต่อไม่ได้ (fail-secure: ฝั่ง Worker
/// จะยังคง enforce กฎล่าสุดที่มีต่อไป ไม่ crash หรือหยุดทำงาน)
///
/// เป็นเจ้าของ "กำลังชี้ไป URL ไหนอยู่" คนเดียว (ActiveBaseUrl/IsUsingFallback) - Worker เป็นคนตัดสินใจ
/// ว่าเมื่อไหร่ควรสลับ (นับ consecutive failures/successes) แต่ตัวสลับจริงอยู่ที่นี่ ผ่าน SwitchTo()
///
/// บั๊กจริงที่เจอ (2026-08-25, ตอนเขียน standalone test คู่กับ Server จริง ก่อนจะไปถึง field test):
/// เดิมตั้งใจสลับ URL ด้วยการ set _httpClient.BaseAddress ใหม่ตรงๆ ใน SwitchTo() (เหมือน constructor)
/// แต่ HttpClient ไม่ยอมให้แก้ BaseAddress เลยหลังจากเคยส่ง request ผ่านมันไปสักครั้ง - โยน
/// InvalidOperationException "This instance has already started one or more requests" ทันที (ไม่ใช่แค่
/// เรื่อง thread-safety/torn-read อย่างที่คิดไว้ตอนแรก - เป็น hard restriction ของ HttpClient เอง)
/// ผลคือ SwitchTo() ตัวแรกที่ Worker เรียกจริงจะ crash exception ที่ไม่มีอะไรจับไว้เลยใน ExecuteAsync
/// ทันที (unhandled exception ใน BackgroundService.ExecuteAsync ทำให้ทั้ง service ตายได้) แก้โดยไม่แตะ
/// _httpClient.BaseAddress อีกเลยหลัง constructor - ทุก method สร้าง absolute URI จาก ActiveBaseUrl เอง
/// ต่อ request แทน (แบบเดียวกับที่ ProbePrimaryAsync ทำอยู่แล้วตั้งแต่แรก) SwitchTo() เลยเหลือแค่เปลี่ยน
/// state ล้วนๆ ไม่ต้องแตะ HttpClient เลย - พิสูจน์แล้วจริงจากการรัน standalone test กับ Server จริง
/// </summary>
public class ServerClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<ServerClient> _logger;

    // string/bool property get-set เป็น atomic operation อยู่แล้วใน .NET (reference/primitive แบบง่าย
    // ไม่มี torn read ให้กังวล) เลยไม่ต้องมี lock เหมือนที่เคยคิดไว้ตอนยังมี _httpClient.BaseAddress
    // ให้ป้องกัน - ผู้อ่านที่แข่งกันตอน SwitchTo() กำลังรันอยู่พอดี จะได้ค่าเก่าทั้งคู่หรือค่าใหม่ทั้งคู่เสมอ
    public string ActiveBaseUrl { get; private set; }
    public bool IsUsingFallback { get; private set; }

    public ServerClient(HttpClient httpClient, IOptions<ServerSettings> settings, ILogger<ServerClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;

        // seed ตรงนี้แทนที่จะเป็น Program.cs closure - ทำให้ ServerClient เป็นเจ้าของเดียวของ "URL ที่
        // กำลังใช้งานจริง" ตลอด lifetime ของ process (จำเป็นเพื่อรองรับสลับไป fallback ทีหลัง) ไม่ set
        // _httpClient.BaseAddress เลยตั้งแต่ต้น (ดู comment บนคลาสเรื่องบั๊กที่เจอ) ทุก method ใช้
        // ActiveBaseUrl สร้าง absolute URI เองทั้งหมด สม่ำเสมอกันหมดไม่มีจุดไหนพึ่ง BaseAddress อีกเลย
        ActiveBaseUrl = settings.Value.BaseUrl;

        // ---- แนบหลักฐานตัวตนของเครื่องนี้กับ "ทุก" request ที่ออกจาก HttpClient ตัวนี้ (2026-08-31) ----
        // ตั้งที่ DefaultRequestHeaders ครั้งเดียวแทนที่จะไปไล่ใส่ทีละ method โดยตั้งใจ: ทุก method ใน
        // คลาสนี้คุยกับ endpoint ฝั่ง agent ของ Server ทั้งหมด ไม่มีข้อยกเว้น - การไล่ใส่ทีละจุดคือจุดที่
        // method ใหม่ในอนาคตจะลืมใส่แล้วกลายเป็นช่องโหว่เงียบๆ อีกรอบ
        _httpClient.DefaultRequestHeaders.Add(AgentCredentials.GuidHeaderName, ClientIdentity.GetOrCreateClientGuid());
        _httpClient.DefaultRequestHeaders.Add(AgentCredentials.SecretHeaderName, AgentCredentials.Secret);
    }

    /// <summary>
    /// สลับ URL ที่ใช้งานจริง - แค่เปลี่ยน state (ActiveBaseUrl/IsUsingFallback) เท่านั้น ไม่แตะ
    /// HttpClient เลย (ดู comment บนคลาสเรื่องบั๊กที่เจอตอน BaseAddress mutation ไม่ได้) มีผลกับทุก
    /// method ในคลาสนี้ทันทีหลังเรียก เพราะทุก method อ่าน ActiveBaseUrl สดๆ ตอนสร้าง request แต่ละครั้ง
    /// </summary>
    public void SwitchTo(string baseUrl, bool isFallback)
    {
        ActiveBaseUrl = baseUrl;
        IsUsingFallback = isFallback;
    }

    /// <summary>ดึง rules + enabled state ล่าสุดจาก server คืน null ถ้าติดต่อไม่ได้</summary>
    public async Task<PollResponse?> PollAsync(string clientGuid, string machineName, CancellationToken ct, string? blocklistVersion = null)
    {
        var baseUrl = ActiveBaseUrl;
        try
        {
            // ส่ง IsUsingFallback ไปด้วยทุกครั้ง - Server เก็บไว้ (ClientMachine.UsingFallback) ให้ Console
            // แสดงเป็น badge ว่าตอนนี้เครื่องนี้ต่อผ่าน LAN ปกติหรือ fallback (Tailscale) อยู่
            // แนบเวอร์ชัน blocklist ที่ถืออยู่ไปด้วย - Server จะส่งรายการเต็มกลับมาเฉพาะตอนที่
            // เปลี่ยนจริงเท่านั้น (ดู /api/poll ฝั่ง Server) ไม่งั้น 40 เครื่องจะลากรายการเดิมข้ามเน็ต
            // ทุก 30 วินาทีโดยไม่มีอะไรต่างจากรอบก่อน
            var blocklistParam = string.IsNullOrEmpty(blocklistVersion)
                ? string.Empty
                : $"&blocklistVersion={Uri.EscapeDataString(blocklistVersion)}";
            var url = baseUrl.TrimEnd('/') + $"/api/poll/{clientGuid}?machineName={Uri.EscapeDataString(machineName)}&usingFallback={(IsUsingFallback ? "true" : "false")}{blocklistParam}";
            var response = await _httpClient.GetFromJsonAsync<PollResponse>(url, ct);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Poll failed - server อาจ offline อยู่หรือ BaseUrl ไม่ถูกต้อง (BaseUrl: {BaseUrl}) (fail-secure: ใช้กฎล่าสุดที่มีต่อไป)",
                baseUrl);
            return null;
        }
    }

    /// <summary>
    /// Probe primary แบบ lightweight ระหว่างที่กำลังใช้ fallback อยู่ - ใช้ absolute URI ของ primary
    /// ตรงๆ (ไม่เกี่ยวกับ ActiveBaseUrl ที่ตอนนี้ชี้ไป fallback อยู่เลย) ผลลัพธ์ใช้แค่นับ
    /// consecutive-success สำหรับตัดสินใจสลับกลับเท่านั้น ไม่ apply กับ _lastKnownState/enforcement
    /// ใดๆ ทั้งสิ้น (ต่างจาก PollAsync ที่ผลจะถูกใช้จริง)
    /// </summary>
    public async Task<bool> ProbePrimaryAsync(string primaryBaseUrl, string clientGuid, string machineName, CancellationToken ct)
    {
        try
        {
            var url = primaryBaseUrl.TrimEnd('/') + $"/api/poll/{clientGuid}?machineName={Uri.EscapeDataString(machineName)}";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            using var probeTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            probeTimeoutCts.CancelAfter(TimeSpan.FromSeconds(5));

            using var response = await _httpClient.SendAsync(request, probeTimeoutCts.Token);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Probe primary ({PrimaryUrl}) ระหว่างใช้ fallback ยังไม่สำเร็จ", primaryBaseUrl);
            return false;
        }
    }

    /// <summary>
    /// ส่ง fingerprint ของโปรแกรมที่ยังไม่รู้จักไปให้ Server จำแนกเป็นชุด (2026-08-31)
    /// คืน null ถ้าติดต่อไม่ได้ - ผู้เรียก (ProgramControlService) จะเอา hash กลับเข้าคิวเองเพื่อ
    /// ลองใหม่รอบหน้า ไม่ใช่ทิ้งไปเงียบๆ
    /// </summary>
    public async Task<ClassifyResponse?> ClassifyProgramsAsync(List<ProgramFingerprintDto> programs, CancellationToken ct)
    {
        try
        {
            var url = ActiveBaseUrl.TrimEnd('/') + "/api/classify";
            var response = await _httpClient.PostAsJsonAsync(url, new { programs }, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("ขอผลจำแนกโปรแกรมไม่สำเร็จ - Server ตอบ {StatusCode}", response.StatusCode);
                return null;
            }

            return await response.Content.ReadFromJsonAsync<ClassifyResponse>(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ขอผลจำแนกโปรแกรมจาก Server ไม่สำเร็จ - จะลองใหม่ในรอบถัดไป");
            return null;
        }
    }

    /// <summary>ส่ง log กลับไปให้ server เก็บ ไม่ throw ถ้า fail (log เป็นแค่ best-effort)</summary>
    public async Task SendLogAsync(LogEntryDto log, CancellationToken ct)
    {
        try
        {
            var url = ActiveBaseUrl.TrimEnd('/') + "/api/logs";
            await _httpClient.PostAsJsonAsync(url, log, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ส่ง log กลับ server ไม่สำเร็จ (จะไม่ retry - log ถัดไปจะพยายามใหม่)");
        }
    }

    /// <summary>
    /// ยืนยันกับ server ว่า command นี้ได้รับ/ประมวลผลแล้ว (กัน command เดิมถูกส่งซ้ำไปเรื่อยๆ ทุกรอบ poll)
    /// ไม่ throw ถ้า fail - ถ้า ack ไม่สำเร็จ command จะยังเป็น Pending ต่อไป และจะเจอผ่าน poll รอบถัดไปอีก
    /// (ไม่เป็นไร เพราะ CommandProcessor กัน process ซ้ำด้วย commandId ไว้แล้ว)
    /// </summary>
    public async Task AckCommandAsync(int commandId, CancellationToken ct)
    {
        try
        {
            var url = ActiveBaseUrl.TrimEnd('/') + $"/api/commands/{commandId}/ack";
            await _httpClient.PostAsync(url, null, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Ack command {CommandId} ไม่สำเร็จ (จะเจอ command นี้ซ้ำในรอบ poll ถัดไป)", commandId);
        }
    }

    /// <summary>
    /// รายงาน "ผลจริง" ของ command กลับไปหา Server (ทาง HTTP) - fallback ของ SignalR hub method
    /// "ReportCommandResult" ใช้เมื่อ Agent ไม่ได้ connect SignalR อยู่ (ดู CommandResultReporter)
    ///
    /// ต่างจาก AckCommandAsync: ack บอกแค่ "ได้รับแล้ว" อันนี้บอก "ทำได้จริงไหม"
    /// ไม่ throw ถ้า fail - Server จะ mark เป็น TimedOut ให้เองหลังครบเวลา ซึ่งถูกต้องแล้ว
    /// สำหรับกรณีที่เครื่องนี้ติดต่อ server ไม่ได้จริงๆ
    /// </summary>
    public async Task ReportCommandResultAsync(int commandId, bool success, string? message, CancellationToken ct)
    {
        try
        {
            var url = ActiveBaseUrl.TrimEnd('/') + $"/api/commands/{commandId}/result";
            await _httpClient.PostAsJsonAsync(
                url,
                new { success, message },
                ct);

            _logger.LogInformation(
                "รายงานผลจริงของ command {CommandId} ผ่าน HTTP สำเร็จ (success: {Success})", commandId, success);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "รายงานผลจริงของ command {CommandId} ไม่สำเร็จทั้ง SignalR และ HTTP - Server จะ mark เป็น TimedOut ให้เองหลังครบเวลา",
                commandId);
        }
    }
}
