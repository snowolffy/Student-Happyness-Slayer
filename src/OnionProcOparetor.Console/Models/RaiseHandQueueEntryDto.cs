namespace OnionProcOparetor.Console.Models;

/// <summary>หนึ่งรายการในคิวยกมือ - ตรงกับ RaiseHandQueueEntryResponse ฝั่ง Server (WP4-2, 2026-11-01)</summary>
public class RaiseHandQueueEntryDto
{
    public int Id { get; set; }
    public string ClientGuid { get; set; } = string.Empty;
    public string MachineName { get; set; } = string.Empty;

    /// <summary>null = ไม่พบนักเรียน login อยู่ตอนยกมือ (ใช้ชื่อเครื่องแทน - ดู StudentDisplayText)</summary>
    public string? StudentName { get; set; }

    public string Reason { get; set; } = string.Empty;
    public string? Note { get; set; }
    public DateTime RaisedAt { get; set; }

    /// <summary>1 = คิวแรก - คำนวณมาจาก Server แล้ว ไม่ต้องคำนวณซ้ำฝั่งนี้</summary>
    public int QueuePosition { get; set; }

    public string StudentDisplayText => string.IsNullOrEmpty(StudentName) ? "—" : StudentName;

    public bool HasNote => !string.IsNullOrWhiteSpace(Note);

    /// <summary>"รอ N นาที/วินาที" คำนวณสดทุกครั้งที่ grid วาดใหม่ (ไม่ cache - แถวนี้ถูก re-render บ่อยพอ จาก DispatcherTimer ของหน้าอื่นที่มีอยู่แล้ว)</summary>
    public string WaitedText
    {
        get
        {
            var elapsed = DateTime.UtcNow - RaisedAt;
            if (elapsed.TotalMinutes >= 1)
            {
                return $"รอ {(int)elapsed.TotalMinutes} นาที";
            }

            return $"รอ {Math.Max(0, (int)elapsed.TotalSeconds)} วินาที";
        }
    }
}
