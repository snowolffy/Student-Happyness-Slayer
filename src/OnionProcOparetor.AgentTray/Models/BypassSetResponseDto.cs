namespace OnionProcOparetor.AgentTray.Models;

/// <summary>ผลลัพธ์จาก POST http://localhost:8788/bypass/set ของ Agent Service</summary>
public class BypassSetResponseDto
{
    public bool Bypassed { get; set; }
}
