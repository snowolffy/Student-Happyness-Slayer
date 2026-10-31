namespace OnionProcOparetor.OnlineConsole.Models;

/// <summary>Ported from OnionProcOparetor.Console/Models/WebsiteRuleDto.cs (identical - GET/POST /api/website-rules)</summary>
public class WebsiteRuleDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string UrlPattern { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;

    /// <summary>null = พิมพ์เอง/วางเอง, ไม่ null = มาจาก WebsiteCatalogSource ตัวนี้ (2026-09-12)</summary>
    public int? SourceCatalogId { get; set; }

    /// <summary>โชว์ในคอลัมน์ "ที่มา" - WebsiteRules.razor เป็นคนเซ็ตชื่อจริงให้หลังโหลด source list เสร็จ</summary>
    public string SourceText { get; set; } = "พิมพ์เอง";

    /// <summary>ห้องที่กฎนี้ใช้ด้วย (WP2-3, 2026-10-31) - null = ใช้กับทุกห้อง</summary>
    public int? RoomId { get; set; }

    /// <summary>โชว์ในคอลัมน์ "ใช้กับ" - WebsiteRules.razor เป็นคนเซ็ตชื่อจริงให้หลังโหลด room list เสร็จ</summary>
    public string ScopeText { get; set; } = "ทุกห้อง";
}
