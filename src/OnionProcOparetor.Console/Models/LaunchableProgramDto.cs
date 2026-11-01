namespace OnionProcOparetor.Console.Models;

/// <summary>โปรแกรมที่ Admin อนุญาตให้เปิดทั้งห้องได้ (WP4-1, 2026-11-01) - ได้จาก GET /api/launchable-programs
/// Port มาจาก OnionProcOparetor.Server/Models/LaunchableProgram.cs</summary>
public class LaunchableProgramDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string? Arguments { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
