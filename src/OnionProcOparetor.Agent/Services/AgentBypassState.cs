namespace OnionProcOparetor.Agent.Services;

/// <summary>
/// สถานะ "Bypass" ปัจจุบัน - เก็บใน memory ของ Agent process เท่านั้น (ไม่เขียนลง DB/config/registry
/// ใดๆ) ตั้งใจให้ default เป็น false (Connected) เสมอทุกครั้งที่ Agent service เริ่มใหม่ (boot/restart/
/// reinstall) เพราะเป็นแค่ singleton ธรรมดาที่ถูกสร้างใหม่ทุกครั้ง ไม่ persist ข้าม process
///
/// เก็บที่ฝั่ง Agent (ไม่ใช่ AgentTray) โดยตั้งใจ - AgentTray ปิด/เปิดใหม่ได้อิสระจาก Agent service
/// (เช่น user logout/login ใหม่) ถ้าเก็บ state ไว้ที่ AgentTray แทน จะทำให้ bypass หายไปเองตอน AgentTray
/// restart ซึ่งผิดจากที่ต้องการ (ต้องอยู่จนกว่าจะมีคน toggle กลับหรือ reboot เครื่องเท่านั้น)
/// </summary>
public class AgentBypassState
{
    private readonly object _lock = new();
    private bool _isBypassed;

    public bool IsBypassed
    {
        get { lock (_lock) { return _isBypassed; } }
    }

    public void Set(bool bypassed)
    {
        lock (_lock)
        {
            _isBypassed = bypassed;
        }
    }
}
