using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using OnionProcOparetor.Console.Models;
using OnionProcOparetor.Console.Services;

namespace OnionProcOparetor.Console;

public partial class DashboardWindow : Window
{
    private static readonly TimeSpan VncSessionReadyTimeout = TimeSpan.FromSeconds(15);

    private readonly ApiClient _apiClient;
    private readonly DispatcherTimer _autoRefreshTimer;
    private readonly LabHubClient _labHubClient = new();

    // ==================== Step 4d: embedded VNC screen view (SCREEN VIEW tab) ====================
    // ทุก field ตรงนี้แตะ/แก้ได้จาก UI thread เท่านั้น (ไม่มี lock ป้องกัน race เพราะทุกจุดที่แก้เป็น
    // UI-thread event handler ทั้งหมด - RfbClient framebuffer loop เองรันบน background thread แยก
    // แต่ callback ของมันเขียนกลับผ่าน Dispatcher.Invoke เท่านั้น) _vncSessionGeneration ใช้กันกรณี
    // await ที่ค้างอยู่ (เช่นกำลังรอ RequestVncSessionAsync/handshake ของเครื่องเก่า) resume มาแล้วโลก
    // เปลี่ยนไปแล้ว (ผู้ใช้สลับเครื่อง/ปิดแท็บระหว่างรอ) - ต้องรู้ตัวว่าเป็นของเก่าแล้วไม่เขียนทับ UI/state
    // ปัจจุบัน (ดู ShowVncViewAsync/StreamVncAsync/OnVncRectangleDecoded)
    private int _vncSessionGeneration;
    private RfbClient? _vncClient;
    private WriteableBitmap? _vncBitmap;
    private CancellationTokenSource? _vncStreamCts;

    public DashboardWindow(ApiClient apiClient)
    {
        InitializeComponent();
        _apiClient = apiClient;
        ServerLabel.Text = $"connected to {_apiClient.BaseUrl}";
        FooterServerText.Text = _apiClient.BaseUrl ?? "-";
        FooterUserText.Text = _apiClient.Username ?? "-";
        Loaded += async (_, _) => await LoadAllDataAsync();

        // auto refresh ทุก 10 วิ จะได้ไม่ต้องกด REFRESH เอง - หยุดเองตอนปิดหน้าต่างกัน
        // เรียก LoadAllDataAsync ทับ ApiClient ที่ปิดไปแล้ว
        _autoRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        _autoRefreshTimer.Tick += async (_, _) => await LoadAllDataAsync();
        _autoRefreshTimer.Start();

        // SignalR เป็นชั้นเสริมคู่ขนานกับ DispatcherTimer ข้างบน (ไม่แทนที่) - ทำให้ตารางเครื่องอัปเดต
        // ไวขึ้นโดยไม่ต้องรอรอบ poll ถ้า SignalR หลุด/ต่อไม่ได้ ตาราง ยังอัปเดตปกติผ่าน auto refresh เดิม
        _labHubClient.ClientStatusChanged += OnClientStatusChangedFromHub;
        _ = _labHubClient.StartAsync(_apiClient.BaseUrl!, CancellationToken.None);

        Closed += async (_, _) =>
        {
            _autoRefreshTimer.Stop();
            DisconnectVncView();
            _labHubClient.ClientStatusChanged -= OnClientStatusChangedFromHub;
            await _labHubClient.DisposeAsync();
        };
    }

    /// <summary>
    /// เรียกจาก SignalR background thread ตอนได้ ClientStatusChanged - update item ที่มีอยู่ใน
    /// _allClients ตาม ClientGuid ที่ match แล้วรีเฟรชตารางผ่าน ApplyClientsFilter เดิม
    /// (ไม่สร้าง binding ใหม่ - ใช้ List + ItemsSource reassign แบบเดียวกับที่ LoadAllDataAsync ทำอยู่แล้ว)
    /// </summary>
    private void OnClientStatusChangedFromHub(ClientMachineDto updated)
    {
        Dispatcher.Invoke(() =>
        {
            var existing = _allClients.FirstOrDefault(c => c.ClientGuid == updated.ClientGuid);
            if (existing is not null)
            {
                existing.MachineName = updated.MachineName;
                existing.LastKnownIp = updated.LastKnownIp;
                existing.IsEnabled = updated.IsEnabled;
                existing.LastSeenAt = updated.LastSeenAt;
                existing.RegisteredAt = updated.RegisteredAt;
                existing.PollIntervalOverrideSeconds = updated.PollIntervalOverrideSeconds;
                existing.IsMissingUnexpectedly = updated.IsMissingUnexpectedly;
            }
            else
            {
                _allClients.Add(updated);
            }

            ApplyClientsFilter();
        });
    }

    private async Task LoadAllDataAsync()
    {
        StatusText.Text = "กำลังโหลดข้อมูล...";
        try
        {
            _allClients = await _apiClient.GetClientsAsync();
            ApplyClientsFilter();

            _allRules = await _apiClient.GetRulesAsync();
            ApplyRulesFilter();

            _allLogs = await _apiClient.GetLogsAsync();
            ApplyLogsFilter();

            var users = await _apiClient.GetUsersAsync();
            UsersGrid.ItemsSource = users;

            StatusText.Text = $"อัพเดตล่าสุด: {DateTime.Now:HH:mm:ss}";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"โหลดข้อมูลไม่สำเร็จ: {ex.Message}";
        }
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        await LoadAllDataAsync();
    }

    private async void GlobalToggleButton_Click(object sender, RoutedEventArgs e)
    {
        var success = await _apiClient.ToggleGlobalAsync();
        StatusText.Text = success ? "Toggle global สำเร็จ" : "Toggle global ไม่สำเร็จ";
        await LoadAllDataAsync();
    }

    /// <summary>
    /// ปุ่มเดียวใน DataGrid row - เปิด ActionPanelWindow โหมด single-client รวม action ทั้งหมดของ
    /// เครื่องนั้น (toggle/settings/lock-unlock/restart/shutdown/delete) แทนที่ ContextMenu เดิม
    /// เพื่อใช้ panel เดียวกับที่ปุ่ม ACTIONS แบบ bulk บน header เปิด (ดู BulkActionsButton_Click)
    /// </summary>
    private async void ClientActionsButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ClientMachineDto client })
        {
            return;
        }

        var panel = new ActionPanelWindow(_apiClient, client) { Owner = this };
        panel.ShowDialog();

        if (panel.DataChanged)
        {
            await LoadAllDataAsync();
        }

        if (panel.ViewScreenRequested is not null)
        {
            await ShowVncViewAsync(panel.ViewScreenRequested);
        }
    }

    private async void ToggleRuleButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: int ruleId })
        {
            var success = await _apiClient.ToggleRuleAsync(ruleId);
            StatusText.Text = success ? "Toggle กฎสำเร็จ" : "Toggle กฎไม่สำเร็จ";
            await LoadAllDataAsync();
        }
    }

    private async void DeleteRuleButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: int ruleId })
        {
            var confirm = System.Windows.MessageBox.Show(
                "ยืนยันการลบกฎนี้? การกระทำนี้ย้อนกลับไม่ได้",
                "ยืนยันการลบ",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes)
            {
                return;
            }

            var success = await _apiClient.DeleteRuleAsync(ruleId);
            StatusText.Text = success ? "ลบกฎสำเร็จ" : "ลบกฎไม่สำเร็จ";
            await LoadAllDataAsync();
        }
    }

    private async void AddRuleButton_Click(object sender, RoutedEventArgs e)
    {
        var name = NewRuleNameBox.Text.Trim();
        var publisher = NewRulePublisherBox.Text.Trim();
        var processNameContains = NewRuleProcessNameContainsBox.Text.Trim();
        var actionCommand = NewRuleActionBox.Text.Trim();
        var killProcess = NewRuleKillCheckBox.IsChecked ?? false;

        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(publisher))
        {
            StatusText.Text = "กรุณากรอกชื่อกฎและ Publisher อย่างน้อย";
            return;
        }

        var rule = new RuleDto
        {
            Name = name,
            PublisherName = publisher,
            ProcessNameContains = string.IsNullOrEmpty(processNameContains) ? null : processNameContains,
            RequireSignedMatch = true,
            KillProcess = killProcess,
            ActionCommand = string.IsNullOrEmpty(actionCommand) ? null : actionCommand,
            IsEnabled = true
        };

        var success = await _apiClient.AddRuleAsync(rule);
        StatusText.Text = success ? "เพิ่มกฎสำเร็จ" : "เพิ่มกฎไม่สำเร็จ";

        if (success)
        {
            NewRuleNameBox.Clear();
            NewRulePublisherBox.Clear();
            NewRuleProcessNameContainsBox.Clear();
            NewRuleActionBox.Clear();
            NewRuleKillCheckBox.IsChecked = false;
        }

        await LoadAllDataAsync();
    }

    private async void AddUserButton_Click(object sender, RoutedEventArgs e)
    {
        var username = NewUserUsernameBox.Text.Trim();
        var password = NewUserPasswordBox.Password;

        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
        {
            StatusText.Text = "กรุณากรอก Username และ Password";
            return;
        }

        if (password.Length < 8)
        {
            StatusText.Text = "Password ต้องมีความยาวอย่างน้อย 8 ตัวอักษร";
            return;
        }

        var (success, message) = await _apiClient.CreateUserAsync(username, password);
        StatusText.Text = message;

        if (success)
        {
            NewUserUsernameBox.Clear();
            NewUserPasswordBox.Clear();
        }

        await LoadAllDataAsync();
    }

    private async void DeleteUserButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: int userId })
        {
            var confirm = System.Windows.MessageBox.Show(
                "ยืนยันการลบ user นี้? การกระทำนี้ย้อนกลับไม่ได้",
                "ยืนยันการลบ",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes)
            {
                return;
            }

            var (success, message) = await _apiClient.DeleteUserAsync(userId);
            StatusText.Text = message;
            await LoadAllDataAsync();
        }
    }

    private void ClientsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var count = ClientsGrid.SelectedItems.Count;
        SelectedClientsCountText.Text = count > 0 ? $"เลือกไว้ {count} เครื่อง" : "";
    }

    private async void BroadcastMessageButton_Click(object sender, RoutedEventArgs e)
    {
        var selected = ClientsGrid.SelectedItems.Cast<ClientMachineDto>().ToList();

        var dialog = new BroadcastMessageWindow(selected.Count) { Owner = this };
        var result = dialog.ShowDialog();

        if (result != true)
        {
            return;
        }

        var targets = dialog.SendToAll ? _allClients : selected;

        if (targets.Count == 0)
        {
            StatusText.Text = "ไม่มีเครื่องให้ส่งข้อความ";
            return;
        }

        StatusText.Text = $"กำลังส่งข้อความไป {targets.Count} เครื่อง...";

        var successCount = 0;
        foreach (var client in targets)
        {
            var payload = new
            {
                message = dialog.MessageText,
                title = string.IsNullOrEmpty(dialog.MessageTitle) ? null : dialog.MessageTitle
            };

            var success = await _apiClient.SendCommandAsync(client.ClientGuid, "BroadcastMessage", payload);
            if (success) successCount++;
        }

        StatusText.Text = $"ส่งข้อความสำเร็จ {successCount}/{targets.Count} เครื่อง";
    }

    /// <summary>
    /// ปุ่ม ACTIONS เดียวบน header (แทนที่ Toggle/Shutdown/Restart Selected เดิม 3 ปุ่มแยก) - เปิด
    /// ActionPanelWindow เดียวกับที่ปุ่ม ACTIONS ต่อแถวใช้ (ดู ClientActionsButton_Click) แต่ปรับเป็น
    /// bulk mode ครอบคลุมทั้ง selected-multiple และ all-clients (เลือกได้ใน panel เอง)
    /// </summary>
    private async void BulkActionsButton_Click(object sender, RoutedEventArgs e)
    {
        var selected = ClientsGrid.SelectedItems.Cast<ClientMachineDto>().ToList();

        if (selected.Count == 0)
        {
            StatusText.Text = "กรุณาเลือกเครื่องอย่างน้อย 1 เครื่อง (Ctrl+คลิก หรือ Shift+คลิก เพื่อเลือกหลายเครื่อง)";
            return;
        }

        var panel = new ActionPanelWindow(_apiClient, selected, _allClients) { Owner = this };
        panel.ShowDialog();

        if (panel.DataChanged)
        {
            await LoadAllDataAsync();
        }
    }

    private async void ResetUserPasswordButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: UserDto user })
        {
            if (user.IsBuiltIn)
            {
                StatusText.Text = "ไม่แนะนำให้ reset password ของ built-in Administrator ด้วยวิธีนี้ ใช้ change password ปกติแทน";
                return;
            }

            var resetWindow = new ResetPasswordWindow(user.Username);
            var result = resetWindow.ShowDialog();

            if (result == true && !string.IsNullOrEmpty(resetWindow.NewPassword))
            {
                var (success, message) = await _apiClient.ResetUserPasswordAsync(user.Id, resetWindow.NewPassword);
                StatusText.Text = message;
                await LoadAllDataAsync();
            }
        }
    }

    private List<ClientMachineDto> _allClients = new();
    private List<RuleDto> _allRules = new();
    private List<LogEntryDto> _allLogs = new();

    private void ApplyClientsFilter()
    {
        // จำ selection เดิมไว้ก่อนเปลี่ยน ItemsSource (ไม่งั้นจะหลุดทุกครั้งที่ reload ข้อมูล
        // เช่นตอน auto-refresh ทุก 10 วิ หรือตอนพิมพ์ค้นหา) แล้วเลือกคืนให้ตาม Id หลังโหลดเสร็จ
        var selectedIds = ClientsGrid.SelectedItems.Cast<ClientMachineDto>().Select(c => c.Id).ToHashSet();

        var keyword = ClientsSearchBox.Text.Trim();
        var filtered = string.IsNullOrEmpty(keyword)
            ? _allClients
            : _allClients.Where(c => c.MachineName.Contains(keyword, StringComparison.OrdinalIgnoreCase)).ToList();

        ClientsGrid.ItemsSource = filtered;

        foreach (var client in filtered.Where(c => selectedIds.Contains(c.Id)))
        {
            ClientsGrid.SelectedItems.Add(client);
        }
    }

    private void ApplyRulesFilter()
    {
        var keyword = RulesSearchBox.Text.Trim();
        RulesGrid.ItemsSource = string.IsNullOrEmpty(keyword)
            ? _allRules
            : _allRules.Where(r =>
                r.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                r.PublisherName.Contains(keyword, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    private void ApplyLogsFilter()
    {
        var keyword = LogsSearchBox.Text.Trim();
        LogsGrid.ItemsSource = string.IsNullOrEmpty(keyword)
            ? _allLogs
            : _allLogs.Where(l =>
                l.MachineName.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                (l.ProcessPath?.Contains(keyword, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (l.DetectedPublisher?.Contains(keyword, StringComparison.OrdinalIgnoreCase) ?? false)).ToList();
    }

    private void ClientsSearchBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyClientsFilter();
    private void RulesSearchBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyRulesFilter();
    private void LogsSearchBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyLogsFilter();

    // ==================== Step 4d: embedded VNC screen view ====================

    /// <summary>
    /// เรียกจากปุ่ม VIEW SCREEN ใน ActionPanelWindow (single-client mode) - ตัด session เดิม (ถ้ามี) ทิ้ง
    /// ก่อนเสมอ แล้วสลับไปแท็บ SCREEN VIEW เริ่มขอ session ใหม่ ทุก await ข้างล่างเช็ค myGeneration ก่อน
    /// แตะ UI/state เสมอ กันกรณีผู้ใช้สลับไปดูเครื่องอื่น/ปิดแท็บระหว่างที่ยังรออยู่ (เช่น รอ Agent ตอบ
    /// หรือรอ handshake) ทำให้ผลลัพธ์ที่มาช้าของ session เก่าไปเขียนทับ session ใหม่กว่า
    /// </summary>
    private async Task ShowVncViewAsync(ClientMachineDto client)
    {
        DisconnectVncView();
        var myGeneration = ++_vncSessionGeneration;

        VncHeaderText.Text = $"Screen View - {client.MachineName}";
        VncStatusText.Text = "Connecting...";
        VncScreenImage.Source = null;
        MainTabControl.SelectedItem = ScreenViewTabItem;

        if (string.IsNullOrWhiteSpace(client.LastKnownIp))
        {
            VncStatusText.Text = "Failed to connect: ไม่ทราบ IP เป้าหมาย (LastKnownIp ว่าง - รอ Agent poll เข้ามาอีกครั้ง)";
            return;
        }

        var sessionResult = await RequestVncSessionAsync(client);
        if (myGeneration != _vncSessionGeneration)
        {
            return; // ถูกแทนที่ไปแล้วระหว่างรอ Agent ตอบ (สลับเครื่อง/ปิดแท็บ) - ทิ้งผลลัพธ์นี้
        }

        if (!sessionResult.Success)
        {
            VncStatusText.Text = $"Failed to connect: {sessionResult.FailureReason}";
            return;
        }

        var rfbClient = new RfbClient();
        var handshakeResult = await rfbClient.ConnectAndHandshakeAsync(
            client.LastKnownIp, sessionResult.Port, sessionResult.Password!);

        if (myGeneration != _vncSessionGeneration)
        {
            rfbClient.Dispose(); // ถูกแทนที่ไปแล้วระหว่างรอ handshake - ปิด connection ที่เพิ่ง connect ทิ้งกัน socket ค้าง
            return;
        }

        if (!handshakeResult.Success)
        {
            VncStatusText.Text = $"Failed to connect: {handshakeResult.FailureReason}";
            rfbClient.Dispose();
            return;
        }

        var bitmap = new WriteableBitmap(
            handshakeResult.FramebufferWidth, handshakeResult.FramebufferHeight, 96, 96, PixelFormats.Bgr32, null);
        VncScreenImage.Source = bitmap;
        VncStatusText.Text = "Connected";

        _vncClient = rfbClient;
        _vncBitmap = bitmap;
        var cts = new CancellationTokenSource();
        _vncStreamCts = cts;

        // Task.Run เพื่อให้ loop ข้างในรันบน thread pool thread จริง (เหมือน VncTestWindow เดิม) - กัน
        // await ข้างในถูก marshal กลับ UI thread เองอัตโนมัติจนบล็อก UI ระหว่างรอ network I/O
        _ = Task.Run(() => StreamVncAsync(rfbClient, bitmap, cts.Token));
    }

    /// <summary>
    /// ตัด session ปัจจุบันทิ้ง (ถ้ามี) - เรียกได้จากหลายจุด: ก่อนเริ่ม session ใหม่, กดปุ่ม DISCONNECT,
    /// สลับออกจากแท็บ SCREEN VIEW (MainTabControl_SelectionChanged), หรือปิดหน้าต่าง Dashboard เพิ่ม
    /// generation ก่อนเสมอ (แม้ไม่มี session ค้างอยู่เลย) เพื่อให้ ShowVncViewAsync ที่อาจกำลัง await
    /// ค้างอยู่ (ยังไม่ทันตั้ง _vncClient/_vncBitmap) รู้ตัวว่าถูกยกเลิกไปแล้วตอน resume เหมือนกัน
    /// </summary>
    private void DisconnectVncView()
    {
        _vncSessionGeneration++;

        _vncStreamCts?.Cancel();
        _vncStreamCts = null;

        _vncClient?.Dispose();
        _vncClient = null;

        _vncBitmap = null;
        VncScreenImage.Source = null;
    }

    private void VncDisconnectButton_Click(object sender, RoutedEventArgs e)
    {
        DisconnectVncView();
        VncHeaderText.Text = "ยังไม่ได้เลือกเครื่อง - กด VIEW SCREEN จากรายการ Clients";
        VncStatusText.Text = "ตัดการเชื่อมต่อแล้ว";
    }

    /// <summary>
    /// SelectionChanged ของ Selector (TabControl/DataGrid/ComboBox ทุกตัวใช้ RoutedEvent ร่วมกัน) bubble
    /// ขึ้นมาจาก control ข้างในแท็บได้ (เช่น ClientsGrid) - เช็ค OriginalSource ตรงกับ MainTabControl เอง
    /// เท่านั้นถึงจะถือว่าเป็นการสลับแท็บจริง กันเรียก DisconnectVncView ผิดจังหวะ (เช่นแค่คลิกแถวใน DataGrid)
    /// </summary>
    private void MainTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, MainTabControl))
        {
            return;
        }

        if (e.RemovedItems.Contains(ScreenViewTabItem))
        {
            DisconnectVncView();
            VncHeaderText.Text = "ยังไม่ได้เลือกเครื่อง - กด VIEW SCREEN จากรายการ Clients";
            VncStatusText.Text = "";
        }
    }

    private async Task StreamVncAsync(RfbClient rfbClient, WriteableBitmap bitmap, CancellationToken ct)
    {
        try
        {
            await rfbClient.RunFramebufferUpdateLoopAsync(rect => OnVncRectangleDecoded(bitmap, rect), ct);
        }
        catch (OperationCanceledException)
        {
            // disconnect ปกติ (สลับเครื่อง/ปิดแท็บ/ปิดหน้าต่าง) - ไม่ต้องรายงาน
        }
        catch (Exception ex)
        {
            if (ct.IsCancellationRequested)
            {
                return; // เพิ่งถูก cancel พอดี (เช่น dispose ระหว่าง read) ไม่ใช่ error จริงของ session นี้
            }

            Dispatcher.Invoke(() =>
            {
                if (!ReferenceEquals(_vncBitmap, bitmap))
                {
                    return; // session นี้ถูกแทนที่ไปแล้ว - เพิกเฉย กัน UI เก่าทับ session ใหม่
                }

                VncStatusText.Text = $"Failed to connect: {ex.Message}";
            });
        }
    }

    /// <summary>เรียกจาก background thread ของ StreamVncAsync (ดู comment ที่จุดเรียก Task.Run ใน ShowVncViewAsync) - ต้อง marshal เข้า UI thread เองผ่าน Dispatcher เสมอ</summary>
    private void OnVncRectangleDecoded(WriteableBitmap bitmap, RfbRectangle rect)
    {
        if (rect.Width == 0 || rect.Height == 0)
        {
            return;
        }

        Dispatcher.Invoke(() =>
        {
            if (!ReferenceEquals(_vncBitmap, bitmap))
            {
                return; // session นี้ถูกแทนที่ไปแล้ว - เพิกเฉย
            }

            var area = new Int32Rect(rect.X, rect.Y, rect.Width, rect.Height);
            bitmap.WritePixels(area, rect.PixelData, rect.Width * 4, 0);
        });
    }

    /// <summary>
    /// ส่ง command StartVncSession แล้วรอผลผ่าน LabHubClient event เดียวกับที่ DashboardWindow subscribe
    /// อยู่แล้ว (SessionId เดียวกันตลอดอายุ Dashboard) มี timeout กันค้างถ้า Agent ไม่ตอบ (offline/SignalR
    /// หลุด) - subscribe/unsubscribe event ชั่วคราวเฉพาะช่วงรอนี้เท่านั้น (เหมือน VncTestWindow เดิม)
    /// </summary>
    private async Task<(bool Success, int Port, string? Password, string? FailureReason)> RequestVncSessionAsync(ClientMachineDto client)
    {
        var tcs = new TaskCompletionSource<(bool, int, string?, string?)>(TaskCreationOptions.RunContinuationsAsynchronously);

        void OnReady(int port, string password) => tcs.TrySetResult((true, port, password, null));
        void OnFailed(string reason) => tcs.TrySetResult((false, 0, null, reason));

        _labHubClient.VncSessionReady += OnReady;
        _labHubClient.VncSessionFailed += OnFailed;

        try
        {
            var sent = await _apiClient.SendCommandAsync(client.ClientGuid, "StartVncSession",
                new { requestingConsoleSessionId = _labHubClient.SessionId });

            if (!sent)
            {
                return (false, 0, null, "ส่ง command StartVncSession ไปยัง Server ไม่สำเร็จ (HTTP)");
            }

            var completed = await Task.WhenAny(tcs.Task, Task.Delay(VncSessionReadyTimeout));
            if (completed != tcs.Task)
            {
                return (false, 0, null, $"รอ Agent ตอบกลับเกิน {VncSessionReadyTimeout.TotalSeconds} วิ (Agent อาจ offline หรือ SignalR ไม่ connected)");
            }

            return await tcs.Task;
        }
        finally
        {
            _labHubClient.VncSessionReady -= OnReady;
            _labHubClient.VncSessionFailed -= OnFailed;
        }
    }
}
