namespace OnionProcOparetor.Server.Models;

/// <summary>
/// แถวเดียวครอบคลุมทั้ง 3 หมวดของ Activity Log (audit การกระทำของครู, agent connect/disconnect,
/// system warning/error) - ใช้ Category แยกประเภทแทนการแยกตาราง เพราะ shape เหมือนกันหมดและ
/// retention rule เดียวกันทั้งหมด (ลบทิ้งเมื่ออายุเกิน 7 วัน - ดู ActivityLogCleanupService)
///
/// เก็บอยู่คนละ DB (activitylog.db) แยกจาก onionprocoparetor.db หลัก (ดู ActivityLogDbContext)
/// ตามที่ผู้ใช้ระบุไว้เพราะคาดว่า volume จะสูงกว่าและ lifecycle ต่างจากข้อมูลหลัก
/// </summary>
public class ActivityLogEntry
{
    public int Id { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public ActivityLogCategory Category { get; set; }
    public ActivityLogSeverity Severity { get; set; }

    /// <summary>รหัสสั้นระบุประเภท event เช่น "RuleToggled", "AgentDisconnected" หรือ logger category (ฝั่ง SystemEvent)</summary>
    public string EventType { get; set; } = string.Empty;

    /// <summary>ข้อความสรุปพร้อมใช้แสดงผลตรงๆ ไม่ต้อง template ฝั่ง client</summary>
    public string Description { get; set; } = string.Empty;

    // ผู้กระทำ (เฉพาะหมวด AdminAction) - denormalize username ไว้เหมือน LogEntry.MachineName
    // เพราะเป็นคนละ DbContext กัน FK ข้าม DB ไม่ได้ และ history ต้องอยู่รอดแม้ admin ถูกลบไปแล้ว
    public int? ActorAdminUserId { get; set; }
    public string? ActorUsername { get; set; }

    // เครื่องเป้าหมาย (หมวด AdminAction ที่เจาะจงเครื่อง และหมวด AgentConnectivity) - denormalize เช่นกัน
    public string? TargetClientGuid { get; set; }
    public string? TargetMachineName { get; set; }

    /// <summary>ข้อมูลเสริมแบบ structured เช่น exception text (หมวด SystemEvent)</summary>
    public string? DetailJson { get; set; }
}

public enum ActivityLogCategory
{
    AdminAction,
    AgentConnectivity,
    SystemEvent
}

public enum ActivityLogSeverity
{
    Info,
    Warning,
    Error,
    Critical
}
