namespace OnionProcOparetor.AgentTray.Models;

/// <summary>ผลลัพธ์จาก POST http://localhost:8788/raisehand/send ของ Agent Service (WP4-2, 2026-11-01)</summary>
public class RaiseHandSendResponseDto
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public int? CooldownSecondsRemaining { get; set; }
    public int? QueuePosition { get; set; }
}
