namespace OnionProcOparetor.Server.Models;

public class ClientMachine
{
    public int Id { get; set; }

    public string ClientGuid { get; set; } = string.Empty;

    public string MachineName { get; set; } = string.Empty;

    public string? LastKnownIp { get; set; }

    public bool IsEnabled { get; set; } = true;

    public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;

    public DateTime RegisteredAt { get; set; } = DateTime.UtcNow;
}
