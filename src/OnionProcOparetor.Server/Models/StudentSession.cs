namespace OnionProcOparetor.Server.Models;

/// <summary>
/// ประวัติการใช้เครื่องของนักเรียนหนึ่งรอบ (WP3-1, 2026-10-31) - สร้างตอน login สำเร็จ ปิดตอน logout
/// (ไม่ว่าด้วยวิธีใด - ดู StudentSessionEndReason) มีอยู่ได้แค่หนึ่งแถว "เปิดอยู่" (EndedAt == null)
/// ต่อ ClientGuid เท่านั้นในเวลาเดียวกัน (บังคับที่ชั้น endpoint ไม่ใช่ DB constraint - ดู
/// Endpoints/StudentAuthEndpoints.cs)
///
/// ไม่ใช่ใบเช็คชื่อทางการ ใช้ดูประกอบเท่านั้น (ดู docs/roadmap.md หัวข้อ "ไม่มีตาราง/คาบเรียน") -
/// Endpoints/StudentSessionEndpoints.cs เป็นคนให้ Console ดึงมาแสดงเป็น "ประวัติการใช้เครื่อง"
/// </summary>
public class StudentSession
{
    public int Id { get; set; }

    public int StudentId { get; set; }

    /// <summary>เครื่องที่ใช้ - denormalize ClientGuid ตรงๆ (pattern เดียวกับ LogEntry.ClientGuid) ไม่ผูก FK
    /// กับ ClientMachine เพราะเครื่องอาจถูกลบออกจากระบบไปแล้วแต่ประวัตินักเรียนต้องยังอยู่ครบ</summary>
    public string ClientGuid { get; set; } = string.Empty;

    /// <summary>ชื่อเครื่อง ณ เวลาที่ login (เผื่อเครื่องถูกเปลี่ยนชื่อ/ลบทีหลัง)</summary>
    public string MachineName { get; set; } = string.Empty;

    /// <summary>ห้องแล็บ ณ เวลาที่ login (snapshot - เครื่องอาจถูกย้ายห้องทีหลัง ประวัติเก่าต้องไม่เปลี่ยนตาม)
    /// null = เครื่องยังไม่จัดห้องตอนนั้น</summary>
    public int? RoomId { get; set; }

    public DateTime StartedAt { get; set; } = DateTime.UtcNow;

    /// <summary>null = ยังใช้งานอยู่ (session เปิดอยู่)</summary>
    public DateTime? EndedAt { get; set; }

    /// <summary>เหตุผลที่ session จบ - มีค่าเฉพาะตอน EndedAt ไม่เป็น null</summary>
    public StudentSessionEndReason? EndReason { get; set; }
}

/// <summary>
/// เหตุผลที่ StudentSession จบ (ดู docs/roadmap.md หัวข้อ "Sign-in และบัญชี" - รายการ StudentSession)
/// ห้ามเปลี่ยนลำดับ/ค่าตัวเลขของสมาชิกที่มีอยู่แล้ว (เก็บเป็น int ใน SQLite - ต่อท้ายเท่านั้น เหมือน AdminRole)
/// </summary>
public enum StudentSessionEndReason
{
    /// <summary>นักเรียนกดออกจากระบบเอง</summary>
    ManualLogout,

    /// <summary>ออกอัตโนมัติเพราะไม่ได้ใช้งานเกินเวลาที่ตั้งไว้ (SignInSettings.AutoLogoutIdleMinutes)</summary>
    Idle,

    /// <summary>เครื่องถูกปิด/รีสตาร์ท</summary>
    Shutdown,

    /// <summary>ครู/Admin สั่ง logout จาก Console</summary>
    TeacherForced,

    /// <summary>นักเรียนกดปุ่ม "ไม่ใช่ฉัน" ในเมนู tray (สลับผู้ใช้)</summary>
    NotMe,

    /// <summary>ถูกเตะออกเพราะบัญชีเดียวกัน login ที่เครื่องอื่น (ตามกติกา "บัญชีเดียวหลายเครื่อง")</summary>
    KickedByOtherLogin
}
