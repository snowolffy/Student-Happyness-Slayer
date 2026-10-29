namespace OnionProcOparetor.Agent.Models;

/// <summary>
/// ผลลัพธ์จาก GET /api/poll/{clientGuid} ฝั่ง Server
/// </summary>
public class PollResponse
{
    /// <summary>true = enforcement เปิดอยู่ (global + per-machine ต้องเปิดพร้อมกัน)</summary>
    public bool Enabled { get; set; }

    public List<RuleDto> Rules { get; set; } = new();

    /// <summary>กฎบล็อกเว็บที่เปิดใช้งานอยู่ (Website Control, 2026-09-11) - ดู WebsiteControlService</summary>
    public List<WebsiteRuleDto> WebsiteRules { get; set; } = new();

    /// <summary>[OPTIONAL] ถ้า Server ตั้ง override ไว้ ให้ใช้ค่านี้แทน PollIntervalSeconds ใน appsettings.json</summary>
    public int? PollIntervalOverrideSeconds { get; set; }

    /// <summary>Command ที่ยังไม่ deliver ของเครื่องนี้ (fallback path คู่ขนานกับ SignalR ReceiveCommand)</summary>
    public List<PendingCommandDto> PendingCommands { get; set; } = new();

    /// <summary>
    /// เวอร์ชันปัจจุบันของ blocklist ฝั่ง Server (Program Control, 2026-08-31)
    /// Agent เก็บไว้แล้วแนบกลับไปกับ poll รอบถัดไป
    /// </summary>
    public string? BlocklistVersion { get; set; }

    /// <summary>
    /// รายการ SHA-256 ที่ต้องบล็อก - null แปลว่า "เวอร์ชันเดิม ใช้รายการที่ถืออยู่ต่อ"
    /// ไม่ใช่ "ไม่มีรายการเลย" (ดู ProgramControlService.ApplyBlocklist - ตีความสองอย่างนี้สลับกัน
    /// จะกลายเป็นล้าง blocklist ทิ้งทุกรอบ poll)
    /// </summary>
    public List<string>? BlockedHashes { get; set; }
}