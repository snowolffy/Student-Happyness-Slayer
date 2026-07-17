namespace OnionProcOparetor.Console.Models;

public class LogEntryDto
{
    public int Id { get; set; }
    public string MachineName { get; set; } = string.Empty;
    public string ProcessPath { get; set; } = string.Empty;
    public string ActionTaken { get; set; } = string.Empty;
}
