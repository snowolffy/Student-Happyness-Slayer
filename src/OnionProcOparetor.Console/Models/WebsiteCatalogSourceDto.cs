namespace OnionProcOparetor.Console.Models;

/// <summary>สถานะการ sync ล่าสุด - ต้องตรงกับ enum WebsiteCatalogSyncStatus ฝั่ง Server เป๊ะ</summary>
public enum WebsiteCatalogSyncStatusDto
{
    NeverRun = 0,
    Success = 1,
    Failed = 2,
    SkippedTooLarge = 3
}

/// <summary>แหล่งข้อมูล catalog ภายนอกที่ sync เข้า WebsiteRule เป็นระยะ (2026-09-12)</summary>
public class WebsiteCatalogSourceDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string SourceUrl { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
    public int MaxEntries { get; set; } = 1000;
    public DateTime? LastSyncedAt { get; set; }
    public WebsiteCatalogSyncStatusDto LastSyncStatus { get; set; }
    public int? LastSyncEntryCount { get; set; }
    public string? LastSyncError { get; set; }

    public string StatusText => LastSyncStatus switch
    {
        WebsiteCatalogSyncStatusDto.NeverRun => "ยังไม่เคย sync",
        WebsiteCatalogSyncStatusDto.Success => $"สำเร็จ ({LastSyncEntryCount} โดเมน)",
        WebsiteCatalogSyncStatusDto.Failed => $"ล้มเหลว: {LastSyncError}",
        WebsiteCatalogSyncStatusDto.SkippedTooLarge => $"เกินเพดาน: {LastSyncError}",
        _ => "ไม่ทราบสถานะ"
    };
}

/// <summary>ผลลัพธ์จาก POST /api/website-rules/bulk</summary>
public class BulkAddResultDto
{
    public int Added { get; set; }
    public int Skipped { get; set; }
}

/// <summary>ผลลัพธ์แบบแบ่งหน้าจาก GET /api/website-rules</summary>
public class WebsiteRulesPageDto
{
    public List<WebsiteRuleDto> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}
