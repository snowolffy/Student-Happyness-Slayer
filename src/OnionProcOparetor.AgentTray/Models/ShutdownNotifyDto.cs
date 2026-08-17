namespace OnionProcOparetor.AgentTray.Models;

/// <summary>
/// ข้อมูลที่ BroadcastPipeListener แปลงจาก envelope "ShowShutdownNotify" แล้วส่งต่อให้ App.xaml.cs
/// ไปเปิด ShutdownNotifyWindow (pattern เดียวกับ BroadcastMessageDto ที่มีอยู่เดิม)
/// </summary>
public class ShutdownNotifyDto
{
    /// <summary>true = Restart, false = Shutdown</summary>
    public bool IsRestart { get; set; }

    /// <summary>วินาทีที่เหลือก่อนเครื่องดับจริง - clamp มาแล้วทั้งฝั่ง Console และฝั่ง Agent</summary>
    public int DelaySeconds { get; set; }

    /// <summary>คำที่เอาไปแทนใน "เครื่องนี้จะ {0} ในอีก N วินาที โดยผู้ดูแลระบบ"</summary>
    public string ActionLabel => IsRestart ? "รีสตาร์ท" : "ปิดเครื่อง";
}
