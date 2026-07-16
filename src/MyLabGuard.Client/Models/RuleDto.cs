namespace MyLabGuard.Client.Models;

/// <summary>
/// กฎที่รับมาจาก Server (โครงสร้างต้องตรงกับ Rule ฝั่ง Server)
/// </summary>
public class RuleDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string PublisherName { get; set; } = string.Empty;
    public bool RequireSignedMatch { get; set; }
    public bool KillProcess { get; set; }
    public string? ActionCommand { get; set; }
    public string? ActionArguments { get; set; }
    public bool IsEnabled { get; set; }
}