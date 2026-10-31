namespace OnionProcOparetor.Server.Models;

/// <summary>
/// โดเมนหนึ่งรายการของ <see cref="AllowListPreset"/> (WP2-2, 2026-10-31) - แยกตารางจาก Preset เอง
/// (ไม่ใช่ CSV column) เพราะหน้า Website Control › ชุด Allow-list ต้องเพิ่ม/ลบทีละโดเมนแยกกัน
/// (ดู wireframe con-website-allow.dc.html ปุ่ม "+ เพิ่ม"/"ลบ" ต่อแถว) ไม่มี navigation property ไป
/// AllowListPreset ตรงกับสไตล์เดิมของ repo นี้ (ดู RuleOverride/WebsiteRule - ผูกกันด้วย Id ธรรมดา
/// query/join เองใน endpoint ไม่ใช้ EF navigation)
/// </summary>
public class AllowListPresetDomain
{
    public int Id { get; set; }

    public int PresetId { get; set; }

    /// <summary>
    /// URL pattern ตาม syntax ของ Chrome/Edge URLAllowlist policy เดียวกับ WebsiteRule.UrlPattern
    /// (ไม่ต้องมี wildcard - Chrome ถือว่า "example.com" ครอบคลุม subdomain/path ทั้งหมดอยู่แล้ว)
    /// </summary>
    public string Domain { get; set; } = string.Empty;
}
