using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace OnionProcOparetor.Console.Models;

/// <summary>
/// สถานะของ 1 เครื่องในหน้า ActionReportWindow - ใช้ enum ของตัวเองแทนที่จะยืม
/// CommandExecutionStatus ฝั่ง Server ตรงๆ เพราะฝั่ง UI มีสถานะเพิ่มที่ backend ไม่มี:
/// Sending/SendFailed ซึ่งเกิดขึ้น "ก่อน" command จะไปถึง DB ด้วยซ้ำ
/// </summary>
public enum ActionReportStatus
{
    /// <summary>กำลังยิง POST /api/commands ให้เครื่องนี้อยู่ (ยังไม่มี commandId)</summary>
    Sending,

    /// <summary>ยิงคำสั่งไม่ออกเลย (server ปฏิเสธ/ต่อไม่ได้) - ไม่มีทางได้ผลจริงกลับมา ถือเป็นสถานะสุดท้าย</summary>
    SendFailed,

    /// <summary>ส่งคำสั่งไปแล้ว กำลังรอผลจริงจากเครื่อง</summary>
    Pending,

    Succeeded,
    Failed,

    /// <summary>เครื่องไม่ตอบผลจริงภายในเวลาที่ Server กำหนด</summary>
    TimedOut
}

/// <summary>
/// 1 แถวในตารางรายงานผล = 1 เครื่องเป้าหมาย
///
/// ต้อง implement INotifyPropertyChanged เพราะ DataGrid ต้องอัปเดตแถวเองตอนผลจริงเข้ามาทาง
/// SignalR ทีละเครื่อง - ถ้าใช้ POCO ธรรมดาแล้วสั่ง refresh ทั้งตาราง จะทำลาย UI virtualization
/// ที่อุตส่าห์เปิดไว้ (สร้าง visual tree ใหม่ทั้งหมดทุกครั้งที่มีเครื่องใดเครื่องหนึ่งตอบกลับ
/// ซึ่งกับ 40 เครื่องที่ทยอยตอบเกือบพร้อมกันจะเห็นกระตุกชัดเจน)
/// </summary>
public class ActionReportRow : INotifyPropertyChanged
{
    private ActionReportStatus _status = ActionReportStatus.Sending;
    private string? _message;
    private int? _commandId;

    public required string ClientGuid { get; init; }
    public required string MachineName { get; init; }

    /// <summary>id ที่ Server สร้างให้ - null จนกว่าจะส่งคำสั่งสำเร็จ (ใช้ match ผลที่เข้ามาทาง SignalR)</summary>
    public int? CommandId
    {
        get => _commandId;
        set => SetField(ref _commandId, value);
    }

    public ActionReportStatus Status
    {
        get => _status;
        set
        {
            if (SetField(ref _status, value))
            {
                // property ที่คำนวณจาก Status ต้องแจ้ง binding ตามไปด้วย ไม่งั้น badge/สีไม่เปลี่ยน
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(StatusTag));
                OnPropertyChanged(nameof(IsPending));
            }
        }
    }

    /// <summary>ข้อความอธิบาย - สำคัญที่สุดตอน Failed (ครูต้องรู้ว่าทำไมเครื่องนี้ถึงไม่สำเร็จ)</summary>
    public string? Message
    {
        get => _message;
        set
        {
            if (SetField(ref _message, value))
            {
                OnPropertyChanged(nameof(TooltipText));
            }
        }
    }

    /// <summary>true = ยังไม่ถึงสถานะสุดท้าย (ใช้นับว่ายังเหลือกี่เครื่องที่รออยู่)</summary>
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

    /// <summary>map เป็น Tag ของ StatusBadge style เดิมที่ Console ใช้อยู่แล้ว (Online/Offline/Warning/Danger)</summary>
    public string StatusTag => Status switch
    {
        ActionReportStatus.Succeeded => "Online",
        ActionReportStatus.Failed => "Danger",
        ActionReportStatus.SendFailed => "Danger",
        ActionReportStatus.TimedOut => "Warning",
        _ => "Offline"
    };

    /// <summary>ข้อความที่โชว์ตอน hover - ครูเห็นสาเหตุความล้มเหลวได้โดยไม่ต้องคลิก</summary>
    public string TooltipText => string.IsNullOrWhiteSpace(Message)
        ? $"{MachineName} ({ClientGuid})"
        : Message;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}

/// <summary>
/// คำสั่งที่ ActionPanelWindow ฝากไว้ให้ DashboardWindow เปิด ActionReportWindow ต่อ
///
/// ทำไมต้องฝากผ่าน DashboardWindow แทนที่ ActionPanelWindow จะเปิดหน้า report เอง:
/// ActionPanelWindow ถูกเปิดด้วย ShowDialog() (modal) ถ้ามันเปิดหน้าต่างใหม่ก่อนปิดตัวเอง
/// จะกลายเป็นซ้อนกัน 2 ชั้นซึ่งโจทย์ห้ามไว้ชัดเจน - ให้ panel ปิดตัวเองจนสุด (คืน control
/// กลับไปที่ DashboardWindow) แล้ว Dashboard ค่อยเปิดหน้า report ต่อ จึงไม่มีการซ้อนกันเลย
/// </summary>
public class ActionReportRequest
{
    /// <summary>ชื่อ action ที่จะโชว์บนหัวหน้าต่าง เช่น "Shutdown"</summary>
    public required string ActionLabel { get; init; }

    /// <summary>รายการเครื่องเป้าหมายทั้งหมด (ยังไม่ได้ส่งคำสั่ง - หน้า report เป็นคนส่งเอง)</summary>
    public required List<ActionReportRow> Rows { get; init; }

    /// <summary>
    /// รหัสชุดคำสั่ง - null เมื่อเป็น action ที่ไม่ได้ผ่านระบบ command/ack (เช่น Toggle ที่เป็น
    /// การแก้ DB ฝั่ง Server ตรงๆ) กรณีนั้นหน้า report จะทำงานแบบ "รู้ผลทันที" ไม่ต้องรอ ack
    /// </summary>
    public string? BatchId { get; init; }

    /// <summary>
    /// ตัวส่งคำสั่งจริงของแต่ละเครื่อง - หน้า report เป็นคนเรียก ไม่ใช่ ActionPanelWindow
    /// เพื่อให้หน้าต่างเปิดขึ้นมา "ทันที" ที่ครูกดปุ่ม แล้วค่อยทยอยเติมผลทีละเครื่อง
    /// (ถ้าให้ panel ส่งให้ครบ 40 เครื่องก่อนแล้วค่อยเปิดหน้า ครูจะเห็นหน้าจอค้างไปหลายวินาที)
    ///
    /// คืน (Ok, Message) - Ok=false แปลว่าส่งคำสั่งไม่ออกเลย (สถานะสุดท้ายทันที ไม่ต้องรอ ack)
    /// สำหรับโหมด tracked ต้องเซ็ต row.CommandId ให้ด้วยก่อน return
    /// </summary>
    public required Func<ActionReportRow, Task<(bool Ok, string? Message)>> SendAsync { get; init; }
}
