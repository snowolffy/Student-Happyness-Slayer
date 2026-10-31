namespace OnionProcOparetor.Console.Models;

/// <summary>กฎบล็อกเว็บที่ได้จาก GET /api/website-rules หรือส่งไปตอน POST /api/website-rules</summary>
public class WebsiteRuleDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string UrlPattern { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;

    /// <summary>null = พิมพ์เอง/วางเอง, ไม่ null = มาจาก WebsiteCatalogSource ตัวนี้ (2026-09-12)</summary>
    public int? SourceCatalogId { get; set; }

    /// <summary>ห้องที่กฎนี้ใช้ด้วย (WP2-3, 2026-10-31) - null = ใช้กับทุกห้อง</summary>
    public int? RoomId { get; set; }

    /// <summary>ข้อความบนปุ่มสลับสถานะในตาราง - เหตุผลเดียวกับ RuleDto.ToggleActionText</summary>
    public string ToggleActionText => IsEnabled ? "ปิดใช้" : "เปิดใช้";

    /// <summary>
    /// โชว์ในคอลัมน์ "ที่มา" ของตาราง - endpoint ไม่ join ชื่อ source มาให้ตรงๆ (แค่ id)
    /// WebsiteControlPage.xaml.cs เป็นคนเซ็ตชื่อจริงให้หลังโหลด source list เสร็จ ค่า default
    /// ตรงนี้ใช้เฉพาะตอนที่ยังจับคู่ไม่ได้ (source โดนลบไปแล้ว หรือยังโหลดไม่เสร็จ)
    /// </summary>
    public string SourceText { get; set; } = "พิมพ์เอง";

    /// <summary>
    /// โชว์ในคอลัมน์ "ใช้กับ" ของตาราง (WP2-3) - เหตุผลและ pattern เดียวกับ RuleDto.ScopeText/SourceText
    /// ด้านบน WebsiteControlPage.xaml.cs เป็นคนเซ็ตชื่อจริงให้หลังโหลด room list เสร็จ
    /// </summary>
    public string ScopeText { get; set; } = "ทุกห้อง";
}
