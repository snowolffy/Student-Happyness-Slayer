using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using OnionProcOparetor.Console.Services;

namespace OnionProcOparetor.Console;

/// <summary>
/// UI-9 (2026-11-04) - หน้าต่างแผนที่เล็กของโหมดซูม (ดู doc comment เต็มใน .xaml) เจ้าของอายุ:
/// BroadcastWarningBarWindow ผ่าน TeacherDrawToolbarWindow (สร้าง/ปิดคู่กับปุ่มซูมในกล่องเครื่องมือ)
/// </summary>
public partial class TeacherZoomMinimapWindow : Window
{
    private const double MapWidth = 220;
    private const double MapHeight = 124;
    private const int RefreshIntervalMs = 400;

    private readonly ScreenCaptureService _captureService;
    private readonly DispatcherTimer _refreshTimer;
    private bool _isPanning;
    private bool _allowClose;

    public TeacherZoomMinimapWindow(ScreenCaptureService captureService)
    {
        InitializeComponent();
        _captureService = captureService;

        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right - 260;
        Top = workArea.Top + 220;

        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(RefreshIntervalMs) };
        _refreshTimer.Tick += (_, _) => RefreshThumbnailAndOverlay();

        SourceInitialized += (_, _) =>
        {
            var excluded = TeacherOnlyWindowHelper.ExcludeFromCapture(this);
            ConsoleDiagnostics.WriteLine(
                $"[teacher-zoom-minimap] SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE) = {excluded}");

            SizeChanged += (_, _) => TeacherOnlyWindowHelper.ApplyRoundedRegion(this, 10);
        };

        Loaded += (_, _) =>
        {
            _refreshTimer.Start();
            RefreshThumbnailAndOverlay();
        };
    }

    private void DragHandle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        DragMove();
        ClampToWorkArea();
    }

    /// <summary>ง้างตำแหน่งหน้าต่างให้อยู่ใน WorkArea เสมอ - เหมือน TeacherDrawToolbarWindow (บั๊กจริงเดียวกันที่เคยเจอกับปุ่มกลม/แผงเครื่องมือ)</summary>
    private void ClampToWorkArea()
    {
        var workArea = SystemParameters.WorkArea;

        if (Left + ActualWidth > workArea.Right)
        {
            Left = Math.Max(workArea.Left, workArea.Right - ActualWidth);
        }
        if (Top + ActualHeight > workArea.Bottom)
        {
            Top = Math.Max(workArea.Top, workArea.Bottom - ActualHeight);
        }
        if (Left < workArea.Left)
        {
            Left = workArea.Left;
        }
        if (Top < workArea.Top)
        {
            Top = workArea.Top;
        }
    }

    private void MapGrid_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _isPanning = true;
        MapGrid.CaptureMouse();
        PanToPoint(e.GetPosition(MapGrid));
    }

    private void MapGrid_MouseMove(object sender, MouseEventArgs e)
    {
        if (_isPanning)
        {
            PanToPoint(e.GetPosition(MapGrid));
        }
    }

    private void MapGrid_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _isPanning = false;
        MapGrid.ReleaseMouseCapture();
    }

    /// <summary>แปลงพิกัดในกรอบแผนที่ (0..MapWidth/Height) เป็นพิกัดจอจริง แล้วปักหมุดเป็นจุดกึ่งกลางซูมใหม่</summary>
    private void PanToPoint(Point localPoint)
    {
        var screenWidth = SystemParameters.PrimaryScreenWidth;
        var screenHeight = SystemParameters.PrimaryScreenHeight;
        var x = (int)Math.Clamp(localPoint.X / MapWidth * screenWidth, 0, screenWidth);
        var y = (int)Math.Clamp(localPoint.Y / MapHeight * screenHeight, 0, screenHeight);
        _captureService.SetManualPanCenter(x, y);
    }

    /// <summary>
    /// ถ่ายภาพย่อของจอทั้งจอใหม่ (คนละเฟรมจากที่ส่งให้นักเรียนจริง - แค่พอให้ครูเห็นภาพรวม) แล้ววาดกรอบทับ
    /// ตำแหน่ง/ขนาดของพื้นที่ซูมปัจจุบัน - เรียกทุก RefreshIntervalMs จาก DispatcherTimer (UI thread) BitBlt
    /// ใช้เวลาหลัก ms เท่านั้นจึงไม่ทำ async แยกเทรดเพิ่ม (ยอมรับได้ที่ 400ms/ครั้ง)
    /// </summary>
    private void RefreshThumbnailAndOverlay()
    {
        var jpeg = ScreenCaptureService.CapturePrimaryScreenJpeg(null);
        if (jpeg is not null)
        {
            var bitmap = new BitmapImage();
            using (var stream = new MemoryStream(jpeg))
            {
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = stream;
                bitmap.EndInit();
            }
            MapImage.Source = bitmap;
        }

        var crop = _captureService.LastCropRegion;
        if (crop is { } region)
        {
            var screenWidth = SystemParameters.PrimaryScreenWidth;
            var screenHeight = SystemParameters.PrimaryScreenHeight;
            var scaleX = MapWidth / screenWidth;
            var scaleY = MapHeight / screenHeight;

            System.Windows.Controls.Canvas.SetLeft(CropRect, region.Left * scaleX);
            System.Windows.Controls.Canvas.SetTop(CropRect, region.Top * scaleY);
            CropRect.Width = Math.Max(1, (region.Right - region.Left) * scaleX);
            CropRect.Height = Math.Max(1, (region.Bottom - region.Top) * scaleY);
            CropRect.Visibility = Visibility.Visible;
        }
        else
        {
            CropRect.Visibility = Visibility.Collapsed;
        }
    }

    public void ForceClose()
    {
        _refreshTimer.Stop();
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

        _refreshTimer.Stop();
        base.OnClosing(e);
    }
}
