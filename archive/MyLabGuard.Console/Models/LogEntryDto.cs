namespace MyLabGuard.Console.Models;

/// <summary>log ที่ได้จาก GET /api/logs</summary>
public class LogEntryDto
{
    public int Id { get; set; }
    public string ClientGuid { get; set; } = string.Empty;
    public string MachineName { get; set; } = string.Empty;
    public string ProcessPath { get; set; } = string.Empty;
    public string? DetectedPublisher { get; set; }
    public bool WasSignedMatch { get; set; }
    public int? MatchedRuleId { get; set; }
    public string? MatchedRuleName { get; set; }
    public string ActionTaken { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
}