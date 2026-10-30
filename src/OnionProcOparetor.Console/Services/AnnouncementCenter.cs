namespace OnionProcOparetor.Console.Services;

/// <summary>ความร้ายแรงของประกาศ - เลขมากกว่า = ร้ายแรงกว่า และได้ขึ้นแถบหัวก่อน</summary>
public enum AnnouncementSeverity
{
    Info = 0,
    Warning = 1,
    Danger = 2
}

/// <summary>ประกาศสถานะของทั้งระบบหนึ่งเรื่อง (เช่น "ปิดการบังคับใช้ทั้งระบบอยู่")</summary>
public sealed class Announcement
{
    /// <summary>ชื่อเรื่องแบบคงที่ เช่น "global-off" - ใช้ตั้งซ้ำ/ลบ โดยไม่ต้องถือ reference</summary>
    public required string Key { get; init; }

    public required AnnouncementSeverity Severity { get; init; }

    /// <summary>ข้อความบรรทัดเดียวบนแถบหัว - ต้องสั้นพอจะไม่โดนตัด (แถบสูงคงที่ ไม่มีบรรทัดที่สอง)</summary>
    public required string Title { get; init; }

    /// <summary>คำอธิบายเพิ่ม - โชว์เป็น ToolTip ของแถบ และในรายการ "+N"</summary>
    public string? Detail { get; init; }

    /// <summary>ป้ายของปุ่มแก้ในตัว เช่น "เปิดการบังคับใช้กลับ" - null = ไม่มีปุ่ม</summary>
    public string? FixText { get; init; }

    public Action? Fix { get; init; }

    public bool HasFix => !string.IsNullOrEmpty(FixText) && Fix is not null;

    /// <summary>ลำดับที่ถูกตั้ง - ใช้ตัดสินเมื่อร้ายแรงเท่ากัน (อันที่เกิดก่อนได้ขึ้นก่อน ไม่สลับไปมา)</summary>
    internal long Sequence { get; set; }
}

/// <summary>
/// ศูนย์รวม "สถานะของทั้งระบบ" ที่ต้องเห็นได้จากทุกหน้า (WP6) แสดงที่ AnnouncementBar บนแถบหัว
///
/// แยกจาก toast โดยเจตนา: toast คือ "เพิ่งเกิดอะไรขึ้น" ซึ่งหายไปได้ ส่วนนี่คือ "ตอนนี้ระบบอยู่ใน
/// สภาพไหน" ซึ่งต้องค้างอยู่จนกว่าสาเหตุจะหายจริง - ใครเป็นคนรู้ว่าสาเหตุหายแล้ว (เช่น
/// LoadAllDataAsync โหลดสำเร็จ) เป็นคนเรียก <see cref="Clear"/> เอง
///
/// ตั้งเรื่องเดิมซ้ำด้วย key เดียวกัน = แทนที่ (ไม่ซ้อน) เพราะ poll ทุก 2-10 วินาทีจะตั้งเรื่องเดิม
/// ซ้ำทุกรอบที่ Server ยังไม่ตอบ
/// </summary>
public sealed class AnnouncementCenter
{
    private readonly Dictionary<string, Announcement> _items = new();
    private long _sequence;

    /// <summary>ยิงทุกครั้งที่รายการเปลี่ยน - ผู้ฟังต้องอยู่บน UI thread (ทุกจุดที่เรียก Set/Clear อยู่บน UI thread)</summary>
    public event Action? Changed;

    /// <summary>ประกาศทั้งหมด เรียงร้ายแรงสุดก่อน - ตัวแรกคือตัวที่ขึ้นแถบหัว</summary>
    public IReadOnlyList<Announcement> Ordered =>
        _items.Values
            .OrderByDescending(a => a.Severity)
            .ThenBy(a => a.Sequence)
            .ToList();

    public bool Has(string key) => _items.ContainsKey(key);

    public void Set(Announcement announcement)
    {
        // ตั้งเรื่องเดิมซ้ำ: คงลำดับเดิมไว้ ไม่งั้นเรื่องที่ถูกตั้งซ้ำทุกรอบ poll จะเด้งไปท้ายแถวตลอด
        announcement.Sequence = _items.TryGetValue(announcement.Key, out var existing)
            ? existing.Sequence
            : ++_sequence;

        var changed = existing is null
                      || existing.Severity != announcement.Severity
                      || existing.Title != announcement.Title
                      || existing.Detail != announcement.Detail
                      || existing.FixText != announcement.FixText;

        _items[announcement.Key] = announcement;

        // poll ตั้งเรื่องเดิมซ้ำทุกรอบ - ถ้าไม่มีอะไรเปลี่ยนจริง ไม่ต้องวาดแถบใหม่ (ไม่งั้น popup "+N"
        // ที่เปิดอ่านอยู่จะถูกสร้างใหม่ใต้เมาส์ทุก 2 วินาที)
        if (changed)
        {
            Changed?.Invoke();
        }
    }

    public void Clear(string key)
    {
        if (_items.Remove(key))
        {
            Changed?.Invoke();
        }
    }
}
