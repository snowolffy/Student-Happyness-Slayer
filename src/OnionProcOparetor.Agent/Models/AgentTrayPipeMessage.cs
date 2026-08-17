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
/// - "StopVncSession" — สั่งให้ AgentTray หยุด winvnc.exe (Message/Title/RequestingConsoleSessionId
///   ไม่ใช้) - Agent ส่งเองอัตโนมัติตอน VncSessionManager.MonitorSessionAsync เห็นว่าไม่มี Console
///   ต่อเข้ามาเลย/reconnect ไม่ทันเวลา (lazy-start ตั้งแต่ Step 4a ต้องไม่ถือ resource ค้าง)
/// - "BlockTargetInput" — สั่งให้ AgentTray บล็อก physical keyboard/mouse ของนักเรียนชั่วคราว (Message/
///   Title/RequestingConsoleSessionId ไม่ใช้) ระหว่างครูควบคุมเครื่องผ่าน full-screen VNC (ดู
///   PhysicalInputBlocker ฝั่ง AgentTray) - ต้องทำจาก AgentTray เพราะเหตุผลเดียวกับ StartVncSession
///   (Session 0 isolation กันไม่ให้ Agent ติดตั้ง low-level hook ที่เห็น input ของ user session ได้)
/// - "UnblockTargetInput" — สั่งให้ AgentTray คืนสิทธิ์ physical input ให้นักเรียน (Message/Title/
///   RequestingConsoleSessionId ไม่ใช้)
/// - "ShowShutdownNotify" — โชว์หน้าต่างนับถอยหลังมุมจอก่อน shutdown/restart
///   (ใช้ Action = "Shutdown"|"Restart" และ DelaySeconds)
///
/// สำคัญ: ไฟล์นี้ถูก "ก๊อปมือ" ไว้ 2 ที่ (Agent/Models + AgentTray/Models) เพราะ solution นี้
/// ไม่มี shared class library ระหว่างโปรเจกต์เลย (ดู LogEntryDto ที่ก๊อปแบบเดียวกัน) -
/// แก้ field ตรงนี้เมื่อไหร่ต้องแก้อีกฝั่งให้ตรงกันเสมอ ไม่งั้น JSON deserialize แล้ว field หาย
/// </summary>
public class AgentTrayPipeMessage
{
    public string Type { get; set; } = string.Empty;
    public string? Message { get; set; }
    public string? Title { get; set; }
    public string? RequestingConsoleSessionId { get; set; }

    /// <summary>ใช้เฉพาะ Type = "ShowShutdownNotify" - "Shutdown" หรือ "Restart"</summary>
    public string? Action { get; set; }

    /// <summary>ใช้เฉพาะ Type = "ShowShutdownNotify" - จำนวนวินาทีที่จะนับถอยหลังก่อนเครื่องดับจริง</summary>
    public int? DelaySeconds { get; set; }
}
