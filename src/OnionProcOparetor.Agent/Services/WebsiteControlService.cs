using Microsoft.Win32;

namespace OnionProcOparetor.Agent.Services;

/// <summary>
/// เขียน Chrome/Edge Enterprise Policy (URLBlocklist ผ่าน Registry) ตามกฎบล็อกเว็บที่ Server ส่งมา
/// (Website Control, 2026-09-11) - คู่ขนานกับ ProgramControlService แต่บล็อกที่ "ระดับ URL ใน
/// เบราว์เซอร์" แทนที่จะปิด process
///
/// ทำไมใช้ registry policy แทน extension: เด็กปิดเองไม่ได้ (browser โชว์ "This setting is managed
/// by your administrator" ไม่มี UI ให้ปิด) และไม่ต้องพึ่ง extension store/inject อะไรเพิ่ม
/// Chrome/Edge watch key นี้เองและ reload policy ภายในไม่กี่วินาทีโดยไม่ต้อง restart browser
///
/// เหมือน process rules ต้อง apply ซ้ำทุกรอบ scan (10 วิ) ไม่ใช่ครั้งเดียวตอนเจอกฎใหม่ เพราะเครื่อง
/// แล็บทุกเครื่องรัน Reboot Restore Rx ล้าง disk (รวม registry) ทุก reboot - ดู
/// Worker.ScanRunningProcessesAsync ที่เรียก Apply ทุกรอบไม่ว่ารายการจะเปลี่ยนหรือไม่ และส่ง empty
/// list เข้ามาเองเมื่อ enforcement ปิดอยู่/bypass (ไม่ได้ skip การเรียกแบบที่ process rules ทำ) เพราะ
/// registry key ที่เคยเขียนไว้จะ "อยู่ต่อ" ในเซสชันเดียวกันจนกว่าจะมีคนลบจริง - ต่างจาก process kill
/// ที่แค่ไม่ทำอะไรต่อก็พอ
///
/// "โหมดสอบ" (WP2-2, 2026-10-31) ใช้ Apply ตัวเดียวกันนี้ - Worker ส่ง blockPatterns = ["*"] (บล็อก
/// ทุกเว็บ) พร้อม allowPatterns = โดเมนที่อนุญาต (เขียนเป็น URLAllowlist คู่กับ URLBlocklist - Chrome/
/// Edge ให้ URLAllowlist ชนะ URLBlocklist เสมอตาม semantics ของ policy พวกนี้) ปกติ (ไม่ใช่โหมดสอบ)
/// allowPatterns จะเป็น null/empty แล้ว URLAllowlist ถูกล้างทิ้งเหมือน URLBlocklist ตอนไม่มีกฎ
///
/// Registry root เป็นพารามิเตอร์ (ไม่ใช่ Registry.LocalMachine ตรงๆ) เพื่อทดสอบได้โดยไม่ต้องเขียน
/// policy จริงใต้ HKLM บนเครื่อง dev (ดู docs/roadmap.md ขั้น 2 "ข้อจำกัดในการทดสอบ") - เหตุผลและ
/// รูปแบบเดียวกับ BrowserHardeningService ที่ทำแบบนี้ไว้แล้วตั้งแต่ WP2-1
/// </summary>
public sealed class WebsiteControlService
{
    private const string BlocklistName = "URLBlocklist";
    private const string AllowlistName = "URLAllowlist";

    private static readonly (string Vendor, string Product)[] Targets =
    {
        ("Google", "Chrome"),
        ("Microsoft", "Edge")
    };

    private readonly ILogger<WebsiteControlService> _logger;
    private readonly RegistryKey _root;

    public WebsiteControlService(ILogger<WebsiteControlService> logger, RegistryKey? root = null)
    {
        _logger = logger;
        _root = root ?? Registry.LocalMachine;
    }

    /// <summary>
    /// เขียน registry ให้ตรงกับ patterns ที่ส่งมา - ส่ง empty collection ให้ blockPatterns = เคลียร์
    /// blocklist ทิ้งทั้งหมด allowPatterns ไม่ระบุ/empty = เคลียร์ allowlist ทิ้งด้วย (โหมดปกติ)
    /// ไม่เก็บ "apply แล้ว" flag ไว้ที่ไหนเลย เพราะ registry เองก็โดนล้างทุก reboot เหมือน state
    /// อื่นๆ ของเครื่องแล็บ (idempotent - เรียกซ้ำด้วย patterns เดิมได้เสมอโดยไม่มีผลข้างเคียง)
    /// </summary>
    public void Apply(IReadOnlyCollection<string> blockPatterns, IReadOnlyCollection<string>? allowPatterns = null)
    {
        var normalizedBlock = Normalize(blockPatterns);
        var normalizedAllow = Normalize(allowPatterns ?? Array.Empty<string>());

        foreach (var (vendor, product) in Targets)
        {
            try
            {
                ApplyListToProduct(vendor, product, BlocklistName, normalizedBlock);
                ApplyListToProduct(vendor, product, AllowlistName, normalizedAllow);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "เขียน URLBlocklist/URLAllowlist policy ของ {Vendor}\\{Product} ไม่สำเร็จ", vendor, product);
            }
        }
    }

    private static List<string> Normalize(IReadOnlyCollection<string> patterns) =>
        patterns
            .Select(p => p?.Trim())
            .Where(p => !string.IsNullOrEmpty(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(p => p!)
            .ToList();

    private void ApplyListToProduct(string vendor, string product, string listName, List<string> patterns)
    {
        var basePath = $@"SOFTWARE\Policies\{vendor}\{product}";

        if (patterns.Count == 0)
        {
            // ไม่มีกฎเปิดใช้งานอยู่เลย (หรือ enforcement ปิดอยู่) - ลบ subkey ทิ้งทั้งก้อนถ้ามี
            using var baseKey = _root.OpenSubKey(basePath, writable: true);
            baseKey?.DeleteSubKeyTree(listName, throwOnMissingSubKey: false);
            return;
        }

        using var listKey = _root.CreateSubKey($@"{basePath}\{listName}", writable: true);
        if (listKey is null)
        {
            return;
        }

        // เขียนทับด้วย index ใหม่ (1..N) แล้วลบ index ที่เหลือจากรอบก่อนที่เกิน N ทิ้ง - กัน pattern
        // เก่าที่ถูกลบ/ปิดไปแล้วค้างอยู่ใน registry ทั้งที่ไม่มีใน patterns รอบนี้แล้ว
        for (var i = 0; i < patterns.Count; i++)
        {
            listKey.SetValue((i + 1).ToString(), patterns[i], RegistryValueKind.String);
        }

        foreach (var existingName in listKey.GetValueNames())
        {
            if (int.TryParse(existingName, out var index) && index > patterns.Count)
            {
                listKey.DeleteValue(existingName, throwOnMissingValue: false);
            }
        }
    }
}
