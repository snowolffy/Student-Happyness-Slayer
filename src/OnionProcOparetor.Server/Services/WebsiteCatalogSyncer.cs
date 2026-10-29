using Microsoft.EntityFrameworkCore;
using OnionProcOparetor.Server.Data;
using OnionProcOparetor.Server.Models;

namespace OnionProcOparetor.Server.Services;

/// <summary>
/// ดึงรายชื่อโดเมนจาก URL ภายนอก (hosts-file format หรือ domain ทีละบรรทัดเปล่าๆ) มา sync เข้า
/// WebsiteRule (2026-09-12) แยกเป็น class เปล่าๆ ไม่ใช่ BackgroundService เอง เพื่อให้ทั้ง timer
/// (WebsiteCatalogSyncService) และปุ่ม "Sync Now" (endpoint) เรียกโค้ดเดียวกันได้
///
/// ข้อจำกัดสำคัญที่ต้องมีเกราะกัน: list สาธารณะบางอันมีโดเมนเป็นแสน (เช่น
/// blocklistproject/Lists/gambling.txt = 342,627 บรรทัด, youtube.txt = 24,295 - เช็คจริงแล้ว
/// 2026-09-12) ซึ่งไม่มีทางใช้กับกลไก registry policy ของเราได้ (Agent เขียน key นี้ทุก 10 วิ)
/// AbsoluteMaxEntries คือเพดานที่ไม่ยอมให้ MaxEntries ของ source ไหนตั้งเกินไปได้ ถ้าเจอ list ที่
/// ใหญ่กว่า cap จะปฏิเสธทั้งชุด ไม่ import บางส่วน (import บางส่วนจะทำให้ไม่รู้ว่าขาดอะไรไปและทำไม)
/// </summary>
public sealed class WebsiteCatalogSyncer
{
    /// <summary>เพดานสูงสุดจริงไม่ว่า source จะตั้ง MaxEntries ไว้เท่าไหร่ก็ตาม</summary>
    public const int AbsoluteMaxEntries = 5000;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<WebsiteCatalogSyncer> _logger;

    public WebsiteCatalogSyncer(IHttpClientFactory httpClientFactory, ILogger<WebsiteCatalogSyncer> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <summary>
    /// Sync source เดียว - เรียกจาก WebsiteCatalogSyncService (timer ทุก 24 ชม.) และ endpoint
    /// "sync-now" (กดทันทีจาก Console) เป็นโค้ดเดียวกัน ไม่ throw ออกไปเลย - ผลลัพธ์เขียนกลับเข้า
    /// field ของ source เอง (LastSyncStatus/LastSyncError/...) ให้ caller เช็คเองว่าสำเร็จไหม
    /// </summary>
    public async Task SyncSourceAsync(AppDbContext db, WebsiteCatalogSource source, CancellationToken ct)
    {
        try
        {
            var client = _httpClientFactory.CreateClient(nameof(WebsiteCatalogSyncer));
            client.Timeout = TimeSpan.FromSeconds(30);

            var body = await client.GetStringAsync(source.SourceUrl, ct);
            var domains = ParseDomains(body);

            var effectiveCap = Math.Min(source.MaxEntries, AbsoluteMaxEntries);
            if (domains.Count > effectiveCap)
            {
                source.LastSyncStatus = WebsiteCatalogSyncStatus.SkippedTooLarge;
                source.LastSyncEntryCount = domains.Count;
                source.LastSyncError =
                    $"พบ {domains.Count} โดเมน เกินเพดาน {effectiveCap} - ไม่ import (เลือก source ที่เจาะจงกว่านี้ หรือปรับ MaxEntries)";
                source.LastSyncedAt = DateTime.UtcNow;

                _logger.LogWarning(
                    "Catalog source '{Name}' ({Url}) มี {Count} โดเมน เกินเพดาน {Cap} - ปฏิเสธทั้งชุด ไม่ import",
                    source.Name, source.SourceUrl, domains.Count, effectiveCap);
                return;
            }

            await ApplyDiffAsync(db, source, domains, ct);

            source.LastSyncStatus = WebsiteCatalogSyncStatus.Success;
            source.LastSyncEntryCount = domains.Count;
            source.LastSyncError = null;
            source.LastSyncedAt = DateTime.UtcNow;

            _logger.LogInformation("Catalog source '{Name}' sync สำเร็จ - {Count} โดเมน", source.Name, domains.Count);
        }
        catch (Exception ex)
        {
            // source เดียวพังต้องไม่ทำให้ source อื่นหรือทั้ง service ล้มไปด้วย (เหตุผลเดียวกับ
            // OnlineGameLookup.LookupAsync - เครือข่ายล่ม/URL เปลี่ยน ไม่ควรทำให้ฟีเจอร์อื่นพังตาม)
            source.LastSyncStatus = WebsiteCatalogSyncStatus.Failed;
            source.LastSyncError = ex.Message;
            source.LastSyncedAt = DateTime.UtcNow;
            _logger.LogWarning(ex, "Sync catalog source '{Name}' ({Url}) ไม่สำเร็จ", source.Name, source.SourceUrl);
        }
    }

    /// <summary>
    /// เพิ่มโดเมนใหม่ / ลบโดเมนที่หลุดจาก list ต้นทางไปแล้ว - แตะเฉพาะแถวที่ SourceCatalogId ตรงกับ
    /// source นี้เท่านั้น ไม่แตะแถว manual (SourceCatalogId เป็น null) หรือของ source อื่น และไม่แตะ
    /// IsEnabled ของแถวที่รอดอยู่ (เคารพการปิดกฎรายตัวด้วยมือของครู เหมือน RuleOverride ชนะ
    /// automation เสมอ)
    /// </summary>
    private static async Task ApplyDiffAsync(AppDbContext db, WebsiteCatalogSource source, List<string> domains, CancellationToken ct)
    {
        var wanted = new HashSet<string>(domains, StringComparer.OrdinalIgnoreCase);

        var existingFromThisSource = await db.WebsiteRules
            .Where(r => r.SourceCatalogId == source.Id)
            .ToListAsync(ct);

        var existingPatterns = new HashSet<string>(
            existingFromThisSource.Select(r => r.UrlPattern), StringComparer.OrdinalIgnoreCase);

        foreach (var rule in existingFromThisSource)
        {
            if (!wanted.Contains(rule.UrlPattern))
            {
                db.WebsiteRules.Remove(rule);
            }
        }

        // เช็คซ้ำกับ WebsiteRule "ทุกแถว" ในระบบ ไม่ใช่แค่ของ source นี้ - กันชนกับกฎ manual หรือ
        // source อื่นที่บังเอิญมีโดเมนเดียวกัน (unique index บน UrlPattern จะ throw ถ้าไม่กันตรงนี้)
        var takenElsewhere = new HashSet<string>(
            await db.WebsiteRules
                .Where(r => r.SourceCatalogId != source.Id)
                .Select(r => r.UrlPattern)
                .ToListAsync(ct),
            StringComparer.OrdinalIgnoreCase);

        var now = DateTime.UtcNow;
        foreach (var domain in domains)
        {
            if (existingPatterns.Contains(domain) || takenElsewhere.Contains(domain))
            {
                continue;
            }

            db.WebsiteRules.Add(new WebsiteRule
            {
                Name = domain,
                UrlPattern = domain,
                IsEnabled = true,
                SourceCatalogId = source.Id,
                CreatedAt = now,
                UpdatedAt = now
            });
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// แปลง hosts-file format ("0.0.0.0 domain" / "127.0.0.1 domain") หรือ domain ทีละบรรทัดเปล่าๆ
    /// ให้เหลือแต่ชื่อโดเมน - ข้ามคอมเมนต์ (#) และบรรทัดว่าง ตัดซ้ำภายในไฟล์เดียวกันทิ้งด้วย
    /// </summary>
    private static List<string> ParseDomains(string body)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rawLine in body.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                continue;
            }

            var domain = (parts[0] == "0.0.0.0" || parts[0] == "127.0.0.1") && parts.Length > 1
                ? parts[1]
                : parts[0];

            domain = domain.Trim();
            if (domain.Length == 0)
            {
                continue;
            }

            if (seen.Add(domain))
            {
                result.Add(domain);
            }
        }

        return result;
    }
}
