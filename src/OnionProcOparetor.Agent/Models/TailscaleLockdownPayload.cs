namespace OnionProcOparetor.Agent.Models;

/// <summary>Payload ของ command "SetTailscaleLockdown" - true = ล็อก (AlwaysOn), false = ปลดล็อกชั่วคราว</summary>
public class TailscaleLockdownPayload
{
    public bool Locked { get; set; }
}
