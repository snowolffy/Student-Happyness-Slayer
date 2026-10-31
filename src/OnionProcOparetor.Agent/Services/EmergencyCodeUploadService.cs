using System.Net.Http.Json;
using Microsoft.Extensions.Options;

namespace OnionProcOparetor.Agent.Services;

/// <summary>
/// ส่งประวัติการใช้รหัสฉุกเฉินที่ค้างอยู่ใน EmergencyCodeService ขึ้น Server เป็นระยะ (WP3-4,
/// 2026-10-31) - ตามที่ roadmap กำหนด "บันทึกการใช้รหัสฉุกเฉินไว้ แล้วส่งขึ้น Server ตอนกลับมาออนไลน์"
///
/// ตั้งใจแยกเป็น hosted service ของตัวเอง ไม่เกาะไปกับ Worker.cs/ServerClient.cs ที่มีอยู่แล้ว (ซึ่งเป็น
/// จุดที่ WP3-3 กำลังแก้พร้อมกันอยู่ - เรื่องบังคับล็อกอิน/แนบ student ไปกับ poll) เพื่อตัดความเสี่ยงชน
/// ไฟล์เดียวกันข้าม WP ทั้งคู่ ผลคือ path นี้ไม่ใช้ประโยชน์จาก ServerClient's Tailscale fallback
/// switching (ActiveBaseUrl/SwitchTo) - ยิงแค่ ServerSettings:BaseUrl (primary) ก่อน แล้วลอง
/// FallbackBaseUrl ต่อถ้าตั้งไว้ ไม่ได้ sync state กับที่ Worker ตัดสินใจสลับไปแล้วจริงๆ ตอนนั้น
/// (ยอมรับ gap นี้ไว้ตรงๆ - ดูรายงาน WP3-4 ว่าเป็นจุดที่อาจรวมเข้ากับ ServerClient ทีหลังได้ถ้าต้องการ)
/// </summary>
public sealed class EmergencyCodeUploadService : BackgroundService
{
    private static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(30);

    private readonly EmergencyCodeService _emergencyCode;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ServerSettings _serverSettings;
    private readonly ILogger<EmergencyCodeUploadService> _logger;

    public EmergencyCodeUploadService(
        EmergencyCodeService emergencyCode,
        IHttpClientFactory httpClientFactory,
        IOptions<ServerSettings> serverSettings,
        ILogger<EmergencyCodeUploadService> logger)
    {
        _emergencyCode = emergencyCode;
        _httpClientFactory = httpClientFactory;
        _serverSettings = serverSettings.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_emergencyCode.IsConfigured)
        {
            // เครื่องนี้ไม่ได้ตั้งค่ารหัสฉุกเฉินไว้ - ไม่มีอะไรให้ส่งขึ้นเลย ไม่ต้องวน loop เปล่าๆ ทุก 30 วิ
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await TryFlushAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "EmergencyCodeUploadService: เกิด error ไม่คาดคิดตอนพยายามส่งประวัติการใช้รหัสฉุกเฉิน");
            }

            try
            {
                await Task.Delay(RetryInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task TryFlushAsync(CancellationToken ct)
    {
        var pending = _emergencyCode.DequeueAllPending();
        if (pending.Count == 0)
        {
            return;
        }

        var baseUrls = new List<string> { _serverSettings.BaseUrl };
        if (!string.IsNullOrWhiteSpace(_serverSettings.FallbackBaseUrl))
        {
            baseUrls.Add(_serverSettings.FallbackBaseUrl);
        }

        foreach (var baseUrl in baseUrls)
        {
            if (await TryUploadToAsync(baseUrl, pending, ct))
            {
                _logger.LogInformation("ส่งประวัติการใช้รหัสฉุกเฉิน {Count} รายการขึ้น {BaseUrl} สำเร็จ", pending.Count, baseUrl);
                return;
            }
        }

        // ทั้ง primary และ fallback (ถ้ามี) ยิงไม่สำเร็จ - เอากลับเข้าคิว ลองรอบหน้า
        _emergencyCode.RequeuePending(pending);
    }

    private async Task<bool> TryUploadToAsync(string baseUrl, List<DateTime> pending, CancellationToken ct)
    {
        try
        {
            using var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(10);
            client.DefaultRequestHeaders.Add(AgentCredentials.GuidHeaderName, ClientIdentity.GetOrCreateClientGuid());
            client.DefaultRequestHeaders.Add(AgentCredentials.SecretHeaderName, AgentCredentials.Secret);

            var url = baseUrl.TrimEnd('/') + "/api/emergency-code/usage";
            var response = await client.PostAsJsonAsync(
                url,
                new { usages = pending.Select(usedAt => new { usedAtUtc = usedAt }) },
                ct);

            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "EmergencyCodeUploadService: ส่งไป {BaseUrl} ไม่สำเร็จ", baseUrl);
            return false;
        }
    }
}
