namespace OnionProcOparetor.Server.Models;

/// <summary>
/// กฎบล็อกเว็บไซต์ - แต่ละแถวคือ URL pattern หนึ่งรายการที่ Agent จะเขียนเข้า Chrome/Edge
/// Enterprise Policy (URLBlocklist ผ่าน Registry) ดู WebsiteControlService ฝั่ง Agent
///
/// ไม่มี publisher/signature/action-command เหมือน Rule (process rules) เพราะ action มีแบบเดียว
/// คือ "เพิ่มเข้า URLBlocklist" ไม่มีทางเลือกอื่น ยังไม่มี per-machine override (WebsiteRuleOverride)
/// ในรอบแรก - เพิ่มทีหลังได้ถ้าจำเป็นจริง (ตัดสินใจร่วมกับ user 2026-09-11)
/// </summary>
public class WebsiteRule
{
    public int Id { get; set; }

    /// <summary>ชื่อกฎ (ไว้แสดงผลใน GUI) เช่น "Roblox"</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// URL pattern ตาม syntax ของ Chrome/Edge URLBlocklist policy เช่น "roblox.com",
    /// "*.roblox.com", "*://*.roblox.com/*" - ดูเอกสาร Chrome Enterprise policy list "URLBlocklist"
    /// </summary>
    public string UrlPattern { get; set; } = string.Empty;

    /// <summary>กฎนี้เปิดใช้งานอยู่ไหม</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// null = พิมพ์เอง/วางเอง (manual หรือ bulk-add) - ไม่ null = มาจาก WebsiteCatalogSource ตัวนี้
    /// WebsiteCatalogSyncer จะ add/remove เฉพาะแถวที่ผูกกับ source ของตัวเองเท่านั้น ไม่แตะแถวที่เป็น
    /// null เด็ดขาด (ดู WebsiteCatalogSyncer.cs) และไม่แตะ IsEnabled ของแถวที่ยัง sync อยู่ต่อด้วย
    /// (เคารพการปิดกฎรายตัวด้วยมือของครู เหมือน RuleOverride ชนะ automation เสมอ)
    /// </summary>
    public int? SourceCatalogId { get; set; }

    /// <summary>
    /// ห้องที่กฎนี้ใช้ด้วย (WP2-3, 2026-10-31) - null = ใช้กับทุกห้อง เหตุผลและ pattern เดียวกับ
    /// Rule.RoomId (ดูที่นั่น) ดู Endpoints/PollEndpoints.cs สำหรับจุดที่กรองกฎตามห้องของเครื่อง
    /// </summary>
    public int? RoomId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
