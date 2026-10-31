using System.Windows;
using System.Windows.Controls;
using OnionProcOparetor.RosterManager.Services;

namespace OnionProcOparetor.RosterManager;

public partial class PromoteWindow : Window
{
    private readonly ApiClient _api;

    public sealed class PromoteRow
    {
        public required string ClassName { get; init; }
        public int Count { get; init; }
        public string ToClassName { get; set; } = string.Empty;
    }

    public PromoteWindow(ApiClient api)
    {
        InitializeComponent();
        _api = api;
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        var counts = await _api.GetClassCountsAsync();
        var rows = counts
            .OrderBy(c => c.ClassName, StringComparer.OrdinalIgnoreCase)
            .Select(c => new PromoteRow { ClassName = c.ClassName, Count = c.Count })
            .ToList();

        RowsGrid.ItemsSource = rows;
        TitleText.Text = $"ปีการศึกษาปัจจุบัน - {counts.Sum(c => c.Count)} คน ใน {counts.Count} ห้อง";
        SummaryText.Text = $"ทั้งหมด {counts.Sum(c => c.Count)} คน ใน {counts.Count} ห้อง - กรอกช่อง \"ย้ายไป\" ของแต่ละห้องด้านซ้าย";
    }

    private async void PromoteAllButton_Click(object sender, RoutedEventArgs e)
    {
        var rows = (RowsGrid.ItemsSource as List<PromoteRow>) ?? new List<PromoteRow>();
        if (rows.Count == 0)
        {
            return;
        }

        var promotedPreview = rows.Count(r => !string.IsNullOrWhiteSpace(r.ToClassName));
        var graduatedPreview = rows.Count - promotedPreview;

        if (!ConfirmWindow.Ask(this, "ยืนยันเลื่อนชั้นทั้งหมด",
                $"จะเลื่อนชั้น {promotedPreview} ห้อง และถือว่าจบการศึกษา {graduatedPreview} ห้อง (ตามที่กรอกไว้)\n" +
                "ระบบจะสำรอง DB อัตโนมัติก่อนเริ่มเลื่อนชั้น - ดำเนินการต่อ?"))
        {
            return;
        }

        PromoteAllButton.IsEnabled = false;

        var graduateAction = int.Parse((string)((ComboBoxItem)GraduateActionCombo.SelectedItem).Tag);
        var mappings = rows
            .Select(r => (r.ClassName, string.IsNullOrWhiteSpace(r.ToClassName) ? (string?)null : r.ToClassName.Trim()))
            .ToList();

        var (success, message, promotedCount, graduatedCount, backupFileName) = await _api.PromoteAsync(mappings, graduateAction);

        if (success)
        {
            ToastService.Success($"เลื่อนชั้นสำเร็จ: เลื่อนชั้น {promotedCount} คน · จบการศึกษา {graduatedCount} คน" +
                (backupFileName is null ? "" : $" (สำรอง DB ไว้ที่ {backupFileName})"));
            DialogResult = true;
            Close();
        }
        else
        {
            StatusText.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "Brush.Danger.Default");
            StatusText.Text = message;
            PromoteAllButton.IsEnabled = true;
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
