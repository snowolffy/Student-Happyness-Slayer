namespace OnionProcOparetor.Server.Models;

/// <summary>
/// Override ค่า Rule.IsEnabled เฉพาะเครื่องเดียว (ไม่กระทบ global rule หรือเครื่องอื่น) - ถ้ามี row
/// สำหรับ (RuleId, ClientGuid) คู่หนึ่ง ค่า IsEnabled ตรงนี้ชนะ Rule.IsEnabled เสมอ 100% (ไม่มี
/// merge/priority ซับซ้อน) ดู /api/poll/{clientGuid} สำหรับจุดที่ resolve ค่านี้จริง
///
/// ClientGuid เป็น plain string เหมือน ClientCommand/LogEntry (ไม่ผูก FK กับ ClientMachine.Id
/// โดยตั้งใจ - ตาม convention เดิมของ project)
/// </summary>
public class RuleOverride
{
    public int Id { get; set; }

    public int RuleId { get; set; }

    public string ClientGuid { get; set; } = string.Empty;

    /// <summary>ค่าที่ override ทับ Rule.IsEnabled สำหรับเครื่องนี้เท่านั้น</summary>
    public bool IsEnabled { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
