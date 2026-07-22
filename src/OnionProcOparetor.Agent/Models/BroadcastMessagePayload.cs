namespace OnionProcOparetor.Agent.Models;

/// <summary>Payload ของ command "BroadcastMessage" - { "message": string, "title": string? } จาก Console</summary>
public class BroadcastMessagePayload
{
    public string Message { get; set; } = string.Empty;
    public string? Title { get; set; }
}
