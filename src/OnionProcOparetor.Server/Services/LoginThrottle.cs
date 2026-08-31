using System.Collections.Concurrent;

namespace OnionProcOparetor.Server.Services;

/// <summary>
/// จำกัดจำนวนครั้งที่เดารหัสผ่านผิดติดกันได้ (2026-08-31, จาก security audit)
///
/// ก่อนหน้านี้ POST /api/auth/login ไม่มีการจำกัดอะไรเลย: อยู่ใน LAN เดียวกันก็ยิงเดารหัสผ่านของ
/// Administrator ได้ไม่จำกัดจำนวนครั้ง ด้วยความเร็วเท่าที่ PBKDF2 100k iterations จะรับไหว ซึ่งกับ
/// รหัสผ่านที่ครูตั้งเองมักไม่ยาวพอจะทนการเดาแบบนั้นได้จริง
///
/// นับแยกตาม (username, IP) โดยตั้งใจ:
///   - นับแค่ username อย่างเดียว = นักเรียนคนเดียวล็อกบัญชีครูทิ้งได้ (denial of service)
///   - นับแค่ IP อย่างเดียว = ไล่เดาหลาย username จาก IP เดียวไม่โดนนับรวมกัน
/// เก็บใน memory ล้วน ไม่ลง DB: ตัวนับหายตอน restart service ยอมรับได้ (การ restart ต้องใช้สิทธิ์
/// admin บนเครื่อง server อยู่แล้ว ซึ่งถ้ามีสิทธิ์นั้นก็ทำอย่างอื่นที่แรงกว่านี้ได้เยอะ) แลกกับการ
/// ไม่ต้องเพิ่ม table/migration และไม่ต้องเขียน DB ทุกครั้งที่มีคน login ผิด
/// </summary>
public sealed class LoginThrottle
{
    /// <summary>ผิดติดกันกี่ครั้งถึงจะเริ่มล็อก</summary>
    private const int MaxFailedAttempts = 5;

    /// <summary>ล็อกนานเท่าไหร่เมื่อครบเพดาน - 5 นาทีทำให้ brute-force ไร้ความหมายในทางปฏิบัติ แต่ครูที่พิมพ์ผิดจริงๆ ก็ยังรอไหว</summary>
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(5);

    /// <summary>ไม่ผิดเลยนานเท่านี้ = ล้างตัวนับทิ้ง (ไม่ให้ความผิดพลาดเมื่อเช้าไปสะสมกับตอนบ่าย)</summary>
    private static readonly TimeSpan AttemptWindow = TimeSpan.FromMinutes(15);

    private sealed class Entry
    {
        public int FailedCount;
        public DateTime LastFailureUtc;
        public DateTime? LockedUntilUtc;
    }

    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);

    private static string BuildKey(string? username, string? remoteIp) =>
        $"{username ?? "?"}|{remoteIp ?? "?"}";

    /// <summary>
    /// คืนเวลาที่เหลือของการล็อก ถ้ายังล็อกอยู่ - null = ยิง login ได้ตามปกติ
    /// เรียก "ก่อน" ตรวจรหัสผ่านเสมอ เพื่อไม่ให้เสีย CPU ไปกับ PBKDF2 ของ request ที่ถูกล็อกอยู่แล้ว
    /// (ซึ่งเป็นช่องทำ CPU-exhaustion DoS ในตัวมันเองถ้าปล่อยให้คำนวณทุก request)
    /// </summary>
    public TimeSpan? GetRemainingLockout(string? username, string? remoteIp)
    {
        if (!_entries.TryGetValue(BuildKey(username, remoteIp), out var entry))
        {
            return null;
        }

        lock (entry)
        {
            if (entry.LockedUntilUtc is not { } until)
            {
                return null;
            }

            var remaining = until - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                // หมดเวลาล็อกแล้ว - เริ่มนับใหม่จากศูนย์ ไม่ใช่ให้เหลือ 4/5 ค้างไว้แล้วโดนล็อกซ้ำทันที
                entry.LockedUntilUtc = null;
                entry.FailedCount = 0;
                return null;
            }

            return remaining;
        }
    }

    /// <summary>เรียกทุกครั้งที่รหัสผ่านผิด (หรือไม่มี username นั้นอยู่จริง - ต้องนับด้วย ไม่งั้นการเดา username ไม่ถูกจำกัด)</summary>
    public void RecordFailure(string? username, string? remoteIp)
    {
        var entry = _entries.GetOrAdd(BuildKey(username, remoteIp), _ => new Entry());

        lock (entry)
        {
            var now = DateTime.UtcNow;
            if (now - entry.LastFailureUtc > AttemptWindow)
            {
                entry.FailedCount = 0;
            }

            entry.FailedCount++;
            entry.LastFailureUtc = now;

            if (entry.FailedCount >= MaxFailedAttempts)
            {
                entry.LockedUntilUtc = now + LockoutDuration;
            }
        }
    }

    /// <summary>เรียกเมื่อ login สำเร็จ - ล้างประวัติของคู่ (username, IP) นั้นทิ้ง</summary>
    public void RecordSuccess(string? username, string? remoteIp)
        => _entries.TryRemove(BuildKey(username, remoteIp), out _);
}
