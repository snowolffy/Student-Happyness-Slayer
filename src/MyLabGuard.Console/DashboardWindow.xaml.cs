using System.Windows;
using System.Windows.Controls;
using MyLabGuard.Console.Models;
using MyLabGuard.Console.Services;

namespace MyLabGuard.Console;

public partial class DashboardWindow : Window
{
    private readonly ApiClient _apiClient;

    public DashboardWindow(ApiClient apiClient)
    {
        InitializeComponent();
        _apiClient = apiClient;
        ServerLabel.Text = $"connected to {_apiClient.BaseUrl}";
        Loaded += async (_, _) => await LoadAllDataAsync();
    }

    private async Task LoadAllDataAsync()
    {
        StatusText.Text = "กำลังโหลดข้อมูล...";
        try
        {
            var clients = await _apiClient.GetClientsAsync();
            ClientsGrid.ItemsSource = clients;

            var rules = await _apiClient.GetRulesAsync();
            RulesGrid.ItemsSource = rules;

            var logs = await _apiClient.GetLogsAsync();
            LogsGrid.ItemsSource = logs;

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
}