namespace OnionProcOparetor.Agent.Models;

/// <summary>
/// แถวประวัติเว็บหนึ่งรายการที่ Agent ส่งขึ้น Server เป็นชุดทุก 1 นาที (WP2-4, 2026-10-31) - ดู
/// Services/WebHistoryCollectionService.cs (ฝั่งอ่าน) และ Server/Models/WebVisit.cs (คู่กันฝั่งเก็บ)
/// ไม่มี ClientGuid/MachineName ในนี้ตั้งใจ - Server บังคับใส่เองจาก agent secret header เสมอ
/// (ดู Server/Endpoints/WebVisitEndpoints.cs) เหมือนกับ LogEntryDto ที่ส่งมาแล้วถูกเขียนทับ
/// </summary>
public class WebVisitDto
{
    public DateTime VisitedAt { get; set; }

    /// <summary>host ของ URL เท่านั้น (เช่น "docs.google.com") - ตัดมาให้แล้วก่อนส่ง ไม่ส่ง URL เต็ม</summary>
    public string Domain { get; set; } = string.Empty;

    public string? PageTitle { get; set; }

    public bool WasBlocked { get; set; }
}
