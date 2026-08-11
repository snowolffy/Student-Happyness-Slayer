using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace OnionProcOparetor.AgentTray.Services;

/// <summary>
/// บล็อกเฉพาะ physical keyboard/mouse ของนักเรียน ระหว่างครูควบคุมเครื่องผ่าน full-screen VNC (Step 4h)
/// - กันนักเรียนแย่งเมาส์/คีย์บอร์ดตอนครูกำลังช่วย โดยไม่บล็อก input ที่ครูส่งมาผ่าน VNC (winvnc.exe
/// เอง synthesize input เข้าเครื่องผ่าน SendInput()/keybd_event()/mouse_event() เหมือนกันหมด - verify
/// จาก UltraVNC source code แล้ว) ใช้ hook เดียวกับ LockScreenWindow (WH_KEYBOARD_LL/WH_MOUSE_LL) แต่
/// filter เพิ่มด้วย LLKHF_INJECTED/LLMHF_INJECTED flag: event ที่มี flag นี้ (มาจาก SendInput-family)
/// ปล่อยผ่าน, event ที่ไม่มี (physical จริง) บล็อก - นี่คือความต่างหลักจาก LockScreenWindow ที่บล็อกทุก
/// อย่างแบบไม่มีข้อยกเว้นเลย (ล็อกเต็มจอไม่ต้องแยก เพราะไม่มี VNC ควบคุมพร้อมกันอยู่แล้วตอนนั้น)
///
/// ต้องรันจาก AgentTray (user session จริง) เหมือนกับเหตุผลเดียวกันที่ VNC screen-capture และ
/// LockScreenWindow's hook ต้องอยู่ที่นี่ - Agent (Windows Service, Session 0) ถูก Session 0 Isolation
/// กันไม่ให้ install low-level hook ที่เห็น input ของ user session ได้เลย
///
/// Failsafe 2 ชั้น (ทั้งคู่จำเป็น ไม่ใช่สำรองของกันและกัน):
/// - Path 1 (หลัก): App.xaml.cs subscribe VncProcessManager.Stopped -> Unblock() ทันทีที่ winvnc.exe
///   หยุดไม่ว่าเพราะอะไร (ครูกด BACK TO GRID, Console หลุด, winvnc.exe crash เอง)
/// - Path 2 (สำรอง): watchdog ในตัวเอง เช็คว่า Agent process ยังรันอยู่ไหมทุก 3 วิ (เหมือน
///   LockScreenWindow.CheckAgentHealth เป๊ะ) จำเป็นเพราะกรณี "Agent process/network หลุดแต่ winvnc.exe
///   เอง (spawn โดย AgentTray ไม่ใช่ Agent) ยังรันอยู่ปกติ" ไม่ทำให้ Path 1 ทำงาน (VncProcessManager
///   ไม่รู้ว่า Agent หลุดไปแล้ว - มันแค่ดูแล lifecycle ของ winvnc.exe process เท่านั้น)
/// </summary>
public sealed class PhysicalInputBlocker
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WH_MOUSE_LL = 14;

    // Diagnostic log แยกต่างหาก (ไม่ผ่าน ILogger เพราะ AgentTray ไม่มี logging infrastructure เดิมเลย -
    // BroadcastPipeListener ใช้ Debug.WriteLine ซึ่งมองไม่เห็นถ้าไม่มี debugger attach อยู่) ใช้
    // %LocalAppData% แทน %ProgramData% เพราะ AgentTray รันเป็น user ธรรมดา (ไม่ elevated) - ProgramData
    // อาจเขียนไม่ได้ถ้า ACL ถูกจำกัดไว้ (เจอปัญหาคล้ายกันฝั่ง Agent's HKLM registry access) เก็บไว้ log
    // การตัดสินใจ block/pass ของ N event แรกหลัง Block() แต่ละครั้งเท่านั้น (ไม่ log ทุก event เพราะจะรัว
    // เกินไปตอนขยับเมาส์) พอสำหรับ diagnose ว่า hook ติดตั้งสำเร็จไหม + flags ที่อ่านได้จริงเป็นอย่างไร
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "OnionProcOparetor", "AgentTray", "logs", "input-blocker.log");

    private const int MaxDiagnosticEventsPerBlockSession = 20;
    private int _diagnosticEventsLogged;

    private static void Log(string message)
    {
        try
        {
            var dir = Path.GetDirectoryName(LogPath);
            if (dir is not null && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            File.AppendAllText(LogPath, $"{DateTime.Now:HH:mm:ss.fff} {message}\r\n");
        }
        catch
        {
            // diagnostic logging เองพังไม่ควรทำให้ hook พังตาม - ปล่อยผ่านเงียบๆ
        }
    }

    /// <summary>bit 4 ของ KBDLLHOOKSTRUCT.flags - set เมื่อ keyboard event มาจาก SendInput()/keybd_event() ไม่ใช่ฮาร์ดแวร์จริง</summary>
    private const uint LLKHF_INJECTED = 0x00000010;

    /// <summary>bit 0 ของ MSLLHOOKSTRUCT.flags - set เมื่อ mouse event มาจาก SendInput()/mouse_event() ไม่ใช่ฮาร์ดแวร์จริง</summary>
    private const uint LLMHF_INJECTED = 0x00000001;

    /// <summary>ชื่อ process ของ OnionProcOparetor.Agent (ไม่รวม .exe) - ต้องตรงกับ AssemblyName จริงเป๊ะๆ (เหมือน LockScreenWindow)</summary>
    private const string AgentProcessName = "OnionProcOparetor.Agent";

    private static readonly TimeSpan AgentWatchdogInterval = TimeSpan.FromSeconds(3);

    private delegate IntPtr LowLevelHookProc(int nCode, IntPtr wParam, IntPtr lParam);

    // ต้องเก็บ delegate ไว้เป็น field กัน GC เก็บทิ้งระหว่างที่ hook ยังติดตั้งอยู่ (เหตุผลเดียวกับ LockScreenWindow)
    private LowLevelHookProc? _keyboardHookProc;
    private IntPtr _keyboardHookId = IntPtr.Zero;

    private LowLevelHookProc? _mouseHookProc;
    private IntPtr _mouseHookId = IntPtr.Zero;

    private DispatcherTimer? _agentWatchdogTimer;

    public bool IsBlocking { get; private set; }

    /// <summary>
    /// ยิงตอน watchdog ตรวจพบว่า process ของ Agent หายไปกลางคันขณะกำลังบล็อกอยู่ - เรียกจาก UI thread
    /// เดียวกับ Dispatcher ของ DispatcherTimer เสมอ (DispatcherTimer.Tick การันตีเรื่องนี้) ผู้ subscribe
    /// (App.xaml.cs) ไม่ต้อง Dispatcher.Invoke ซ้ำอีกชั้น
    /// </summary>
    public event Action? AgentDiedWhileBlocked;

    /// <summary>เรียกจาก UI thread เท่านั้น (DispatcherTimer ข้างในต้องสร้าง/stop จาก thread เดียวกัน) - idempotent</summary>
    public void Block()
    {
        Log("Block() called");
        if (IsBlocking)
        {
            Log("Block() no-op - already blocking");
            return;
        }

        _diagnosticEventsLogged = 0;
        InstallKeyboardHook();
        InstallMouseHook();
        StartAgentWatchdog();
        IsBlocking = true;
        Log($"Block() done - keyboardHookId={_keyboardHookId} mouseHookId={_mouseHookId} IsBlocking={IsBlocking}");
    }

    /// <summary>เรียกจาก UI thread เท่านั้น (เหตุผลเดียวกับ Block()) - idempotent, ปลอดภัยเรียกซ้ำได้แม้ไม่ได้ block อยู่</summary>
    public void Unblock()
    {
        Log("Unblock() called");
        if (!IsBlocking)
        {
            Log("Unblock() no-op - not blocking");
            return;
        }

        UninstallHooks();
        StopAgentWatchdog();
        IsBlocking = false;
        Log("Unblock() done");
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

        if (_keyboardHookId == IntPtr.Zero)
        {
            Log($"InstallKeyboardHook FAILED - SetWindowsHookEx returned 0, Win32Error={Marshal.GetLastWin32Error()}");
        }
    }

    /// <summary>
    /// บล็อกเฉพาะ key ที่ไม่มี LLKHF_INJECTED flag (physical จริง) - event ที่มี flag (มาจาก winvnc.exe
    /// synthesize ให้ตอนครูพิมพ์ผ่าน VNC) ปล่อยผ่านไปตามปกติผ่าน CallNextHookEx
    /// </summary>
    private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var hookStruct = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            var isInjected = (hookStruct.flags & LLKHF_INJECTED) != 0;

            if (_diagnosticEventsLogged < MaxDiagnosticEventsPerBlockSession)
            {
                _diagnosticEventsLogged++;
                Log($"KeyboardHookCallback vkCode={hookStruct.vkCode} flags=0x{hookStruct.flags:X} isInjected={isInjected} -> {(isInjected ? "PASS THROUGH" : "BLOCKED")}");
            }

            if (!isInjected)
            {
                return (IntPtr)1;
            }
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

        if (_mouseHookId == IntPtr.Zero)
        {
            Log($"InstallMouseHook FAILED - SetWindowsHookEx returned 0, Win32Error={Marshal.GetLastWin32Error()}");
        }
    }

    /// <summary>
    /// บล็อกเฉพาะ mouse event ที่ไม่มี LLMHF_INJECTED flag (physical จริง) - event ที่มี flag (มาจาก
    /// winvnc.exe synthesize ให้ตอนครูคลิก/ขยับเมาส์ผ่าน VNC) ปล่อยผ่านไปตามปกติผ่าน CallNextHookEx
    /// </summary>
    private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var hookStruct = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
            var isInjected = (hookStruct.flags & LLMHF_INJECTED) != 0;

            if (_diagnosticEventsLogged < MaxDiagnosticEventsPerBlockSession)
            {
                _diagnosticEventsLogged++;
                Log($"MouseHookCallback wParam={wParam} flags=0x{hookStruct.flags:X} isInjected={isInjected} -> {(isInjected ? "PASS THROUGH" : "BLOCKED")}");
            }

            if (!isInjected)
            {
                return (IntPtr)1;
            }
        }

        return CallNextHookEx(_mouseHookId, nCode, wParam, lParam);
    }

    /// <summary>ต้อง unhook ทั้งคู่ทันทีตอน Unblock() - ไม่ปล่อยค้าง ไม่งั้นจะกระทบการใช้งานปกติหลังคืนสิทธิ์</summary>
    private void UninstallHooks()
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
    }

    // ==================== Agent watchdog (failsafe path 2) ====================

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
    /// เช็คทุก 3 วิระหว่างบล็อกอยู่ว่า process ของ Agent ยังรันอยู่ไหม (pattern เดียวกับ
    /// LockScreenWindow.CheckAgentHealth เป๊ะ - ดู comment เต็มที่นั่น) จำเป็นเป็น failsafe แยกจาก
    /// VncProcessManager.Stopped เพราะ "Agent ตายแต่ winvnc.exe ยังรันอยู่" ไม่ทำให้ Stopped ยิงเลย -
    /// ถ้าปล่อยไว้นักเรียนจะโดนบล็อก input ค้างตลอดไปโดยไม่มีทางให้ Console สั่งปลดอีกต่อไป (Agent เป็น
    /// ตัวกลางเดียวที่ relay UnblockTargetInput มาให้ - ดู BroadcastPipeListener) เลือก self-unblock
    /// ทันทีที่เจอครั้งแรก เหมือน LockScreenWindow
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
            StopAgentWatchdog(); // กันยิงซ้ำระหว่างรอ caller เรียก Unblock()
            AgentDiedWhileBlocked?.Invoke();
        }
    }

    // ==================== Win32 interop ====================

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int x;
        public int y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
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
