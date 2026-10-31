namespace OnionProcOparetor.OnlineConsole.Models;

/// <summary>Ported from OnionProcOparetor.Console/Models/StudentSessionHistoryRowDto.cs (identical -
/// GET /api/students/sessions/history). WP3-5, 2026-10-31.</summary>
public class StudentSessionHistoryRowDto
{
    public int Id { get; set; }
    public string ClientGuid { get; set; } = string.Empty;
    public string MachineName { get; set; } = string.Empty;
    public int? RoomId { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public string? EndReason { get; set; }
    public StudentSessionStudentDto? Student { get; set; }

    public bool IsActive => EndedAt is null;

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

    public string DurationText
    {
        get
        {
            if (!EndedAt.HasValue)
            {
                return "ยังใช้อยู่";
            }

            var span = EndedAt.Value - StartedAt;
            return span.TotalHours >= 1
                ? $"{(int)span.TotalHours} ชม. {span.Minutes} น."
                : $"{Math.Max(0, (int)span.TotalMinutes)} น.";
        }
    }
}

public class StudentSessionStudentDto
{
    public int Id { get; set; }
    public string StudentCode { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string ClassName { get; set; } = string.Empty;
}
