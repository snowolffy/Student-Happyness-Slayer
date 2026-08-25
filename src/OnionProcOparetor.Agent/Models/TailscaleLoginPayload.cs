namespace OnionProcOparetor.Agent.Models;

/// <summary>
/// Payload ของ command "RunTailscaleLogin" - AuthKey เป็น secret ที่ครูคัดลอกมาจาก Tailscale admin
/// console เอง ห้าม log ค่านี้ที่ไหนทั้งสิ้น (ดู CommandProcessor.HandleRunTailscaleLoginAsync)
/// </summary>
public class TailscaleLoginPayload
{
    public string AuthKey { get; set; } = string.Empty;
}
