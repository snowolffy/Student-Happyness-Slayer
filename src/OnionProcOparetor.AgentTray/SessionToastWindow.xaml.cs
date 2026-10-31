using System.Windows;
using System.Windows.Threading;

namespace OnionProcOparetor.AgentTray;

/// <summary>เด้งแจ้ง "เข้าสู่ระบบเป็น [ชื่อ]" มุมขวาล่าง 5 วินาทีแล้วหายเอง (WP3-3, 2026-10-31) - ดู doc comment ใน .xaml</summary>
public partial class SessionToastWindow : Window
{
    private const double ScreenMargin = 16;
    private readonly DispatcherTimer _autoCloseTimer;

    public SessionToastWindow(string message)
    {
        InitializeComponent();
        MessageText.Text = message;

        _autoCloseTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _autoCloseTimer.Tick += (_, _) => Close();
        _autoCloseTimer.Start();
        Closed += (_, _) => _autoCloseTimer.Stop();

        Loaded += (_, _) => MoveToBottomRight();
    }

    private void MoveToBottomRight()
    {
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right - ActualWidth - ScreenMargin;
        // เหนือตำแหน่งของ ExamModeBadgeWindow/StaffSessionBadgeWindow เล็กน้อย (ScreenMargin*2 + ความสูง
        // ป้ายโดยประมาณ) กันทับกันถ้าบังเอิญเกิดพร้อมกัน - ไม่คำนวณ exact เพราะ toast นี้อยู่ไม่นาน (5 วิ)
        Top = workArea.Bottom - ActualHeight - ScreenMargin - 70;
    }
}
