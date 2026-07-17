namespace OnionProcOparetor.Server.Models;

public class Rule
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string PublisherName { get; set; } = string.Empty;

    public string? ProcessNameContains { get; set; }

    public bool RequireSignedMatch { get; set; } = true;

    public bool KillProcess { get; set; } = false;

    public string? ActionCommand { get; set; }

    public string? ActionArguments { get; set; }

    public bool IsEnabled { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
