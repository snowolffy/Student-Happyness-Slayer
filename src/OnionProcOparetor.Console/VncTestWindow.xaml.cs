using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OnionProcOparetor.Console.Models;
using OnionProcOparetor.Console.Services;

namespace OnionProcOparetor.Console;

/// <summary>
/// หน้าต่างทดสอบชั่วคราวสำหรับ Step 4c เท่านั้น (ไม่ใช่ production UI ตามที่ตกลง) - กด CONNECT แล้ว:
/// 1. สั่ง command "StartVncSession" ไปยัง Agent เครื่องเป้าหมาย
/// 2. รอ Agent ตอบกลับ port/password ผ่าน SignalR (LabHubClient.VncSessionReady/VncSessionFailed)
/// 3. ต่อ RfbClient ตรงไปยัง IP:port นั้น ทำ handshake ให้ครบ (auth + ServerInit) - Step 4b
/// 4. เริ่ม RunFramebufferUpdateLoopAsync บน background thread แล้ว render แต่ละ rectangle ที่ decode
///    ได้ลง WriteableBitmap ผูกกับ Image control เพื่อดูภาพหน้าจอเครื่องเป้าหมายสด (view-only ยังไม่รับ
///    input ใดๆ - Step 4d+) - ไม่ embed เข้า Dashboard จริง
/// </summary>
public partial class VncTestWindow : Window
{
    private static readonly TimeSpan SessionReadyTimeout = TimeSpan.FromSeconds(15);

    private readonly ApiClient _apiClient;
    private readonly LabHubClient _labHubClient;
    private readonly ClientMachineDto _client;

    private RfbClient? _rfbClient;
    private WriteableBitmap? _screenBitmap;
    private CancellationTokenSource? _streamCts;

    /// <summary>true หลัง stream framebuffer เริ่มทำงานสำเร็จ - ปิดปุ่ม Connect ค้างไว้ (test window ชั่วคราว ไม่รองรับ reconnect ระหว่างสตรีม ปิดหน้าต่างเพื่อหยุดแทน)</summary>
    private bool _streamStarted;

    public VncTestWindow(ApiClient apiClient, LabHubClient labHubClient, ClientMachineDto client)
    {
        InitializeComponent();
        _apiClient = apiClient;
        _labHubClient = labHubClient;
        _client = client;

        HeaderText.Text = $"VNC Screen View Test - {client.MachineName}";
    }

    private async void ConnectButton_Click(object sender, RoutedEventArgs e)
    {
        ConnectButton.IsEnabled = false;
        LogTextBox.Clear();
        ResultText.Text = "";

        await RunTestAsync();

        if (!_streamStarted)
        {
            ConnectButton.IsEnabled = true;
        }
    }

    private async Task RunTestAsync()
    {
        if (string.IsNullOrWhiteSpace(_client.LastKnownIp))
        {
            AppendLog("ไม่มี IP ของเครื่องนี้ (LastKnownIp ว่าง) - ต้องรอ Agent poll เข้ามาอีกครั้งหลัง Server เริ่มบันทึก IP (ดู /api/poll)");
            ResultText.Text = "ล้มเหลว: ไม่ทราบ IP เป้าหมาย";
            return;
        }

        AppendLog($"กำลังขอให้ Agent (IP: {_client.LastKnownIp}) เปิด VNC session ผ่าน command StartVncSession...");

        var sessionResult = await RequestVncSessionAsync();
        if (!sessionResult.Success)
        {
            AppendLog($"ขอเปิด VNC session ไม่สำเร็จ: {sessionResult.FailureReason}");
            ResultText.Text = $"ล้มเหลว: {sessionResult.FailureReason}";
            return;
        }

        AppendLog($"Agent เปิด VNC session สำเร็จ - Port: {sessionResult.Port} (password ไม่โชว์ใน log)");

        var rfbClient = new RfbClient();
        rfbClient.LogMessage += AppendLog;

        var handshakeResult = await rfbClient.ConnectAndHandshakeAsync(
            _client.LastKnownIp, sessionResult.Port, sessionResult.Password!);

        if (!handshakeResult.Success)
        {
            ResultText.Text = $"ล้มเหลว: {handshakeResult.FailureReason}";
            rfbClient.Dispose();
            return;
        }

        ResultText.Text = $"เชื่อมต่อสำเร็จ - Resolution: {handshakeResult.FramebufferWidth}x{handshakeResult.FramebufferHeight}, " +
                           $"Desktop: \"{handshakeResult.DesktopName}\" - กำลังสตรีมภาพหน้าจอ...";

        _screenBitmap = new WriteableBitmap(
            handshakeResult.FramebufferWidth, handshakeResult.FramebufferHeight, 96, 96, PixelFormats.Bgr32, null);
        ScreenImage.Source = _screenBitmap;

        _rfbClient = rfbClient;
        _streamCts = new CancellationTokenSource();
        _streamStarted = true;

        // Task.Run เพื่อให้ loop ข้างในรันบน thread pool thread จริง (ไม่มี WPF SynchronizationContext
        // ผูกอยู่) - ถ้าเรียกตรงๆ จาก continuation ของ UI-thread async method เฉยๆ await ข้างในจะถูก
        // marshal กลับ UI thread เองอัตโนมัติทุกครั้ง ทำให้ decode/callback ไปรันบน UI thread โดยไม่ตั้งใจ
        // และบล็อก UI ระหว่างรอ network I/O ได้
        _ = Task.Run(() => StreamFramebufferAsync(rfbClient, _streamCts.Token));
    }

    private async Task StreamFramebufferAsync(RfbClient rfbClient, CancellationToken ct)
    {
        try
        {
            await rfbClient.RunFramebufferUpdateLoopAsync(OnRectangleDecoded, ct);
        }
        catch (OperationCanceledException)
        {
            // ปิดหน้าต่างหรือ cancel ปกติ ไม่ต้องรายงานเป็น error
        }
        catch (Exception ex)
        {
            Dispatcher.Invoke(() =>
            {
                AppendLog($"Framebuffer streaming หยุดเพราะ error: {ex.Message}");
                ResultText.Text = $"สตรีมภาพหยุดกะทันหัน: {ex.Message}";
            });
        }
    }

    /// <summary>
    /// เรียกจาก background thread ของ StreamFramebufferAsync (ดู comment ที่จุดเรียก Task.Run) - decode
    /// เสร็จแล้วตั้งแต่ RfbClient (PixelData ตรงกับ Bgr32 อยู่แล้ว ไม่ต้องแปลงอะไรเพิ่ม) เหลือแค่ marshal
    /// การเขียนลง WriteableBitmap กลับ UI thread เท่านั้นตามที่ต้องระวัง - WritePixels(Int32Rect, ...)
    /// เป็น convenience overload ที่ทำ Lock/AddDirtyRect/Unlock ให้ครบในตัวอยู่แล้ว
    /// </summary>
    private void OnRectangleDecoded(RfbRectangle rect)
    {
        if (rect.Width == 0 || rect.Height == 0)
        {
            return;
        }

        Dispatcher.Invoke(() =>
        {
            if (_screenBitmap is null)
            {
                return;
            }

            var area = new Int32Rect(rect.X, rect.Y, rect.Width, rect.Height);
            var stride = rect.Width * 4;
            _screenBitmap.WritePixels(area, rect.PixelData, stride, 0);
        });
    }

    /// <summary>
    /// ส่ง command แล้วรอผลผ่าน LabHubClient event (มี timeout กันค้างตลอดกาลถ้า Agent ไม่ตอบเลย เช่น
    /// offline อยู่จริง หรือ SignalR หลุดพอดี) - subscribe/unsubscribe event ชั่วคราวเฉพาะช่วงรอนี้เท่านั้น
    /// </summary>
    private async Task<(bool Success, int Port, string? Password, string? FailureReason)> RequestVncSessionAsync()
    {
        var tcs = new TaskCompletionSource<(bool, int, string?, string?)>(TaskCreationOptions.RunContinuationsAsynchronously);

        void OnReady(int port, string password) => tcs.TrySetResult((true, port, password, null));
        void OnFailed(string reason) => tcs.TrySetResult((false, 0, null, reason));

        _labHubClient.VncSessionReady += OnReady;
        _labHubClient.VncSessionFailed += OnFailed;

        try
        {
            var sent = await _apiClient.SendCommandAsync(_client.ClientGuid, "StartVncSession",
                new { requestingConsoleSessionId = _labHubClient.SessionId });

            if (!sent)
            {
                return (false, 0, null, "ส่ง command StartVncSession ไปยัง Server ไม่สำเร็จ (HTTP)");
            }

            var completed = await Task.WhenAny(tcs.Task, Task.Delay(SessionReadyTimeout));
            if (completed != tcs.Task)
            {
                return (false, 0, null, $"รอ Agent ตอบกลับเกิน {SessionReadyTimeout.TotalSeconds} วิ (SignalR อาจไม่ connected หรือ Agent offline)");
            }

            return await tcs.Task;
        }
        finally
        {
            _labHubClient.VncSessionReady -= OnReady;
            _labHubClient.VncSessionFailed -= OnFailed;
        }
    }

    /// <summary>
    /// เรียกได้จากทั้ง UI thread (handshake ตอน Step 4b ยังรันตรงจาก UI-thread continuation) และจาก
    /// background thread (RfbClient.LogMessage ที่ raise จากใน RunFramebufferUpdateLoopAsync ซึ่งตั้งใจ
    /// ย้ายไปรันผ่าน Task.Run ใน Step 4c - ดู comment ที่จุดเรียก Task.Run ใน RunTestAsync) - เคย crash
    /// จริงตอนเทส Step 4c เพราะ LogTextBox.AppendText ถูกเรียกจาก background thread ตรงๆ โดยไม่ผ่าน
    /// Dispatcher ("The calling thread cannot access this object because a different thread owns it")
    /// จึงต้องเช็ค CheckAccess ก่อนเสมอ ไม่ใช่พึ่งว่าผู้เรียกอยู่ thread ไหน
    /// </summary>
    private void AppendLog(string message)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => AppendLog(message));
            return;
        }

        LogTextBox.AppendText($"{DateTime.Now:HH:mm:ss.fff}  {message}\n");
        LogTextBox.ScrollToEnd();
        Services.ConsoleDiagnostics.WriteLine($"[VncTestWindow] {message}");
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void VncTestWindow_OnClosed(object? sender, EventArgs e)
    {
        _streamCts?.Cancel();
        _rfbClient?.Dispose();
    }
}
