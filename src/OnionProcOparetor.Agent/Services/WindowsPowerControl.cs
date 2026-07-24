using System.Runtime.InteropServices;

namespace OnionProcOparetor.Agent.Services;

/// <summary>
/// สั่ง shutdown/restart เครื่องจริงผ่าน ExitWindowsEx (user32.dll) - เรียกจาก Agent (Session 0)
/// ได้ตรงๆ เพราะเป็น system-level operation (ต่างจาก LockWorkstation ที่ต้องผ่าน AgentTray เพราะ
/// เป็น per-session operation - ดู AgentTrayNotifier.SendLockWorkstationAsync)
///
/// ต้องเปิด SE_SHUTDOWN_NAME privilege ก่อนเรียก ExitWindowsEx เสมอ - Windows Service โดย
/// default ไม่มี privilege นี้ enable อยู่ (แม้จะรันเป็น LocalSystem ก็ตาม) ถ้าไม่เปิดก่อน
/// ExitWindowsEx จะ fail เงียบๆ (คืน false, GetLastError = ERROR_PRIVILEGE_NOT_HELD)
/// </summary>
internal static class WindowsPowerControl
{
    private const uint EWX_SHUTDOWN = 0x00000001;
    private const uint EWX_REBOOT = 0x00000002;
    private const uint EWX_FORCEIFHUNG = 0x00000010;
    private const uint EWX_POWEROFF = 0x00000008;

    private const uint TOKEN_ADJUST_PRIVILEGES = 0x0020;
    private const uint TOKEN_QUERY = 0x0008;
    private const uint SE_PRIVILEGE_ENABLED = 0x00000002;
    private const string SE_SHUTDOWN_NAME = "SeShutdownPrivilege";

    /// <summary>
    /// เรียก ExitWindowsEx จริง - ไม่ใส่ EWX_FORCE เพื่อให้ user มีโอกาส save งานก่อน (ใส่แค่
    /// EWX_FORCEIFHUNG กันแค่กรณี process ค้างไม่ตอบสนอง) คืน true ถ้า Windows รับคำสั่งสำเร็จ
    /// (ไม่ได้แปลว่าเครื่อง shutdown เสร็จสมบูรณ์แล้ว - เป็นแค่ค่าที่ ExitWindowsEx คืนตอนรับคำสั่ง)
    /// </summary>
    public static bool ExitWindows(bool isRestart)
    {
        if (!TryEnableShutdownPrivilege())
        {
            return false;
        }

        var flags = (isRestart ? EWX_REBOOT : (EWX_SHUTDOWN | EWX_POWEROFF)) | EWX_FORCEIFHUNG;
        return ExitWindowsEx(flags, 0);
    }

    private static bool TryEnableShutdownPrivilege()
    {
        if (!OpenProcessToken(GetCurrentProcess(), TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out var tokenHandle))
        {
            return false;
        }

        try
        {
            if (!LookupPrivilegeValue(null, SE_SHUTDOWN_NAME, out var luid))
            {
                return false;
            }

            var tokenPrivileges = new TOKEN_PRIVILEGES
            {
                PrivilegeCount = 1,
                Luid = luid,
                Attributes = SE_PRIVILEGE_ENABLED
            };

            return AdjustTokenPrivileges(tokenHandle, false, ref tokenPrivileges, 0, IntPtr.Zero, IntPtr.Zero);
        }
        finally
        {
            CloseHandle(tokenHandle);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID
    {
        public uint LowPart;
        public int HighPart;
    }

    // PrivilegeCount = 1 เสมอในที่นี้ เลย flatten LUID + Attributes ตัวเดียวแทน array เต็มรูปแบบ
    // (idiom มาตรฐานสำหรับ single-privilege AdjustTokenPrivileges ใน P/Invoke)
    [StructLayout(LayoutKind.Sequential)]
    private struct TOKEN_PRIVILEGES
    {
        public uint PrivilegeCount;
        public LUID Luid;
        public uint Attributes;
    }

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool LookupPrivilegeValue(string? lpSystemName, string lpName, out LUID luid);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool AdjustTokenPrivileges(
        IntPtr tokenHandle, bool disableAllPrivileges, ref TOKEN_PRIVILEGES newState,
        uint bufferLength, IntPtr previousState, IntPtr returnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool ExitWindowsEx(uint flags, uint reason);
}
