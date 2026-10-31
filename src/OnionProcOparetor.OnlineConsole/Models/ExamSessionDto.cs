namespace OnionProcOparetor.OnlineConsole.Models;

/// <summary>Ported from OnionProcOparetor.Console/Models/ExamSessionDto.cs - สรุปของ "โหมดสอบ" ครั้งหนึ่ง
/// รูปร่างตรงกับ anonymous object ที่ ExamSessionEndpoints.BuildSummaryAsync คืนกลับ (WP2-2, 2026-10-31)</summary>
public class ExamSessionDto
{
    public int Id { get; set; }
    public int? RoomId { get; set; }
    public string? RoomName { get; set; }
    public List<string> ClientGuids { get; set; } = new();
    public int TargetClientCount { get; set; }
    public List<int> PresetIds { get; set; } = new();
    public List<string> PresetNames { get; set; } = new();
    public List<string> ExtraDomains { get; set; } = new();
    public List<string> AllowedDomains { get; set; } = new();
    public DateTime StartedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public string StartedByUsername { get; set; } = string.Empty;

    /// <summary>ไว้แสดงบนหน้า/ประกาศ - "ห้อง Com1 (40 เครื่อง)" หรือ "เครื่องที่เลือกไว้ (3 เครื่อง)"</summary>
    public string TargetDescription => RoomId is not null
        ? $"ห้อง {RoomName ?? "(ลบไปแล้ว)"} ({TargetClientCount} เครื่อง)"
        : $"เครื่องที่เลือกไว้ ({TargetClientCount} เครื่อง)";
}
