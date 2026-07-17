namespace OnionProcOparetor.Server.Models;

public class LogEntry
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

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
