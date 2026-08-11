namespace OnionProcOparetor.Console.Models;

/// <summary>Override ค่า Rule.IsEnabled เฉพาะเครื่องเดียว - ได้จาก GET /api/clients/{clientGuid}/rule-overrides</summary>
public class RuleOverrideDto
{
    public int Id { get; set; }
    public int RuleId { get; set; }
    public string ClientGuid { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
