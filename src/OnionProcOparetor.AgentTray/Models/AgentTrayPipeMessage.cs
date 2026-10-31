namespace OnionProcOparetor.AgentTray.Models;

/// <summary>
/// Envelope กลางของทุก message ที่ได้รับจาก OnionProcOparetor.Agent ผ่าน named pipe
/// (ต้องตรงกับ AgentTrayPipeMessage ฝั่ง Agent เป๊ะๆ) Type บอกว่าควรทำอะไร:
/// - "Broadcast" — โชว์ popup ข้อความ (ใช้ Message/Title)
/// - "ShowLockScreen" — แสดงหน้าจอล็อกเต็มจอ (Message/Title ไม่ใช้)
/// - "HideLockScreen" — ปิดหน้าจอล็อก (Message/Title ไม่ใช้)
/// - "StartVncSession" — start winvnc.exe ในนี้เอง (ใช้ RequestingConsoleSessionId) - ต้องทำจาก
///   AgentTray เพราะรันใน user session จริง ต่างจาก Agent (Session 0 - capture หน้าจอไม่ได้เลย)
/// - "StopVncSession" — หยุด winvnc.exe (Message/Title/RequestingConsoleSessionId ไม่ใช้) - Agent
///   ส่งเองอัตโนมัติตอนไม่มี Console ต่อเข้ามาเลย/reconnect ไม่ทันเวลา (lazy-start)
/// - "ShowShutdownNotify" — โชว์หน้าต่างนับถอยหลังมุมจอก่อน shutdown/restart (ใช้ Action, DelaySeconds)
/// - "GetRunningProcesses" — สแกนโปรแกรมที่มีหน้าต่างอยู่บน desktop จริง แล้วส่งผลกลับผ่าน local HTTP
///   callback (ใช้ RequestingConsoleSessionId) - ต้องทำจาก AgentTray เพราะรันใน user session จริง
/// - "ExamModeStatus" (WP2-2, 2026-10-31) — ขึ้น/ลบป้ายมุมขวาล่าง "โหมดสอบ · เหลือ N นาที" (ใช้
///   ExamModeExpiresAtUtc: มีค่า = แสดงป้ายแล้วนับถอยหลังเอง, null = ซ่อนป้าย) ดู ExamModeBadgeWindow
/// - "ShowLockScreen" (แก้ WP3-3, 2026-10-31) — ใช้ Message ด้วยแล้ว: ข้อความที่ Agent ตัดสินแล้ว
///   (payload จาก Console หรือ fallback จาก SignInSettings.LockScreenDefaultMessage) null/ว่าง = ใช้
///   LockScreenWindow.LockMessage (const เดิม) เป็น fail-safe สุดท้าย
/// - "SignInStatus" (WP3-3, 2026-10-31) — ขึ้น/ลบจอล็อกอินนักเรียน (StudentLoginWindow) (ใช้
///   RequireLogin + Message = ข้อความบนจอล็อกอิน) ดู App.xaml.cs OnSignInStatusChanged
///
/// ต้อง "ตรงกัน" กับ OnionProcOparetor.Agent/Models/AgentTrayPipeMessage.cs เป๊ะๆ เสมอ เพราะ
/// solution นี้ไม่มี shared class library ระหว่างโปรเจกต์ (ก๊อปมือแบบเดียวกับ LogEntryDto) -
/// เพิ่ม/แก้ field ที่นี่แล้วต้องไปแก้อีกฝั่งด้วยทุกครั้ง
///
/// WP2b (2026-10-30): เพิ่ม Secret - ค่าปัจจุบันของ LoopbackSecret ฝั่ง Agent แนบมาทุก message
/// (ดู AgentTrayNotifier.SendAsync ฝั่ง Agent) BroadcastPipeListener เก็บค่านี้ไว้ใน
/// LoopbackSecretStore ทุกครั้งที่รับ message แล้ว ClientApiClient แนบกลับไปกับ
/// /vnc/session-result และ /processes/list-result เพื่อให้ Agent ตรวจได้ว่าเป็น AgentTray จริง
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

    /// <summary>WP2b: secret ปัจจุบันของ Agent process (ดู LoopbackSecret ฝั่ง Agent) แนบมาทุก message - ไม่ใช้ต่อ Type ใดโดยเฉพาะ</summary>
    public string? Secret { get; set; }

    /// <summary>ใช้เฉพาะ Type = "ExamModeStatus" - null = ไม่มีโหมดสอบทำงานอยู่ (WP2-2, 2026-10-31)</summary>
    public DateTime? ExamModeExpiresAtUtc { get; set; }

    /// <summary>ใช้เฉพาะ Type = "SignInStatus" (WP3-3, 2026-10-31) - true = ต้อง login ก่อนใช้เครื่อง</summary>
    public bool? RequireLogin { get; set; }
}
