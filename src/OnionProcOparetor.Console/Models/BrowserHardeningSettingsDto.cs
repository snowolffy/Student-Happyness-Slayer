namespace OnionProcOparetor.Console.Models;

/// <summary>
/// ตั้งค่า "ป้องกันการเลี่ยง" ของ Website Control (GET /api/browser-hardening, POST
/// /api/browser-hardening/set) - WP2-1, 2026-10-31 ดู WebsiteAntiEvasionTab
/// </summary>
public class BrowserHardeningSettingsDto
{
    public int Id { get; set; }
    public bool BlockExtensions { get; set; }
    public bool BlockGuestAndIncognito { get; set; }
    public bool BlockDevTools { get; set; }
    public bool LockDnsAndProxy { get; set; }
    public bool BlockOtherBrowsers { get; set; }
    public DateTime UpdatedAt { get; set; }
}
