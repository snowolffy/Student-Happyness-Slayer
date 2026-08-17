using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace OnionProcOparetor.AgentTray.Controls;

/// <summary>
/// Progress bar นับถอยหลังพร้อมข้อความกำกับ - ใช้ซ้ำได้กับทุกหน้าที่ต้องนับถอยหลังให้ user เห็น
/// (ตอนนี้มี ShutdownNotifyWindow ใช้อยู่ที่เดียว แต่ออกแบบให้ reuse ได้)
///
/// อยู่ในโปรเจกต์ AgentTray ไม่ใช่ shared library เพราะ solution นี้ไม่มี shared class library
/// ระหว่าง Console/Agent/AgentTray เลย (ทุกโปรเจกต์ก๊อป DTO ของตัวเองแยกกัน ไม่มี ProjectReference
/// ระหว่างกันสักเส้น) - การสร้าง shared library ใหม่จะกระทบทั้ง 4 csproj + publish layout
/// ของ installer ซึ่งเกินขอบเขตงานนี้
///
/// **เรื่อง drift (สำคัญที่สุดของ control นี้):** ไม่นับถอยหลังด้วยการลบทีละ 1 ทุก tick
/// (`_remaining--` ใน DispatcherTimer) เพราะ DispatcherTimer ไม่รับประกันความเที่ยงตรงเลย -
/// tick จะถูกเลื่อนออกไปทุกครั้งที่ UI thread ไม่ว่าง แล้ว error จะ "สะสม" ไปเรื่อยๆ จนนับ
/// ถึง 0 ช้ากว่าเวลาจริงหลายวินาที (ยิ่งตั้ง 120 วิยิ่งเห็นชัด) แทนที่จะทำแบบนั้น เก็บ
/// "เวลาเป้าหมายสัมบูรณ์" (_deadlineUtc) ไว้ตั้งแต่ตอน Start() แล้วคำนวณเวลาที่เหลือจาก
/// นาฬิกาจริงใหม่ทุก tick - tick ที่มาช้าจะ "ข้าม" เลขที่ควรข้ามไปเอง error ไม่สะสม
/// ตรงกับฝั่ง Agent (CommandProcessor.DelayThenExitWindowsAsync) ที่ใช้ deadline เหมือนกัน
/// </summary>
public partial class CountdownProgressBar : UserControl
{
    private DispatcherTimer? _timer;
    private DateTime _deadlineUtc;
    private DateTime _startedUtc;
    private int _totalSeconds;
    private string _labelFormat = "{0}";
    private bool _completed;

    /// <summary>เรียกครั้งเดียวเมื่อนับถึง 0 - เรียกบน UI thread เสมอ</summary>
    public event EventHandler? Completed;

    public CountdownProgressBar()
    {
        InitializeComponent();
        Unloaded += (_, _) => Stop();
    }

    /// <summary>
    /// เริ่มนับถอยหลัง totalSeconds วินาที
    /// labelFormat เป็น composite format string ที่มี {0} = วินาทีที่เหลือ (ปัดขึ้น)
    /// เช่น "เครื่องนี้จะปิดเครื่องในอีก {0} วินาที โดยผู้ดูแลระบบ"
    /// เรียกซ้ำได้ - จะรีเซ็ตการนับใหม่ทั้งหมด (ใช้ตอน Agent ส่ง command ซ้ำมาทับของเดิม)
    /// </summary>
    public void Start(int totalSeconds, string labelFormat)
    {
        Stop();

        _totalSeconds = Math.Max(1, totalSeconds);
        _labelFormat = string.IsNullOrEmpty(labelFormat) ? "{0}" : labelFormat;
        _completed = false;
        _startedUtc = DateTime.UtcNow;
        _deadlineUtc = _startedUtc.AddSeconds(_totalSeconds);

        // tick ถี่กว่า 1 วิ (100ms) เพื่อให้แถบเลื่อนลื่นตา ไม่กระตุกทีละช่อง - ตัวเลขวินาที
        // ยังเปลี่ยนแค่ตอนที่ค่าปัดขึ้นเปลี่ยนจริงเท่านั้น
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _timer.Tick += (_, _) => UpdateFromClock();
        _timer.Start();

        UpdateFromClock();
    }

    /// <summary>หยุดนับ (ไม่ยิง Completed) - idempotent เรียกซ้ำได้</summary>
    public void Stop()
    {
        if (_timer is not null)
        {
            _timer.Stop();
            _timer = null;
        }
    }

    private void UpdateFromClock()
    {
        var nowUtc = DateTime.UtcNow;
        var remaining = _deadlineUtc - nowUtc;

        if (remaining < TimeSpan.Zero)
        {
            remaining = TimeSpan.Zero;
        }

        // ปัดขึ้น: เหลือ 9.3 วิ ให้แสดง "10" ไม่ใช่ "9" - user เห็นเลขเริ่มต้นตรงกับที่ครูตั้งไว้
        // และเห็น "1" ตลอดวินาทีสุดท้ายจริงๆ ก่อนจะกลายเป็น 0 พอดีตอนเครื่องดับ
        var secondsLeft = (int)Math.Ceiling(remaining.TotalSeconds);
        CountdownLabel.Text = string.Format(_labelFormat, secondsLeft);

        // สัดส่วนที่ "ผ่านไปแล้ว" - แถบเต็มขึ้นเรื่อยๆ จนเต็มพอดีตอนเครื่องดับ
        var elapsed = (nowUtc - _startedUtc).TotalSeconds;
        var progress = Math.Clamp(elapsed / _totalSeconds, 0d, 1d);
        ApplyProgress(progress);

        if (remaining > TimeSpan.Zero || _completed)
        {
            return;
        }

        _completed = true;
        Stop();
        Completed?.Invoke(this, EventArgs.Empty);
    }

    private double _lastProgress;

    private void ApplyProgress(double progress)
    {
        _lastProgress = progress;
        FillBorder.Width = TrackBorder.ActualWidth * progress;
    }

    /// <summary>
    /// ความกว้างของแถบเป็น px ที่คำนวณจาก ActualWidth ของ track - ต้องคำนวณใหม่ตอน layout
    /// เปลี่ยน ไม่งั้นรอบแรก (ก่อน layout pass แรกเสร็จ) ActualWidth ยังเป็น 0 แถบจะไม่ขึ้นเลย
    /// </summary>
    private void TrackBorder_SizeChanged(object sender, SizeChangedEventArgs e) => ApplyProgress(_lastProgress);
}
