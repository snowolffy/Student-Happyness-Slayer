using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using OnionProcOparetor.Console.Models;
using OnionProcOparetor.Console.Services;

namespace OnionProcOparetor.Console;

public partial class DashboardWindow : Window
{
    private readonly ApiClient _apiClient;
    private readonly DispatcherTimer _autoRefreshTimer;
    private readonly LabHubClient _labHubClient = new();

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

    private async void ToggleClientButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: int clientId })
        {
            var success = await _apiClient.ToggleClientAsync(clientId);
            StatusText.Text = success ? "Toggle client สำเร็จ" : "Toggle client ไม่สำเร็จ";
            await LoadAllDataAsync();
        }

    }

    private async void DeleteClientButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: int clientId })
        {
            var confirm = System.Windows.MessageBox.Show(
                "ยืนยันการลบเครื่องนี้? การกระทำนี้ย้อนกลับไม่ได้",
                "ยืนยันการลบ",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes)
            {
                return;
            }

            var success = await _apiClient.DeleteClientAsync(clientId);
            StatusText.Text = success ? "ลบเครื่องสำเร็จ" : "ลบเครื่องไม่สำเร็จ";
            await LoadAllDataAsync();
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

    private async void ToggleSelectedClientsButton_Click(object sender, RoutedEventArgs e)
    {
        var selected = ClientsGrid.SelectedItems.Cast<ClientMachineDto>().ToList();

        if (selected.Count == 0)
        {
            StatusText.Text = "กรุณาเลือกเครื่องอย่างน้อย 1 เครื่อง (Ctrl+คลิก หรือ Shift+คลิก เพื่อเลือกหลายเครื่อง)";
            return;
        }

        var confirm = System.Windows.MessageBox.Show(
            $"ยืนยัน toggle สถานะของ {selected.Count} เครื่องที่เลือกไว้?",
            "ยืนยัน Bulk Toggle",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        StatusText.Text = $"กำลัง toggle {selected.Count} เครื่อง...";

        var successCount = 0;
        foreach (var client in selected)
        {
            var success = await _apiClient.ToggleClientAsync(client.Id);
            if (success) successCount++;
        }

        StatusText.Text = $"Toggle สำเร็จ {successCount}/{selected.Count} เครื่อง";
        await LoadAllDataAsync();
    }

    private async void ClientSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: ClientMachineDto client })
        {
            var settingsWindow = new ClientSettingsWindow(_apiClient, client)
            {
                Owner = this
            };

            var result = settingsWindow.ShowDialog();

            if (result == true)
            {
                await LoadAllDataAsync();
            }
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
}
