namespace OnionProcOparetor.Console.Models;

public class RuleDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string PublisherName { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
}
