namespace OnionProcOparetor.RemoteBridge.Services;

/// <summary>
/// กัน brute-force เดา RemoteSecret หลังจากเปิด endpoint นี้ออกสู่อินเทอร์เน็ตจริงผ่าน Tailscale
/// Funnel - เดิมไม่มีตัวนี้เลย ถ้าเป็นแค่ LAN ภายในความเสี่ยงต่ำพอจะข้ามได้ แต่พอเป็น public URL แล้ว
/// บอทสแกนอินเทอร์เน็ตจะมาชนได้ตลอดเวลา
///
/// เจตนาให้ง่ายกว่า LoginThrottle ฝั่ง Server มาก (ไม่มี per-user/per-IP เพราะ traffic ผ่าน Funnel
/// relay มา ไม่รู้ IP จริงของผู้ยิงที่เชื่อถือได้) - นับความล้มเหลวรวมทั้ง process เดียว ถ้าเดาผิดถี่
/// เกินไปในช่วงเวลาสั้นๆ ก็ล็อกทุก request ชั่วคราว ไม่ว่าใครยิงมา (รวม ESP32 ตัวจริงด้วยถ้าเผลอตั้ง
/// secret ผิดในเครื่องมันเอง - ยอมรับ tradeoff นี้เพื่อความง่าย)
/// </summary>
public sealed class RemoteTriggerThrottle
{
    private const int MaxFailuresBeforeLockout = 5;
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(5);

    private readonly object _lock = new();
    private readonly List<DateTime> _recentFailures = new();
    private DateTime? _lockedUntilUtc;

    /// <summary>true = ตอนนี้ยังอยู่ในช่วงล็อก ห้ามประมวลผล request ใดๆ เลยแม้ secret จะถูกก็ตาม</summary>
    public bool IsLockedOut()
    {
        lock (_lock)
        {
            return _lockedUntilUtc is { } until && DateTime.UtcNow < until;
        }
    }

    public void RecordFailure()
    {
        lock (_lock)
        {
            var now = DateTime.UtcNow;
            _recentFailures.RemoveAll(t => now - t > Window);
            _recentFailures.Add(now);

            if (_recentFailures.Count >= MaxFailuresBeforeLockout)
            {
                _lockedUntilUtc = now + LockoutDuration;
                _recentFailures.Clear();
            }
        }
    }

    public void RecordSuccess()
    {
        lock (_lock)
        {
            _recentFailures.Clear();
        }
    }
}
