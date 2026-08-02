using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;

namespace OnionProcOparetor.AgentTray;

/// <summary>
/// หน้าจอล็อกเต็มจอสีดำ - แทนที่ WorkstationLocker.Lock() (LockWorkStation() ธรรมดา) เดิมทั้งหมด
/// เพราะ LockWorkStation() แค่เรียก Windows lock screen ปกติ (เหมือนกด Win+L) ซึ่ง user ยังปลดล็อก
/// กลับมาใช้เครื่องเองได้ทันทีด้วย password ของตัวเอง - ไม่ตรงกับที่ต้องการ (ล็อกจนกว่าครูจะสั่งปลด
/// จาก Console เท่านั้น ไม่มี local password ไม่มี timeout อัตโนมัติ)
///
/// ปลดล็อกได้ทางเดียว: เรียก ForceClose() จาก App.xaml.cs (ตอนได้รับ "HideLockScreen" ผ่าน pipe หรือตอน
/// AgentDiedWhileLocked ทำงาน - ดู CheckAgentHealth) - Closing ปกติ (Alt-F4 ที่หลุดจาก hook ทางใดทางหนึ่ง,
/// หรือ system close อื่นๆ) ถูกกันไว้เสมอ
///
/// App.xaml.cs สร้าง instance ของคลาสนี้แยกหนึ่งตัวต่อจอ (ดู System.Windows.Forms.Screen.AllScreens) - มี
/// แค่ instance เดียวเท่านั้น (installInputBlocking=true) ที่ติดตั้ง keyboard/mouse hook + Agent watchdog
/// จริง เพราะ hook เป็นกลไกระดับ OS ทั้งเครื่อง (system-wide) ไม่ผูกกับจอไหนจอหนึ่ง ติดตั้งซ้ำหลายจอไม่มี
/// ประโยชน์เพิ่มเลยแถมต้อง unhook ให้ครบทุกจุดด้วย ส่วน instance อื่นเป็นแค่ overlay ภาพเปล่าๆ ที่ปิดไม่ได้
/// เหมือนกันทุกจอ (โดน OnClosing กันไว้เหมือนกันหมด)
/// </summary>
public partial class LockScreenWindow : Window
{
    /// <summary>ข้อความที่แสดงกลางจอ - แก้ wording ตรงนี้จุดเดียว</summary>
    public const string LockMessage = "เครื่องนี้ถูกล็อกโดยผู้ดูแลระบบ\nกรุณาติดต่อผู้ดูแลระบบเพื่อปลดล็อก";

    private const int WH_KEYBOARD_LL = 13;
    private const int WH_MOUSE_LL = 14;

    /// <summary>ชื่อ process ของ OnionProcOparetor.Agent (ไม่รวม .exe) - ต้องตรงกับ AssemblyName จริงเป๊ะๆ (ดู Directory.Build.props/Agent.csproj)</summary>
    private const string AgentProcessName = "OnionProcOparetor.Agent";

    private static readonly TimeSpan AgentWatchdogInterval = TimeSpan.FromSeconds(3);

    private delegate IntPtr LowLevelHookProc(int nCode, IntPtr wParam, IntPtr lParam);

    private readonly bool _installInputBlocking;

    // ต้องเก็บ delegate ไว้เป็น field กัน GC เก็บทิ้งระหว่างที่ hook ยังติดตั้งอยู่ (ไม่งั้น Windows
    // อาจเรียก callback ผ่าน function pointer ที่ตายไปแล้ว - crash แบบสุ่มเวลาไม่แน่นอน)
    private LowLevelHookProc? _keyboardHookProc;
    private IntPtr _keyboardHookId = IntPtr.Zero;

    private LowLevelHookProc? _mouseHookProc;
    private IntPtr _mouseHookId = IntPtr.Zero;

    private DispatcherTimer? _agentWatchdogTimer;
    private bool _allowClose;

    /// <summary>
    /// ยิงตอน watchdog ตรวจพบว่า process ของ Agent หายไปกลางคันขณะล็อกอยู่ (ดู CheckAgentHealth) - เรียกจาก
    /// UI thread เดียวกับ Dispatcher ของหน้าต่างนี้อยู่แล้ว (DispatcherTimer.Tick การันตีเรื่องนี้) ผู้
    /// subscribe (App.xaml.cs) ไม่ต้อง Dispatcher.Invoke ซ้ำอีกชั้น เรียกได้เฉพาะจาก instance ที่
    /// installInputBlocking=true เท่านั้น (ตัวอื่นไม่มี watchdog เลยไม่มีทางยิง event นี้)
    /// </summary>
    public event Action? AgentDiedWhileLocked;

    public LockScreenWindow(System.Windows.Forms.Screen screen, bool installInputBlocking)
    {
        InitializeComponent();
        LockMessageText.Text = LockMessage;
        _installInputBlocking = installInputBlocking;

        PositionToScreen(screen);

        Loaded += (_, _) =>
        {
            if (_installInputBlocking)
            {
                InstallKeyboardHook();
                InstallMouseHook();
                StartAgentWatchdog();
            }
        };

        // ดึง keyboard focus กลับมาทันทีถ้าหลุดไประหว่างยังล็อกอยู่ (ยังไม่ได้ ForceClose()) - เป็น
        // secondary layer เสริมจาก keyboard hook เท่านั้น (hook กันไม่ให้คีย์ที่พิมพ์ไปโดนหน้าต่างอื่นได้อยู่
        // แล้วไม่ว่า focus จะอยู่ตรงไหนก็ตาม) แต่การดึง focus กลับมาไว้ที่ตัวเองเพิ่มเติมยังมีประโยชน์ต่อ
        // input method อื่นที่อาศัย focus จริง (เช่น IME/screen reader) ไม่ใช่แค่ raw keydown/mouse event
        Deactivated += (_, _) =>
        {
            if (!_allowClose)
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (!_allowClose)
                    {
                        Activate();
                    }
                }), DispatcherPriority.Input);
            }
        };
    }

    /// <summary>
    /// แปลง physical pixel bounds ของจอเป้าหมาย -> WPF DIP แล้วตั้ง Left/Top/Width/Height ก่อน Show() - ใช้
    /// อัตราส่วนจากจอหลัก (PrimaryScreen) จอเดียวคูณกับทุกจอเสมอ เพราะแอปนี้ไม่ได้ประกาศ per-monitor DPI
    /// awareness ไว้ใน manifest (Windows เลย apply DPI scale เดียวทั้งแอปตามจอหลัก - "system DPI aware"
    /// ธรรมดา) WindowState=Maximized (ตั้งไว้ใน XAML อยู่แล้ว) จะเติมเต็มจอเป้าหมายให้พอดีเองจาก Left/Top ที่
    /// ตั้งไว้นี้ (Windows ตัดสินว่า maximize ไปจอไหนจาก Left/Top ตอนสร้าง native window จริง ไม่ใช่จอหลัก
    /// เสมอไป) - พึ่งพฤติกรรม maximize-to-monitor ของ Windows เองแทนคำนวณ Width/Height เป๊ะๆ เอง แม่นกว่า
    /// </summary>
    private void PositionToScreen(System.Windows.Forms.Screen screen)
    {
        var primaryBoundsPx = System.Windows.Forms.Screen.PrimaryScreen!.Bounds;
        var dipScaleX = SystemParameters.PrimaryScreenWidth / primaryBoundsPx.Width;
        var dipScaleY = SystemParameters.PrimaryScreenHeight / primaryBoundsPx.Height;

        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = screen.Bounds.Left * dipScaleX;
        Top = screen.Bounds.Top * dipScaleY;
        Width = screen.Bounds.Width * dipScaleX;
        Height = screen.Bounds.Height * dipScaleY;
    }

    /// <summary>เรียกจาก App.xaml.cs เท่านั้น (HideLockScreen ปกติ หรือ AgentDiedWhileLocked จาก instance เจ้าของ watchdog) - ทางเดียวที่ปิดหน้าต่างนี้ได้จริง</summary>
    public void ForceClose()
    {
        _allowClose = true;

        if (_installInputBlocking)
        {
            UninstallInputBlocking();
        }

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

    // ==================== Keyboard hook ====================

    private void InstallKeyboardHook()
    {
        if (_keyboardHookId != IntPtr.Zero)
        {
            return; // ติดตั้งไปแล้ว
        }

        _keyboardHookProc = KeyboardHookCallback;
        using var curProcess = Process.GetCurrentProcess();
        using var curModule = curProcess.MainModule!;
        _keyboardHookId = SetWindowsHookEx(WH_KEYBOARD_LL, _keyboardHookProc, GetModuleHandle(curModule.ModuleName), 0);
    }

    /// <summary>
    /// บล็อกทุกคีย์ไม่มีข้อยกเว้นเลย (ไม่ใช่แค่ denylist ของคีย์ผสมหนีบางตัว) - ปลดล็อกได้ทางเดียวคือ Console
    /// สั่งผ่าน ForceClose() ซึ่งไม่ได้พึ่ง keyboard escape ใดๆ อยู่แล้วตั้งแต่ต้น จึงไม่จำเป็นต้อง whitelist
    /// คีย์ไหนไว้เลยแม้แต่ตัวเดียว - คีย์บอร์ดใน Windows route ตาม keyboard focus (property ของ HWND ที่
    /// focus อยู่) ไม่ใช่ตาม z-order ที่มองเห็น ดังนั้นถ้า focus หลุดจาก lock screen ไปที่หน้าต่างอื่นโดย
    /// ครูไม่รู้ตัว (เช่น Activate() ที่เรียกจาก background thread โดน Windows' anti-focus-stealing
    /// protection ปฏิเสธเงียบๆ) การพิมพ์ตัวอักษรธรรมดาจะไม่ถูกกันเลยถ้าไม่มี hook แบบนี้ - ต้องกันที่ระดับ
    /// นี้ (ก่อนถึง window ไหนทั้งสิ้น) ไม่ใช่พึ่งแค่ focus management ฝั่ง WPF (ดู Deactivated handler ใน
    /// constructor ที่เป็นแค่ secondary layer เสริม ไม่ใช่ตัวกันหลัก)
    ///
    /// Ctrl-Alt-Del ยังกันไม่ได้เหมือนเดิมตามดีไซน์ของ Windows เอง (Secure Attention Sequence รับประกันโดย
    /// OS ไม่มี user-mode hook ตัวไหนดักได้เลยไม่ว่าจะเขียนโค้ดแบบไหน) - เป็นทางออกฉุกเฉินสุดท้ายที่เหลืออยู่
    /// เสมอ (เปิด Task Manager ไป kill AgentTray.exe ได้ ถ้าทุกกลไกอื่นพังหมด)
    /// </summary>
    private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            return (IntPtr)1;
        }

        return CallNextHookEx(_keyboardHookId, nCode, wParam, lParam);
    }

    // ==================== Mouse hook ====================

    private void InstallMouseHook()
    {
        if (_mouseHookId != IntPtr.Zero)
        {
            return; // ติดตั้งไปแล้ว
        }

        _mouseHookProc = MouseHookCallback;
        using var curProcess = Process.GetCurrentProcess();
        using var curModule = curProcess.MainModule!;
        _mouseHookId = SetWindowsHookEx(WH_MOUSE_LL, _mouseHookProc, GetModuleHandle(curModule.ModuleName), 0);
    }

    /// <summary>
    /// บล็อกทุก mouse event ไม่มีข้อยกเว้น (move/click ซ้าย-กลาง-ขวา/wheel) - ก่อนหน้านี้ไม่มีกลไกกันเมาส์
    /// เลยสักบรรทัด (พึ่ง Topmost บังหน้าจอไว้เฉยๆ ซึ่งพังได้ถ้ามีจอที่สอง หรือหน้าต่างอื่น set ตัวเองเป็น
    /// topmost ทับ) หมายเหตุผลข้างเคียงที่ตั้งใจ (ไม่ใช่บั๊ก): บล็อก WM_MOUSEMOVE ด้วยทำให้ cursor ค้างนิ่งอยู่
    /// กับที่ตลอดช่วงล็อก (verify แล้วจาก Microsoft doc - การ block ที่ระดับนี้กันการอัพเดตตำแหน่ง cursor ใน
    /// ตัวเลย ไม่ใช่แค่กันไม่ให้หน้าต่างอื่นเห็น event) ตรงกับพฤติกรรมของ kiosk-mode screen locker ทั่วไป
    /// </summary>
    private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            return (IntPtr)1;
        }

        return CallNextHookEx(_mouseHookId, nCode, wParam, lParam);
    }

    /// <summary>ต้อง unhook ทั้งคู่ทันทีตอนปิดหน้าจอล็อก - ไม่ปล่อยค้าง ไม่งั้นจะกระทบการใช้งานปกติหลังปลดล็อก</summary>
    private void UninstallInputBlocking()
    {
        if (_keyboardHookId != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_keyboardHookId);
            _keyboardHookId = IntPtr.Zero;
        }
        _keyboardHookProc = null;

        if (_mouseHookId != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_mouseHookId);
            _mouseHookId = IntPtr.Zero;
        }
        _mouseHookProc = null;

        StopAgentWatchdog();
    }

    // ==================== Agent watchdog (failsafe) ====================

    private void StartAgentWatchdog()
    {
        _agentWatchdogTimer = new DispatcherTimer { Interval = AgentWatchdogInterval };
        _agentWatchdogTimer.Tick += (_, _) => CheckAgentHealth();
        _agentWatchdogTimer.Start();
    }

    private void StopAgentWatchdog()
    {
        _agentWatchdogTimer?.Stop();
        _agentWatchdogTimer = null;
    }

    /// <summary>
    /// เช็คทุก 3 วิระหว่างล็อกอยู่ว่า process ของ Agent (Windows Service) ยังรันอยู่ไหม - ใช้
    /// Process.GetProcessesByName ตรงๆ (ไม่ใช้ ServiceController เพราะไม่ต้องพึ่งสิทธิ์ SCM ใช้งานได้แม้ตอน
    /// dev รัน Agent เป็น console app ธรรมดาไม่ได้ลงเป็น Windows Service จริง) ถ้า process หายไปกลางคัน
    /// (crash/ถูก kill) แปลว่าไม่มีทางใดเหลือให้ Console สั่งปลดล็อกเครื่องนี้ได้อีกแล้ว (Agent เป็นตัวกลาง
    /// เดียวที่ relay "HideLockScreen" มาให้ AgentTray ผ่าน named pipe - ดู BroadcastPipeListener) ปล่อยให้
    /// ล็อกค้างตลอดไปอันตรายกว่าการปลดล็อกฉุกเฉินเอง (เครื่องใช้งานไม่ได้เลยจนกว่าจะมีคนไปแตะเครื่องจริง) จึง
    /// เลือก self-unlock ทันทีที่เจอครั้งแรก ไม่หน่วง/debounce หลายรอบ - ไม่มี false-positive จากช่วง "Agent
    /// ยังไม่ทันสตาร์ท" ให้ต้องกันไว้ เพราะ ณ จังหวะที่เริ่มล็อกได้ Agent ต้องเพิ่งพิสูจน์ตัวเองว่ายังมีชีวิตอยู่
    /// แล้ว (เป็นคนส่งคำสั่ง ShowLockScreen มาเองผ่าน pipe เดียวกันนี้)
    ///
    /// **ข้อจำกัดที่ยังเหลืออยู่ (รายงานตรงๆ ไม่ปิดบัง)**: watchdog นี้แก้ได้เฉพาะกรณี "Agent ตายแต่ AgentTray
    /// ยังรันอยู่" เท่านั้น ถ้า AgentTray เอง (process ที่ถือ hook อยู่) ตายกลางคัน โค้ดในไฟล์นี้ไม่มีทางทำงาน
    /// ได้อีกต่อไป (ตายไปพร้อมกัน) ต้องพึ่ง Windows เองเท่านั้น - low-level hook (WH_KEYBOARD_LL/WH_MOUSE_LL)
    /// ที่ owning process ตายไปจะถูก Windows unhook อัตโนมัติหลัง timeout (LowLevelHooksTimeout, default
    /// ~300ms/ครั้ง มีจำนวนครั้งจำกัดก่อนจะยอมปล่อยเลิก - verify จาก Microsoft doc) แปลว่าเมาส์/คีย์บอร์ดจะ
    /// "ค้างชั่วคราว" ระดับวินาทีก่อนกลับมาใช้งานได้เอง ไม่ใช่ค้างถาวร แต่ก็ไม่ใช่ instant เหมือน path ของ
    /// watchdog นี้ (ที่เรียก UnhookWindowsHookEx เองตรงๆ ทันที) - เป็น known limitation ที่แก้เพิ่มไม่ได้
    /// จากโค้ดฝั่งเดียวกันที่กำลังตายอยู่พอดี
    /// </summary>
    private void CheckAgentHealth()
    {
        Process[] processes;
        try
        {
            processes = Process.GetProcessesByName(AgentProcessName);
        }
        catch
        {
            return; // เช็คไม่ได้ชั่วคราว - รอรอบถัดไป ไม่ถือว่า Agent ตายจากความล้มเหลวของการเช็คเอง
        }

        var agentAlive = processes.Length > 0;
        foreach (var process in processes)
        {
            process.Dispose();
        }

        if (!agentAlive)
        {
            StopAgentWatchdog(); // กันยิงซ้ำระหว่างรอ caller เรียก ForceClose()
            AgentDiedWhileLocked?.Invoke();
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelHookProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);
}
