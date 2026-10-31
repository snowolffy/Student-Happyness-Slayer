using OnionProcOparetor.Agent.Models;

namespace OnionProcOparetor.Agent.Services;

/// <summary>
/// ประวัติเว็บ (WP2-4, 2026-10-31) - สำเนาไฟล์ History (SQLite) ของ Chrome/Edge ของ user ที่ login
/// คอมอยู่ แล้วอ่านเฉพาะ URL ใหม่ตั้งแต่ครั้งก่อนด้วย ChromeHistoryReader ส่งขึ้น Server เป็นชุดทุก 1 นาที
/// (เรียกจาก Worker.RunPeriodicWebHistoryCollectionAsync)
///
/// ต้องสำเนาไฟล์ก่อนอ่านเสมอ ไม่อ่านไฟล์จริงของ Chrome/Edge ตรงๆ (browser ที่เปิดอยู่ถือ lock ของตัวเองไว้)
/// Chrome ใช้ WAL mode ตามปกติ จึงสำเนา -wal/-journal sidecar ไปด้วยถ้ามี ไม่งั้นจะอ่านไม่เห็น visit ที่
/// ยังไม่ checkpoint ลง main file (เป็นกรณีปกติ ไม่ใช่ edge case - Chrome checkpoint WAL เป็นระยะเท่านั้น)
///
/// cursor (ตำแหน่งล่าสุดที่อ่านไปแล้วของแต่ละ browser) เก็บใน memory เท่านั้น ไม่เขียนลงดิสก์เลย - ตรงกับ
/// docs/roadmap.md "disk freeze": เครื่องแล็บรีบูตแล้ว Chrome History ของ user ก็หายไปด้วยเหมือนกัน (ไม่
/// รอดจาก Reboot Restore Rx เหมือนไฟล์อื่นที่ Agent เขียนตอน runtime) จึงไม่มีประโยชน์ที่จะ persist cursor
/// ข้ามการรีบูต - เริ่มนับจาก 0 ใหม่ทุกครั้งที่ Agent เริ่มทำงานถูกต้องแล้ว ส่วนกรณี Agent restart โดยไม่
/// รีบูตเครื่อง (เช่นอัปเดต) ประวัติเก่าที่เคยส่งไปแล้วอาจถูกส่งซ้ำได้ (ไม่มี unique constraint กันซ้ำฝั่ง
/// Server) ยอมรับได้เพราะไม่ใช่ใบเช็คชื่อทางการ (ดู Server/Models/WebVisit.cs)
/// </summary>
public sealed class WebHistoryCollectionService
{
    private sealed record BrowserProfile(string Name, string RelativeHistoryPath);

    private static readonly BrowserProfile[] Browsers =
    {
        new("Chrome", @"AppData\Local\Google\Chrome\User Data\Default\History"),
        new("Edge", @"AppData\Local\Microsoft\Edge\User Data\Default\History")
    };

    private readonly ILogger<WebHistoryCollectionService> _logger;
    private readonly UserSessionLauncher _userSessionLauncher;

    // cursor ล่าสุดต่อ browser (Chrome timestamp ดิบ - ไมโครวินาทีนับจาก 1601-01-01) key = Browsers[].Name
    private readonly Dictionary<string, long> _lastChromeTimestampByBrowser = new();

    public WebHistoryCollectionService(ILogger<WebHistoryCollectionService> logger, UserSessionLauncher userSessionLauncher)
    {
        _logger = logger;
        _userSessionLauncher = userSessionLauncher;
    }

    /// <summary>
    /// อ่านประวัติใหม่ของทุก browser ที่รองรับ แปลงเป็น WebVisitDto พร้อมคำนวณ WasBlocked ตาม patterns ที่
    /// ส่งมา (ตัวเดียวกับที่ WebsiteControlService.Apply ใช้เขียน policy อยู่ ณ ตอนนี้ - best-effort ไม่ใช่
    /// สถานะย้อนหลังจริงตอนที่ visit เกิดขึ้น ดู doc comment ของ WasBlocked ใน Models/WebVisit.cs ฝั่ง
    /// Server) คืน list เปล่าถ้าไม่มีใคร login อยู่/ไม่มีอะไรใหม่ - ไม่ throw ไม่ว่ากรณีใด (best-effort
    /// เหมือน ServerClient.SendLogAsync)
    /// </summary>
    public List<WebVisitDto> CollectNewVisits(string[] blockPatterns, string[]? allowPatterns)
    {
        var profileDirectory = _userSessionLauncher.TryGetActiveUserProfileDirectory();
        if (string.IsNullOrWhiteSpace(profileDirectory))
        {
            // ไม่มีใคร login หน้าเครื่องเลยตอนนี้ - ไม่ใช่ error (ปกติตอนเครื่องอยู่หน้าล็อกอิน)
            return new List<WebVisitDto>();
        }

        var results = new List<WebVisitDto>();
        foreach (var browser in Browsers)
        {
            try
            {
                results.AddRange(CollectFromBrowser(browser, profileDirectory, blockPatterns, allowPatterns));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "อ่านประวัติเว็บของ {Browser} ไม่สำเร็จรอบนี้ (จะลองใหม่รอบถัดไป)", browser.Name);
            }
        }

        return results;
    }

    private List<WebVisitDto> CollectFromBrowser(
        BrowserProfile browser, string profileDirectory, string[] blockPatterns, string[]? allowPatterns)
    {
        var historyPath = Path.Combine(profileDirectory, browser.RelativeHistoryPath);
        if (!File.Exists(historyPath))
        {
            return new List<WebVisitDto>(); // ยังไม่เคยใช้ browser นี้บน user นี้เลย
        }

        var tempPath = Path.Combine(Path.GetTempPath(), $"onionproc-history-{browser.Name}-{Guid.NewGuid():N}.sqlite");
        try
        {
            CopyHistoryFileWithSidecars(historyPath, tempPath);

            var since = _lastChromeTimestampByBrowser.GetValueOrDefault(browser.Name, 0L);
            var visits = ChromeHistoryReader.ReadNewVisits(tempPath, since);
            if (visits.Count == 0)
            {
                return new List<WebVisitDto>();
            }

            // visits เรียง visit_time ASC (ดู ChromeHistoryReader) - ตัวสุดท้ายคือใหม่ที่สุด
            _lastChromeTimestampByBrowser[browser.Name] = visits[^1].ChromeTimestamp;

            var dtos = new List<WebVisitDto>();
            foreach (var visit in visits)
            {
                if (!Uri.TryCreate(visit.Url, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Host))
                {
                    continue; // chrome://, about:, data: ฯลฯ - ไม่มี host ให้รายงานเป็นโดเมน
                }

                dtos.Add(new WebVisitDto
                {
                    VisitedAt = visit.VisitedAtUtc,
                    Domain = uri.Host,
                    PageTitle = visit.Title,
                    WasBlocked = IsDomainBlocked(uri.Host, blockPatterns, allowPatterns)
                });
            }

            return dtos;
        }
        finally
        {
            DeleteIfExists(tempPath);
            DeleteIfExists(tempPath + "-wal");
            DeleteIfExists(tempPath + "-journal");
            DeleteIfExists(tempPath + "-shm");
        }
    }

    private static void CopyHistoryFileWithSidecars(string sourcePath, string destPath)
    {
        File.Copy(sourcePath, destPath, overwrite: true);

        // WAL mode - visit ล่าสุดอาจยังไม่ checkpoint ลง main file เลย (ดู doc comment บนคลาส)
        foreach (var suffix in new[] { "-wal", "-journal" })
        {
            var sidecar = sourcePath + suffix;
            if (File.Exists(sidecar))
            {
                File.Copy(sidecar, destPath + suffix, overwrite: true);
            }
        }
    }

    private static void DeleteIfExists(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // ไฟล์ temp ลบไม่ได้ (เช่นยังมี handle ค้างอยู่แป๊บหนึ่ง) - ไม่ถือเป็น error ร้ายแรง ปล่อยให้ OS
            // เก็บกวาด temp เอง
        }
    }

    /// <summary>
    /// จำลอง semantics ของ Chrome/Edge URLBlocklist/URLAllowlist แบบง่าย (เดียวกับที่ Console's
    /// WebsiteAllowListPlaceholderTab.TestUrlButton_Click ใช้ทดสอบ): domain ตรงเป๊ะ หรือ host ลงท้ายด้วย
    /// "." + domain, "*" ครอบคลุมทุกเว็บ (โหมดสอบ) - ไม่รองรับ wildcard รูปแบบอื่นของ Chrome policy เต็มๆ
    /// (เช่น "*://*.roblox.com/*") เป็น best-effort ตามที่คุยกับ user (ดู doc comment ของ WasBlocked)
    /// URLAllowlist ชนะ URLBlocklist เสมอตาม semantics จริงของ policy พวกนี้
    /// </summary>
    private static bool IsDomainBlocked(string host, string[] blockPatterns, string[]? allowPatterns)
    {
        if (allowPatterns is { Length: > 0 } && MatchesAny(host, allowPatterns))
        {
            return false;
        }

        return MatchesAny(host, blockPatterns);
    }

    private static bool MatchesAny(string host, IEnumerable<string> patterns) =>
        patterns.Any(p => p == "*"
            || string.Equals(host, p, StringComparison.OrdinalIgnoreCase)
            || host.EndsWith("." + p, StringComparison.OrdinalIgnoreCase));
}
