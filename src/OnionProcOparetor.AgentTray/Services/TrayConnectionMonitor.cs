using System.Windows.Threading;

namespace OnionProcOparetor.AgentTray.Services;

/// <summary>
/// สถานะ tray icon 3 แบบ (WP UI-7b, 2026-11-03 - ดู docs/roadmap.md "UI-7" บรรทัดความหมาย tray):
///   Connected    = Agent ติดต่อได้ (GET /status สำเร็จ) และ Agent รายงานว่าคุย Server ได้ (LastPollSucceeded=true)
///   Disconnected = Agent ติดต่อได้ แต่ Agent คุย Server ไม่ได้ (LastPollSucceeded=false)
///   Error        = AgentTray คุยกับ Agent local API ไม่ได้เลย (GET /status คืน null - ดู ClientApiClient.GetStatusAsync)
/// </summary>
public enum TrayConnectionState
{
    Connected,
    Disconnected,
    Error
}

/// <summary>
/// Poll GET /status ของ Agent ทุก 5 วิ (ความถี่เดียวกับ poll อื่นๆ ใน AgentTray เช่น
/// RaiseHandBadgeWindow/StudentLoginWindow) เพื่อตัดสินสถานะ tray icon - ยิง StateChanged เฉพาะตอน
/// สถานะเปลี่ยนจริงเท่านั้น (ไม่ยิงซ้ำทุก tick) ให้ App.xaml.cs สลับ TaskbarIcon.Icon เฉพาะตอนจำเป็น
///
/// ไม่เขียนอะไรลง disk เลย (สถานะอยู่ใน memory ของ instance นี้ล้วนๆ) - ตรงกับข้อจำกัด disk freeze
/// ของเครื่องนักเรียน (ดู CLAUDE.md)
/// </summary>
public class TrayConnectionMonitor
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(5);

    private readonly ClientApiClient _apiClient;
    private readonly DispatcherTimer _timer;
    private TrayConnectionState? _lastState;

    public event Action<TrayConnectionState>? StateChanged;

    public TrayConnectionMonitor(ClientApiClient apiClient)
    {
        _apiClient = apiClient;
        _timer = new DispatcherTimer { Interval = TickInterval };
        _timer.Tick += async (_, _) => await PollAsync();
    }

    /// <summary>เริ่ม timer + เช็คครั้งแรกทันที ไม่ต้องรอ tick แรกครบ 5 วิ (เหมือน
    /// InitializeExamModeBadgeFromStatusAsync ของ App.xaml.cs - ให้ icon ถูกต้องเร็วที่สุดตอน Tray เพิ่งเริ่ม)</summary>
    public void Start()
    {
        _timer.Start();
        _ = PollAsync();
    }

    public void Stop() => _timer.Stop();

    private async Task PollAsync()
    {
        var status = await _apiClient.GetStatusAsync();

        var state = status is null
            ? TrayConnectionState.Error
            : status.LastPollSucceeded
                ? TrayConnectionState.Connected
                : TrayConnectionState.Disconnected;

        if (state == _lastState)
        {
            return; // ไม่เปลี่ยน - ไม่ยิง event ซ้ำ (กันสลับ icon object ทุก 5 วิโดยไม่จำเป็น)
        }

        _lastState = state;
        StateChanged?.Invoke(state);
    }
}
