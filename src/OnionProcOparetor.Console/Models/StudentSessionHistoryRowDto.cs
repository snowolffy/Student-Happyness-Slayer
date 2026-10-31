namespace OnionProcOparetor.Console.Models;

/// <summary>
/// แถวหนึ่งของ "ประวัติการใช้เครื่อง" (WP3-5, 2026-10-31) - ได้จาก GET /api/students/sessions/history
/// ตรงตาม wireframe docs/wireframes/nf-attendance.dc.html ใช้ดูประกอบเท่านั้น ไม่ใช่ใบเช็คชื่อทางการ
/// (ดู docs/roadmap.md หัวข้อ "ไม่มีตาราง/คาบเรียน")
/// </summary>
public class StudentSessionHistoryRowDto
{
    public int Id { get; set; }
    public string ClientGuid { get; set; } = string.Empty;
    public string MachineName { get; set; } = string.Empty;
    public int? RoomId { get; set; }
    public DateTime StartedAt { get; set; }

    /// <summary>null = ยังใช้งานอยู่ (session เปิดอยู่)</summary>
    public DateTime? EndedAt { get; set; }

    /// <summary>ชื่อ enum StudentSessionEndReason ฝั่ง Server (ManualLogout/Idle/Shutdown/TeacherForced/
    /// NotMe/KickedByOtherLogin) เป็น string - null ถ้า session ยังเปิดอยู่</summary>
    public string? EndReason { get; set; }

    public StudentSessionStudentDto? Student { get; set; }

    public bool IsActive => EndedAt is null;

    /// <summary>ข้อความ "ออกโดย" ภาษาไทย ตรงตาม wireframe (คอลัมน์ "ออกโดย")</summary>
    public string EndReasonText => EndReason switch
    {
        "ManualLogout" => "กดออกเอง",
        "Idle" => "ไม่ได้ใช้งาน",
        "Shutdown" => "ปิดเครื่อง",
        "TeacherForced" => "ครูสั่ง logout",
        "NotMe" => "ไม่ใช่ฉัน (คนถัดไป)",
        "KickedByOtherLogin" => "เตะออก (login เครื่องอื่น)",
        _ => "ยังใช้งานอยู่"
    };

    /// <summary>ข้อความ "นาน" - คำนวณฝั่ง client จาก StartedAt/EndedAt (Server ไม่ส่งระยะเวลามาตรงๆ)</summary>
    public string DurationText
    {
        get
        {
            if (!EndedAt.HasValue)
            {
                return "ยังใช้อยู่";
            }

            var span = EndedAt.Value - StartedAt;
            if (span.TotalHours >= 1)
            {
                return $"{(int)span.TotalHours} ชม. {span.Minutes} น.";
            }

            return $"{Math.Max(0, (int)span.TotalMinutes)} น.";
        }
    }
}

/// <summary>ข้อมูลนักเรียนที่ซ้อนอยู่ใน StudentSessionHistoryRowDto.Student</summary>
public class StudentSessionStudentDto
{
    public int Id { get; set; }
    public string StudentCode { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string ClassName { get; set; } = string.Empty;
}

/// <summary>session ที่เปิดอยู่ของเครื่องหนึ่ง - ได้จาก GET /api/students/sessions/active/{clientGuid}
/// (Console inspector ใช้ดึง sessionId มาสั่ง force-logout - GET /api/clients ไม่มี sessionId ติดมาด้วย)</summary>
public class ActiveStudentSessionDto
{
    public bool Active { get; set; }
    public int? SessionId { get; set; }
    public DateTime? StartedAt { get; set; }
    public StudentSessionStudentDto? Student { get; set; }
}
