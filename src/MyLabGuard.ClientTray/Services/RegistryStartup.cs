using Microsoft.Win32;

namespace MyLabGuard.ClientTray.Services;

/// <summary>
/// จัดการ Registry Run key เพื่อให้ MyLabGuard.ClientTray auto-start ตอน user login
/// ใช้ HKEY_CURRENT_USER (ไม่ใช่ HKEY_LOCAL_MACHINE) เพราะ:
/// 1. ไม่ต้องสิทธิ์ Administrator ในการเขียน (ต่างจาก Client Service ที่ต้อง SYSTEM)
/// 2. เหมาะกับ Tray ที่ควรรันแค่ตอน user คนนั้น login เท่านั้น ไม่ใช่รันตลอดแบบ service
/// </summary>
public static class RegistryStartup
{
    private const string RunKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "MyLabGuardClientTray";

    /// <summary>
    /// เช็คว่า auto-start ถูกตั้งค่าไว้แล้วหรือยัง ถ้ายัง ให้ตั้งค่าใหม่ให้อัตโนมัติ
    /// เรียกตอน Tray เริ่มทำงานครั้งแรก (App.xaml.cs OnStartup) - ทำงานแบบ idempotent
    /// (เรียกซ้ำกี่ครั้งก็ได้ ไม่มีผลข้างเคียง ถ้า key ถูกต้องอยู่แล้วจะไม่เขียนซ้ำ)
    /// </summary>
    public static void EnsureAutoStartEnabled()
    {
        try
        {
            // สำคัญ: ต้องใช้ Environment.ProcessPath เพื่อดึง path ของ .exe ที่กำลังรันอยู่จริง
            // ห้าม hardcode path เด็ดขาด เพราะ path จะต่างกันระหว่างเครื่อง dev กับเครื่องที่ install จริง
            // (เช่น dev อาจรันจาก bin\Debug\... แต่เครื่องจริงจะอยู่ที่ Program Files หรือโฟลเดอร์ install อื่น)
            var exePath = Environment.ProcessPath;

            if (string.IsNullOrWhiteSpace(exePath))
            {
                // หา path ไม่ได้ (ไม่ควรเกิดขึ้นได้จริง แต่กันไว้เผื่อ edge case) - ข้ามไปเลย
                // ไม่ throw เพราะ auto-start ไม่ใช่ฟีเจอร์ที่ critical พอจะทำให้ทั้งแอป crash
                return;
            }

            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                ?? Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);

            if (key is null)
            {
                return;
            }

            var existingValue = key.GetValue(ValueName) as string;

            // Idempotent: ถ้า path ตรงกับที่ตั้งไว้แล้ว ไม่ต้องเขียนซ้ำ
            // ถ้า path เปลี่ยนไป (เช่น ย้ายตำแหน่งไฟล์ install ใหม่) ให้ update ให้ตรงล่าสุด
            if (!string.Equals(existingValue, exePath, StringComparison.OrdinalIgnoreCase))
            {
                key.SetValue(ValueName, exePath, RegistryValueKind.String);
            }
        }
        catch
        {
            // เขียน registry ไม่สำเร็จ (เช่น permission แปลกๆ) - ไม่ถือเป็น error ร้ายแรง
            // Tray ยังใช้งานได้ปกติ แค่จะไม่ auto-start ครั้งถัดไปเท่านั้น ไม่ควร crash ทั้งแอป
        }
    }

    /// <summary>
    /// ลบ auto-start ออก (เผื่อใช้ตอน uninstall หรือถ้า user อยากปิดฟีเจอร์นี้ทีหลัง)
    /// </summary>
    public static void DisableAutoStart()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            key?.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        catch
        {
            // ลบไม่สำเร็จ - ไม่ถือเป็น error ร้ายแรง เช่นเดียวกับตอน enable
        }
    }
}