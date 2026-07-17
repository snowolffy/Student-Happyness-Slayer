namespace OnionProcOparetor.Console.Models;

public class UserDto
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public bool IsBuiltIn { get; set; }
}
