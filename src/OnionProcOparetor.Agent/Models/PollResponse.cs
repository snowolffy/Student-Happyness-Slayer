namespace OnionProcOparetor.Agent.Models;

/// <summary>
/// ผลลัพธ์จาก GET /api/poll/{clientGuid} ฝั่ง Server
/// </summary>
public class PollResponse
{
    /// <summary>true = enforcement เปิดอยู่ (global + per-machine ต้องเปิดพร้อมกัน)</summary>
    public bool Enabled { get; set; }

    public List<RuleDto> Rules { get; set; } = new();

    /// <summary>[OPTIONAL] ถ้า Server ตั้ง override ไว้ ให้ใช้ค่านี้แทน PollIntervalSeconds ใน appsettings.json</summary>
    public int? PollIntervalOverrideSeconds { get; set; }
}