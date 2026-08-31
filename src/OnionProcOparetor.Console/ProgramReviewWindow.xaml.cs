using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using OnionProcOparetor.Console.Models;
using OnionProcOparetor.Console.Services;

namespace OnionProcOparetor.Console;

/// <summary>
/// คิวตรวจสอบของ Program Control (2026-08-31) - ครูดูโปรแกรมที่ระบบเจอแต่ตัดสินเองไม่ได้
/// แล้วกดอนุญาต/บล็อกถาวร ผลผูกกับ SHA-256 ของไฟล์ มีผลกับทุกเครื่องในรอบ poll ถัดไป
///
/// ทุก async void handler ต้องมี try/catch ครอบเต็ม - ไม่มีใคร await handler พวกนี้ exception ที่หลุด
/// ออกไปจะกลายเป็น unhandled (App.xaml.cs มี DispatcherUnhandledException รับไว้อยู่ก็จริง แต่ผลคือ
/// ครูเห็นกล่อง error ลอยๆ แทนที่จะเห็นสาเหตุในบริบทของหน้าต่างนี้)
/// </summary>
public partial class ProgramReviewWindow : Window
{
    private readonly ApiClient _apiClient;
    private List<ProgramVerdictDto> _items = new();

    public ProgramReviewWindow(ApiClient apiClient)
    {
        InitializeComponent();
        _apiClient = apiClient;
        Loaded += async (_, _) => await RefreshAsync();
    }

    private string SelectedFilter =>
        (FilterCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "flagged";

    private async void RefreshButton_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

    private async void FilterCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // ยิงตอน InitializeComponent ตั้ง IsSelected ครั้งแรกด้วย ซึ่งตอนนั้น _apiClient ยังเป็น null
        // (constructor ยังไม่ทันได้ assign) - Loaded handler จะ refresh ให้อยู่แล้ว ข้ามรอบนั้นไป
        if (!IsLoaded) return;
        await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        try
        {
            StatusText.Text = "กำลังโหลด...";
            VerdictsGrid.ItemsSource = null;
            SetButtonsEnabled(false);

            _items = await _apiClient.GetProgramVerdictsAsync(SelectedFilter);
            VerdictsGrid.ItemsSource = _items;

            StatusText.Text = _items.Count == 0
                ? "ไม่มีรายการในหมวดนี้"
                : $"พบ {_items.Count} รายการ - เรียงตามจำนวนครั้งที่พบมากที่สุดก่อน";
        }
        catch (System.Exception ex)
        {
            StatusText.Text = $"โหลดไม่สำเร็จ: {ex.Message}";
        }
    }

    private void VerdictsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (VerdictsGrid.SelectedItem is not ProgramVerdictDto selected)
        {
            ReasonsText.Text = "เลือกโปรแกรมจากตารางด้านบนเพื่อดูเหตุผล";
            SetButtonsEnabled(false);
            return;
        }

        var header = $"{selected.DisplayName}\nSHA-256: {selected.Sha256}\n";
        if (!string.IsNullOrWhiteSpace(selected.Publisher))
        {
            header += $"Publisher: {selected.Publisher}"
                + (selected.IsSignedPublisher ? " (จากลายเซ็นดิจิทัล)" : " (จาก metadata เท่านั้น - ปลอมได้ง่าย)")
                + "\n";
        }
        if (!string.IsNullOrWhiteSpace(selected.OnlineMatchName))
        {
            header += $"ตรงกับเกมชื่อ: {selected.OnlineMatchName}\n";
        }

        ReasonsText.Text = header + "\n" + selected.ReasonsText;
        SetButtonsEnabled(true);
    }

    private void SetButtonsEnabled(bool enabled)
    {
        AllowButton.IsEnabled = enabled;
        BlockButton.IsEnabled = enabled;
    }

    private async void AllowButton_Click(object sender, RoutedEventArgs e)
        => await DecideAsync(ProgramActionDto.Allow, "อนุญาต");

    private async void BlockButton_Click(object sender, RoutedEventArgs e)
        => await DecideAsync(ProgramActionDto.Block, "บล็อก");

    private async Task DecideAsync(ProgramActionDto action, string verb)
    {
        if (VerdictsGrid.SelectedItem is not ProgramVerdictDto selected)
        {
            return;
        }

        try
        {
            SetButtonsEnabled(false);
            StatusText.Text = $"กำลังตั้งค่าให้ {verb} '{selected.FileName}'...";

            var success = await _apiClient.DecideProgramVerdictAsync(selected.Id, (int)action);
            if (!success)
            {
                StatusText.Text = $"ตั้งค่าไม่สำเร็จ - ลองใหม่อีกครั้ง";
                SetButtonsEnabled(true);
                return;
            }

            await RefreshAsync();
            StatusText.Text = action == ProgramActionDto.Block
                ? $"บล็อก '{selected.FileName}' แล้ว - ทุกเครื่องจะเริ่มบล็อกภายในรอบ poll ถัดไป"
                : $"อนุญาต '{selected.FileName}' แล้ว - จะไม่ถูกถามซ้ำอีก";
        }
        catch (System.Exception ex)
        {
            StatusText.Text = $"ตั้งค่าไม่สำเร็จ: {ex.Message}";
            SetButtonsEnabled(true);
        }
    }
}
