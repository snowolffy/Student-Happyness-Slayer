namespace OnionProcOparetor.Console.Models;

public class LoginResponse
{
    public string? Message { get; set; }
    public string? Username { get; set; }
    public string? Token { get; set; }
    public bool HasDefaultPassword { get; set; }
    public int AdminId { get; set; }
}
