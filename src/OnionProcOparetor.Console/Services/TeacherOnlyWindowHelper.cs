using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace OnionProcOparetor.Console.Services;

/// <summary>
/// "ส่งจอครู" คำขอเพิ่ม 3 พ.ย. 2569 - helper Win32 ที่ใช้ร่วมกันระหว่างหน้าต่างที่ "ครูเห็นแต่นักเรียนห้ามเห็น"
/// (กรอบแดงรอบจอ, แถบ/ปุ่มกลมตอนหุบ, แผงเครื่องมือเขียน) ทุกหน้าต่างกลุ่มนี้ต้อง:
/// 1) SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE) - ตัดตัวเองออกจากภาพที่ BitBlt จับได้ (ตัดสินแล้ว
///    3 พ.ย. ว่าต้องทดสอบจริงว่าภาพที่จับได้ไม่มีกรอบ/แถบติด - ดู ScreenCaptureServiceTests/รายงาน WP4-4)
/// 2) (บางหน้าต่าง) คลิกทะลุได้ (กรอบแดงต้องทะลุตลอด, แผงเขียนทะลุเฉพาะตอนไม่ได้อยู่ในโหมดเขียน)
///
/// ตรงข้ามกับหน้าต่างกลุ่มนี้คือ DrawOverlayWindow (เส้นที่ครูเขียน) ซึ่ง "ห้าม" เรียก
/// SetExcludeFromCapture เพราะเส้นต้องติดไปในภาพที่ส่งให้นักเรียนเห็นจริงตามที่ตัดสินไว้
/// </summary>
internal static class TeacherOnlyWindowHelper
{
    private const int WDA_NONE = 0x00000000;
    private const int WDA_EXCLUDEFROMCAPTURE = 0x00000011;

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_LAYERED = 0x00080000;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint dwAffinity);

    [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
    private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
    private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    private static IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex) =>
        IntPtr.Size == 8 ? GetWindowLongPtr64(hWnd, nIndex) : new IntPtr(GetWindowLong32(hWnd, nIndex));

    private static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong) =>
        IntPtr.Size == 8 ? SetWindowLongPtr64(hWnd, nIndex, dwNewLong) : new IntPtr(SetWindowLong32(hWnd, nIndex, dwNewLong.ToInt32()));

    /// <summary>
    /// ตัดหน้าต่างนี้ออกจากภาพที่ตัวจับภาพของระบบ (BitBlt/PrintWindow/Windows Graphics Capture) จับได้
    /// - เรียกตอน SourceInitialized (ต้องมี HWND จริงแล้ว) คืน false เงียบๆ ถ้า Windows เวอร์ชันนี้ไม่
    /// รองรับ (WDA_EXCLUDEFROMCAPTURE ต้อง Windows 10 2004+ ขึ้นไป) - ไม่ throw เพราะหน้าต่างยังต้องใช้
    /// งานได้ต่อแม้ exclude ไม่สำเร็จ (แค่จะไปติดภาพซึ่งเป็น degrade ที่ยอมรับได้มากกว่าโปรแกรมพัง)
    /// </summary>
    public static bool ExcludeFromCapture(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            var ok = SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE);
            if (!ok)
            {
                var error = Marshal.GetLastWin32Error();
                OnionProcOparetor.Console.Services.ConsoleDiagnostics.WriteLine(
                    $"[TeacherOnlyWindowHelper] SetWindowDisplayAffinity ล้มเหลว GetLastError={error}");
            }
            return ok;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>คืนกลับเป็นจับภาพได้ปกติ (ไม่ได้ใช้จริงรอบนี้ - เผื่ออนาคต)</summary>
    public static void IncludeInCapture(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd != IntPtr.Zero)
        {
            SetWindowDisplayAffinity(hwnd, WDA_NONE);
        }
    }

    /// <summary>
    /// สลับคลิกทะลุ (ไม่รับ mouse input เลย ส่งต่อให้หน้าต่าง/โปรแกรมที่อยู่ข้างใต้) - ใช้กับกรอบแดง
    /// (ทะลุตลอด) และแผงเขียน (ทะลุเฉพาะตอนไม่ได้อยู่ในโหมดเขียน) ต้องเป็นหน้าต่างที่ AllowsTransparency=True
    /// อยู่แล้ว (WS_EX_LAYERED ถูกตั้งให้โดย WPF เอง) ฟังก์ชันนี้แค่เพิ่ม/ลบ WS_EX_TRANSPARENT
    /// </summary>
    public static void SetClickThrough(Window window, bool clickThrough)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        var extendedStyle = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
        var withLayered = extendedStyle | WS_EX_LAYERED;
        var newStyle = clickThrough ? (withLayered | WS_EX_TRANSPARENT) : (withLayered & ~WS_EX_TRANSPARENT);
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(newStyle));
    }
}
