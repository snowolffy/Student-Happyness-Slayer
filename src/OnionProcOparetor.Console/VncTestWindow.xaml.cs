using System.Windows;
using OnionProcOparetor.Console.Models;
using OnionProcOparetor.Console.Services;

namespace OnionProcOparetor.Console;

/// <summary>
/// หน้าต่างทดสอบชั่วคราวสำหรับ Step 4b เท่านั้น (ไม่ใช่ production UI ตามที่ตกลง) - กด CONNECT แล้ว:
/// 1. สั่ง command "StartVncSession" ไปยัง Agent เครื่องเป้าหมาย
/// 2. รอ Agent ตอบกลับ port/password ผ่าน SignalR (LabHubClient.VncSessionReady/VncSessionFailed)
/// 3. ต่อ RfbClient ตรงไปยัง IP:port นั้น ทำ handshake ให้ครบ (auth + ServerInit)
/// 4. รายงานผล resolution/desktop name หรือ failure reason - ไม่ render ภาพใดๆ (Step 4c/4d)
/// </summary>
public partial class VncTestWindow : Window
{
    private static readonly TimeSpan SessionReadyTimeout = TimeSpan.FromSeconds(15);

    private readonly ApiClient _apiClient;
    private readonly LabHubClient _labHubClient;
    private readonly ClientMachineDto _client;

    public VncTestWindow(ApiClient apiClient, LabHubClient labHubClient, ClientMachineDto client)
    {
        InitializeComponent();
        _apiClient = apiClient;
        _labHubClient = labHubClient;
        _client = client;

        HeaderText.Text = $"VNC Handshake Test - {client.MachineName}";
    }

    private async void ConnectButton_Click(object sender, RoutedEventArgs e)
    {
        ConnectButton.IsEnabled = false;
        LogTextBox.Clear();
        ResultText.Text = "";

        try
        {
            await RunTestAsync();
        }
        finally
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

        using var rfbClient = new RfbClient();
        rfbClient.LogMessage += AppendLog;

        var handshakeResult = await rfbClient.ConnectAndHandshakeAsync(
            _client.LastKnownIp, sessionResult.Port, sessionResult.Password!);

        if (handshakeResult.Success)
        {
            ResultText.Text = $"สำเร็จ - Resolution: {handshakeResult.FramebufferWidth}x{handshakeResult.FramebufferHeight}, Desktop: \"{handshakeResult.DesktopName}\"";
        }
        else
        {
            ResultText.Text = $"ล้มเหลว: {handshakeResult.FailureReason}";
        }
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
    /// เรียกได้ทั้งจาก UI thread ตรงๆ (RunTestAsync's own log) และจาก RfbClient.LogMessage event ซึ่ง raise
    /// จาก continuation หลัง await ภายใน method เดียวกันนี้ - เพราะเรียกจาก UI-thread-initiated async
    /// method (ConnectButton_Click) เสมอ WPF's SynchronizationContext จะ marshal continuation กลับ UI
    /// thread ให้อัตโนมัติอยู่แล้ว ไม่ต้อง Dispatcher.Invoke ซ้ำ
    /// </summary>
    private void AppendLog(string message)
    {
        LogTextBox.AppendText($"{DateTime.Now:HH:mm:ss.fff}  {message}\n");
        LogTextBox.ScrollToEnd();
        Services.ConsoleDiagnostics.WriteLine($"[VncTestWindow] {message}");
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
