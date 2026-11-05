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
    private const int SM_CXCURSOR = 13;
    private const int CURSOR_SHOWING = 0x00000001;
    private const int DI_NORMAL = 0x0003;

    private Timer? _timer;
    private volatile bool _captureInFlight;
    private bool _disposed;

    /// <summary>
    /// คำขอเพิ่ม 3 พ.ย. 2569 ("Zoom แบบแว่นขยาย") - true = ย่อ crop รอบตำแหน่งเมาส์ของครูแทนจับทั้งจอ
    /// นักเรียนเห็นภาพซูมตามครู real-time เพราะเฟรมที่ส่งไปคือภาพที่ครอปมาแล้วตรงๆ (ไม่มี pan/zoom
    /// control ฝั่งนักเรียนเลยตามที่ตัดสินไว้ - นักเรียนได้แต่ดูเฟรมที่ส่งมา)
    /// </summary>
    public volatile bool ZoomEnabled;

    /// <summary>UI-9 (2026-11-04): ระดับซูม 1-5 เท่า ปรับได้จาก slider/ช่องกรอกตัวเลขในกล่องเครื่องมือ (เดิมคงที่ 3 เท่า) - อ่าน/เขียนได้จากเทรดอื่น (toolbox UI thread) double เป็น volatile ไม่ได้ จึงใช้ Interlocked.Exchange ผ่าน bit-pattern แทน</summary>
    private long _zoomLevelBits = BitConverter.DoubleToInt64Bits(3.0);

    public double ZoomLevel
    {
        get => BitConverter.Int64BitsToDouble(Interlocked.Read(ref _zoomLevelBits));
        set => Interlocked.Exchange(ref _zoomLevelBits, BitConverter.DoubleToInt64Bits(Math.Clamp(value, 1.0, 5.0)));
    }

    /// <summary>
    /// UI-9 (2026-11-04): ตำแหน่ง (พิกัดจอจริง) ที่ครูลากกรอบในหน้าต่างแผนที่เล็กไปปักหมุดไว้ - ถ้าไม่ null
    /// จะใช้แทนตำแหน่งเมาส์ปัจจุบัน (GetCursorPos) เป็นจุดกึ่งกลางของพื้นที่ซูม จนกว่าจะถูกเคลียร์
    /// (ClearManualPan) หรือตั้งค่าใหม่ - ให้ครูลากดูมุมอื่นของจอได้โดยไม่ต้องขยับเมาส์จริงทับเนื้อหา
    /// </summary>
    private (int X, int Y)? _manualPanCenter;
    private readonly object _manualPanGate = new();

    public void SetManualPanCenter(int x, int y)
    {
        lock (_manualPanGate)
        {
            _manualPanCenter = (x, y);
        }
    }

    public void ClearManualPan()
    {
        lock (_manualPanGate)
        {
            _manualPanCenter = null;
        }
    }

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
            var cropRegion = ZoomEnabled ? ComputeZoomCropRegion() : (RECT?)null;
            LastCropRegion = cropRegion;
            var jpeg = CapturePrimaryScreenJpeg(cropRegion);
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

    /// <summary>UI-9 (2026-11-04): พื้นที่ crop ของเฟรมล่าสุด (null = ไม่ได้อยู่โหมดซูม หรือยังไม่เคย capture) - ให้หน้าต่างแผนที่เล็ก (TeacherZoomMinimapWindow) อ่านไปวาดกรอบทับภาพรวมจอ</summary>
    public RECT? LastCropRegion { get; private set; }

    /// <summary>
    /// คำนวณพื้นที่ crop สำหรับโหมด zoom - ปกติใช้ตำแหน่งเมาส์ปัจจุบันของครู (GetCursorPos) เป็นจุดกึ่งกลาง
    /// แต่ถ้าครูลากกรอบในแผนที่เล็กปักหมุดตำแหน่งไว้ (SetManualPanCenter) จะใช้จุดนั้นแทนจนกว่าจะเคลียร์
    /// (UI-9 คำขอ "ลากกรอบในแผนที่เพื่อย้ายตำแหน่งได้") ขนาด crop = ความกว้าง/สูงจอจริงหารด้วย ZoomLevel
    /// (ปรับได้ 1-5 เท่า ต่างจากเดิมที่ตายตัว 3 เท่า) แล้ว clamp ไม่ให้เกินขอบจอ (ไม่งั้น BitBlt จากพื้นที่
    /// นอกจอจะได้ภาพเพี้ยน/ดำ) คืน null ถ้าอ่านตำแหน่งเมาส์ไม่สำเร็จและไม่มีจุดปักหมุด (ใช้จับภาพทั้งจอแทน)
    /// </summary>
    private RECT? ComputeZoomCropRegion()
    {
        var screenWidth = GetSystemMetrics(SM_CXSCREEN);
        var screenHeight = GetSystemMetrics(SM_CYSCREEN);
        if (screenWidth <= 0 || screenHeight <= 0)
        {
            return null;
        }

        int centerX, centerY;
        (int X, int Y)? manualPan;
        lock (_manualPanGate)
        {
            manualPan = _manualPanCenter;
        }

        if (manualPan is { } pinned)
        {
            centerX = pinned.X;
            centerY = pinned.Y;
        }
        else if (GetCursorPos(out var cursor))
        {
            centerX = cursor.X;
            centerY = cursor.Y;
        }
        else
        {
            return null;
        }

        var zoomLevel = ZoomLevel;
        var cropWidth = Math.Max(1, (int)Math.Round(screenWidth / zoomLevel));
        var cropHeight = Math.Max(1, (int)Math.Round(screenHeight / zoomLevel));

        var left = Math.Clamp(centerX - cropWidth / 2, 0, Math.Max(0, screenWidth - cropWidth));
        var top = Math.Clamp(centerY - cropHeight / 2, 0, Math.Max(0, screenHeight - cropHeight));

        return new RECT { Left = left, Top = top, Right = left + cropWidth, Bottom = top + cropHeight };
    }

    /// <summary>
    /// capture จอหลักเต็มความละเอียดจริงด้วย BitBlt แล้วย่อ+เข้ารหัส JPEG - static เพราะไม่มี state (ไว้เรียกตรงจาก test harness ได้โดยไม่ต้องสร้าง ScreenCaptureService ทั้งก้อน)
    /// cropRegion (คำขอเพิ่ม 3 พ.ย. 2569 - โหมด zoom): ไม่ null = BitBlt มาจากแค่พื้นที่นี้ของจอจริง แล้ว
    /// สเกลขยายให้เต็ม TargetWidth เท่าเดิม (ภาพเลยดู "ซูมเข้า") null = capture ทั้งจอตามปกติ
    /// </summary>
    public static byte[]? CapturePrimaryScreenJpeg(RECT? cropRegion = null, bool drawCursor = true)
    {
        var fullScreenWidth = GetSystemMetrics(SM_CXSCREEN);
        var fullScreenHeight = GetSystemMetrics(SM_CYSCREEN);
        if (fullScreenWidth <= 0 || fullScreenHeight <= 0)
        {
            return null;
        }

        var srcX = cropRegion?.Left ?? 0;
        var srcY = cropRegion?.Top ?? 0;
        var captureWidth = cropRegion is { } r ? Math.Max(1, r.Right - r.Left) : fullScreenWidth;
        var captureHeight = cropRegion is { } r2 ? Math.Max(1, r2.Bottom - r2.Top) : fullScreenHeight;

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
            bitmap = CreateCompatibleBitmap(screenDc, captureWidth, captureHeight);
            if (memDc == IntPtr.Zero || bitmap == IntPtr.Zero)
            {
                return null;
            }

            oldBitmap = SelectObject(memDc, bitmap);
            if (!BitBlt(memDc, 0, 0, captureWidth, captureHeight, screenDc, srcX, srcY, SRCCOPY))
            {
                return null;
            }

            if (drawCursor)
            {
                DrawCursorOnto(memDc, srcX, srcY, captureWidth, captureHeight);
            }

            // UI-12: ไม่ใช้ Imaging.CreateBitmapSourceFromHBitmap อีกแล้ว - DrawIconEx ลง DDB 32 บิตเขียน alpha=0 ในพื้นที่เคอร์เซอร์
            // (ทดสอบจริง: เคอร์เซอร์ I-beam กลายเป็นกล่องดำ 32x32 เมื่อ WPF ตีความ alpha) จึงดึงพิกเซลดิบด้วย GetDIBits แล้วสร้าง Bgr32 (ไม่สนใจ alpha)
            // GetDIBits ต้องการ bitmap ที่ "ไม่ได้ถูก select อยู่ใน DC" - คืน bitmap เดิมก่อน
            SelectObject(memDc, oldBitmap);
            oldBitmap = IntPtr.Zero;
            var source = CreateOpaqueBitmapSource(screenDc, bitmap, captureWidth, captureHeight);
            if (source is null)
            {
                return null;
            }

            var targetHeight = Math.Max(1, (int)Math.Round(TargetWidth * ((double)captureHeight / captureWidth)));
            var scaled = new TransformedBitmap(
                source, new ScaleTransform((double)TargetWidth / captureWidth, (double)targetHeight / captureHeight));

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

    private static BitmapSource? CreateOpaqueBitmapSource(IntPtr hdc, IntPtr bitmap, int width, int height)
    {
        var header = new BITMAPINFOHEADER
        {
            biSize = Marshal.SizeOf<BITMAPINFOHEADER>(),
            biWidth = width,
            biHeight = -height, // ลบ = top-down
            biPlanes = 1,
            biBitCount = 32,
            biCompression = 0 // BI_RGB
        };

        var stride = width * 4;
        var pixels = new byte[stride * height];
        if (GetDIBits(hdc, bitmap, 0, (uint)height, pixels, ref header, 0 /* DIB_RGB_COLORS */) == 0)
        {
            return null;
        }

        var result = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgr32, null, pixels, stride);
        result.Freeze();
        return result;
    }

    /// <summary>
    /// UI-12 (2026-11-05): BitBlt ไม่เก็บเคอร์เซอร์เมาส์ - นักเรียนเลยไม่เห็นว่าครูชี้อะไรอยู่ จึงวาดเคอร์เซอร์ลง memory DC
    /// หลัง BitBlt ด้วย GetCursorInfo + DrawIconEx โดยหัก hotspot (GetIconInfo) และหักมุม crop ของโหมดซูม
    /// (srcX/srcY) - วาดที่ขนาดจริงในพิกัดก่อนย่อ ดังนั้นการสเกลภายหลัง (ย่อเป็น TargetWidth/ขยายตอนซูม) กระทบเคอร์เซอร์เท่ากับภาพ
    /// ถ้าเคอร์เซอร์ซ่อนอยู่ (CURSOR_SHOWING ไม่ติด) หรืออยู่นอกพื้นที่ที่จับ = ไม่วาด ล้มเหลวเงียบ ๆ (ภาพไร้เคอร์เซอร์ดีกว่าไม่มีภาพ)
    /// </summary>
    private static void DrawCursorOnto(IntPtr memDc, int srcX, int srcY, int captureWidth, int captureHeight)
    {
        var info = new CURSORINFO { cbSize = Marshal.SizeOf<CURSORINFO>() };
        if (!GetCursorInfo(ref info) || (info.flags & CURSOR_SHOWING) == 0 || info.hCursor == IntPtr.Zero)
        {
            return;
        }

        if (!GetIconInfo(info.hCursor, out var iconInfo))
        {
            return;
        }

        try
        {
            // GetIconInfo สร้าง bitmap ใหม่ 2 ตัว (mask/color) ให้เราเป็นเจ้าของ - ต้องลบเองทั้งคู่ ไม่งั้น GDI object รั่วทุกเฟรม (8 เฟรม/วินาที)
            var x = info.ptScreenPos.X - (int)iconInfo.xHotspot - srcX;
            var y = info.ptScreenPos.Y - (int)iconInfo.yHotspot - srcY;

            // ขนาดเคอร์เซอร์: DrawIconEx ขนาด 0 = ขนาดจริงของทรัพยากร - ใช้ SM_CXCURSOR เป็นกรอบหยาบ ๆ ไว้ข้ามตอนอยู่นอกพื้นที่เท่านั้น
            var cursorSize = Math.Max(GetSystemMetrics(SM_CXCURSOR), 64);
            if (x >= captureWidth || y >= captureHeight || x + cursorSize <= 0 || y + cursorSize <= 0)
            {
                return;
            }

            DrawIconEx(memDc, x, y, info.hCursor, 0, 0, 0, IntPtr.Zero, DI_NORMAL);
        }
        finally
        {
            if (iconInfo.hbmMask != IntPtr.Zero)
            {
                DeleteObject(iconInfo.hbmMask);
            }

            if (iconInfo.hbmColor != IntPtr.Zero)
            {
                DeleteObject(iconInfo.hbmColor);
            }
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

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT point);

    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CURSORINFO
    {
        public int cbSize;
        public int flags;
        public IntPtr hCursor;
        public POINT ptScreenPos;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ICONINFO
    {
        public bool fIcon;
        public uint xHotspot;
        public uint yHotspot;
        public IntPtr hbmMask;
        public IntPtr hbmColor;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public int biSize;
        public int biWidth;
        public int biHeight;
        public short biPlanes;
        public short biBitCount;
        public int biCompression;
        public int biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public int biClrUsed;
        public int biClrImportant;
    }

    [DllImport("gdi32.dll")]
    private static extern int GetDIBits(
        IntPtr hdc, IntPtr hbm, uint start, uint cLines, byte[] lpvBits, ref BITMAPINFOHEADER lpbmi, uint usage);

    [DllImport("user32.dll")]
    private static extern bool GetCursorInfo(ref CURSORINFO pci);

    [DllImport("user32.dll")]
    private static extern bool GetIconInfo(IntPtr hIcon, out ICONINFO piconinfo);

    [DllImport("user32.dll")]
    private static extern bool DrawIconEx(
        IntPtr hdc, int xLeft, int yTop, IntPtr hIcon, int cxWidth, int cyWidth,
        int istepIfAniCur, IntPtr hbrFlickerFreeDraw, int diFlags);
}

/// <summary>พื้นที่ crop บนจอจริง (พิกเซล, สัมบูรณ์) - ใช้กับโหมด zoom เท่านั้น (ดู ComputeZoomCropRegion)</summary>
public struct RECT
{
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;
}
