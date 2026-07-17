namespace OnionProcOparetor.Console.Models;

public class ClientMachineDto
{
    public int Id { get; set; }
    public string ClientGuid { get; set; } = string.Empty;
    public string MachineName { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
}
