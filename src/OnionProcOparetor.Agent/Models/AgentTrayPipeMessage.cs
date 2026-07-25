namespace OnionProcOparetor.Agent.Models;

/// <summary>
/// Envelope กลางของทุก message ที่ Agent ส่งผ่าน named pipe ไปหา AgentTray (ดู AgentTrayNotifier)
/// Type บอกว่า AgentTray ควรทำอะไร:
/// - "Broadcast" — โชว์ popup ข้อความ (ใช้ Message/Title)
/// - "ShowLockScreen" — แสดงหน้าจอล็อกเต็มจอ (Message/Title ไม่ใช้)
/// - "HideLockScreen" — ปิดหน้าจอล็อก (Message/Title ไม่ใช้)
/// - "StartVncSession" — สั่งให้ AgentTray start winvnc.exe เอง (ใช้ RequestingConsoleSessionId) - ต้อง
///   ทำจาก AgentTray (รันใน user session จริง) ไม่ใช่ Agent (Session 0) เพราะ Session 0 isolation ทำให้
///   process ใน Session 0 capture หน้าจอของ user session ไม่ได้เลย (verify แล้วจริงจากการ debug -
///   winvnc.exe ที่ Agent spawn เอง connect+auth ผ่านหมดแต่ตอบ ServerInit ไม่ได้เพราะไม่เห็น desktop)
/// </summary>
public class AgentTrayPipeMessage
{
    public string Type { get; set; } = string.Empty;
    public string? Message { get; set; }
    public string? Title { get; set; }
    public string? RequestingConsoleSessionId { get; set; }
}
