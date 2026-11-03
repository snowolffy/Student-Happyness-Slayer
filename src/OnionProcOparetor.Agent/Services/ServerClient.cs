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

    /// <summary>ดึง rules + enabled state ล่าสุดจาก server คืน null ถ้าติดต่อไม่ได้ - studentCode (WP3-3,
    /// 2026-10-31) แค่แนบไปให้เห็นใน log ฝั่ง Server เฉยๆ (ดู ClientState.CurrentStudentCode) ไม่มีผลต่อ
    /// การตัดสินใจของ poll เลย - "ตัวจริง" ของนักเรียนที่ login อยู่มาจาก StudentSession ที่ AgentTray
    /// สร้างตรงกับ Server อยู่แล้ว (ดู StudentAuthEndpoints) ไม่ได้พึ่ง query param นี้</summary>
    public Task<PollResponse?> PollAsync(string clientGuid, string machineName, CancellationToken ct, string? blocklistVersion = null, string? studentCode = null) =>
        PollViaAsync(ActiveBaseUrl, IsUsingFallback, clientGuid, machineName, ct, blocklistVersion, studentCode);

    /// <summary>
    /// ช่วงเริ่มทำงาน (WP5): แข่ง poll ระหว่าง LAN (primary) กับ Tailscale (fallback) ดู StartupRouteRace
    /// ทางไหนได้ PollResponse ก่อนจะถูกตั้งเป็นทางที่ใช้งานจริง (SwitchTo) ทันที ผลของ poll ที่ชนะใช้ได้เลย
    /// เหมือน PollAsync ปกติ (ไม่ต้องยิงซ้ำ) แต่ละทางส่ง usingFallback ตรงกับทางของตัวเอง (LAN=false,
    /// Tailscale=true) Server จึงไม่ได้ค่าผิดจาก request ของทางที่ชนะ - ถ้ายิงไปทั้งสองทาง ทางที่แพ้อาจไปถึง
    /// Server ทีหลังแล้วเขียนทับค่าได้ ผู้เรียก (Worker) จึงต้อง poll ซ้ำเร็วๆ อีกรอบเมื่อ FallbackFired
    /// </summary>
    public async Task<StartupRaceResult<PollResponse>> RaceStartupPollAsync(
        string primaryBaseUrl, string fallbackBaseUrl, string clientGuid, string machineName,
        CancellationToken ct, string? blocklistVersion = null)
    {
        var result = await StartupRouteRace.RunAsync(
            token => PollViaAsync(primaryBaseUrl, false, clientGuid, machineName, token, blocklistVersion),
            token => PollViaAsync(fallbackBaseUrl, true, clientGuid, machineName, token, blocklistVersion),
            StartupRouteRace.PrimaryHeadStart,
            ct);
        // หมายเหตุ: ไม่แนบ studentCode ที่นี่ตั้งใจ - ช่วง startup race เกิดเร็วมากหลัง service เพิ่งเริ่ม
        // (ก่อนจะมีใคร login ทันเวลา) ไม่คุ้มเพิ่มพารามิเตอร์ที่ไม่มีผลต่อ logic อะไรเลย (ดู PollAsync)

        if (result.Response is not null)
        {
            SwitchTo(result.ViaFallback ? fallbackBaseUrl : primaryBaseUrl, result.ViaFallback);
        }

        return result;
    }

    /// <summary>
    /// ใช้หลังการแข่งตอนเริ่มทำงานได้ Tailscale: probe LAN เบื้องหลังเป็นระยะ (2, 5, 10 แล้วทุก 30 วิ ตาม
    /// ExponentialBackoffRetryPolicy) จนกว่า LAN จะตอบ คืน true เมื่อ LAN ตอบ คืน false เมื่อถูกยกเลิก
    /// ไม่ probe ระหว่างที่ isPaused() เป็น true (เช่น bypass เปิดอยู่ ห้ามยิงออกไปหา Server)
    /// </summary>
    public Task<bool> WaitForPrimaryAsync(string primaryBaseUrl, string clientGuid, string machineName, Func<bool> isPaused, CancellationToken ct) =>
        StartupRouteRace.WaitUntilReachableAsync(
            token => ProbePrimaryAsync(primaryBaseUrl, clientGuid, machineName, token),
            isPaused,
            attempt => ExponentialBackoffRetryPolicy.GetDelay(attempt),
            ct);

    private async Task<PollResponse?> PollViaAsync(string baseUrl, bool viaFallback, string clientGuid, string machineName, CancellationToken ct, string? blocklistVersion, string? studentCode = null)
    {
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
            // studentCode (WP3-3, 2026-10-31) - ดู doc comment บน PollAsync ว่าทำไมไม่มีผลต่อ logic ใดๆ
            var studentParam = string.IsNullOrEmpty(studentCode)
                ? string.Empty
                : $"&studentCode={Uri.EscapeDataString(studentCode)}";
            var url = baseUrl.TrimEnd('/') + $"/api/poll/{clientGuid}?machineName={Uri.EscapeDataString(machineName)}&usingFallback={(viaFallback ? "true" : "false")}{blocklistParam}{studentParam}";
            var response = await _httpClient.GetFromJsonAsync<PollResponse>(url, ct);
            return response;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // ถูกยกเลิกโดยตั้งใจ (แพ้การแข่งตอนเริ่มทำงาน หรือ service กำลังหยุด) ไม่ใช่ server มีปัญหา
            _logger.LogDebug("Poll ไปที่ {BaseUrl} ถูกยกเลิก", baseUrl);
            return null;
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
            // usingFallback=true เพราะ probe นี้ถูกเรียกเฉพาะตอนที่กำลังใช้ fallback อยู่จริงเท่านั้น (WP5) -
            // เดิมไม่ส่งพารามิเตอร์นี้ Server จึงตีความเป็น false แล้วเขียน ClientMachine.UsingFallback = false
            // ทุกครั้งที่ probe LAN สำเร็จ ทั้งที่เครื่องยังใช้ fallback อยู่ (badge ใน Console กระพริบสลับไปมา)
            var url = primaryBaseUrl.TrimEnd('/') + $"/api/poll/{clientGuid}?machineName={Uri.EscapeDataString(machineName)}&usingFallback=true";
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

    /// <summary>
    /// ขอ Path/Arguments จริงของโปรแกรมที่ Admin อนุญาตให้เปิดทั้งห้องได้ (WP4-1, 2026-11-01) - เรียกก่อน
    /// รัน LaunchProgram command เสมอ (ดู CommandProcessor.HandleLaunchProgramAsync) เพื่อตรวจซ้ำว่า
    /// โปรแกรมนี้ยังอยู่ในรายการจริง ณ เวลาที่รัน ไม่ใช่ตอนที่ command ถูกสร้างขึ้นมา (รายการอาจถูกแก้/ลบ
    /// ไปแล้วระหว่างที่ command ค้างอยู่ใน queue)
    ///
    /// คืน null ทั้งกรณี "ไม่พบ" (404 - ถูกลบ/ไม่อยู่ในรายการ) และกรณี "ติดต่อ Server ไม่ได้" เพราะทั้งสอง
    /// กรณีผู้เรียกต้องปฏิเสธการรันเหมือนกัน (fail-secure - ไม่รันถ้ายืนยันกับ Server ไม่ได้ว่าอนุญาตจริง)
    /// </summary>
    public async Task<LaunchableProgramDto?> GetLaunchableProgramAsync(int programId, CancellationToken ct)
    {
        try
        {
            var url = ActiveBaseUrl.TrimEnd('/') + $"/api/launchable-programs/{programId}/for-agent";
            var response = await _httpClient.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("ขอข้อมูลโปรแกรม id={ProgramId} ไม่สำเร็จ - Server ตอบ {StatusCode}", programId, response.StatusCode);
                return null;
            }

            return await response.Content.ReadFromJsonAsync<LaunchableProgramDto>(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ขอข้อมูลโปรแกรม id={ProgramId} จาก Server ไม่สำเร็จ", programId);
            return null;
        }
    }

    /// <summary>
    /// ดาวน์โหลดไฟล์แจกจริง (WP4-3, 2026-11-01) - เรียกก่อนเขียนลง Desktop ของ user เสมอ (ดู
    /// CommandProcessor.HandleDownloadFileAsync) คืน (null, null) ถ้าติดต่อ Server ไม่ได้/404 - ผู้เรียก
    /// ปฏิเสธการเขียนไฟล์ทันที (fail-secure เหมือน GetLaunchableProgramAsync) ชื่อไฟล์ได้จาก
    /// Content-Disposition ที่ Server ตั้งไว้ (ไม่เชื่อ 100% - ผู้เรียกต้อง sanitize ซ้ำอีกชั้นเสมอ)
    /// </summary>
    public async Task<(byte[]? Bytes, string? FileName)> DownloadDistributedFileAsync(int distributedFileId, CancellationToken ct)
    {
        try
        {
            var url = ActiveBaseUrl.TrimEnd('/') + $"/api/files/distributed/{distributedFileId}/content";
            var response = await _httpClient.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("ดาวน์โหลดไฟล์แจก id={DistributedFileId} ไม่สำเร็จ - Server ตอบ {StatusCode}", distributedFileId, response.StatusCode);
                return (null, null);
            }

            var fileName = response.Content.Headers.ContentDisposition?.FileNameStar
                ?? response.Content.Headers.ContentDisposition?.FileName;
            var bytes = await response.Content.ReadAsByteArrayAsync(ct);
            return (bytes, fileName);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ดาวน์โหลดไฟล์แจก id={DistributedFileId} จาก Server ไม่สำเร็จ", distributedFileId);
            return (null, null);
        }
    }

    /// <summary>
    /// ขอรายละเอียดจริงของงานเก็บไฟล์ (WP4-3, 2026-11-01) - เรียกก่อนบอก AgentTray ให้โชว์ตัวเลือกไฟล์เสมอ
    /// (ดู CommandProcessor.HandleCollectFileAsync) คืน null ทั้งกรณี "ไม่พบ" และกรณี "ติดต่อ Server ไม่ได้"
    /// (ผู้เรียกปฏิเสธเหมือนกันทั้งคู่ - fail-secure เหมือน GetLaunchableProgramAsync)
    /// </summary>
    public async Task<CollectionJobForAgentDto?> GetCollectionJobForAgentAsync(int jobId, CancellationToken ct)
    {
        try
        {
            var url = ActiveBaseUrl.TrimEnd('/') + $"/api/files/collection-jobs/{jobId}/for-agent";
            var response = await _httpClient.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("ขอรายละเอียดงานเก็บไฟล์ id={JobId} ไม่สำเร็จ - Server ตอบ {StatusCode}", jobId, response.StatusCode);
                return null;
            }

            return await response.Content.ReadFromJsonAsync<CollectionJobForAgentDto>(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ขอรายละเอียดงานเก็บไฟล์ id={JobId} จาก Server ไม่สำเร็จ", jobId);
            return null;
        }
    }

    /// <summary>
    /// ส่งไฟล์ที่นักเรียนเลือกส่งขึ้น Server (WP4-3, 2026-11-01) - เรียกจาก local loopback endpoint
    /// POST /files/collect-result (ดู Program.cs) หลัง AgentTray อัปโหลดไฟล์มาให้ Agent relay ต่อ
    /// คืนข้อความ error ที่ Server ตอบกลับมาตรงๆ (เช่น "ไฟล์ใหญ่เกินกำหนด") ให้ Tray โชว์ให้นักเรียนเห็น
    /// ไม่ใช่แค่ true/false เฉยๆ
    /// </summary>
    public async Task<(bool Success, string? Error)> UploadCollectedFileAsync(int jobId, string fileName, byte[] fileBytes, CancellationToken ct)
    {
        try
        {
            var url = ActiveBaseUrl.TrimEnd('/') + $"/api/files/collection-jobs/{jobId}/upload";
            using var content = new MultipartFormDataContent();
            using var fileContent = new ByteArrayContent(fileBytes);
            fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
            content.Add(fileContent, "file", fileName);

            var response = await _httpClient.PostAsync(url, content, ct);
            if (response.IsSuccessStatusCode)
            {
                return (true, null);
            }

            string? error = null;
            try
            {
                var errorBody = await response.Content.ReadFromJsonAsync<ErrorResponseDto>(ct);
                error = errorBody?.Error;
            }
            catch
            {
                // ตอบมาไม่เป็น JSON ตามรูปแบบที่คาด (เช่น Server ตัดการเชื่อมต่อกลางทางเพราะเกิน
                // MaxRequestBodySize) - ใช้ status code ธรรมดาแทน ไม่ throw ต่อ
            }

            _logger.LogWarning("ส่งไฟล์ที่เก็บได้ (job {JobId}) ไม่สำเร็จ - Server ตอบ {StatusCode}: {Error}", jobId, response.StatusCode, error);
            return (false, error ?? $"Server ปฏิเสธ (HTTP {(int)response.StatusCode})");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ส่งไฟล์ที่เก็บได้ (job {JobId}) ขึ้น Server ไม่สำเร็จ", jobId);
            return (false, "ติดต่อ Server ไม่ได้");
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
    /// ส่งชุดประวัติเว็บที่สะสมไว้ขึ้น server (WP2-4, 2026-10-31) - best-effort เหมือน SendLogAsync ไม่
    /// throw ถ้า fail (ชุดที่ส่งไม่สำเร็จจะหายไปเลย ไม่ retry - รอบถัดไปอีก 1 นาทีจะมีของใหม่มาแทนอยู่ดี
    /// ไม่ใช่ใบเช็คชื่อทางการที่ต้องครบทุกแถว ดู Services/WebHistoryCollectionService.cs)
    /// </summary>
    public async Task SendWebVisitsAsync(List<WebVisitDto> visits, CancellationToken ct)
    {
        try
        {
            var url = ActiveBaseUrl.TrimEnd('/') + "/api/web-visits";
            await _httpClient.PostAsJsonAsync(url, new { visits }, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "ส่งประวัติเว็บ {Count} รายการขึ้น server ไม่สำเร็จ (จะไม่ retry - รอบถัดไปจะมีของใหม่มาแทน)",
                visits.Count);
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
            var response = await _httpClient.PostAsync(url, null, ct);

            // เดิมทิ้ง response ทั้งก้อนโดยไม่ดูเลย ซึ่งแปลว่า ack ที่ถูก Server ปฏิเสธ (401 เพราะ
            // agent secret/GUID ไม่ตรง, 404 เพราะ command ถูกลบไปแล้ว) เงียบสนิททั้งสองฝั่ง:
            // Agent คิดว่า ack แล้ว ส่วน Server ยังเห็นเป็น Pending อยู่ - เป็นสาเหตุที่คำสั่งค้างใน
            // หน้า PENDING COMMANDS ทั้งที่รายงานผลว่าสำเร็จไปแล้ว แล้วหาสาเหตุไม่เจอเพราะไม่มี log
            // ที่ไหนเลย (PostAsync ไม่ throw เมื่อได้ status code ที่ไม่ใช่ 2xx)
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Ack command {CommandId} ถูก Server ปฏิเสธด้วย HTTP {StatusCode} - command นี้จะยังค้างเป็น Pending และถูกส่งกลับมาซ้ำทุกรอบ poll",
                    commandId, (int)response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Ack command {CommandId} ไม่สำเร็จ (จะเจอ command นี้ซ้ำในรอบ poll ถัดไป)", commandId);
        }
    }

    /// <summary>
    /// ยกมือขอความช่วยเหลือจากครู (WP4-2, 2026-11-01) - ส่งต่อให้ Server ตรงๆ (clientGuid/secret แนบมาจาก
    /// DefaultRequestHeaders อยู่แล้วทุก request - ดู constructor) คืน null เฉพาะตอนติดต่อ Server ไม่ได้
    /// เลย (network/timeout) ส่วน cooldown/มีรายการค้างอยู่แล้วเป็น "ตอบสำเร็จ แต่ Success=false" ที่ Server
    /// ตอบ 200 กลับมาเสมอ (ดู RaiseHandEndpoints.cs) ไม่ throw ไม่ว่ากรณีไหน - ผู้เรียก (loopback endpoint
    /// ด้านล่างใน Program.cs) เป็นคนแปลงเป็น HTTP response ให้ AgentTray อีกชั้น
    /// </summary>
    public async Task<RaiseHandResult?> SendRaiseHandAsync(string reason, string? note, CancellationToken ct)
    {
        try
        {
            var url = ActiveBaseUrl.TrimEnd('/') + "/api/raisehand";
            var response = await _httpClient.PostAsJsonAsync(url, new { reason, note }, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("ยกมือไม่สำเร็จ - Server ตอบ {StatusCode}", response.StatusCode);
                return null;
            }

            return await response.Content.ReadFromJsonAsync<RaiseHandResult>(cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ยกมือไม่สำเร็จ - ติดต่อ Server ไม่ได้");
            return null;
        }
    }

    /// <summary>ยกเลิกยกมือที่ค้างอยู่ของเครื่องนี้ (WP4-2, 2026-11-01) - คืน false ทั้งกรณีติดต่อไม่ได้และ Server ตอบว่าไม่พบรายการให้ยกเลิก (ผู้เรียกไม่ต้องแยกสองกรณีนี้ - ผลลัพธ์ฝั่ง UI เหมือนกัน)</summary>
    public async Task<bool> CancelRaiseHandAsync(CancellationToken ct)
    {
        try
        {
            var url = ActiveBaseUrl.TrimEnd('/') + "/api/raisehand/cancel";
            var response = await _httpClient.PostAsync(url, null, ct);
            if (!response.IsSuccessStatusCode)
            {
                return false;
            }

            var result = await response.Content.ReadFromJsonAsync<RaiseHandCancelResponse>(cancellationToken: ct);
            return result?.Success ?? false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ยกเลิกยกมือไม่สำเร็จ - ติดต่อ Server ไม่ได้");
            return false;
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

/// <summary>รูปแบบ error ทั่วไปที่ endpoint ฝั่ง Server ส่งกลับ (WP4-3, 2026-11-01) - ใช้แค่ parse ข้อความ
/// error จาก UploadCollectedFileAsync เพื่อโชว์ให้นักเรียนเห็นข้อความที่ตรงปัญหาจริง</summary>
internal sealed record ErrorResponseDto(string? Error);

/// <summary>ผลของการแข่งตอนเริ่มทำงาน - Response เป็น null = ทั้งสองทางยังไม่ตอบ</summary>
public sealed record StartupRaceResult<T>(T? Response, bool ViaFallback, bool FallbackFired) where T : class;

/// <summary>
/// ตรรกะการเลือกเส้นทางตอนเริ่มทำงาน (WP5) แยกออกมาเป็น static ล้วนๆ ไม่พึ่ง HttpClient/registry/DI
/// เพื่อให้ทดสอบได้โดยตรงโดยไม่ต้องรัน Agent ทั้งตัว (Agent ต้องใช้สิทธิ์ admin และเขียน HKLM)
///
/// ตกลงกับผู้ใช้แล้ว (docs/roadmap.md หัวข้อ "เครือข่ายและความปลอดภัย"): ยิง LAN ก่อน ถ้า LAN ยังไม่ตอบ
/// ภายใน PrimaryHeadStart ให้ยิง Tailscale ตามไปด้วย ทางไหนตอบสำเร็จก่อนใช้ทางนั้น
/// </summary>
public static class StartupRouteRace
{
    public static readonly TimeSpan PrimaryHeadStart = TimeSpan.FromSeconds(2);

    /// <summary>
    /// tryPrimary/tryFallback คืน null = ทางนั้นไม่สำเร็จ (exception ถูกนับเป็นไม่สำเร็จด้วย) ทางที่แพ้จะถูก
    /// cancel ผ่าน token ของมันเอง ไม่รอให้จบ ถ้า LAN ล้มเหลวเร็วกว่า headStart (เช่น connection refused)
    /// จะยิง Tailscale ทันทีไม่ต้องรอให้ครบ 2 วิ เพราะรอไปก็ไม่มีอะไรให้ LAN ชนะแล้ว
    /// </summary>
    public static async Task<StartupRaceResult<T>> RunAsync<T>(
        Func<CancellationToken, Task<T?>> tryPrimary,
        Func<CancellationToken, Task<T?>> tryFallback,
        TimeSpan primaryHeadStart,
        CancellationToken ct) where T : class
    {
        using var primaryCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        using var fallbackCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        var primaryTask = SafeAsync(tryPrimary, primaryCts.Token);

        // LAN ได้เปรียบ: รอ LAN ก่อนไม่เกิน primaryHeadStart (Task.WhenAny ไม่ throw แม้ delay ถูก cancel)
        await Task.WhenAny(primaryTask, Task.Delay(primaryHeadStart, ct));
        if (primaryTask.IsCompleted && primaryTask.Result is { } early)
        {
            return new StartupRaceResult<T>(early, ViaFallback: false, FallbackFired: false);
        }

        if (ct.IsCancellationRequested)
        {
            primaryCts.Cancel();
            return new StartupRaceResult<T>(null, ViaFallback: false, FallbackFired: false);
        }

        var fallbackTask = SafeAsync(tryFallback, fallbackCts.Token);

        // ใส่ LAN ไว้ก่อนใน list - ถ้าเสร็จพร้อมกันพอดี Task.WhenAny จะเลือกตัวแรกใน list ที่เสร็จแล้ว
        var pending = new List<Task<T?>>();
        if (!primaryTask.IsCompleted)
        {
            pending.Add(primaryTask);
        }
        pending.Add(fallbackTask);

        while (pending.Count > 0)
        {
            var done = await Task.WhenAny(pending);
            pending.Remove(done);

            if (done.Result is { } winner)
            {
                var viaFallback = ReferenceEquals(done, fallbackTask);
                (viaFallback ? primaryCts : fallbackCts).Cancel();
                return new StartupRaceResult<T>(winner, viaFallback, FallbackFired: true);
            }
        }

        return new StartupRaceResult<T>(null, ViaFallback: false, FallbackFired: true);
    }

    /// <summary>
    /// วน probe จนกว่าจะสำเร็จ (คืน true) หรือถูกยกเลิก (คืน false) รอ delayForAttempt(attempt) ก่อน probe
    /// แต่ละครั้ง โดย attempt เริ่มที่ 1 (เพิ่งรู้ว่าทางนี้ไม่ตอบ ไม่ต้อง probe ซ้ำทันที) ข้ามรอบที่ isPaused()
    /// </summary>
    public static async Task<bool> WaitUntilReachableAsync(
        Func<CancellationToken, Task<bool>> probe,
        Func<bool> isPaused,
        Func<int, TimeSpan> delayForAttempt,
        CancellationToken ct)
    {
        for (var attempt = 1; !ct.IsCancellationRequested; attempt++)
        {
            try
            {
                await Task.Delay(delayForAttempt(attempt), ct);
            }
            catch (OperationCanceledException)
            {
                return false;
            }

            if (isPaused())
            {
                continue;
            }

            bool ok;
            try
            {
                ok = await probe(ct);
            }
            catch
            {
                ok = false;
            }

            if (ok)
            {
                return true;
            }
        }

        return false;
    }

    private static async Task<T?> SafeAsync<T>(Func<CancellationToken, Task<T?>> attempt, CancellationToken ct) where T : class
    {
        try
        {
            return await attempt(ct);
        }
        catch
        {
            return null;
        }
    }
}
