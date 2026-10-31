using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace OnionProcOparetor.AgentTray.Services;

/// <summary>
/// ตรวจเวลาที่ไม่ได้ใช้งานคีย์บอร์ด/เมาส์ (WP3-3, 2026-10-31) - ใช้ auto-logout นักเรียนตาม
/// SignInSettings.AutoLogoutIdleMinutes (default 15 นาที, ดู docs/roadmap.md "logout อัตโนมัติ")
///
/// ใช้ GetLastInputInfo (user32.dll) ซึ่งเป็น system-wide idle time ของทั้งเครื่อง (ไม่ใช่แค่ของ
/// AgentTray) - ตรงตามที่ต้องการ เพราะ "ไม่ได้ใช้งาน" หมายถึงไม่มีใครแตะคีย์บอร์ด/เมาส์เลยไม่ว่าโฟกัส
/// จะอยู่หน้าต่างไหน ไม่ต้องมี hook เหมือน LockScreenWindow (ไม่ต้อง "บล็อก" input แค่ "วัดเวลา" เฉยๆ)
///
/// เรียก Tick ผ่าน DispatcherTimer ทุก 30 วิ - ถี่พอจะตรวจจับได้ภายในครึ่งนาที ไม่ถี่เกินจนเปลืองโดยไม่
/// จำเป็น (idle timeout สั้นสุดที่ Server ยอมตั้งคือ 1 นาที ดู SignInSettingsEndpoints validation)
/// </summary>
public class IdleActivityMonitor
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(30);

    private readonly DispatcherTimer _timer;
    private int _idleMinutesThreshold = 15;
    private bool _fired;

    /// <summary>ยิงครั้งเดียวต่อช่วง idle หนึ่งรอบ (ไม่ยิงซ้ำทุก tick ตราบใดที่ยังไม่มีการขยับเมาส์/คีย์บอร์ดเลยหลังยิงไปแล้ว) - ผู้ฟังเป็นคนเรียก Reset() เองหลัง logout จริงสำเร็จ</summary>
    public event Action? IdleThresholdReached;

    public IdleActivityMonitor()
    {
        _timer = new DispatcherTimer { Interval = TickInterval };
        _timer.Tick += (_, _) => CheckIdle();
    }

    public void Start() => _timer.Start();

    public void Stop() => _timer.Stop();

    /// <summary>เรียกทุกครั้งที่ ClientState/poll อัปเดตค่า AutoLogoutIdleMinutes ใหม่มา (ดู App.xaml.cs)</summary>
    public void UpdateThresholdMinutes(int minutes)
    {
        if (minutes > 0)
        {
            _idleMinutesThreshold = minutes;
        }
    }

    /// <summary>เรียกหลัง logout สำเร็จ (ไม่ว่าด้วยวิธีไหน) - กันยิง IdleThresholdReached ซ้ำทันทีใน tick ถัดไปก่อนที่ user คนใหม่จะเริ่มแตะคีย์บอร์ด/เมาส์</summary>
    public void Reset() => _fired = false;

    private void CheckIdle()
    {
        var idleSeconds = GetIdleSeconds();
        if (idleSeconds is null)
        {
            return; // เรียก Win32 API ไม่สำเร็จ - ข้าม tick นี้ไป ไม่ถือเป็น idle
        }

        var idleMinutes = idleSeconds.Value / 60.0;
        if (idleMinutes < _idleMinutesThreshold)
        {
            _fired = false; // มีการขยับล่าสุดภายในเกณฑ์ - รีเซ็ตให้พร้อมยิงใหม่รอบหน้า
            return;
        }

        if (!_fired)
        {
            _fired = true;
            IdleThresholdReached?.Invoke();
        }
    }

    private static double? GetIdleSeconds()
    {
        var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        if (!GetLastInputInfo(ref info))
        {
            return null;
        }

        var idleTicks = (uint)Environment.TickCount - info.dwTime;
        return idleTicks / 1000.0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }

    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);
}
