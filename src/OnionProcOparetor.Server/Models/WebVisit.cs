namespace OnionProcOparetor.Server.Models;

/// <summary>
/// ประวัติการเข้าเว็บหนึ่งรายการของเครื่องหนึ่ง (WP2-4, 2026-10-31) - Agent อ่านจากไฟล์ History
/// (SQLite) ของ Chrome/Edge ของ user ที่ login คอมอยู่ แล้วส่งเฉพาะ URL ใหม่ตั้งแต่ครั้งก่อนขึ้นมาเป็นชุด
/// ทุก 1 นาที (ดู Agent/Services/WebHistoryCollectionService.cs) - ไม่ใช่ใบเช็คชื่อทางการ ใช้ดูประกอบ
/// เท่านั้น (ดู docs/roadmap.md หัวข้อ "ไม่มีตาราง/คาบเรียน")
///
/// StudentId/StudentFullName/StudentClassName เป็น snapshot ตอนบันทึก (เดินตาม pattern เดียวกับ
/// StudentSession.MachineName/RoomId - ดู Models/StudentSession.cs) หา session ที่เปิดอยู่ ณ ตอนที่
/// batch นี้มาถึง Server ครั้งเดียวแล้วใส่ให้ทุกแถวของ batch (ดู Endpoints/WebVisitEndpoints.cs) - แม่นพอ
/// สำหรับ batch ที่ส่งถี่ทุก 1 นาที WP3-1 (sign-in) มีอยู่แล้วตอนเขียน WP นี้ จึงเติมคอลัมน์นักเรียนได้ทันที
/// (ต่างจากที่ตั้งใจไว้ตอน WP2-1 ว่า "คอลัมน์นักเรียนยังว่างจนกว่าขั้น 3 จะทำ") null ทั้งสามคอลัมน์ = ไม่มี
/// ใคร login อยู่บนเครื่องนั้นตอนที่ batch มาถึง (ปิดบังคับ sign-in หรือยังไม่ได้ลง WP3-3 บนเครื่องนั้น)
/// </summary>
public class WebVisit
{
    public int Id { get; set; }

    /// <summary>denormalize ตรงๆ เหมือน LogEntry/StudentSession - เครื่องอาจถูกลบออกจากระบบไปแล้วแต่ประวัติ
    /// ต้องยังอยู่ครบ</summary>
    public string ClientGuid { get; set; } = string.Empty;

    public string MachineName { get; set; } = string.Empty;

    public DateTime VisitedAt { get; set; }

    /// <summary>host ของ URL เท่านั้น (เช่น "docs.google.com") ไม่ใช่ URL เต็ม - Agent ตัดมาให้แล้วก่อนส่ง</summary>
    public string Domain { get; set; } = string.Empty;

    public string? PageTitle { get; set; }

    /// <summary>
    /// true = ตอนที่ Agent ส่งขึ้นมา กำลังบล็อกโดเมนนี้อยู่ (เทียบกับ WebsiteRule/โหมดสอบที่ใช้งานอยู่
    /// ณ เวลาที่ส่ง - ไม่ใช่สถานะย้อนหลังจริง ณ เวลาที่ visit เกิดขึ้น เพราะ Agent ไม่ได้เก็บ history ของกฎ
    /// เอง เป็น best-effort ตามที่คุยกับ user)
    /// </summary>
    public bool WasBlocked { get; set; }

    public int? StudentId { get; set; }
    public string? StudentFullName { get; set; }
    public string? StudentClassName { get; set; }

    /// <summary>เวลาที่ Server รับ batch นี้เข้ามา (ต่างจาก VisitedAt ซึ่งเป็นเวลาที่ Agent อ่านมาจาก
    /// History - ใช้เป็น cutoff ของ retention cleanup ดู Services/MainDbRetentionService.cs)</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
