using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using OnionProcOparetor.Console.Models;
using OnionProcOparetor.Console.Services;

namespace OnionProcOparetor.Console;

/// <summary>
/// หน้ารายงานผลของ action ที่ครูสั่ง - เปิด "แทนที่" ActionPanelWindow ทันทีที่กดปุ่ม
/// (panel ปิดตัวเองก่อน แล้ว DashboardWindow เป็นคนเปิดหน้านี้ต่อ ไม่ซ้อนกัน - ดู ActionReportRequest)
///
/// ทำงาน 2 โหมด:
/// - **tracked** (BatchId != null) - action ที่ผ่านระบบ command/ack จริง (Lock/Unlock/Shutdown/Restart)
///   ส่งคำสั่งแล้วรอผลจริงจากแต่ละเครื่อง อัปเดตแบบ real-time ผ่าน SignalR + poll สำรอง
/// - **immediate** (BatchId == null) - action ที่ Server ทำให้เสร็จในคำขอเดียว ไม่มี ack จากเครื่อง
///   (Toggle) รู้ผลทันทีตอนส่งเสร็จ ไม่ต้องรออะไรต่อ
///
/// ปิดหน้าต่างได้ตลอดเวลาแม้ยังรอผลไม่ครบ - ต้อง unsubscribe ทุกอย่างให้เกลี้ยงตอนปิด ไม่งั้น
/// ผลที่มาถึงทีหลังจะวิ่งเข้า handler ของหน้าต่างที่ตายไปแล้ว (Dispatcher.Invoke บน window
/// ที่ปิดแล้วทำให้ทั้งแอปพัง) - ดู Closed handler ท้ายไฟล์
/// </summary>
public partial class ActionReportWindow : Window
{
    /// <summary>
    /// จำนวนคำสั่งที่ยิงพร้อมกันสูงสุด - 40 เครื่องยิงรวดเดียวพร้อมกันอาจทำให้ Server (SQLite
    /// เขียนทีละ transaction) คอขวด และ HttpClient timeout 10 วิอาจไม่พอ จำกัดไว้ให้ทยอยส่ง
    /// แต่ยังเร็วกว่าส่งทีละเครื่องเรียงกันมาก (40 เครื่อง ≈ 5 รอบ)
    /// </summary>
    private const int MaxConcurrentSends = 8;

    /// <summary>
    /// รอบ poll สำรองเผื่อ SignalR หลุด - ถี่กว่า Dashboard (10 วิ) เพราะหน้านี้ครูจ้องรอผลอยู่
    /// pattern เดียวกับ DashboardWindow ที่ poll คู่ขนานกับ SignalR อยู่แล้ว
    /// </summary>
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    private readonly ApiClient _apiClient;
    private readonly LabHubClient? _labHubClient;
    private readonly ActionReportRequest _request;
    private readonly ObservableCollection<ActionReportRow> _rows;
    private readonly DispatcherTimer? _pollTimer;

    private bool _isClosed;

    /// <param name="labHubClient">
    /// ใช้ connection เดิมของ DashboardWindow (join group "console" ไว้แล้ว) ไม่เปิดใหม่ -
    /// null ได้ ถ้า null จะเหลือแค่ poll อย่างเดียวซึ่งยังทำงานได้ครบ แค่ช้ากว่า
    /// </param>
    public ActionReportWindow(ApiClient apiClient, LabHubClient? labHubClient, ActionReportRequest request)
    {
        InitializeComponent();
        // WindowStartupLocation="Manual" + WindowCenteringHelper - กลไกเดียวกับหน้าต่างย่อยอื่น
        // ทั้งหมด (2026-09-10) ของเดิมใช้ CenterOwner ของ WPF เอง ซึ่งวางเยื้องจากกึ่งกลางจริง
        Loaded += (_, _) => WindowCenteringHelper.CenterOverOwnerOrScreen(this);

        _apiClient = apiClient;
        _labHubClient = labHubClient;
        _request = request;
        _rows = new ObservableCollection<ActionReportRow>(request.Rows);

        ResultsGrid.ItemsSource = _rows;

        HeaderText.Text = request.Rows.Count == 1
            ? $"ผลการทำงาน: {request.ActionLabel} - {request.Rows[0].MachineName}"
            : $"ผลการทำงาน: {request.ActionLabel} ({request.Rows.Count} เครื่อง)";

        UpdateSummary();

        if (_labHubClient is not null)
        {
            _labHubClient.CommandResultChanged += OnCommandResultChanged;
        }

        // poll สำรองเฉพาะโหมด tracked - โหมด immediate ไม่มีอะไรให้รอ
        if (request.BatchId is not null)
        {
            _pollTimer = new DispatcherTimer { Interval = PollInterval };
            _pollTimer.Tick += async (_, _) => await PollBatchStatusAsync();
            _pollTimer.Start();
        }

        Loaded += async (_, _) => await SendAllAsync();
        Closed += OnWindowClosed;
    }

    /// <summary>
    /// ยิงคำสั่งให้ทุกเครื่องแบบขนานจำกัดจำนวน - แต่ละเครื่องอัปเดตแถวตัวเองทันทีที่ส่งเสร็จ
    /// ครูเลยเห็นความคืบหน้าไปเรื่อยๆ ไม่ใช่รอเงียบจนครบแล้วค่อยเห็นทีเดียว
    /// </summary>
    private async Task SendAllAsync()
    {
        using var throttle = new SemaphoreSlim(MaxConcurrentSends);

        var sendTasks = _rows.Select(async row =>
        {
            await throttle.WaitAsync();
            try
            {
                var (ok, message) = await _request.SendAsync(row);

                if (_isClosed)
                {
                    return; // ครูปิดหน้าต่างไประหว่างที่ยังส่งไม่ครบ - ไม่ต้องแตะ UI ต่อ
                }

                if (!ok)
                {
                    row.Status = ActionReportStatus.SendFailed;
                    row.Message = message ?? "ส่งคำสั่งไม่สำเร็จ";
                }
                else if (_request.BatchId is null)
                {
                    // โหมด immediate - จบตั้งแต่ส่งเสร็จ ไม่มี ack ให้รอ
                    row.Status = ActionReportStatus.Succeeded;
                    row.Message = message;
                }
                else
                {
                    row.Status = ActionReportStatus.Pending;
                    row.Message = message ?? "ส่งคำสั่งแล้ว กำลังรอผลจากเครื่อง...";
                }
            }
            catch (Exception ex)
            {
                if (!_isClosed)
                {
                    row.Status = ActionReportStatus.SendFailed;
                    row.Message = $"ส่งคำสั่งไม่สำเร็จ: {ex.Message}";
                }
            }
            finally
            {
                throttle.Release();
            }
        }).ToList();

        await Task.WhenAll(sendTasks);

        if (!_isClosed)
        {
            UpdateSummary();

            // ผลบางส่วนอาจมาถึง Server ก่อนที่หน้านี้จะส่งครบทุกเครื่องด้วยซ้ำ (เครื่องแรกๆ
            // ตอบกลับเร็วมากถ้าต่อ SignalR อยู่) - sync ทีเดียวหลังส่งครบ กันตกหล่น
            await PollBatchStatusAsync();
        }
    }

    /// <summary>
    /// เรียกจาก SignalR background thread - ต้อง marshal เข้า UI thread ก่อนแตะ row
    /// (ObservableCollection/INotifyPropertyChanged ผูกกับ DataGrid อยู่ อัปเดตข้าม thread ไม่ได้)
    /// </summary>
    private void OnCommandResultChanged(CommandResultDto dto)
    {
        if (_isClosed)
        {
            return;
        }

        // BeginInvoke ไม่ใช่ Invoke โดยตั้งใจ - ไม่ block SignalR receive loop รอ UI thread ว่าง
        // (ตอน 40 เครื่องตอบกลับเกือบพร้อมกัน การ block ทีละอันจะหน่วง hub connection ทั้งเส้น)
        Dispatcher.BeginInvoke(() =>
        {
            if (_isClosed)
            {
                return; // อาจปิดไปแล้วระหว่างที่ callback นี้รอคิวอยู่
            }

            ApplyResult(dto);
            UpdateSummary();
        });
    }

    /// <summary>fallback ของ SignalR - ดึงสถานะทั้ง batch มาทับทีเดียว</summary>
    private async Task PollBatchStatusAsync()
    {
        if (_isClosed || _request.BatchId is null)
        {
            return;
        }

        var results = await _apiClient.GetBatchStatusAsync(_request.BatchId);

        if (_isClosed)
        {
            return;
        }

        foreach (var dto in results)
        {
            ApplyResult(dto);
        }

        UpdateSummary();

        // รอผลครบทุกเครื่องแล้ว - ไม่ต้อง poll ต่อ (ประหยัด request ให้ Server ถ้าครูเปิด
        // หน้านี้ค้างไว้นานๆ) SignalR ยัง subscribe อยู่เผื่อมีผลตามมาทีหลังจริงๆ
        if (_pollTimer is not null && _rows.All(r => !r.IsPending))
        {
            _pollTimer.Stop();
        }
    }

    /// <summary>เอาผลจาก Server มาทับแถวที่ตรงกัน - match ด้วย CommandId เป็นหลัก</summary>
    private void ApplyResult(CommandResultDto dto)
    {
        var row = _rows.FirstOrDefault(r => r.CommandId == dto.CommandId);

        if (row is null)
        {
            return; // ผลของ batch อื่น หรือของเครื่องที่ไม่ได้อยู่ในหน้านี้ - ไม่เกี่ยวข้อง
        }

        // ไม่ถอยสถานะกลับ: SendFailed เป็นสถานะสุดท้ายฝั่ง Console แล้ว และผลที่ poll ได้
        // อาจเก่ากว่าที่ SignalR เพิ่ง push มา (race ปกติของ 2 ช่องทางคู่ขนาน)
        if (row.Status == ActionReportStatus.SendFailed)
        {
            return;
        }

        var newStatus = dto.ExecutionStatus switch
        {
            "Succeeded" => ActionReportStatus.Succeeded,
            "Failed" => ActionReportStatus.Failed,
            "TimedOut" => ActionReportStatus.TimedOut,
            _ => ActionReportStatus.Pending
        };

        // ผลที่ยังเป็น Pending ไม่ควรทับสถานะสุดท้ายที่ได้มาแล้ว (poll ช้ากว่า SignalR)
        if (newStatus == ActionReportStatus.Pending && !row.IsPending)
        {
            return;
        }

        row.Status = newStatus;

        if (!string.IsNullOrWhiteSpace(dto.ExecutionMessage))
        {
            row.Message = dto.ExecutionMessage;
        }
    }

    /// <summary>สรุปด้านบน เช่น "12/15 สำเร็จ, 2 ไม่ตอบสนอง, 1 ล้มเหลว"</summary>
    private void UpdateSummary()
    {
        var total = _rows.Count;
        var succeeded = _rows.Count(r => r.Status == ActionReportStatus.Succeeded);
        var failed = _rows.Count(r => r.Status is ActionReportStatus.Failed or ActionReportStatus.SendFailed);
        var timedOut = _rows.Count(r => r.Status == ActionReportStatus.TimedOut);
        var pending = _rows.Count(r => r.IsPending);

        var parts = new List<string> { $"{succeeded}/{total} สำเร็จ" };

        if (timedOut > 0)
        {
            parts.Add($"{timedOut} ไม่ตอบสนอง");
        }

        if (failed > 0)
        {
            parts.Add($"{failed} ล้มเหลว");
        }

        SummaryText.Text = string.Join(", ", parts);

        // indicator ชัดเจนว่ายังรออยู่ - โจทย์กำหนดไว้ว่าปิดหน้าต่างได้ตลอดแต่ต้องรู้ว่ายังไม่ครบ
        PendingIndicatorText.Text = pending > 0
            ? $"กำลังรอผลจากอีก {pending} เครื่อง... (ปิดหน้าต่างนี้ได้ ผลที่เหลือยังถูกบันทึกไว้ที่ Server ตามปกติ)"
            : "ได้รับผลครบทุกเครื่องแล้ว";
    }

    private void ResultsGrid_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (ResultsGrid.SelectedItem is not ActionReportRow row)
        {
            SelectedDetailText.Text = "เลือกแถวเพื่อดูรายละเอียดของเครื่องนั้น";
            return;
        }

        SelectedDetailText.Text = string.IsNullOrWhiteSpace(row.Message)
            ? $"{row.MachineName} ({row.ClientGuid}) - {row.StatusText}"
            : $"{row.MachineName} ({row.ClientGuid}) - {row.StatusText}\n{row.Message}";
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    /// <summary>
    /// ยกเลิก command ที่ยังค้าง Pending อยู่ (ปุ่มต่อแถว โชว์เฉพาะตอน IsPending) - เพิ่มขึ้นหลัง
    /// บั๊กจริง (2026-08-25): Restart/Shutdown ที่ ack ไม่ทันเครื่องดับจริงจะค้าง Pending แล้วถูก
    /// ส่งซ้ำทุกรอบ poll วนไม่จบหลังเครื่องบูตกลับมา ปุ่มนี้ให้ครูตัดจบได้ทันทีจาก Server โดยไม่ต้อง
    /// รอ Agent เลย ประโยชน์คู่: กู้คืนได้ด้วยถ้ากดสั่งปิด/ล็อกเครื่องผิดแล้วยังทัน (ก่อนเครื่องตอบ
    /// สถานะสุดท้าย)
    /// </summary>
    private async void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: ActionReportRow row } button)
        {
            return;
        }

        if (row.CommandId is not int commandId)
        {
            return; // ยังไม่มี CommandId (โหมด Sending) - ยกเลิกไม่ได้ ปุ่มไม่ควรโชว์อยู่แล้วในเคสนี้
        }

        button.IsEnabled = false;

        var (success, message) = await _apiClient.CancelCommandAsync(commandId);

        if (_isClosed)
        {
            return;
        }

        if (success)
        {
            row.Status = ActionReportStatus.Failed;
            row.Message = "ยกเลิกโดยครู";
            UpdateSummary();
        }
        else
        {
            // ยกเลิกไม่สำเร็จ (เช่น server ติดต่อไม่ได้) - เปิดปุ่มกลับมาให้ลองใหม่ได้
            button.IsEnabled = true;
            row.Message = message ?? "ยกเลิกไม่สำเร็จ";
        }
    }

    /// <summary>
    /// จุดสำคัญที่สุดของ lifecycle หน้านี้: ครูปิดหน้าต่างได้ตลอดแม้ ack ยังมาไม่ครบ ซึ่งแปลว่า
    /// จะมีผลวิ่งเข้ามาทาง SignalR "หลัง" หน้าต่างตายไปแล้วเสมอในเคสนั้น ถ้าไม่ unsubscribe
    /// event handler จะยังถูกเรียกอยู่ (LabHubClient เป็นของ DashboardWindow อายุยืนกว่าหน้านี้)
    /// แล้วไปเรียก Dispatcher ของหน้าต่างที่ปิดแล้ว = crash ทั้งแอป
    ///
    /// _isClosed ตั้งเป็นตัวแรกสุดเพื่อกัน callback ที่ "เข้าคิว Dispatcher ไว้แล้ว" ก่อนหน้านี้
    /// (unsubscribe อย่างเดียวไม่พอ เพราะ BeginInvoke ที่ queue ไว้แล้วจะยังทำงานต่อ)
    /// </summary>
    private void OnWindowClosed(object? sender, EventArgs e)
    {
        _isClosed = true;

        if (_labHubClient is not null)
        {
            _labHubClient.CommandResultChanged -= OnCommandResultChanged;
        }

        _pollTimer?.Stop();
    }
}
