namespace OnionProcOparetor.OnlineConsole.Models;

/// <summary>Ported from OnionProcOparetor.Console/Models/RuleOverrideDto.cs (identical)</summary>
public class RuleOverrideDto
{
    public int Id { get; set; }
    public int RuleId { get; set; }
    public string ClientGuid { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
