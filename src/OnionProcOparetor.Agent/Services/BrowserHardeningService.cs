using Microsoft.Win32;
using OnionProcOparetor.Agent.Models;

namespace OnionProcOparetor.Agent.Services;

/// <summary>
/// เขียน Chrome/Edge Enterprise Policy "ป้องกันการเลี่ยง" ตามค่าที่ Server ส่งมา (WP2-1, 2026-10-31)
/// คู่ขนานกับ WebsiteControlService แต่คุมพฤติกรรมของเบราว์เซอร์เองแทนที่จะคุม URL ที่เข้าได้
///
/// เหมือน WebsiteControlService ต้อง apply ซ้ำทุกรอบ (ไม่ใช่ครั้งเดียวตอนค่าเปลี่ยน) เพราะเครื่องแล็บ
/// ทุกเครื่องรัน Reboot Restore Rx ล้าง registry ทุก reboot - ดู Worker.ScanRunningProcessesAsync
/// ที่เรียก Apply(null) เองตอน bypass/ปิด enforcement เพื่อเคลียร์ policy ออกจริง ไม่ใช่แค่หยุดยุ่งเฉยๆ
///
/// Registry root เป็นพารามิเตอร์ (ไม่ใช่ Registry.LocalMachine ตรงๆ) เพื่อทดสอบได้โดยไม่ต้องเขียน
/// policy จริงใต้ HKLM บนเครื่อง dev (ดู docs/roadmap.md ขั้น 2 "ข้อจำกัดในการทดสอบ") - DI จะ inject
/// Registry.LocalMachine ให้เองถ้าไม่มีใคร register RegistryKey ไว้ (ใช้ default parameter value)
/// </summary>
public sealed class BrowserHardeningService
{
    private const string IncognitoValueName = "IncognitoModeAvailability"; // Chrome
    private const string InPrivateValueName = "InPrivateModeAvailability"; // Edge
    private const string GuestModeValueName = "BrowserGuestModeEnabled"; // ทั้งคู่
    private const string DevToolsValueName = "DeveloperToolsAvailability";
    private const string DnsOverHttpsValueName = "DnsOverHttpsMode";
    private const string ProxyModeValueName = "ProxyMode";
    private const string ExtensionBlocklistKeyName = "ExtensionInstallBlocklist";

    private static readonly (string Vendor, string Product)[] Targets =
    {
        ("Google", "Chrome"),
        ("Microsoft", "Edge")
    };

    private readonly ILogger<BrowserHardeningService> _logger;
    private readonly RegistryKey _root;

    public BrowserHardeningService(ILogger<BrowserHardeningService> logger, RegistryKey? root = null)
    {
        _logger = logger;
        _root = root ?? Registry.LocalMachine;
    }

    /// <summary>
    /// เขียน/ล้าง policy ให้ตรงกับ settings - ส่ง null = เคลียร์ทุกอย่างทิ้ง (bypass/ปิด enforcement)
    /// Idempotent เหมือน WebsiteControlService.Apply - เรียกซ้ำด้วยค่าเดิมได้เสมอโดยไม่มีผลข้างเคียง
    /// </summary>
    public void Apply(BrowserHardeningSettingsDto? settings)
    {
        foreach (var (vendor, product) in Targets)
        {
            try
            {
                ApplyToProduct(vendor, product, settings);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "เขียน policy ป้องกันการเลี่ยงของ {Vendor}\\{Product} ไม่สำเร็จ", vendor, product);
            }
        }
    }

    private void ApplyToProduct(string vendor, string product, BrowserHardeningSettingsDto? settings)
    {
        var basePath = $@"SOFTWARE\Policies\{vendor}\{product}";

        // ---- ห้ามติดตั้ง extension: ExtensionInstallBlocklist\1 = "*" ----
        if (settings?.BlockExtensions == true)
        {
            using var listKey = _root.CreateSubKey($@"{basePath}\{ExtensionBlocklistKeyName}", writable: true);
            listKey?.SetValue("1", "*", RegistryValueKind.String);
        }
        else
        {
            using var baseKey = _root.OpenSubKey(basePath, writable: true);
            baseKey?.DeleteSubKeyTree(ExtensionBlocklistKeyName, throwOnMissingSubKey: false);
        }

        // ---- ค่าเดี่ยวที่เหลือ: เขียนถ้าเปิด, ลบทิ้งถ้าปิด ----
        using var key = _root.CreateSubKey(basePath, writable: true);
        if (key is null)
        {
            return;
        }

        if (settings?.BlockGuestAndIncognito == true)
        {
            // Chrome ใช้ IncognitoModeAvailability, Edge ใช้ InPrivateModeAvailability - ตั้งทั้งคู่ไว้เสมอ
            // (ค่าที่ไม่เกี่ยวกับ product นั้นจะเป็น dead value เฉยๆ ไม่มีผล ไม่ต้องแยก Targets)
            key.SetValue(IncognitoValueName, 1, RegistryValueKind.DWord); // 1 = Disabled
            key.SetValue(InPrivateValueName, 1, RegistryValueKind.DWord); // 1 = Disabled
            key.SetValue(GuestModeValueName, 0, RegistryValueKind.DWord); // 0 = false
        }
        else
        {
            key.DeleteValue(IncognitoValueName, throwOnMissingValue: false);
            key.DeleteValue(InPrivateValueName, throwOnMissingValue: false);
            key.DeleteValue(GuestModeValueName, throwOnMissingValue: false);
        }

        if (settings?.BlockDevTools == true)
        {
            key.SetValue(DevToolsValueName, 2, RegistryValueKind.DWord); // 2 = Disallowed
        }
        else
        {
            key.DeleteValue(DevToolsValueName, throwOnMissingValue: false);
        }

        if (settings?.LockDnsAndProxy == true)
        {
            key.SetValue(DnsOverHttpsValueName, "off", RegistryValueKind.String);
            key.SetValue(ProxyModeValueName, "system", RegistryValueKind.String);
        }
        else
        {
            key.DeleteValue(DnsOverHttpsValueName, throwOnMissingValue: false);
            key.DeleteValue(ProxyModeValueName, throwOnMissingValue: false);
        }
    }
}
