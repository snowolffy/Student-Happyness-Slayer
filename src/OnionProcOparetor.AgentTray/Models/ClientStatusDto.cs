namespace OnionProcOparetor.AgentTray.Models;

/// <summary>ผลลัพธ์จาก GET http://localhost:8788/status ของ Agent Service</summary>
public class ClientStatusDto
{
    public string MachineName { get; set; } = string.Empty;
    public string ClientGuid { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    public int RulesCount { get; set; }
    public DateTime? LastPollAt { get; set; }
    public bool LastPollSucceeded { get; set; }
}
