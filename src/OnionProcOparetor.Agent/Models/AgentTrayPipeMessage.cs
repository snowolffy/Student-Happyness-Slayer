namespace OnionProcOparetor.Agent.Models;

/// <summary>
/// Envelope กลางของทุก message ที่ Agent ส่งผ่าน named pipe ไปหา AgentTray (ดู AgentTrayNotifier)
/// Type บอกว่า AgentTray ควรทำอะไร:
/// - "Broadcast" — โชว์ popup ข้อความ (ใช้ Message/Title)
/// - "ShowLockScreen" — แสดงหน้าจอล็อกเต็มจอ (Message/Title ไม่ใช้)
/// - "HideLockScreen" — ปิดหน้าจอล็อก (Message/Title ไม่ใช้)
/// </summary>
public class AgentTrayPipeMessage
{
    public string Type { get; set; } = string.Empty;
    public string? Message { get; set; }
    public string? Title { get; set; }
}
