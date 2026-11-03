using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace OnionProcOparetor.Console.Services;

/// <summary>
/// "ส่งจอครู" (WP4-4, 2026-11-01) - capture จอหลักของเครื่องครู (primary screen) เป็นเฟรม JPEG ต่อเนื่อง
/// ที่ fps/ความละเอียดที่ลดแล้ว (ดู TargetWidth/JpegQuality) วัดจริงบนเครื่อง dev นี้ (1920x1080 ย่อเป็น
/// 960px กว้าง, quality 45): เฉลี่ย ~18-28 KB/เฟรม, encode เฟรมเดียวใช้เวลา ~15-35ms (เหลือเฟือสำหรับ
/// เป้าหมาย 8 fps = ต้องการ &lt;125ms/เฟรม) - ดูตัวเลขจริงจาก harness ทดสอบใน WP4-4 report
///
/// ใช้ Win32 BitBlt ตรงๆ (ไม่ใช่ System.Drawing.Common/System.Windows.Forms) เพราะ:
/// - Console.csproj ไม่มี UseWindowsForms (เพิ่มไม่ได้ง่ายๆ เพราะทั้งโปรเจกต์มีหลายไฟล์เรียก
///   "Application.Current" ตรงๆ ซึ่งจะชนกับ System.Windows.Forms.Application ทันทีที่เปิด - เจอใน
///   IconPresenter/ThemeService/ToastService) การเพิ่ม UseWindowsForms จะบังคับแก้ไฟล์เหล่านั้นด้วย
///   ซึ่งอยู่นอกขอบเขตของ WP นี้
/// - WPF เองมี Imaging.CreateBitmapSourceFromHBitmap ที่แปลง HBITMAP จาก BitBlt เป็น BitmapSource ได้
///   ตรงๆ อยู่แล้ว ใช้ JpegBitmapEncoder ตัวเดียวกับที่ CapturePreview (App.xaml.cs) ใช้ถ่าย UI screenshot
///   ได้เลย ไม่ต้องเพิ่ม NuGet package ใหม่
///
/// ไม่เขียนลงดิสก์เลย (เฟรมอยู่ใน memory ตลอดทาง capture -> encode -> ส่ง SignalR)
/// </summary>
public sealed class ScreenCaptureService : IDisposable
{
    /// <summary>ความกว้างเป้าหมายหลังย่อ (คงอัตราส่วนเดิม) - 960px กว้าง เพียงพอให้อ่านจอนักเรียนได้ชัดบนจอ 1920x1080 ของเขา แต่เล็กพอให้เฟรมเล็ก (เลือกตามโจทย์ "ลดเฟรม" ใน roadmap)</summary>
    public const int TargetWidth = 960;

    /// <summary>คุณภาพ JPEG (0-100) - 45 คือจุดที่อ่านข้อความบนสไลด์/โปรแกรมได้ชัดพอ แต่เฟรมไม่ใหญ่เกินไปสำหรับ 5-10 fps ต่อเนื่อง (เทียบด้วยตาจริงระหว่างพัฒนา)</summary>
    public const int JpegQuality = 45;

    private const int SRCCOPY = 0x00CC0020;
    private const int SM_CXSCREEN = 0;
    private const int SM_CYSCREEN = 1;

    private Timer? _timer;
    private volatile bool _captureInFlight;
    private bool _disposed;

    /// <summary>เรียกทุกครั้งที่ capture+encode เฟรมใหม่สำเร็จ - เรียกจาก timer thread ไม่ใช่ UI thread</summary>
    public event Action<byte[]>? FrameCaptured;

    /// <summary>จำนวนเฟรมที่ capture สำเร็จทั้งหมดตั้งแต่ Start() - ไว้โชว์ "ส่งแล้ว N เฟรม" หรือใช้วัด throughput</summary>
    public int FrameCount { get; private set; }

    /// <summary>bytes รวมของทุกเฟรมที่ capture สำเร็จทั้งหมด - หารด้วย FrameCount เพื่อได้ bytes/เฟรมเฉลี่ย</summary>
    public long TotalBytes { get; private set; }

    /// <summary>
    /// เริ่ม capture loop ด้วย System.Threading.Timer (ไม่ใช่ DispatcherTimer - ตั้งใจไม่ผูกกับ UI
    /// thread เพราะ capture+encode ใช้เวลาหลัก ms ถ้าทำบน UI thread จะกระตุกหน้าต่างอื่นของ Console)
    /// ถ้า tick ก่อนหน้ายังไม่เสร็จ (เครื่องช้า/CPU ตัน) จะ "ข้าม" tick ใหม่ไปเลย (ไม่ซ้อนคิว capture
    /// หลายตัวพร้อมกัน) - เหมือน drop-stale-frame แต่ทำที่ต้นทางเลยไม่ใช่ปลายทาง
    /// </summary>
    public void Start(int fps)
    {
        var intervalMs = Math.Max(1, 1000 / Math.Max(1, fps));
        _timer = new Timer(_ => CaptureTick(), null, 0, intervalMs);
    }

    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
    }

    private void CaptureTick()
    {
        if (_captureInFlight)
        {
            return; // tick ก่อนหน้ายังไม่เสร็จ - ข้ามรอบนี้ไปเลย กัน capture ซ้อนกันหลายตัวพร้อมกัน
        }

        _captureInFlight = true;
        try
        {
            var jpeg = CapturePrimaryScreenJpeg();
            if (jpeg is not null)
            {
                FrameCount++;
                TotalBytes += jpeg.Length;
                FrameCaptured?.Invoke(jpeg);
            }
        }
        catch
        {
            // capture เฟรมเดียวพลาด (เช่นเครื่องกำลัง lock/switch user พอดี) - ข้ามเฟรมนี้ไป เฟรมถัดไป
            // (ไม่ถึง 200ms) จะลองใหม่เอง ไม่ควรทำให้ timer หยุดทำงานทั้งก้อนเพราะเฟรมเดียวพลาด
        }
        finally
        {
            _captureInFlight = false;
        }
    }

    /// <summary>capture จอหลักเต็มความละเอียดจริงด้วย BitBlt แล้วย่อ+เข้ารหัส JPEG - static เพราะไม่มี state (ไว้เรียกตรงจาก test harness ได้โดยไม่ต้องสร้าง ScreenCaptureService ทั้งก้อน)</summary>
    public static byte[]? CapturePrimaryScreenJpeg()
    {
        var screenWidth = GetSystemMetrics(SM_CXSCREEN);
        var screenHeight = GetSystemMetrics(SM_CYSCREEN);
        if (screenWidth <= 0 || screenHeight <= 0)
        {
            return null;
        }

        var screenDc = GetDC(IntPtr.Zero);
        if (screenDc == IntPtr.Zero)
        {
            return null;
        }

        var memDc = IntPtr.Zero;
        var bitmap = IntPtr.Zero;
        var oldBitmap = IntPtr.Zero;

        try
        {
            memDc = CreateCompatibleDC(screenDc);
            bitmap = CreateCompatibleBitmap(screenDc, screenWidth, screenHeight);
            if (memDc == IntPtr.Zero || bitmap == IntPtr.Zero)
            {
                return null;
            }

            oldBitmap = SelectObject(memDc, bitmap);
            if (!BitBlt(memDc, 0, 0, screenWidth, screenHeight, screenDc, 0, 0, SRCCOPY))
            {
                return null;
            }

            var source = Imaging.CreateBitmapSourceFromHBitmap(
                bitmap, IntPtr.Zero, System.Windows.Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());

            var targetHeight = Math.Max(1, (int)Math.Round(TargetWidth * ((double)screenHeight / screenWidth)));
            var scaled = new TransformedBitmap(
                source, new ScaleTransform((double)TargetWidth / screenWidth, (double)targetHeight / screenHeight));

            var encoder = new JpegBitmapEncoder { QualityLevel = JpegQuality };
            encoder.Frames.Add(BitmapFrame.Create(scaled));

            using var stream = new MemoryStream();
            encoder.Save(stream);
            return stream.ToArray();
        }
        finally
        {
            if (oldBitmap != IntPtr.Zero)
            {
                SelectObject(memDc, oldBitmap);
            }

            if (bitmap != IntPtr.Zero)
            {
                DeleteObject(bitmap);
            }

            if (memDc != IntPtr.Zero)
            {
                DeleteDC(memDc);
            }

            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Stop();
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int width, int height);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hObject);

    [DllImport("gdi32.dll")]
    private static extern bool BitBlt(
        IntPtr hdcDest, int xDest, int yDest, int width, int height,
        IntPtr hdcSrc, int xSrc, int ySrc, int rop);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);
}
