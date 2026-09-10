namespace OnionProcOparetor.OnlineConsole.Models;

/// <summary>
/// Ported from OnionProcOparetor.Console/Models/ActionReportModels.cs, simplified: the WPF version
/// implements INotifyPropertyChanged because a WPF DataGrid needs per-row change notification to avoid
/// destroying UI virtualization on every SignalR update. Blazor doesn't have that constraint - a
/// component just calls StateHasChanged() after mutating its model - so this is a plain mutable class.
/// </summary>
public enum ActionReportStatus
{
    /// <summary>กำลังยิง POST /api/commands ให้เครื่องนี้อยู่ (ยังไม่มี commandId)</summary>
    Sending,

    /// <summary>ยิงคำสั่งไม่ออกเลย - ถือเป็นสถานะสุดท้าย</summary>
    SendFailed,

    /// <summary>ส่งคำสั่งไปแล้ว กำลังรอผลจริงจากเครื่อง</summary>
    Pending,

    Succeeded,
    Failed,

    /// <summary>เครื่องไม่ตอบผลจริงภายในเวลาที่ Server กำหนด</summary>
    TimedOut
}

/// <summary>1 แถวในตารางรายงานผล = 1 เครื่องเป้าหมาย</summary>
public class ActionReportRow
{
    public required string ClientGuid { get; init; }
    public required string MachineName { get; init; }

    /// <summary>id ที่ Server สร้างให้ - null จนกว่าจะส่งคำสั่งสำเร็จ</summary>
    public int? CommandId { get; set; }

    public ActionReportStatus Status { get; set; } = ActionReportStatus.Sending;

    /// <summary>ข้อความอธิบาย - สำคัญที่สุดตอน Failed</summary>
    public string? Message { get; set; }

    /// <summary>true = ยังไม่ถึงสถานะสุดท้าย</summary>
    public bool IsPending => Status is ActionReportStatus.Sending or ActionReportStatus.Pending;

    public string StatusText => Status switch
    {
        ActionReportStatus.Sending => "กำลังส่ง",
        ActionReportStatus.SendFailed => "ส่งไม่ออก",
        ActionReportStatus.Pending => "รอผล",
        ActionReportStatus.Succeeded => "สำเร็จ",
        ActionReportStatus.Failed => "ล้มเหลว",
        ActionReportStatus.TimedOut => "ไม่ตอบสนอง",
        _ => Status.ToString()
    };

    /// <summary>map เป็น CSS status-badge class เดิม (online/offline/warning/danger) - ดู wwwroot/css/theme.css</summary>
    public string StatusCssClass => Status switch
    {
        ActionReportStatus.Succeeded => "badge-online",
        ActionReportStatus.Failed => "badge-danger",
        ActionReportStatus.SendFailed => "badge-danger",
        ActionReportStatus.TimedOut => "badge-warning",
        _ => "badge-offline"
    };
}

/// <summary>
/// คำขอเปิดหน้ารายงานผล - Clients.razor สร้างอันนี้ไว้ให้ ActionReport modal เป็นคนยิง SendAsync เอง
/// ทีละเครื่อง (ไม่รอส่งครบทุกเครื่องก่อนเปิด modal) เหตุผลเดียวกับ WPF: เปิดหน้าจอทันทีที่ครูกดปุ่ม
/// แล้วค่อยทยอยเติมผลทีละแถว แทนที่จะค้างจอไปหลายวินาทีถ้ามีเครื่องเยอะ
/// </summary>
public class ActionReportRequest
{
    public required string ActionLabel { get; init; }
    public required List<ActionReportRow> Rows { get; init; }

    /// <summary>null = action ที่ไม่ได้ผ่านระบบ command/ack (เช่น Toggle) - รู้ผลทันที ไม่ต้องรอ ack</summary>
    public string? BatchId { get; init; }

    public required Func<ActionReportRow, Task<(bool Ok, string? Message)>> SendAsync { get; init; }
}
