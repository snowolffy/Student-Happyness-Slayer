using System.Net.Http.Json;
using OnionProcOparetor.Agent.Models;

namespace OnionProcOparetor.Agent.Services;

/// <summary>
/// คุยกับ Server ผ่าน HTTP - poll เพื่อดึง rules/enabled state และ push log กลับ
/// ออกแบบให้ fail เงียบๆ ถ้า server ติดต่อไม่ได้ (fail-secure: ฝั่ง Worker
/// จะยังคง enforce กฎล่าสุดที่มีต่อไป ไม่ crash หรือหยุดทำงาน)
/// </summary>
public class ServerClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<ServerClient> _logger;

    public ServerClient(HttpClient httpClient, ILogger<ServerClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    /// <summary>ดึง rules + enabled state ล่าสุดจาก server คืน null ถ้าติดต่อไม่ได้</summary>
    public async Task<PollResponse?> PollAsync(string clientGuid, string machineName, CancellationToken ct)
    {
        try
        {
            var url = $"/api/poll/{clientGuid}?machineName={Uri.EscapeDataString(machineName)}";
            var response = await _httpClient.GetFromJsonAsync<PollResponse>(url, ct);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Poll failed - server อาจ offline อยู่ (fail-secure: ใช้กฎล่าสุดที่มีต่อไป)");
            return null;
        }
    }

    /// <summary>ส่ง log กลับไปให้ server เก็บ ไม่ throw ถ้า fail (log เป็นแค่ best-effort)</summary>
    public async Task SendLogAsync(LogEntryDto log, CancellationToken ct)
    {
        try
        {
            await _httpClient.PostAsJsonAsync("/api/logs", log, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ส่ง log กลับ server ไม่สำเร็จ (จะไม่ retry - log ถัดไปจะพยายามใหม่)");
        }
    }
}
