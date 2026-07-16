namespace MyLabGuard.Client.Models;

/// <summary>
/// Log ที่จะส่งกลับไปให้ Server (โครงสร้างต้องตรงกับ LogEntry ฝั่ง Server)
/// </summary>
public class LogEntryDto
{
    public string ClientGuid { get; set; } = string.Empty;
    public string MachineName { get; set; } = string.Empty;
    public string ProcessPath { get; set; } = string.Empty;
    public string? DetectedPublisher { get; set; }
    public bool WasSignedMatch { get; set; }
    public int? MatchedRuleId { get; set; }
    public string? MatchedRuleName { get; set; }
    public string ActionTaken { get; set; } = string.Empty;
}