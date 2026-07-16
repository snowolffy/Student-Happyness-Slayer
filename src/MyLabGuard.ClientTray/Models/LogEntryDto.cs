namespace MyLabGuard.ClientTray.Models;

/// <summary>log entry จาก GET http://localhost:8788/logs/recent</summary>
public class LogEntryDto
{
    public string ProcessPath { get; set; } = string.Empty;
    public string? DetectedPublisher { get; set; }
    public bool WasSignedMatch { get; set; }
    public string? MatchedRuleName { get; set; }
    public string ActionTaken { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
}