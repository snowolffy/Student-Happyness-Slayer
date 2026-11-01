using System.Collections.Concurrent;

namespace OnionProcOparetor.Server.Services;

/// <summary>
/// คิวยกมือของนักเรียน (WP4-2, 2026-11-01 - ขั้น 4 เครื่องมือสอน) เก็บในหน่วยความจำล้วนๆ ตามที่ตกลงไว้ใน
/// docs/roadmap.md ("คิวยกมือเก็บในหน่วยความจำ หายเมื่อรีสตาร์ต Server ยอมรับได้") ไม่มี migration/ตาราง DB
/// ใดๆ เพราะเป็นแค่ "ใครกำลังรอครูอยู่ตอนนี้" ไม่ใช่ประวัติที่ต้องเก็บถาวร (ประวัติจริงไปลงที่ Activity Log
/// ผ่าน IActivityLogService.LogStudentActionAsync แทน - ดู RaiseHandEndpoints.cs)
///
/// Singleton เดียวทั้ง process (ดู Program.cs AddSingleton) ใช้ ConcurrentDictionary กัน race ระหว่างหลาย
/// เครื่องยกมือพร้อมกัน เหมือน pattern ของ AgentConnectionRegistry
/// </summary>
public sealed class RaiseHandQueue
{
    /// <summary>
    /// กันกดซ้ำเล่น (docs/roadmap.md "กดซ้ำได้หลัง 1 นาที (กันกดเล่น)") นับจากเวลาที่ "ยกมือ" ครั้งก่อนของ
    /// เครื่องเดียวกันเสมอ ไม่ว่าครูจะกด "ไปช่วยแล้ว"/นักเรียนยกเลิกไปแล้วหรือยัง - เลือกแบบนี้เพื่อให้
    /// deterministic และทดสอบง่ายที่สุด (ไม่ต้องสนสถานะ resolve/cancel เลย)
    /// </summary>
    public static readonly TimeSpan Cooldown = TimeSpan.FromMinutes(1);

    private readonly ConcurrentDictionary<string, RaiseHandEntry> _activeByClientGuid = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, DateTime> _lastRaisedAtByClientGuid = new(StringComparer.OrdinalIgnoreCase);
    private int _nextId;

    /// <summary>
    /// ลองยกมือใหม่ของเครื่องนี้ - คืน Entry ที่สร้างสำเร็จ หรือ null พร้อม CooldownSecondsRemaining (ถ้ายัง
    /// ไม่ถึงเวลา) หรือ null ทั้งคู่ (ถ้ามีรายการค้างอยู่แล้วของเครื่องนี้ที่ยังไม่ resolve/cancel)
    /// </summary>
    public (RaiseHandEntry? Entry, int? CooldownSecondsRemaining) TryRaise(
        string clientGuid, string machineName, string? studentName, string reason, string? note)
    {
        var now = DateTime.UtcNow;

        if (_lastRaisedAtByClientGuid.TryGetValue(clientGuid, out var lastRaisedAt))
        {
            var elapsed = now - lastRaisedAt;
            if (elapsed < Cooldown)
            {
                return (null, (int)Math.Ceiling((Cooldown - elapsed).TotalSeconds));
            }
        }

        if (_activeByClientGuid.ContainsKey(clientGuid))
        {
            // มีรายการค้างอยู่แล้ว (ยังไม่ resolve/cancel) - ไม่สร้างซ้ำซ้อนให้เครื่องเดียวกัน
            return (null, null);
        }

        var entry = new RaiseHandEntry
        {
            Id = Interlocked.Increment(ref _nextId),
            ClientGuid = clientGuid,
            MachineName = machineName,
            StudentName = studentName,
            Reason = reason,
            Note = note,
            RaisedAt = now
        };

        _activeByClientGuid[clientGuid] = entry;
        _lastRaisedAtByClientGuid[clientGuid] = now;

        return (entry, null);
    }

    /// <summary>รายการที่ยังรออยู่ทั้งหมด เรียงตามเวลายกมือ (เก่าสุดก่อน = คิวที่ 1)</summary>
    public List<RaiseHandEntry> GetActiveQueue() =>
        _activeByClientGuid.Values.OrderBy(e => e.RaisedAt).ToList();

    /// <summary>ตำแหน่งในคิว (1 = คนแรก) - null ถ้าไม่พบ (เช่นถูก resolve ไปพอดีก่อนเรียก)</summary>
    public int? GetQueuePosition(int entryId)
    {
        var queue = GetActiveQueue();
        var index = queue.FindIndex(e => e.Id == entryId);
        return index < 0 ? null : index + 1;
    }

    /// <summary>ครูกด "ไปช่วยแล้ว" - คืน Entry ที่ถูกเอาออก หรือ null ถ้าไม่พบ (id ไม่ตรง/ถูกเอาออกไปแล้ว)</summary>
    public RaiseHandEntry? Resolve(int entryId)
    {
        var match = _activeByClientGuid.Values.FirstOrDefault(e => e.Id == entryId);
        if (match is null)
        {
            return null;
        }

        _activeByClientGuid.TryRemove(match.ClientGuid, out _);
        return match;
    }

    /// <summary>นักเรียนกด "ยกเลิก" เอง - เอาเฉพาะของเครื่องนี้ออก ไม่แตะ cooldown (ยังต้องรอครบ 1 นาทีเหมือนเดิม)</summary>
    public RaiseHandEntry? CancelByClient(string clientGuid) =>
        _activeByClientGuid.TryRemove(clientGuid, out var removed) ? removed : null;

    /// <summary>ครูกด "ล้างคิวทั้งหมด" - คืนจำนวนรายการที่ล้างไป</summary>
    public int ClearAll()
    {
        var count = _activeByClientGuid.Count;
        _activeByClientGuid.Clear();
        return count;
    }
}

/// <summary>หนึ่งรายการในคิวยกมือ - plain data เก็บในหน่วยความจำเท่านั้น (ดู doc comment บน RaiseHandQueue)</summary>
public sealed class RaiseHandEntry
{
    public int Id { get; set; }
    public string ClientGuid { get; set; } = string.Empty;
    public string MachineName { get; set; } = string.Empty;

    /// <summary>null = ไม่มีนักเรียน login อยู่ตอนนั้น (ฝั่ง Console ใช้ชื่อเครื่องแสดงแทน)</summary>
    public string? StudentName { get; set; }

    public string Reason { get; set; } = string.Empty;
    public string? Note { get; set; }
    public DateTime RaisedAt { get; set; }
}
