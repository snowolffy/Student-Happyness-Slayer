namespace MyLabGuard.Console.Models;

/// <summary>กฎที่ได้จาก GET /api/rules หรือส่งไปตอน POST /api/rules</summary>
public class RuleDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string PublisherName { get; set; } = string.Empty;
    public bool RequireSignedMatch { get; set; } = true;
    public string? ActionCommand { get; set; }
    public string? ActionArguments { get; set; }
    public bool IsEnabled { get; set; } = true;
}