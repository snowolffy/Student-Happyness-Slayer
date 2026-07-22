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
            _logger.LogWarning(ex,
                "Poll failed - server อาจ offline อยู่หรือ BaseUrl ไม่ถูกต้อง (BaseUrl: {BaseUrl}) (fail-secure: ใช้กฎล่าสุดที่มีต่อไป)",
                _httpClient.BaseAddress);
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

    /// <summary>
    /// ยืนยันกับ server ว่า command นี้ได้รับ/ประมวลผลแล้ว (กัน command เดิมถูกส่งซ้ำไปเรื่อยๆ ทุกรอบ poll)
    /// ไม่ throw ถ้า fail - ถ้า ack ไม่สำเร็จ command จะยังเป็น Pending ต่อไป และจะเจอผ่าน poll รอบถัดไปอีก
    /// (ไม่เป็นไร เพราะ CommandProcessor กัน process ซ้ำด้วย commandId ไว้แล้ว)
    /// </summary>
    public async Task AckCommandAsync(int commandId, CancellationToken ct)
    {
        try
        {
            await _httpClient.PostAsync($"/api/commands/{commandId}/ack", null, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Ack command {CommandId} ไม่สำเร็จ (จะเจอ command นี้ซ้ำในรอบ poll ถัดไป)", commandId);
        }
    }
}
