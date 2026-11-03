namespace OnionProcOparetor.Agent.Models;

/// <summary>
/// payload ของ command "StartScreenBroadcast" (WP4-4, 2026-11-01) - Console ส่งมาตอนครูกด "เริ่มส่งจอ"
/// (ดู wireframe tc-broadcast.dc.html) ต่อเครื่องเป้าหมายทุกเครื่องที่เลือกไว้ Agent ใช้ค่านี้บอก
/// AgentTray ว่าจะเปิดหน้าต่างดูจอครูแบบไหน (ดู CommandProcessor.HandleStartScreenBroadcastAsync)
/// </summary>
public class StartScreenBroadcastPayload
{
    /// <summary>"FullscreenLocked" (ค่าเริ่มต้น) หรือ "Windowed" - ดู ScreenBroadcastViewerMode</summary>
    public string? ViewerMode { get; set; }

    /// <summary>ชื่อที่แสดงของครูที่กำลังส่งจอ (โชว์ในแถบสถานะล่างจอนักเรียน "กำลังดูจอครู · ครู [ชื่อ]")</summary>
    public string? TeacherDisplayName { get; set; }
}

/// <summary>ค่าที่ยอมรับของ StartScreenBroadcastPayload.ViewerMode - เก็บเป็น string ไม่ใช่ enum เหตุผลเดียวกับ OpenUrlMode (Console/Agent คนละเวอร์ชันต้องยังส่ง/รับกันได้)</summary>
public static class ScreenBroadcastViewerMode
{
    public const string FullscreenLocked = "FullscreenLocked";
    public const string Windowed = "Windowed";
}
