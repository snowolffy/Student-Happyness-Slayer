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

    /// <summary>
    /// ชื่อที่แสดง ณ ตอนที่บันทึก (WP4, 2026-10-30) - denormalize เหมือน ActorUsername ด้วยเหตุผลเดียวกัน
    /// เก็บแยกจาก ActorUsername เพราะ roadmap ต้องการทั้งสองชื่อใน Audit Log พร้อมกัน (username คงที่/ตามหา
    /// ตัวจริงได้เสมอ ส่วน DisplayName แก้ไขได้ภายหลัง - เก็บค่า ณ ตอนนั้นไว้กันความหมายเพี้ยนย้อนหลัง)
    /// </summary>
    public string? ActorDisplayName { get; set; }

    /// <summary>
    /// ผู้กระทำฝั่งนักเรียน (หมวด StudentAction - WP3-1, 2026-10-31) - denormalize เหมือน ActorUsername/
    /// ActorAdminUserId ข้างบนด้วยเหตุผลเดียวกัน (คนละ DbContext, ต้องอยู่รอดแม้ Student ถูกลบไปแล้ว)
    /// ใช้คู่กันเสมอ - มีค่าเฉพาะตอน Category == StudentAction
    /// </summary>
    public int? ActorStudentId { get; set; }
    public string? ActorStudentCode { get; set; }

    // เครื่องเป้าหมาย (หมวด AdminAction ที่เจาะจงเครื่อง, AgentConnectivity และ StudentAction) - denormalize เช่นกัน
    public string? TargetClientGuid { get; set; }
    public string? TargetMachineName { get; set; }

    /// <summary>ข้อมูลเสริมแบบ structured เช่น exception text (หมวด SystemEvent)</summary>
    public string? DetailJson { get; set; }
}

public enum ActivityLogCategory
{
    AdminAction,
    AgentConnectivity,
    SystemEvent,

    /// <summary>การกระทำของนักเรียนเอง เช่น login/logout/เปลี่ยนรหัสผ่าน (WP3-1, 2026-10-31)</summary>
    StudentAction
}

public enum ActivityLogSeverity
{
    Info,
    Warning,
    Error,
    Critical
}
