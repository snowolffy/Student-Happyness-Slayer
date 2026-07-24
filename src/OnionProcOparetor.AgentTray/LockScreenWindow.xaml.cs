using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;

namespace OnionProcOparetor.AgentTray;

/// <summary>
/// หน้าจอล็อกเต็มจอสีดำ - แทนที่ WorkstationLocker.Lock() (LockWorkStation() ธรรมดา) เดิมทั้งหมด
/// เพราะ LockWorkStation() แค่เรียก Windows lock screen ปกติ (เหมือนกด Win+L) ซึ่ง user ยังปลดล็อก
/// กลับมาใช้เครื่องเองได้ทันทีด้วย password ของตัวเอง - ไม่ตรงกับที่ต้องการ (ล็อกจนกว่าครูจะสั่งปลด
/// จาก Console เท่านั้น ไม่มี local password ไม่มี timeout อัตโนมัติ)
///
/// ปลดล็อกได้ทางเดียว: เรียก ForceClose() จาก App.xaml.cs ตอนได้รับ "HideLockScreen" ผ่าน pipe -
/// Closing ปกติ (Alt-F4 ที่หลุดจาก hook ทางใดทางหนึ่ง, หรือ system close อื่นๆ) ถูกกันไว้เสมอ
///
/// เครื่องมีจอเดียว (single monitor) - ไม่ต้องรองรับ multi-monitor ในเวอร์ชันนี้ WindowState=Maximized
/// บนจอเดียวก็ครอบทั้งจอพอดีอยู่แล้ว
/// </summary>
public partial class LockScreenWindow : Window
{
    /// <summary>ข้อความที่แสดงกลางจอ - แก้ wording ตรงนี้จุดเดียว</summary>
    public const string LockMessage = "เครื่องนี้ถูกล็อกโดยผู้ดูแลระบบ\nกรุณาติดต่อผู้ดูแลระบบเพื่อปลดล็อก";

    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int VK_TAB = 0x09;
    private const int VK_ESCAPE = 0x1B;
    private const int VK_LWIN = 0x5B;
    private const int VK_RWIN = 0x5C;
    private const int VK_F4 = 0x73;
    private const int VK_MENU = 0x12; // Alt
    private const int VK_CONTROL = 0x11;

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    // ต้องเก็บ delegate ไว้เป็น field กัน GC เก็บทิ้งระหว่างที่ hook ยังติดตั้งอยู่ (ไม่งั้น Windows
    // อาจเรียก callback ผ่าน function pointer ที่ตายไปแล้ว - crash แบบสุ่มเวลาไม่แน่นอน)
    private LowLevelKeyboardProc? _hookProc;
    private IntPtr _hookId = IntPtr.Zero;
    private bool _allowClose;

    public LockScreenWindow()
    {
        InitializeComponent();
        LockMessageText.Text = LockMessage;
        Loaded += (_, _) => InstallHook();
    }

    /// <summary>เรียกจาก App.xaml.cs เท่านั้น ตอนได้รับ "HideLockScreen" - ทางเดียวที่ปิดหน้าต่างนี้ได้จริง</summary>
    public void ForceClose()
    {
        _allowClose = true;
        UninstallHook();
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_allowClose)
        {
            // กันการปิดทุกทางที่ไม่ได้มาจาก ForceClose() (เช่น Alt-F4 เผื่อหลุดจาก hook ทางใดทางหนึ่ง)
            // ปลดล็อกได้จาก Console เท่านั้นตามดีไซน์ - ไม่มีทางปลดจากฝั่งเครื่อง client เอง
            e.Cancel = true;
            return;
        }

        base.OnClosing(e);
    }

    private void InstallHook()
    {
        if (_hookId != IntPtr.Zero)
        {
            return; // ติดตั้งไปแล้ว
        }

        _hookProc = HookCallback;
        using var curProcess = System.Diagnostics.Process.GetCurrentProcess();
        using var curModule = curProcess.MainModule!;
        _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _hookProc, GetModuleHandle(curModule.ModuleName), 0);
    }

    /// <summary>ต้อง unhook ทันทีตอนปิดหน้าจอล็อก - ไม่ปล่อยค้าง ไม่งั้นจะกระทบการใช้งานปกติหลังปลดล็อก</summary>
    private void UninstallHook()
    {
        if (_hookId != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }

        _hookProc = null;
    }

    /// <summary>
    /// Suppress Alt-Tab, Win key (ซ้าย/ขวา), Ctrl-Esc, Alt-F4 ระหว่างล็อกอยู่ - คืนค่าโดยไม่เรียก
    /// CallNextHookEx ต่อ (คืน (IntPtr)1 แทน) เพื่อกัน Windows ประมวลผล key combination นั้นต่อไปเลย
    /// (มาตรฐานเดียวกับที่ kiosk-mode screen locker ทั่วไปใช้)
    ///
    /// Ctrl-Alt-Del กันไม่ได้ตามดีไซน์ของ Windows เอง (Secure Attention Sequence รับประกันโดย OS
    /// ไม่มี user-mode hook ตัวไหนดักได้) - เป็นข้อจำกัดที่ทราบอยู่แล้ว ไม่พยายามกัน
    /// </summary>
    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN))
        {
            var vkCode = Marshal.ReadInt32(lParam);
            var altPressed = (GetAsyncKeyState(VK_MENU) & 0x8000) != 0;
            var ctrlPressed = (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0;

            var shouldBlock =
                (vkCode == VK_TAB && altPressed) ||       // Alt-Tab
                vkCode == VK_LWIN || vkCode == VK_RWIN ||  // Win key (เปิด Start Menu)
                (vkCode == VK_ESCAPE && ctrlPressed) ||    // Ctrl-Esc (เปิด Start Menu)
                (vkCode == VK_F4 && altPressed);           // Alt-F4

            if (shouldBlock)
            {
                return (IntPtr)1;
            }
        }

        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);
}
