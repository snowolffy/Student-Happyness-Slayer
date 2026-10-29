using System.ComponentModel;
using System.Windows;

namespace OnionProcOparetor.RemoteBridge.Services;

/// <summary>
/// ทำให้ปุ่มกากบาทของหน้าต่าง "ซ่อน" แทนที่จะ "ปิดจริง" (RemoteBridge เป็น tray app ที่ต้องอยู่ตลอด)
/// แนวคิดเดียวกับ OnionProcOparetor.AgentTray.Services.HideOnCloseWindowBehavior - ก๊อปมาเป็นไฟล์
/// เล็กๆ ของตัวเองแทนการอ้างอิงข้าม project โดยตั้งใจ เพื่อให้ RemoteBridge ยังลบทิ้งได้ทั้งโฟลเดอร์
/// โดยไม่กระทบ AgentTray เลย
/// </summary>
public static class HideOnCloseWindowBehavior
{
    public static void EnableHideOnClose(this Window window)
    {
        window.ShowInTaskbar = false;
        window.Closing += OnWindowClosing;
    }

    /// <summary>ปิดหน้าต่างนี้ "จริงๆ" ข้าม hide-on-close - ใช้ตอนโค้ดตั้งใจปิดเอง (เช่น LoginWindow ปิดตัวเองหลัง login สำเร็จเพื่อเปิด StatusWindow แทน) ไม่ใช่ user กดกากบาท</summary>
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
