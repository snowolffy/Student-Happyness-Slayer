using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace OnionProcOparetor.Agent.Services;

/// <summary>
/// สตาร์ท process ใน "interactive user session" จาก Windows Service ที่รันอยู่ใน Session 0
///
/// ทำไมต้องมีทั้งไฟล์นี้แทนที่จะเรียก Process.Start() เฉยๆ: Session 0 ถูก isolate ออกจาก
/// session ของ user ตั้งแต่ Windows Vista เป็นต้นมา - process ที่ service สตาร์ทด้วย
/// Process.Start() จะไปเกิดใน Session 0 ด้วย ซึ่ง "ไม่มี desktop" ให้แสดง UI เลย
/// AgentTray ที่เกิดแบบนั้นจะรันอยู่จริงแต่มองไม่เห็น ไม่มี tray icon ไม่มีหน้าต่างใดๆ และ
/// named pipe ที่มันเปิดก็จะไม่ใช่ตัวที่ user เห็นผลอยู่ดี = ไม่ได้แก้ปัญหาอะไรเลย
///
/// ลำดับที่ถูกต้องคือ: หา session ที่ user login อยู่ → ขอ token ของ user นั้น → duplicate เป็น
/// primary token → สร้าง environment block ของ user นั้น → CreateProcessAsUser ลงไปที่
/// "winsta0\default" (desktop จริงของ user)
///
/// **ต้องรันเป็น LocalSystem เท่านั้น** - WTSQueryUserToken ต้องการสิทธิ์ SE_TCB_NAME ซึ่งมีแต่
/// LocalSystem (Agent ติดตั้งเป็น service ด้วย sc.exe โดยไม่ระบุ account = LocalSystem อยู่แล้ว)
/// ถ้ารัน Agent เป็น user ธรรมดาตอน dev ฟังก์ชันนี้จะ fail ที่ WTSQueryUserToken เสมอ ซึ่ง
/// จัดการไว้แล้ว (log แล้วคืน false ไม่ throw)
/// </summary>
public class UserSessionLauncher
{
    private readonly ILogger<UserSessionLauncher> _logger;

    public UserSessionLauncher(ILogger<UserSessionLauncher> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// สตาร์ท exe ที่ระบุใน session ของ user ที่กำลัง active อยู่
    /// คืน true ถ้าสร้าง process สำเร็จ - ไม่ throw ไม่ว่ากรณีใด (ผู้เรียกเป็น safety net
    /// ไม่ควรพังเพราะ recover ไม่สำเร็จ)
    /// </summary>
    public bool TryLaunchInActiveUserSession(string executablePath)
    {
        try
        {
            return LaunchCore(executablePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "เกิดข้อผิดพลาดที่ไม่คาดคิดตอนพยายามสตาร์ท '{ExePath}' ใน user session", executablePath);
            return false;
        }
    }

    private bool LaunchCore(string executablePath)
    {
        if (!File.Exists(executablePath))
        {
            _logger.LogError("ไม่พบไฟล์ '{ExePath}' - สตาร์ทใน user session ไม่ได้", executablePath);
            return false;
        }

        var sessionId = ResolveActiveSessionId();
        if (sessionId is null)
        {
            _logger.LogWarning(
                "ไม่พบ user session ที่ active อยู่บนเครื่องนี้ (อาจไม่มีใคร login อยู่หน้าเครื่องเลย) - ข้ามการสตาร์ท AgentTray");
            return false;
        }

        var userToken = IntPtr.Zero;
        var duplicatedToken = IntPtr.Zero;
        var environmentBlock = IntPtr.Zero;

        try
        {
            if (!WTSQueryUserToken(sessionId.Value, out userToken))
            {
                var error = Marshal.GetLastWin32Error();
                _logger.LogError(
                    "WTSQueryUserToken(session {SessionId}) ล้มเหลว (error {Error}: {Message}) - Agent ต้องรันเป็น LocalSystem เท่านั้นถึงจะมีสิทธิ์ SE_TCB_NAME",
                    sessionId.Value, error, new Win32Exception(error).Message);
                return false;
            }

            // token ที่ได้จาก WTSQueryUserToken เป็น "impersonation token" ใช้สร้าง process ตรงๆ ไม่ได้
            // ต้อง duplicate เป็น "primary token" ก่อนเสมอ (TokenType = TokenPrimary)
            var securityAttributes = new SECURITY_ATTRIBUTES();
            securityAttributes.nLength = Marshal.SizeOf(securityAttributes);

            if (!DuplicateTokenEx(
                    userToken,
                    MAXIMUM_ALLOWED,
                    ref securityAttributes,
                    SecurityImpersonationLevel.SecurityIdentification,
                    TokenType.TokenPrimary,
                    out duplicatedToken))
            {
                var error = Marshal.GetLastWin32Error();
                _logger.LogError("DuplicateTokenEx ล้มเหลว (error {Error}: {Message})", error, new Win32Exception(error).Message);
                return false;
            }

            // environment block ของ user นั้นจริงๆ (%APPDATA%, %USERPROFILE% ฯลฯ) - ถ้าไม่ส่งไป
            // process ใหม่จะได้ environment ของ LocalSystem ซึ่งชี้ path ผิดหมด
            // ไม่ใช่ตัวตัดสินความสำเร็จ - ถ้าสร้างไม่ได้ก็ยังสตาร์ทต่อได้ (ส่ง null แทน)
            if (!CreateEnvironmentBlock(out environmentBlock, duplicatedToken, false))
            {
                _logger.LogWarning(
                    "CreateEnvironmentBlock ล้มเหลว (error {Error}) - จะสตาร์ทต่อโดยไม่มี environment block ของ user",
                    Marshal.GetLastWin32Error());
                environmentBlock = IntPtr.Zero;
            }

            var startupInfo = new STARTUPINFO();
            startupInfo.cb = Marshal.SizeOf(startupInfo);

            // สำคัญมาก: ต้องระบุ desktop ให้ชัดเจน ไม่งั้น process จะไม่มี desktop ให้วาด UI
            // "winsta0\default" คือ interactive desktop จริงที่ user มองเห็นอยู่
            startupInfo.lpDesktop = @"winsta0\default";

            var workingDirectory = Path.GetDirectoryName(executablePath);

            // CreateProcessAsUser แก้ไข command line buffer ที่ส่งเข้าไปได้ (documented behaviour)
            // เลยต้องส่งเป็น StringBuilder/ตัวแปรที่เขียนทับได้ ไม่ใช่ string literal
            var commandLine = new System.Text.StringBuilder($"\"{executablePath}\"");

            var created = CreateProcessAsUser(
                duplicatedToken,
                null,
                commandLine,
                IntPtr.Zero,
                IntPtr.Zero,
                false,
                CREATE_UNICODE_ENVIRONMENT | CREATE_NEW_CONSOLE,
                environmentBlock,
                workingDirectory,
                ref startupInfo,
                out var processInfo);

            if (!created)
            {
                var error = Marshal.GetLastWin32Error();
                _logger.LogError(
                    "CreateProcessAsUser('{ExePath}', session {SessionId}) ล้มเหลว (error {Error}: {Message})",
                    executablePath, sessionId.Value, error, new Win32Exception(error).Message);
                return false;
            }

            _logger.LogInformation(
                "สตาร์ท '{ExePath}' ใน user session {SessionId} สำเร็จ (PID {ProcessId})",
                executablePath, sessionId.Value, processInfo.dwProcessId);

            // handle ของ process/thread ที่ได้มาต้องปิดเสมอ - ไม่ปิดจะ leak handle ทุกครั้งที่
            // recover (process ใหม่ยังรันต่อตามปกติ การปิด handle ไม่ได้ฆ่ามัน)
            CloseHandleSafe(processInfo.hProcess);
            CloseHandleSafe(processInfo.hThread);

            return true;
        }
        finally
        {
            if (environmentBlock != IntPtr.Zero)
            {
                DestroyEnvironmentBlock(environmentBlock);
            }

            CloseHandleSafe(duplicatedToken);
            CloseHandleSafe(userToken);
        }
    }

    /// <summary>
    /// หา session ที่ user login อยู่จริง - ลอง console session (จอที่ต่อกับเครื่องจริง) ก่อน
    /// เพราะเป็นเคสปกติของเครื่อง lab แล้วค่อย fallback ไปไล่ดูทุก session ที่ WTSActive
    /// (เผื่อเครื่องถูกใช้ผ่าน Remote Desktop ซึ่ง console session จะว่างเปล่า)
    /// </summary>
    private uint? ResolveActiveSessionId()
    {
        var consoleSessionId = WTSGetActiveConsoleSessionId();

        // 0xFFFFFFFF = ไม่มี console session อยู่ตอนนี้ (เช่นระหว่างสลับ session)
        if (consoleSessionId != 0xFFFFFFFF && HasLoggedOnUser(consoleSessionId))
        {
            return consoleSessionId;
        }

        _logger.LogDebug(
            "console session ({ConsoleSessionId}) ไม่มี user login อยู่ - ไล่หา session อื่นที่ active แทน",
            consoleSessionId);

        return FindFirstActiveSession();
    }

    /// <summary>
    /// คืน profile directory (เช่น C:\Users\student12) ของ user ที่ login อยู่หน้าเครื่องตอนนี้ (WP2-4,
    /// 2026-10-31) - ใช้หา %LOCALAPPDATA% ของ user นั้นเพื่ออ่านไฟล์ History ของ Chrome/Edge (ดู
    /// Services/WebHistoryCollectionService.cs) คนละเหตุผลกับ TryLaunchInActiveUserSession แต่ใช้
    /// ResolveActiveSessionId/WTSQueryUserToken ตัวเดียวกัน (ต้องรันเป็น LocalSystem เหมือนกัน ดู comment
    /// หัวไฟล์) คืน null ถ้าไม่มีใคร login อยู่/หาไม่ได้ - ไม่ throw
    /// </summary>
    public string? TryGetActiveUserProfileDirectory()
    {
        var sessionId = ResolveActiveSessionId();
        if (sessionId is null)
        {
            return null;
        }

        if (!WTSQueryUserToken(sessionId.Value, out var userToken))
        {
            return null;
        }

        try
        {
            // ตาม MSDN: เรียกครั้งแรกด้วย buffer ที่พอประมาณ ถ้าไม่พอ lpcchSize จะถูกเขียนเป็นขนาดที่
            // ต้องการจริง แล้วลองอีกครั้งด้วยขนาดนั้น (path จริงไม่ค่อยยาวเกิน 260 แต่เผื่อไว้)
            var capacity = 260;
            var buffer = new System.Text.StringBuilder(capacity);
            if (!GetUserProfileDirectory(userToken, buffer, ref capacity))
            {
                buffer = new System.Text.StringBuilder(capacity);
                if (!GetUserProfileDirectory(userToken, buffer, ref capacity))
                {
                    _logger.LogWarning("GetUserProfileDirectory ล้มเหลว (error {Error})", Marshal.GetLastWin32Error());
                    return null;
                }
            }

            return buffer.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "เกิดข้อผิดพลาดที่ไม่คาดคิดตอนอ่าน user profile directory");
            return null;
        }
        finally
        {
            CloseHandleSafe(userToken);
        }
    }

    /// <summary>เช็คว่า session นี้มี user login อยู่จริงไหม โดยลองขอ token ดู แล้วปิดทิ้งทันที</summary>
    private static bool HasLoggedOnUser(uint sessionId)
    {
        if (!WTSQueryUserToken(sessionId, out var token))
        {
            return false;
        }

        CloseHandleSafe(token);
        return true;
    }

    private uint? FindFirstActiveSession()
    {
        var sessionInfoPtr = IntPtr.Zero;

        try
        {
            if (!WTSEnumerateSessions(WTS_CURRENT_SERVER_HANDLE, 0, 1, ref sessionInfoPtr, out var sessionCount))
            {
                _logger.LogWarning("WTSEnumerateSessions ล้มเหลว (error {Error})", Marshal.GetLastWin32Error());
                return null;
            }

            var structSize = Marshal.SizeOf<WTS_SESSION_INFO>();

            for (var i = 0; i < sessionCount; i++)
            {
                var current = sessionInfoPtr + (i * structSize);
                var sessionInfo = Marshal.PtrToStructure<WTS_SESSION_INFO>(current);

                if (sessionInfo.State != WTS_CONNECTSTATE_CLASS.WTSActive)
                {
                    continue;
                }

                if (HasLoggedOnUser(sessionInfo.SessionID))
                {
                    return sessionInfo.SessionID;
                }
            }

            return null;
        }
        finally
        {
            if (sessionInfoPtr != IntPtr.Zero)
            {
                WTSFreeMemory(sessionInfoPtr);
            }
        }
    }

    private static void CloseHandleSafe(IntPtr handle)
    {
        if (handle != IntPtr.Zero)
        {
            CloseHandle(handle);
        }
    }

    // ---- Win32 constants ----

    private const uint MAXIMUM_ALLOWED = 0x2000000;
    private const uint CREATE_UNICODE_ENVIRONMENT = 0x00000400;
    private const uint CREATE_NEW_CONSOLE = 0x00000010;
    private static readonly IntPtr WTS_CURRENT_SERVER_HANDLE = IntPtr.Zero;

    // ---- Win32 structs / enums ----

    [StructLayout(LayoutKind.Sequential)]
    private struct SECURITY_ATTRIBUTES
    {
        public int nLength;
        public IntPtr lpSecurityDescriptor;
        public bool bInheritHandle;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct STARTUPINFO
    {
        public int cb;
        public string? lpReserved;
        public string? lpDesktop;
        public string? lpTitle;
        public uint dwX;
        public uint dwY;
        public uint dwXSize;
        public uint dwYSize;
        public uint dwXCountChars;
        public uint dwYCountChars;
        public uint dwFillAttribute;
        public uint dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public uint dwProcessId;
        public uint dwThreadId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WTS_SESSION_INFO
    {
        public uint SessionID;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string pWinStationName;

        public WTS_CONNECTSTATE_CLASS State;
    }

    private enum WTS_CONNECTSTATE_CLASS
    {
        WTSActive,
        WTSConnected,
        WTSConnectQuery,
        WTSShadow,
        WTSDisconnected,
        WTSIdle,
        WTSListen,
        WTSReset,
        WTSDown,
        WTSInit
    }

    private enum SecurityImpersonationLevel
    {
        SecurityAnonymous,
        SecurityIdentification,
        SecurityImpersonation,
        SecurityDelegation
    }

    private enum TokenType
    {
        TokenPrimary = 1,
        TokenImpersonation
    }

    // ---- P/Invoke ----

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WTSGetActiveConsoleSessionId();

    [DllImport("wtsapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WTSQueryUserToken(uint sessionId, out IntPtr phToken);

    [DllImport("wtsapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WTSEnumerateSessions(
        IntPtr hServer, int reserved, int version, ref IntPtr ppSessionInfo, out int pCount);

    [DllImport("wtsapi32.dll")]
    private static extern void WTSFreeMemory(IntPtr pMemory);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DuplicateTokenEx(
        IntPtr hExistingToken,
        uint dwDesiredAccess,
        ref SECURITY_ATTRIBUTES lpTokenAttributes,
        SecurityImpersonationLevel impersonationLevel,
        TokenType tokenType,
        out IntPtr phNewToken);

    [DllImport("userenv.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateEnvironmentBlock(out IntPtr lpEnvironment, IntPtr hToken, bool bInherit);

    [DllImport("userenv.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyEnvironmentBlock(IntPtr lpEnvironment);

    [DllImport("userenv.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetUserProfileDirectory(IntPtr hToken, System.Text.StringBuilder lpProfileDir, ref int lpcchSize);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateProcessAsUser(
        IntPtr hToken,
        string? lpApplicationName,
        System.Text.StringBuilder lpCommandLine,
        IntPtr lpProcessAttributes,
        IntPtr lpThreadAttributes,
        bool bInheritHandles,
        uint dwCreationFlags,
        IntPtr lpEnvironment,
        string? lpCurrentDirectory,
        ref STARTUPINFO lpStartupInfo,
        out PROCESS_INFORMATION lpProcessInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);
}
