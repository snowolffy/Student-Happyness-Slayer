using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;

namespace OnionProcOparetor.AgentTray;

/// <summary>
/// ป้ายมุมขวาล่าง "โหมดสอบ · เหลือ N นาที" (WP2-2, 2026-10-31) - ดู doc comment ใน .xaml
///
/// รับ ExpiresAtUtc "ครั้งเดียว" ตอนสร้าง/อัปเดต (ดู UpdateExpiresAt) แล้วนับถอยหลังด้วย
/// DispatcherTimer ของตัวเอง ไม่ต้องให้ Agent ส่ง pipe message มาทุก tick (ดู comment ที่จุดเรียกฝั่ง
/// Worker.ScanRunningProcessesAsync ว่าทำไม) - ถ้าเวลาหมดจริงก่อนที่ Agent จะส่ง "ExamModeStatus"
/// (null) มาบอกให้ปิด (เช่น poll ล้มเหลวพอดีช่วงนั้น) หน้าต่างนี้ปิดตัวเองได้เลยจากนาฬิกาของตัวเอง -
/// ไม่ต้องรอ Agent (เครื่องนักเรียนกับ Agent อยู่เครื่องเดียวกัน นาฬิกาเดียวกันอยู่แล้ว)
/// </summary>
public partial class ExamModeBadgeWindow : Window
{
    private const double ScreenMargin = 16;

    private readonly DispatcherTimer _tickTimer;
    private DateTime _expiresAtUtc;
    private bool _allowClose;

    public ExamModeBadgeWindow(DateTime expiresAtUtc)
    {
        InitializeComponent();

        _expiresAtUtc = expiresAtUtc;
        RefreshRemainingText();

        _tickTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        _tickTimer.Tick += (_, _) => RefreshRemainingText();
        _tickTimer.Start();

        // ต้องรอ Loaded ก่อนถึงจะรู้ ActualWidth/ActualHeight จริง (เหตุผลเดียวกับ ShutdownNotifyWindow)
        Loaded += (_, _) => MoveToBottomRight();
    }

    /// <summary>เรียกตอน Agent ส่ง ExamModeStatus ใหม่มาที่มีเวลาต่างจากเดิม (เช่นแก้ไขเวลาหมดอายุ) - ยังไม่เคยเกิดจริงใน UI ปัจจุบัน (ไม่มีช่องแก้เวลาระหว่างโหมดสอบทำงาน) แต่เผื่อไว้ให้ถูกต้อง</summary>
    public void UpdateExpiresAt(DateTime expiresAtUtc)
    {
        _expiresAtUtc = expiresAtUtc;
        RefreshRemainingText();
    }

    private void RefreshRemainingText()
    {
        var remaining = _expiresAtUtc - DateTime.UtcNow;
        if (remaining <= TimeSpan.Zero)
        {
            // หมดเวลาจริงตามนาฬิกาเครื่องนี้เอง - ปิดตัวเองได้เลยไม่ต้องรอ Agent (ดู doc comment บนคลาส)
            ForceClose();
            return;
        }

        var minutes = Math.Max(1, (int)Math.Ceiling(remaining.TotalMinutes));
        RemainingText.Text = $"· เหลือ {minutes} นาที";

        // ย้ายตำแหน่งซ้ำทุก tick เผื่อความกว้างข้อความเปลี่ยน (เช่น "9 นาที" -> "60 นาที") ที่ทำให้มุมขวา
        // ขยับ - MoveToBottomRight เองเช็ค ActualWidth/Height สดเสมอ ไม่ใช่ snapshot ตอนสร้าง
        if (IsLoaded)
        {
            MoveToBottomRight();
        }
    }

    /// <summary>วางมุมล่างขวาของ WorkArea - เหตุผลและข้อจำกัดเดียวกับ ShutdownNotifyWindow (จอเดียว)</summary>
    private void MoveToBottomRight()
    {
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right - ActualWidth - ScreenMargin;
        Top = workArea.Bottom - ActualHeight - ScreenMargin;
    }

    /// <summary>ปิดจริง - เรียกตอนหมดเวลา, ตอน Agent สั่งจบโหมดสอบ, หรือตอน App ปิดตัว</summary>
    public void ForceClose()
    {
        _allowClose = true;
        _tickTimer.Stop();
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_allowClose)
        {
            // กันปิดทุกทางที่ไม่ได้มาจาก ForceClose() - นักเรียนห้ามปิดป้ายนี้เอง (ไม่มี UI ให้กดอยู่แล้ว
            // แต่เผื่อมีใครพยายาม Alt+F4/ปิดจาก Task Manager ผ่าน window message)
            e.Cancel = true;
            return;
        }

        base.OnClosing(e);
    }
}
