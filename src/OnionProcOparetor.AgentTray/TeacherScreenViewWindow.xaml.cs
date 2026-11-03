using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace OnionProcOparetor.AgentTray;

/// <summary>
/// "ส่งจอครู" (WP4-4, 2026-11-01) - แสดงภาพจอครูที่ Agent ส่งมาทาง pipe (ScreenFrame) เรื่อยๆ จนกว่าครู
/// จะกด "หยุดส่ง" (ดู App.xaml.cs OnStopScreenViewRequested) ปิดได้ทางเดียวคือ ForceClose() เหมือน
/// LockScreenWindow เป๊ะๆ (OnClosing กันการปิดทุกทางที่ไม่ได้ผ่าน ForceClose - Alt-F4/ปุ่ม X บน title
/// bar ของโหมดหน้าต่างลอยกดแล้วไม่มีผล)
///
/// สองโหมด (ดู ScreenBroadcastViewerMode ฝั่ง Agent/Console) ตั้งค่า property ของ Window จากโค้ดในนี้
/// (ไม่แยก XAML 2 ไฟล์) เพราะความต่างมีแค่ WindowStyle/Topmost/ResizeMode/ขนาด - เนื้อหา (Image + แถบ
/// สถานะล่าง) เหมือนกันทุกตัวอักษร:
/// - FullscreenLocked: เต็มจอ (เหมือน LockScreenWindow) Topmost, ปิด keyboard/mouse ไม่ได้ (ดู
///   PhysicalInputBlocker ที่ App.xaml.cs เป็นคนเรียก Block()/Unblock() ให้ - ไม่ทำในคลาสนี้เอง เพื่อ
///   ใช้ instance เดียวกับที่ VNC full-screen ใช้อยู่แล้ว ไม่สร้าง hook ซ้ำสอง mechanism)
/// - Windowed: หน้าต่างปกติ ลากย้าย/ปรับขนาดได้ ไม่ Topmost (ให้ซ้อนหลังโปรแกรมอื่นได้ตาม wireframe)
/// </summary>
public partial class TeacherScreenViewWindow : Window
{
    private bool _allowClose;

    public TeacherScreenViewWindow(string viewerMode, string? teacherDisplayName)
    {
        InitializeComponent();

        var teacherLabel = string.IsNullOrWhiteSpace(teacherDisplayName) ? "ครู" : $"ครู {teacherDisplayName}";
        StatusLeftText.Text = $"กำลังดูจอครู · {teacherLabel}";

        if (viewerMode == "Windowed")
        {
            WindowStyle = WindowStyle.SingleBorderWindow;
            ResizeMode = ResizeMode.CanResize;
            Topmost = false;
            ShowInTaskbar = true;
            Width = 480;
            Height = 320;
            WindowStartupLocation = WindowStartupLocation.Manual;

            var workArea = SystemParameters.WorkArea;
            Left = workArea.Right - Width - 16;
            Top = workArea.Top + 16;

            StatusRightText.Text = string.Empty;
        }
        else
        {
            // FullscreenLocked (ค่า default) - เต็มจอหลักเสมอ (ไม่รองรับหลายจอรอบนี้ - ดูรายงาน WP4-4)
            WindowStyle = WindowStyle.None;
            WindowState = WindowState.Maximized;
            Topmost = true;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;

            StatusRightText.Text = "เมาส์และคีย์บอร์ดถูกปิดชั่วคราว";
        }
    }

    /// <summary>
    /// เรียกจาก App.xaml.cs ทุกครั้งที่ได้เฟรมใหม่จาก pipe (ScreenFrameReceived) - ต้องเรียกจาก UI
    /// thread เท่านั้น (ผู้เรียกเป็นคน Dispatcher.BeginInvoke มาแล้ว) decode ด้วย BitmapCacheOption.OnLoad
    /// แล้ว Freeze ทันที เพื่อให้ MemoryStream ปิดได้โดยไม่ทำให้ภาพเสีย (ภาพที่ไม่ freeze จะผูกกับ stream
    /// เดิมแบบ lazy-load ซึ่ง Dispose stream ไปแล้วจะทำให้ decode พังทีหลัง)
    /// </summary>
    public void UpdateFrame(byte[] jpegBytes)
    {
        try
        {
            using var stream = new MemoryStream(jpegBytes);
            var bitmap = new BitmapImage();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.BeginInit();
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();

            FrameImage.Source = bitmap;
            WaitingText.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            // เฟรมเพี้ยน/ตัดครึ่งทาง - ข้ามเฟรมนี้ไป (เฟรมถัดไปจะมาเองใน <200ms) ไม่ใช่ error ที่ user ต้องเห็น
            Services.TrayDiagnostics.Log($"[teacher-screen] decode เฟรมไม่สำเร็จ (ข้ามเฟรมนี้): {ex.Message}");
        }
    }

    /// <summary>เรียกจาก App.xaml.cs เท่านั้น (StopScreenView ปกติ หรือ cleanup ตอนปิดโปรแกรม) - ทางเดียวที่ปิดหน้าต่างนี้ได้จริง</summary>
    public void ForceClose()
    {
        _allowClose = true;
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_allowClose)
        {
            // กันการปิดทุกทางที่ไม่ได้มาจาก ForceClose() (Alt-F4 / ปุ่ม X ของโหมดหน้าต่างลอย) - หยุดได้
            // จากครูกด "หยุดส่ง" เท่านั้นตามดีไซน์ (ดู wireframe tc-broadcast-student.dc.html)
            e.Cancel = true;
            return;
        }

        base.OnClosing(e);
    }
}
