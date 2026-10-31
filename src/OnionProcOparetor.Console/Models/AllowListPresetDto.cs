namespace OnionProcOparetor.Console.Models;

/// <summary>ชุด Allow-list สำเร็จรูป/กำหนดเอง ของ "โหมดสอบ" (WP2-2, 2026-10-31) - ดู Server/Models/AllowListPreset.cs</summary>
public class AllowListPresetDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsBuiltIn { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public List<AllowListPresetDomainDto> Domains { get; set; } = new();

    /// <summary>ไว้แสดงในรายการชุดทางซ้าย (ดู WebsiteAllowListPlaceholderTab) - "7 โดเมน" ตรงกับ wireframe con-website-allow</summary>
    public string DomainCountText => $"{Domains.Count} โดเมน";
}

public class AllowListPresetDomainDto
{
    public int Id { get; set; }
    public string Domain { get; set; } = string.Empty;
}
