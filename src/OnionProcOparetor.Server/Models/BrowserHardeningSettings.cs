namespace OnionProcOparetor.Server.Models;

/// <summary>
/// ตั้งค่า "ป้องกันการเลี่ยง" ของ Website Control (WP2-1, 2026-10-31) - เก็บเป็น single row เดียว
/// ใน DB เหมือน <see cref="GlobalState"/> (Id = 1 เสมอ) เปิด/ปิดได้ทีละข้อจาก Server แล้ว Agent
/// เขียน Chrome/Edge Enterprise Policy ตามค่าที่เปิดอยู่ (ดู Agent/Services/BrowserHardeningService.cs)
///
/// แยกตารางจาก GlobalState ตั้งใจ (ไม่เพิ่มคอลัมน์ใน GlobalState) เพราะเป็นกลุ่มตั้งค่าคนละเรื่องกัน
/// (บล็อกเว็บ/เบราว์เซอร์ vs enforcement ทั้งห้อง) เหมือนที่ WebsiteRule แยกจาก Rule
/// </summary>
public class BrowserHardeningSettings
{
    public int Id { get; set; }

    /// <summary>ห้ามติดตั้ง extension - เขียน ExtensionInstallBlocklist = ["*"]</summary>
    public bool BlockExtensions { get; set; } = true;

    /// <summary>ปิด Guest / InPrivate / Incognito - BrowserGuestModeEnabled=false, IncognitoModeAvailability/InPrivateModeAvailability=Disabled</summary>
    public bool BlockGuestAndIncognito { get; set; } = true;

    /// <summary>ปิด DevTools - DeveloperToolsAvailability = Disallowed</summary>
    public bool BlockDevTools { get; set; } = true;

    /// <summary>ล็อก DNS-over-HTTPS / proxy - DnsOverHttpsMode = off, ProxyMode = system</summary>
    public bool LockDnsAndProxy { get; set; } = true;

    /// <summary>
    /// บล็อกเบราว์เซอร์อื่น (Firefox, Opera, Brave ฯลฯ) - ไม่เขียน registry ของตัวเอง แต่สร้าง/เปิดใช้
    /// กฎใน Program Control (ตาราง Rule) แทน ดู BrowserHardeningEndpoints.ApplyOtherBrowserRulesAsync
    /// </summary>
    public bool BlockOtherBrowsers { get; set; } = false;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
