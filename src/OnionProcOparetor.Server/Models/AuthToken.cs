namespace OnionProcOparetor.Server.Models;

public class AuthToken
{
    public int Id { get; set; }

    public string Token { get; set; } = string.Empty;

    public int AdminUserId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime ExpiresAt { get; set; }
}
