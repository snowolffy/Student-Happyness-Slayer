using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OnionProcOparetor.Console.Models;
using OnionProcOparetor.Console.Services;

namespace OnionProcOparetor.Console;

/// <summary>
/// ชั่วคราวสำหรับยืนยัน VncSessionPool + RfbClient Thumbnail mode ที่เพิ่มใน Step 4e เท่านั้น (ไม่ใช่
/// production UI - Grid จริงเก็บไว้ Step 4f) เปิดทุกเครื่องที่เลือกไว้พร้อมกันผ่าน pool เดียวกัน (ทดสอบ
/// two-phase reserve-then-connect + MaxConcurrentSessions) เป็น Thumbnail mode ทั้งหมด (ทดสอบ 16bpp
/// decode/downscale/throttle) มีปุ่มสลับทีละเครื่องเป็น FullRes เพื่อยืนยันว่า RequestModeSwitch ไม่
/// reconnect ใหม่จริง (TCP connection/handshake เดิม)
/// </summary>
public partial class VncPoolTestWindow : Window
{
    private sealed class SessionTile
    {
        public required Border Border { get; init; }
        public required TextBlock StatusText { get; init; }
        public required Image Image { get; init; }
        public required Button ToggleModeButton { get; init; }
        public WriteableBitmap? Bitmap { get; set; }
        public VncViewMode? BitmapMode { get; set; }
    }

    private readonly VncSessionPool _pool;
    private readonly List<ClientMachineDto> _clients;
    private readonly Dictionary<string, SessionTile> _tiles = new();

    public VncPoolTestWindow(ApiClient apiClient, LabHubClient labHubClient, List<ClientMachineDto> clients)
    {
        InitializeComponent();
        _pool = new VncSessionPool(apiClient, labHubClient);
        _clients = clients;

        HeaderText.Text = $"VNC Pool Test - {clients.Count} เครื่อง (Thumbnail mode ทั้งหมด, จำกัดพร้อมกันได้ {VncSessionPool.MaxConcurrentSessions} เครื่อง)";

        Loaded += (_, _) => OpenAll();
    }

    /// <summary>
    /// ยิงเปิดทุกเครื่องพร้อมกันโดยตั้งใจ (ไม่ await ทีละตัวเรียงคิว) - จุดประสงค์หลักของการเทสรอบนี้คือ
    /// ยืนยันว่า VncSessionPool.OpenAsync หลายตัวพร้อมกันไม่ชนกัน (two-phase reserve-then-connect)
    /// </summary>
    private void OpenAll()
    {
        foreach (var client in _clients)
        {
            var tile = CreateTile(client);
            SessionsPanel.Children.Add(tile.Border);
            _tiles[client.ClientGuid] = tile;

            _ = OpenSessionAsync(client, tile);
        }
    }

    private async Task OpenSessionAsync(ClientMachineDto client, SessionTile tile)
    {
        var (success, handle, failureReason) = await _pool.OpenAsync(client, VncViewMode.Thumbnail);

        if (!success || handle is null)
        {
            Dispatcher.Invoke(() => tile.StatusText.Text = $"ปฏิเสธ: {failureReason}");
            return;
        }

        handle.StatusChanged += () => Dispatcher.Invoke(() => OnStatusChanged(tile, handle));
        handle.RectangleDecoded += rect => OnRectangleDecoded(tile, handle, rect);

        Dispatcher.Invoke(() => OnStatusChanged(tile, handle));

        tile.ToggleModeButton.Tag = handle;
    }

    private void OnStatusChanged(SessionTile tile, VncSessionHandle handle)
    {
        tile.StatusText.Text = handle.Status switch
        {
            VncSessionStatus.Connecting => "Connecting...",
            VncSessionStatus.Connected => $"Connected ({handle.Mode}, {handle.DisplayWidth}x{handle.DisplayHeight})",
            VncSessionStatus.Failed => $"Failed: {handle.FailureReason}",
            VncSessionStatus.Closed => "Closed",
            _ => handle.Status.ToString()
        };

        tile.ToggleModeButton.IsEnabled = handle.Status == VncSessionStatus.Connected;
    }

    /// <summary>
    /// เรียกจาก background thread (RfbClient framebuffer loop ผ่าน VncSessionHandle) - marshal เข้า UI
    /// thread เองเสมอ (บทเรียนจาก Step 4c: ห้ามพึ่งว่าผู้เรียกอยู่ thread ไหน) สร้าง/resize
    /// WriteableBitmap ใหม่ถ้ายังไม่มีหรือ mode เปลี่ยนไปจากตอนสร้างครั้งล่าสุด (ตอนกด TOGGLE MODE)
    /// </summary>
    private void OnRectangleDecoded(SessionTile tile, VncSessionHandle handle, RfbRectangle rect)
    {
        if (rect.Width == 0 || rect.Height == 0)
        {
            return;
        }

        Dispatcher.Invoke(() =>
        {
            if (tile.Bitmap is null || tile.BitmapMode != handle.Mode)
            {
                var width = handle.DisplayWidth ?? rect.Width;
                var height = handle.DisplayHeight ?? rect.Height;
                tile.Bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgr32, null);
                tile.BitmapMode = handle.Mode;
                tile.Image.Source = tile.Bitmap;
            }

            if (rect.X + rect.Width > tile.Bitmap.PixelWidth || rect.Y + rect.Height > tile.Bitmap.PixelHeight)
            {
                return; // เผื่อ rectangle ตกค้างจากช่วงเปลี่ยน mode พอดี (ไม่ควรเกิดได้จริงเพราะ loop เดียว - กันไว้เฉยๆ)
            }

            var area = new Int32Rect(rect.X, rect.Y, rect.Width, rect.Height);
            tile.Bitmap.WritePixels(area, rect.PixelData, rect.Width * 4, 0);
        });
    }

    private SessionTile CreateTile(ClientMachineDto client)
    {
        var image = new Image { Stretch = Stretch.Uniform, Width = 280, Height = 158 };
        var imageBorder = new Border { Background = Brushes.Black, Child = image };

        var nameText = new TextBlock { Text = client.MachineName, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 6, 0, 2) };
        var statusText = new TextBlock { Text = "รอคิว...", FontSize = 11, Margin = new Thickness(0, 0, 0, 6) };

        var toggleButton = new Button { Content = "TOGGLE MODE", Padding = new Thickness(8, 4, 8, 4), FontSize = 11, IsEnabled = false, Margin = new Thickness(0, 0, 4, 0) };
        toggleButton.Click += ToggleModeButton_Click;

        var closeButton = new Button { Content = "CLOSE", Padding = new Thickness(8, 4, 8, 4), FontSize = 11, Tag = client.ClientGuid };
        closeButton.Click += CloseButton_Click;

        var buttonsPanel = new StackPanel { Orientation = Orientation.Horizontal };
        buttonsPanel.Children.Add(toggleButton);
        buttonsPanel.Children.Add(closeButton);

        var content = new StackPanel();
        content.Children.Add(imageBorder);
        content.Children.Add(nameText);
        content.Children.Add(statusText);
        content.Children.Add(buttonsPanel);

        var border = new Border
        {
            BorderBrush = (Brush)FindResource("Brush.Border.Default"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10),
            Margin = new Thickness(0, 0, 10, 10),
            Width = 300,
            Child = content
        };

        return new SessionTile { Border = border, StatusText = statusText, Image = image, ToggleModeButton = toggleButton };
    }

    private void ToggleModeButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: VncSessionHandle handle })
        {
            return;
        }

        var newMode = handle.Mode == VncViewMode.Thumbnail ? VncViewMode.FullRes : VncViewMode.Thumbnail;
        handle.RequestModeSwitch(newMode);
        OnStatusChanged(_tiles[handle.Client.ClientGuid], handle);
    }

    private async void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string clientGuid })
        {
            return;
        }

        await _pool.CloseAsync(clientGuid);

        if (_tiles.TryGetValue(clientGuid, out var tile))
        {
            tile.StatusText.Text = "Closed";
            tile.ToggleModeButton.IsEnabled = false;
        }
    }

    private async void CloseAllButton_Click(object sender, RoutedEventArgs e)
    {
        await _pool.CloseAllAsync();

        foreach (var tile in _tiles.Values)
        {
            tile.StatusText.Text = "Closed";
            tile.ToggleModeButton.IsEnabled = false;
        }
    }

    private async void VncPoolTestWindow_OnClosed(object? sender, EventArgs e)
    {
        await _pool.CloseAllAsync();
    }
}
