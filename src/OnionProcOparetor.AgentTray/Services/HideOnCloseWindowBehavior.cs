using System.ComponentModel;
using System.Windows;

namespace OnionProcOparetor.AgentTray.Services;

/// <summary>
/// ทำให้ปุ่มกากบาทของหน้าต่าง "ซ่อน" แทนที่จะ "ปิดจริง" พร้อมเอาออกจาก taskbar
///
/// เหตุผล: AgentTray เป็น tray app ที่ต้องอยู่ตลอด (ShutdownMode="OnExplicitShutdown")
/// หน้าต่างอย่าง StatusWindow/LogTerminalWindow เป็นแค่หน้าจอเสริมที่เปิดดูเป็นครั้งคราว
/// การปิดจริงทำให้ต้องสร้างใหม่ทั้งหมดทุกครั้ง (login ใหม่ โหลด log ใหม่) ทั้งที่ user
/// คาดหวังแค่ "เอาออกไปจากตาก่อน" - Hide() ตรงกับความคาดหวังนั้นมากกว่า
///
/// ShowInTaskbar = false มาคู่กันเสมอ: ถ้าซ่อนหน้าต่างแล้วแต่ยังมีปุ่มค้างบน taskbar user
/// จะกดแล้วงงว่าทำไมไม่มีอะไรขึ้น ทางเข้าหน้าต่างเหล่านี้คือ tray icon เท่านั้นตามดีไซน์
///
/// ใช้เป็น extension method แทน base class โดยตั้งใจ - การแทรก base class ใหม่ต้องแก้ทั้ง
/// .xaml (root element ของทุกหน้า) และ .xaml.cs ซึ่งกระทบมากกว่าเรียก 1 บรรทัดใน constructor
/// </summary>
public static class HideOnCloseWindowBehavior
{
    /// <summary>เรียกใน constructor ของหน้าต่างที่ต้องการให้ปุ่มกากบาท = ซ่อน (ไม่ใช่ปิด)</summary>
    public static void EnableHideOnClose(this Window window)
    {
        window.ShowInTaskbar = false;
        window.Closing += OnWindowClosing;
    }

    /// <summary>
    /// ปิดหน้าต่างนี้ "จริงๆ" โดยข้าม hide-on-close - ใช้ตอนที่โค้ดตั้งใจปิดเอง ไม่ใช่ user
    /// กดกากบาท (เช่น LoginWindow ปิดตัวเองหลัง login สำเร็จเพื่อเปิด StatusWindow แทน)
    ///
    /// pattern เดียวกับ LockScreenWindow.ForceClose() ที่มีอยู่แล้วในโปรเจกต์นี้: หน้าต่างที่
    /// กันปิดไว้ ต้องมีทางออกที่ชัดเจนสำหรับโค้ดเสมอ ไม่งั้นจะปิดไม่ลงเลย
    /// </summary>
    public static void ForceClose(this Window window)
    {
        window.Closing -= OnWindowClosing;
        window.Close();
    }

    private static void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        if (sender is not Window window)
        {
            return;
        }

        e.Cancel = true;
        window.Hide();
    }
}
