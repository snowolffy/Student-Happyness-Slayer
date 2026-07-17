namespace OnionProcOparetor.Server.Models;

public class GlobalState
{
    public int Id { get; set; }

    public bool IsEnabled { get; set; } = true;

    public string? Note { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
