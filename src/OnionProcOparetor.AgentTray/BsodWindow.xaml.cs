using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using OnionProcOparetor.AgentTray.Services;

namespace OnionProcOparetor.AgentTray;

/// <summary>
/// จอฟ้าปลอมเต็มจอ - ส่วนที่สองของมุก shutdown (ดู Services/JokeShutdownController.cs) โชว์ทันทีที่
/// เสียงจรวด "ระเบิด" แล้วค้างอยู่จนกว่า ShutdownNotifyWindow ตัวจริงจะนับครบและ ForceClose() ตัวเอง
/// (JokeShutdownController subscribe Closed ของมันไว้แล้วมาปิดหน้าต่างนี้พร้อมกัน) - ไม่มี timer
/// ของตัวเองเลย เพื่อไม่ให้จังหวะเพี้ยนไปจากเวลา shutdown จริงที่ฝั่ง Agent คุมอยู่
///
/// ปิดเองไม่ได้เหมือน LockScreenWindow/ShutdownNotifyWindow - ปิดได้ทางเดียวคือ ForceClose()
/// จาก JokeShutdownController เท่านั้น กัน user Alt-F4 หลุดมุกก่อนเวลา
/// </summary>
public partial class BsodWindow : Window
{
    // Windows 11 (22H2+) ปัดมุมหน้าต่างให้อัตโนมัติผ่าน DWM แม้จะเป็น WindowStyle="None" ก็ตาม
    // (นโยบายระดับ compositor ไม่ใช่ WPF chrome) ทำให้เห็นขอบเล็กๆ ที่มุมจอโผล่มาแม้ตั้ง Left/Top/
    // Width/Height เต็มจอตรงๆ แล้ว (พบจริงตอนทดสอบ 2026-09-15 หลังแก้ปัญหา Maximized ไปแล้วรอบนึง)
    // ต้องสั่งปิดทั้งสองอย่างตรงๆ ผ่าน DwmSetWindowAttribute: ปิดการปัดมุม (corner preference) และ
    // ปิดเส้น accent border (บาง build/ธีมวาดเส้นขอบสีตาม accent color ของ Windows ให้ทุกหน้าต่างที่
    // active อยู่ ไม่เกี่ยวกับ WPF เลย) - ครอบ try-catch เพราะ Windows รุ่นเก่ากว่านี้ไม่มี attribute
    // พวกนี้ (เรียกแล้วพังก็แค่เหลือขอบนิดหน่อยไว้เหมือนเดิม ไม่ใช่ฟีเจอร์หลักที่ต้องรับประกัน)
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWA_BORDER_COLOR = 34;
    private const int DWMWC_DONTROUND = 1;
    private const uint DWMWA_COLOR_NONE = 0xFFFFFFFE;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int pvAttribute, int cbAttribute);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref uint pvAttribute, int cbAttribute);

    private bool _allowClose;

    public BsodWindow(string imagePath)
    {
        InitializeComponent();

        // เต็มจอด้วยพิกัด/ขนาดจริงแทน WindowState="Maximized" - Maximized + WindowStyle="None" บาง
        // เครื่อง/ธีมยังเหลือ non-client resize frame บางๆ ให้เห็นเป็นเส้นขอบดำของ desktop ทะลุออกมา
        // (พบจริงตอนทดสอบ 2026-09-15) จอเดียวเท่านั้น ตรงกับสมมติฐานเดิมของ ShutdownNotifyWindow
        // (ไม่รองรับ multi-monitor) - ค่าพวกนี้เป็น DIP ของจอหลักอยู่แล้วไม่ต้องแปลงหน่วยเพิ่ม
        Left = 0;
        Top = 0;
        Width = SystemParameters.PrimaryScreenWidth;
        Height = SystemParameters.PrimaryScreenHeight;

        // ต้องรอ SourceInitialized ก่อนถึงจะมี window handle จริงให้ DwmSetWindowAttribute เรียกได้
        SourceInitialized += (_, _) => DisableDwmChrome();

        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(imagePath, UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();
            BsodImage.Source = bitmap;
        }
        catch (Exception ex)
        {
            // โหลดภาพพัง ก็ยังโชว์พื้นหลังฟ้าเปล่าๆ ได้ต่อ - ดีกว่าโยน exception แล้วมุกทำให้
            // Tray ล่มตอน shutdown จริง (ดูเหตุผลเดียวกันใน JokeShutdownController.Start)
            TrayDiagnostics.Log($"[joke] โหลดภาพจอฟ้าปลอมไม่สำเร็จ: {ex.Message}");
        }
    }

    private void DisableDwmChrome()
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;

            var cornerPreference = DWMWC_DONTROUND;
            DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref cornerPreference, sizeof(int));

            var borderColor = DWMWA_COLOR_NONE;
            DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref borderColor, sizeof(uint));
        }
        catch (Exception ex)
        {
            TrayDiagnostics.Log($"[joke] ปิด DWM corner/border ไม่สำเร็จ: {ex.Message} - เหลือขอบนิดหน่อยไว้ ไม่ใช่เรื่องคอขาดบาดตาย");
        }
    }

    /// <summary>ปิดจริง - เรียกจาก JokeShutdownController เท่านั้น</summary>
    public void ForceClose()
    {
        _allowClose = true;
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            return;
        }

        base.OnClosing(e);
    }
}
