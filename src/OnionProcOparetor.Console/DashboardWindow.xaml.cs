using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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

    // ==================== Step 4f: Live View grid (LIVE VIEW tab) ====================
    // pool แยกต่างหากจาก Step 4d โดยตั้งใจ (ไม่ migrate SCREEN VIEW tab มาใช้ pool นี้ - เก็บของเดิมไว้
    // ทำงานเหมือนเดิมทุกประการ ลดความเสี่ยง regression) เหมือน Step 4d ทุก field ตรงนี้แตะจาก UI thread
    // เท่านั้น (background thread ของแต่ละ handle เขียนกลับผ่าน Dispatcher.Invoke เสมอ)
    private readonly VncSessionPool _liveViewPool;
    private readonly Dictionary<string, LiveViewCell> _liveViewCells = new();

    // ไม่ null = เครื่องนี้กำลังแสดง full-screen overlay อยู่ (ดู ExpandLiveViewCell/BackToGridButton_Click)
    // - session อื่นใน grid ยังรันเบื้องหลังต่อปกติ แค่ถูกโอเวอร์เลย์บังไว้เฉยๆ ไม่ pause/reconnect
    private string? _liveViewFullscreenClientGuid;
    private WriteableBitmap? _liveViewFullscreenBitmap;
    private VncViewMode? _liveViewFullscreenBitmapMode;

    private sealed class LiveViewCell
    {
        public required Border Border { get; init; }
        public required Image Image { get; init; }
        public required TextBlock StatusOverlayText { get; init; }
        public required ClientMachineDto Client { get; init; }
        public WriteableBitmap? Bitmap { get; set; }
        public VncViewMode? BitmapMode { get; set; }
        public VncSessionHandle? Handle { get; set; }
    }

    public DashboardWindow(ApiClient apiClient)
    {
        InitializeComponent();
        _apiClient = apiClient;
        ServerLabel.Text = $"connected to {_apiClient.BaseUrl}";
        FooterServerText.Text = _apiClient.BaseUrl ?? "-";
        FooterUserText.Text = _apiClient.Username ?? "-";
        Loaded += async (_, _) => await LoadAllDataAsync();

        _liveViewPool = new VncSessionPool(_apiClient, _labHubClient);

        // auto refresh ทุกกี่วิตาม Settings (ค่า default เดิมคือ 10 วิ) จะได้ไม่ต้องกด REFRESH เอง -
        // หยุดเองตอนปิดหน้าต่างกันเรียก LoadAllDataAsync ทับ ApiClient ที่ปิดไปแล้ว
        _autoRefreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(SettingsService.Current.Connection.DashboardPollIntervalSeconds)
        };
        _autoRefreshTimer.Tick += async (_, _) => await LoadAllDataAsync();
        _autoRefreshTimer.Start();

        // อัพเดต interval แบบ live ทันทีที่บันทึก Settings ใหม่ - ไม่ต้อง restart Console (ดู SettingsWindow)
        SettingsService.Changed += OnSettingsChanged;

        // SignalR เป็นชั้นเสริมคู่ขนานกับ DispatcherTimer ข้างบน (ไม่แทนที่) - ทำให้ตารางเครื่องอัปเดต
        // ไวขึ้นโดยไม่ต้องรอรอบ poll ถ้า SignalR หลุด/ต่อไม่ได้ ตาราง ยังอัปเดตปกติผ่าน auto refresh เดิม
        _labHubClient.ClientStatusChanged += OnClientStatusChangedFromHub;
        _ = _labHubClient.StartAsync(_apiClient.BaseUrl!, CancellationToken.None);

        Closed += async (_, _) =>
        {
            _autoRefreshTimer.Stop();
            SettingsService.Changed -= OnSettingsChanged;
            DisconnectVncView();
            await CloseAllLiveViewSessionsAsync();
            _labHubClient.ClientStatusChanged -= OnClientStatusChangedFromHub;
            await _labHubClient.DisposeAsync();
        };
    }

    private void OnSettingsChanged(ConsoleSettings settings)
    {
        Dispatcher.Invoke(() => _autoRefreshTimer.Interval = TimeSpan.FromSeconds(settings.Connection.DashboardPollIntervalSeconds));
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
            ApplyLiveViewClientList();

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

    /// <summary>
    /// ปุ่มเกียร์บน toolbar (แทนที่ TOGGLE GLOBAL เดิม - ลบออกเพราะ effect เดียวกัน (ปิด/เปิดทุกเครื่อง
    /// พร้อมกัน) ทำได้อยู่แล้วผ่านปุ่ม ACTIONS > เลือก "ทุกเครื่อง" > TOGGLE ENFORCEMENT ใน
    /// ActionPanelWindow - ดู BulkActionsButton_Click ไม่ได้ลบ ApiClient.ToggleGlobalAsync()/Server
    /// endpoint ออก เพราะยังเป็น mechanism จริงที่ผูกกับ GlobalState ฝั่ง Server อยู่) เปิด ContextMenu
    /// ให้เลือก Settings/About แทนที่จะแยกปุ่มคนละจุด
    /// </summary>
    private void SettingsMenuButton_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu();

        var settingsItem = new MenuItem { Header = "Settings..." };
        settingsItem.Click += (_, _) => new SettingsWindow { Owner = this }.ShowDialog();
        menu.Items.Add(settingsItem);

        var aboutItem = new MenuItem { Header = "About..." };
        aboutItem.Click += (_, _) => new AboutWindow { Owner = this }.ShowDialog();
        menu.Items.Add(aboutItem);

        menu.PlacementTarget = (Button)sender;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen = true;
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

    /// <summary>ชั่วคราวสำหรับยืนยัน VncSessionPool + RfbClient Thumbnail mode (Step 4e) เท่านั้น - เปิดทุกเครื่องที่เลือกไว้ผ่าน VncPoolTestWindow (ไม่ใช่ production UI - ดู comment ในไฟล์นั้น)</summary>
    private void PoolTestButton_Click(object sender, RoutedEventArgs e)
    {
        var selected = ClientsGrid.SelectedItems.Cast<ClientMachineDto>().ToList();

        if (selected.Count == 0)
        {
            StatusText.Text = "กรุณาเลือกเครื่องอย่างน้อย 1 เครื่อง (Ctrl+คลิก หรือ Shift+คลิก เพื่อเลือกหลายเครื่อง)";
            return;
        }

        var testWindow = new VncPoolTestWindow(_apiClient, _labHubClient, selected) { Owner = this };
        testWindow.Show();
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
    private async void MainTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
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

        if (e.RemovedItems.Contains(LiveViewTabItem) && !SettingsService.Current.Vnc.KeepLiveViewOpenOnTabLeave)
        {
            await CloseAllLiveViewSessionsAsync();
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

        // ตั้งแต่ Step 4e event มี clientGuid มาด้วย (VncSessionPool อาจมีหลาย request ค้างรอพร้อมกัน) -
        // Dashboard เปิดได้ทีละเครื่องเท่านั้นตอนนี้ (Step 4d) เลยไม่มีผลต่างเชิงพฤติกรรม แต่ต้องกรองให้
        // ถูกต้องตาม client ที่ request ไปเสมอ ไม่ใช่รับทุก event ที่เข้ามา
        void OnReady(string clientGuid, int port, string password)
        {
            if (clientGuid == client.ClientGuid)
            {
                tcs.TrySetResult((true, port, password, null));
            }
        }

        void OnFailed(string clientGuid, string reason)
        {
            if (clientGuid == client.ClientGuid)
            {
                tcs.TrySetResult((false, 0, null, reason));
            }
        }

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

    // ==================== Step 4f: Live View grid (CCTV wall) ====================

    /// <summary>เรียกจาก LoadAllDataAsync ทุกครั้งที่ _allClients เปลี่ยน (poll 10 วิ/SignalR/REFRESH) - จำ selection เดิมไว้ก่อนเปลี่ยน ItemsSource เหมือน ApplyClientsFilter กันเลือกหลุดกลางคันตอนกำลังจะกด ADD</summary>
    private void ApplyLiveViewClientList()
    {
        var selectedIds = LiveViewClientListBox.SelectedItems.Cast<ClientMachineDto>().Select(c => c.Id).ToHashSet();

        LiveViewClientListBox.ItemsSource = _allClients;

        foreach (var client in _allClients.Where(c => selectedIds.Contains(c.Id)))
        {
            LiveViewClientListBox.SelectedItems.Add(client);
        }
    }

    /// <summary>
    /// เปิด session แบบ Thumbnail mode เฉพาะเครื่องที่เลือกไว้ (lazy - ไม่ auto-connect ทั้งหมดตั้งแต่เปิด
    /// หน้า ตามที่ตกลง) ข้ามเครื่องที่เปิดอยู่ใน grid แล้ว เช็ค MaxConcurrentSessions ก่อนเสมอ (single
    /// source - ดู VncSessionPool.MaxConcurrentSessions) ไม่ปล่อยให้เงียบๆ ล้มเหลวถ้าเกิน limit
    /// </summary>
    private void AddSelectedToGridButton_Click(object sender, RoutedEventArgs e)
    {
        var selected = LiveViewClientListBox.SelectedItems.Cast<ClientMachineDto>()
            .Where(c => !_liveViewCells.ContainsKey(c.ClientGuid))
            .ToList();

        if (selected.Count == 0)
        {
            LiveViewStatusText.Text = _liveViewCells.Count == 0
                ? "กรุณาเลือกเครื่องอย่างน้อย 1 เครื่อง"
                : "เครื่องที่เลือกเปิดอยู่ใน grid แล้วทั้งหมด";
            return;
        }

        var remaining = VncSessionPool.MaxConcurrentSessions - _liveViewCells.Count;
        if (selected.Count > remaining)
        {
            LiveViewStatusText.Text = remaining <= 0
                ? $"Grid เต็มแล้ว (สูงสุด {VncSessionPool.MaxConcurrentSessions} เครื่อง) - เอาเครื่องออกก่อนเพิ่มใหม่"
                : $"เลือกเกินจำนวนที่เพิ่มได้ (เหลือที่ว่างอีก {remaining} เครื่อง จากทั้งหมด {VncSessionPool.MaxConcurrentSessions}) - เลือกน้อยลง";
            return;
        }

        foreach (var client in selected)
        {
            AddCellToGrid(client);
        }

        UpdateLiveViewStatusAndButtonState();
    }

    private void AddCellToGrid(ClientMachineDto client)
    {
        var cell = CreateLiveViewCell(client);
        LiveViewGrid.Children.Add(cell.Border);
        _liveViewCells[client.ClientGuid] = cell;

        _ = OpenLiveViewSessionAsync(client, cell);
    }

    /// <summary>
    /// เรียกจาก UI-thread continuation เสมอ (AddCellToGrid ไม่ผ่าน Task.Run) - เหมือน Step 4d
    /// ShowVncViewAsync เลยแตะ UI ตรงๆ ได้หลัง await โดยไม่ต้อง Dispatcher.Invoke เช็ค _liveViewCells
    /// ก่อนเสมอหลัง await กันกรณีผู้ใช้กด X เอา cell นี้ออกไปแล้วระหว่างที่ยังรอ connect อยู่ (ทิ้งผลลัพธ์
    /// ที่มาช้าไป ไม่งั้นจะไปแก้ cell ที่หลุดจาก grid ไปแล้ว)
    /// </summary>
    private async Task OpenLiveViewSessionAsync(ClientMachineDto client, LiveViewCell cell)
    {
        var (success, handle, failureReason) = await _liveViewPool.OpenAsync(client, VncViewMode.Thumbnail);

        if (!_liveViewCells.TryGetValue(client.ClientGuid, out var currentCell) || !ReferenceEquals(currentCell, cell))
        {
            return; // ถูกเอาออกจาก grid ไปแล้วระหว่างรอ (หรือถูกเพิ่มใหม่กลายเป็นคนละ cell object) - ทิ้งผลลัพธ์นี้
        }

        if (!success || handle is null)
        {
            cell.StatusOverlayText.Text = $"Failed: {failureReason}";
            cell.StatusOverlayText.Visibility = Visibility.Visible;
            return;
        }

        cell.Handle = handle;
        handle.StatusChanged += () => Dispatcher.Invoke(() => OnLiveViewStatusChanged(cell, handle));
        handle.RectangleDecoded += rect => OnLiveViewRectangleDecoded(cell, handle, rect);

        OnLiveViewStatusChanged(cell, handle);
    }

    private void OnLiveViewStatusChanged(LiveViewCell cell, VncSessionHandle handle)
    {
        cell.StatusOverlayText.Text = handle.Status switch
        {
            VncSessionStatus.Connecting => "Connecting...",
            VncSessionStatus.Connected => "",
            VncSessionStatus.Failed => $"Failed: {handle.FailureReason}",
            VncSessionStatus.Closed => "Closed",
            _ => ""
        };
        cell.StatusOverlayText.Visibility = string.IsNullOrEmpty(cell.StatusOverlayText.Text) ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>
    /// เรียกจาก background thread ของ VncSessionHandle (RfbClient framebuffer loop) เสมอ - ต้อง
    /// marshal เข้า UI thread เองผ่าน Dispatcher (เหมือน Step 4d/4e ทุกจุด) ตัดสินใจว่าจะเขียนลง
    /// thumbnail bitmap ของ cell ใน grid หรือ fullscreen bitmap จาก _liveViewFullscreenClientGuid ปัจจุบัน
    /// - สร้าง/resize WriteableBitmap ใหม่ถ้ายังไม่มีหรือ mode เปลี่ยนไปจากตอนสร้างครั้งล่าสุด (สลับ
    /// Thumbnail/FullRes เปลี่ยนขนาดภาพเสมอ)
    /// </summary>
    private void OnLiveViewRectangleDecoded(LiveViewCell cell, VncSessionHandle handle, RfbRectangle rect)
    {
        if (rect.Width == 0 || rect.Height == 0)
        {
            return;
        }

        Dispatcher.Invoke(() =>
        {
            if (_liveViewFullscreenClientGuid == handle.Client.ClientGuid)
            {
                if (_liveViewFullscreenBitmap is null || _liveViewFullscreenBitmapMode != handle.Mode)
                {
                    var width = handle.DisplayWidth ?? rect.Width;
                    var height = handle.DisplayHeight ?? rect.Height;
                    _liveViewFullscreenBitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgr32, null);
                    _liveViewFullscreenBitmapMode = handle.Mode;
                    FullscreenImage.Source = _liveViewFullscreenBitmap;
                }

                WriteLiveViewRectSafely(_liveViewFullscreenBitmap, rect);
            }
            else
            {
                if (cell.Bitmap is null || cell.BitmapMode != handle.Mode)
                {
                    var width = handle.DisplayWidth ?? rect.Width;
                    var height = handle.DisplayHeight ?? rect.Height;
                    cell.Bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgr32, null);
                    cell.BitmapMode = handle.Mode;
                    cell.Image.Source = cell.Bitmap;
                }

                WriteLiveViewRectSafely(cell.Bitmap, rect);
            }
        });
    }

    private static void WriteLiveViewRectSafely(WriteableBitmap bitmap, RfbRectangle rect)
    {
        if (rect.X + rect.Width > bitmap.PixelWidth || rect.Y + rect.Height > bitmap.PixelHeight)
        {
            return; // ขนาดยังไม่ตรงกับ mode ปัจจุบันพอดี (ไม่ควรเกิดได้จริงในทางทฤษฎี - กันไว้เฉยๆ)
        }

        var area = new Int32Rect(rect.X, rect.Y, rect.Width, rect.Height);
        bitmap.WritePixels(area, rect.PixelData, rect.Width * 4, 0);
    }

    /// <summary>สร้าง UI ของ cell หนึ่งช่องใน grid แบบ code-behind (เหมือน pattern เดียวกับ VncPoolTestWindow) - layer ซ้อนกันใน Grid เดียว: ภาพ -> ข้อความสถานะกลางจอ -> ป้ายชื่อเครื่องมุมล่าง -> ปุ่ม X มุมบน คลิกที่ไหนก็ได้ใน Border (ยกเว้นปุ่ม X) เพื่อขยายเต็มจอ</summary>
    private LiveViewCell CreateLiveViewCell(ClientMachineDto client)
    {
        var image = new Image { Stretch = Stretch.Uniform };

        var statusText = new TextBlock
        {
            Text = "Connecting...",
            Foreground = Brushes.White,
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 200
        };

        var nameLabel = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(160, 0, 0, 0)),
            VerticalAlignment = VerticalAlignment.Bottom,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Child = new TextBlock
            {
                Text = client.MachineName,
                Foreground = Brushes.White,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(6, 3, 6, 3),
                TextTrimming = TextTrimming.CharacterEllipsis
            }
        };

        var removeButton = new Button
        {
            Content = "✕",
            Width = 22,
            Height = 22,
            Padding = new Thickness(0),
            FontSize = 11,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 4, 4, 0),
            Tag = client.ClientGuid
        };
        removeButton.Click += RemoveGridCellButton_Click;

        var overlayGrid = new Grid();
        overlayGrid.Children.Add(image);
        overlayGrid.Children.Add(statusText);
        overlayGrid.Children.Add(nameLabel);
        overlayGrid.Children.Add(removeButton);

        var border = new Border
        {
            Background = Brushes.Black,
            BorderBrush = (Brush)FindResource("Brush.Border.Default"),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(3),
            Child = overlayGrid,
            Cursor = Cursors.Hand
        };

        // Button.Click กิน MouseLeftButtonUp ที่ trigger มันไปแล้ว (ButtonBase mark e.Handled ให้เอง) เลย
        // ไม่ bubble มาถึง handler นี้ตอนกดปุ่ม X - คลิกที่ไหนอื่นใน cell จะ trigger ขยายเต็มจอ
        border.MouseLeftButtonUp += (_, _) => ExpandLiveViewCell(client.ClientGuid);

        return new LiveViewCell
        {
            Border = border,
            Image = image,
            StatusOverlayText = statusText,
            Client = client
        };
    }

    /// <summary>คลิกที่ thumbnail ใน grid - สลับ session นั้นเป็น FullRes mode (ไม่ reconnect) แล้วแสดง full-screen overlay ทับทั้งหน้า Dashboard - session อื่นใน grid ยังรันเบื้องหลังต่อปกติ ไม่ pause/reconnect (ตามที่ตกลง)</summary>
    private void ExpandLiveViewCell(string clientGuid)
    {
        if (!_liveViewCells.TryGetValue(clientGuid, out var cell) || cell.Handle is not { Status: VncSessionStatus.Connected } handle)
        {
            return; // ยังไม่ connect เสร็จ (ยัง Connecting/Failed) - ขยายไม่ได้
        }

        _liveViewFullscreenClientGuid = clientGuid;
        _liveViewFullscreenBitmap = null;
        _liveViewFullscreenBitmapMode = null;
        FullscreenImage.Source = null;
        FullscreenHeaderText.Text = cell.Client.MachineName;

        LiveViewFullscreenOverlay.Visibility = Visibility.Visible;

        handle.RequestModeSwitch(VncViewMode.FullRes);
    }

    /// <summary>กลับไป grid - สลับ mode คืนเป็น Thumbnail (ไม่ reconnect) แล้วซ่อน overlay เครื่องอื่นใน grid ไม่ถูกแตะเลยตลอดช่วงที่อยู่ full-screen (ตามที่ตกลง)</summary>
    private void BackToGridButton_Click(object sender, RoutedEventArgs e)
    {
        if (_liveViewFullscreenClientGuid is not null &&
            _liveViewCells.TryGetValue(_liveViewFullscreenClientGuid, out var cell) &&
            cell.Handle is { } handle)
        {
            handle.RequestModeSwitch(VncViewMode.Thumbnail);
        }

        _liveViewFullscreenClientGuid = null;
        _liveViewFullscreenBitmap = null;
        _liveViewFullscreenBitmapMode = null;
        FullscreenImage.Source = null;

        LiveViewFullscreenOverlay.Visibility = Visibility.Collapsed;
    }

    /// <summary>ปุ่ม X บน cell - เอาออกจาก grid + dispose session นั้นทิ้ง ไม่กระทบ session อื่นเลย (ตามที่ตกลงใน Step 4e)</summary>
    private async void RemoveGridCellButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string clientGuid })
        {
            return;
        }

        if (_liveViewFullscreenClientGuid == clientGuid)
        {
            BackToGridButton_Click(sender, e); // กำลังขยายเครื่องนี้อยู่พอดี - กลับ grid ก่อนเอาออก
        }

        if (_liveViewCells.Remove(clientGuid, out var cell))
        {
            LiveViewGrid.Children.Remove(cell.Border);
        }

        await _liveViewPool.CloseAsync(clientGuid);
        UpdateLiveViewStatusAndButtonState();
    }

    private void UpdateLiveViewStatusAndButtonState()
    {
        LiveViewStatusText.Text = $"เปิดอยู่ {_liveViewCells.Count}/{VncSessionPool.MaxConcurrentSessions} เครื่อง";
        AddSelectedToGridButton.IsEnabled = _liveViewCells.Count < VncSessionPool.MaxConcurrentSessions;
    }

    /// <summary>เรียกตอนออกจากแท็บ LIVE VIEW หรือปิด Dashboard - dispose ทุก session ใน grid ให้ครบ (ตามที่ตกลง) รวมถึงปิด full-screen overlay ถ้ากำลังเปิดอยู่พอดี</summary>
    private async Task CloseAllLiveViewSessionsAsync()
    {
        if (_liveViewFullscreenClientGuid is not null)
        {
            _liveViewFullscreenClientGuid = null;
            _liveViewFullscreenBitmap = null;
            _liveViewFullscreenBitmapMode = null;
            FullscreenImage.Source = null;
            LiveViewFullscreenOverlay.Visibility = Visibility.Collapsed;
        }

        LiveViewGrid.Children.Clear();
        _liveViewCells.Clear();

        await _liveViewPool.CloseAllAsync();
        UpdateLiveViewStatusAndButtonState();
    }
}
