namespace OnionProcOparetor.AgentTray.Models;

/// <summary>Message ที่ได้รับจาก OnionProcOparetor.Agent ผ่าน named pipe (command "BroadcastMessage" จาก Console)</summary>
public class BroadcastMessageDto
{
    public string Message { get; set; } = string.Empty;
    public string? Title { get; set; }
}
