namespace OnionProcOparetor.OnlineConsole.Models;

/// <summary>Ported from Server/Models/LaunchableProgram.cs (WP4-1, 2026-11-01) - GET /api/launchable-programs</summary>
public class LaunchableProgramDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string? Arguments { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
