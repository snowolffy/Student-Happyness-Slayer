namespace OnionProcOparetor.Agent.Models;

/// <summary>
/// payload ของ command "OpenUrl" (WP4-1, 2026-11-01) - Console ส่งมาตอนครูเปิดเว็บทั้งห้อง/เครื่องที่
/// เลือก (ดู wireframe tc-openurl.dc.html) Agent ต้องตรวจ Url ซ้ำว่าเป็น http/https เท่านั้นก่อนเปิดเสมอ
/// (ดู CommandProcessor.HandleOpenUrlAsync) - ห้ามเชื่อ validation ฝั่ง Console เพียงอย่างเดียว เพราะ
/// ใครก็ตามที่มี admin token ยิง POST /api/commands ตรงๆ ได้โดยไม่ผ่าน Console เลย
/// </summary>
public class OpenUrlPayload
{
    public string Url { get; set; } = string.Empty;

    /// <summary>"NewTab" (ค่าเริ่มต้น) หรือ "Fullscreen" - ดู OpenUrlMode</summary>
    public string? Mode { get; set; }
}

/// <summary>ค่าที่ยอมรับของ OpenUrlPayload.Mode - เก็บเป็น string ใน payload (ไม่ใช่ enum) เพราะ Console เก่ากว่า/ใหม่กว่า Agent ต้องยังส่ง/รับกันได้โดยไม่ throw ตอน deserialize</summary>
public static class OpenUrlMode
{
    public const string NewTab = "NewTab";
    public const string Fullscreen = "Fullscreen";
}
