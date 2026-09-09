using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using OnionProcOparetor.Console.Models;
using OnionProcOparetor.Console.Services;

namespace OnionProcOparetor.Console;

/// <summary>
/// "Config Rule" dialog (Action Panel → CONFIG RULE...) - แสดงกฎ global ทั้งหมดพร้อม toggle เฉพาะ
/// เครื่องเดียว (ClientGuid ที่ระบุ) ไม่กระทบ global rule หรือเครื่องอื่นเลย override ชนะ global เสมอ
/// 100% (ดู /api/poll/{clientGuid} ฝั่ง Server สำหรับจุดที่ resolve ค่าจริง)
///
/// สร้าง row แบบ dynamic ใน code-behind (ไม่ใช่ XAML DataTemplate) เพราะจำนวนกฎไม่คงที่ และแต่ละแถว
/// ต้องผูก event handler ที่ capture ruleId ของตัวเอง - ตาม pattern เดียวกับที่ App.xaml.cs (AgentTray)
/// สร้าง tray context menu items แบบ dynamic ใน C# แทน XAML
///
/// แต่ละ toggle มีผลทันที (เรียก API ทันทีตอนกด ไม่ต้องกด SAVE รวม) ตาม convention เดิมของแอปนี้
/// (ToggleRuleButton_Click/LockButton_Click ฯลฯ ก็ immediate เหมือนกันหมด)
/// </summary>
public partial class RuleOverridesWindow : Window
{
    private readonly ApiClient _apiClient;
    private readonly ClientMachineDto _client;

    public RuleOverridesWindow(ApiClient apiClient, ClientMachineDto client)
    {
        InitializeComponent();
        _apiClient = apiClient;
        _client = client;

        MachineNameText.Text = $"Config Rule - {client.MachineName}";
        Loaded += async (_, _) => await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        StatusText.Text = "กำลังโหลด...";
        RulesListPanel.Children.Clear();

        var rules = await _apiClient.GetRulesAsync();
        var overrides = await _apiClient.GetRuleOverridesAsync(_client.ClientGuid);
        var overrideByRuleId = overrides.ToDictionary(o => o.RuleId);

        if (rules.Count == 0)
        {
            StatusText.Text = "ยังไม่มีกฎใดๆ ในระบบ (เพิ่มกฎได้จากแท็บ RULES ก่อน)";
            return;
        }

        foreach (var rule in rules)
        {
            overrideByRuleId.TryGetValue(rule.Id, out var ruleOverride);
            RulesListPanel.Children.Add(BuildRow(rule, ruleOverride));
        }

        StatusText.Text = "";
    }

    private Border BuildRow(RuleDto rule, RuleOverrideDto? ruleOverride)
    {
        var hasOverride = ruleOverride is not null;
        var effectiveEnabled = ruleOverride?.IsEnabled ?? rule.IsEnabled;

        var row = new Border
        {
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 0, 0, 6),
            BorderThickness = new Thickness(1)
        };
        // SetResourceReference ไม่ใช่ FindResource: FindResource คืน "ค่า ณ ตอนนั้น" ครั้งเดียว ซึ่งจะค้างอยู่กับ
        // ธีมเดิมตลอดไปเมื่อผู้ใช้สลับธีม - SetResourceReference ผูกเป็น DynamicResource จริงเหมือนที่เขียนใน XAML
        // จึงตามธีมใหม่ให้เอง (แถวพวกนี้ถูกสร้างด้วย code-behind ล้วน ไม่มี XAML ให้ผูกให้อัตโนมัติ)
        row.SetResourceReference(BackgroundProperty, "Brush.Background.Card");
        row.SetResourceReference(BorderBrushProperty, "Brush.Border.Default");

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var namePanel = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var nameText = new TextBlock { Text = rule.Name, FontWeight = FontWeights.SemiBold };
        nameText.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text.Primary");
        namePanel.Children.Add(nameText);
        namePanel.Children.Add(new TextBlock
        {
            Text = hasOverride
                ? $"Override: {(ruleOverride!.IsEnabled ? "ON" : "OFF")} (global: {(rule.IsEnabled ? "ON" : "OFF")})"
                : $"Using global ({(rule.IsEnabled ? "ON" : "OFF")})",
            Style = (Style)FindResource("Caption"),
            Margin = new Thickness(0, 2, 0, 0)
        });
        Grid.SetColumn(namePanel, 0);
        grid.Children.Add(namePanel);

        var checkBox = new CheckBox
        {
            IsChecked = effectiveEnabled,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 10, 0)
        };
        checkBox.SetResourceReference(ForegroundProperty, "Brush.Text.Primary");
        checkBox.Checked += async (_, _) => await SetOverrideAsync(rule, true);
        checkBox.Unchecked += async (_, _) => await SetOverrideAsync(rule, false);
        Grid.SetColumn(checkBox, 1);
        grid.Children.Add(checkBox);

        var resetButton = new Button
        {
            Content = "RESET",
            Style = (Style)FindResource("ButtonSecondary"),
            Padding = new Thickness(8, 4, 8, 4),
            FontSize = 10,
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = hasOverride ? Visibility.Visible : Visibility.Collapsed
        };
        resetButton.Click += async (_, _) => await ClearOverrideAsync(rule);
        Grid.SetColumn(resetButton, 2);
        grid.Children.Add(resetButton);

        row.Child = grid;
        return row;
    }

    private async Task SetOverrideAsync(RuleDto rule, bool isEnabled)
    {
        StatusText.Text = $"กำลังบันทึก override ของ '{rule.Name}'...";
        var success = await _apiClient.SetRuleOverrideAsync(_client.ClientGuid, rule.Id, isEnabled);
        StatusText.Text = success
            ? $"ตั้ง override '{rule.Name}' เป็น {(isEnabled ? "ON" : "OFF")} สำเร็จ - มีผลรอบ poll ถัดไป"
            : $"บันทึก override ของ '{rule.Name}' ไม่สำเร็จ";

        if (success)
        {
            await RefreshAsync();
        }
    }

    private async Task ClearOverrideAsync(RuleDto rule)
    {
        StatusText.Text = $"กำลังลบ override ของ '{rule.Name}'...";
        var success = await _apiClient.ClearRuleOverrideAsync(_client.ClientGuid, rule.Id);
        StatusText.Text = success
            ? $"ลบ override ของ '{rule.Name}' แล้ว - กลับไปใช้ค่า global"
            : $"ลบ override ของ '{rule.Name}' ไม่สำเร็จ";

        if (success)
        {
            await RefreshAsync();
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
