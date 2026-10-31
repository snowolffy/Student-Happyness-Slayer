namespace OnionProcOparetor.Agent.Models;

/// <summary>
/// ตั้งค่า "ป้องกันการเลี่ยง" ที่รับมาจาก Server (โครงสร้างต้องตรงกับ BrowserHardeningSettings
/// ฝั่ง Server) - ดู Services/BrowserHardeningService.cs ที่เอาค่านี้ไปเขียน Chrome/Edge policy
/// ไม่รวม BlockOtherBrowsers เพราะข้อนั้น Server จัดการผ่านกฎ Program Control (ตาราง Rule) แทน
/// Agent ฝั่งนี้ไม่ต้องรู้จักฟิลด์นั้นเลย
/// </summary>
public class BrowserHardeningSettingsDto
{
    public bool BlockExtensions { get; set; }
    public bool BlockGuestAndIncognito { get; set; }
    public bool BlockDevTools { get; set; }
    public bool LockDnsAndProxy { get; set; }
}
