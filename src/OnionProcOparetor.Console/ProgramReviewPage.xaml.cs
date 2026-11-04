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
/// WP6 (2026-10-30): ย้ายจากหน้าต่างลอย (ProgramReviewWindow) มาเป็นหน้าใน shell ของ Dashboard
/// logic เดิมทั้งหมด ต่างกันแค่ (1) ApiClient ส่งเข้ามาทาง <see cref="Initialize"/> เพราะหน้านี้ถูก
/// สร้างโดย XAML ของ Dashboard (2) ผลลัพธ์/error ไปที่ toast (3) โหลดใหม่ทุกครั้งที่เปิดหน้านี้
/// (ผูกกับ IsVisibleChanged - ดู constructor) แทนที่จะโหลดครั้งเดียวตอนเปิดหน้าต่าง
///
/// ทุก async void handler ต้องมี try/catch ครอบเต็ม - ไม่มีใคร await handler พวกนี้ exception ที่หลุด
/// ออกไปจะกลายเป็น unhandled (App.xaml.cs มี DispatcherUnhandledException รับไว้อยู่ก็จริง แต่ผลคือ
/// ครูเห็นกล่อง error ลอยๆ แทนที่จะเห็นสาเหตุในบริบทของหน้านี้)
/// </summary>
public partial class ProgramReviewPage : UserControl
{
    private ApiClient? _apiClient;
    private List<ProgramVerdictDto> _items = new();
    private bool _suppressCategoryEvent;

    public ProgramReviewPage()
    {
        InitializeComponent();
        // โหลดตอน "เห็น" ไม่ใช่ตอน Loaded - TabControl ยิง Loaded ให้เนื้อหาของทุกแท็บตั้งแต่เปิด
        // Dashboard (ผ่าน logical tree) แม้แท็บนั้นยังไม่เคยถูกเลือก (เจอตอนถ่ายภาพตรวจ: หน้าที่ไม่ได้
        // เปิดยิง API ไปแล้ว) ส่วน IsVisible เป็นจริงเฉพาะตอนแท็บนี้ถูกเลือกอยู่จริงเท่านั้น
        IsVisibleChanged += async (_, e) =>
        {
            if (e.NewValue is true)
            {
                await RefreshAsync();
            }
        };
    }

    /// <summary>เรียกครั้งเดียวจาก DashboardWindow หลัง InitializeComponent</summary>
    public void Initialize(ApiClient apiClient) => _apiClient = apiClient;

    private string SelectedFilter =>
        (FilterCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "flagged";

    /// <summary>null = ทุกหมวด (Tag ของตัวเลือก "ทุกหมวด" เป็น null)</summary>
    private int? SelectedCategory =>
        (CategoryCombo.SelectedItem as ComboBoxItem)?.Tag as int?;

    private async void RefreshButton_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

    private async void FilterCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // ยิงตอน InitializeComponent ตั้ง IsSelected ครั้งแรกด้วย - Loaded จะ refresh ให้อยู่แล้ว ข้ามรอบนั้นไป
        if (!IsLoaded) return;
        await RefreshAsync();
    }

    private async void CategoryCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // _suppressCategoryEvent กัน RebuildCategoryCombo (ซึ่งถูกเรียกจากใน RefreshAsync เอง) ทำให้
        // เกิด refresh ซ้อนอีกรอบ - ไม่ใช่แค่เปลืองรอบ แต่ทำให้ selection ที่เพิ่งกู้คืนถูกทับด้วย
        if (!IsLoaded || _suppressCategoryEvent) return;
        await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        if (_apiClient is null)
        {
            return;
        }

        try
        {
            // UI-4 (2026-11-04): ไอคอนหมุนคู่กับข้อความ "กำลังโหลด..." - ปิดเสมอใน finally ด้านล่าง
            SummaryLoadingIcon.Visibility = Visibility.Visible;
            SummaryText.Text = "กำลังโหลด...";
            VerdictsGrid.ItemsSource = null;
            EmptyText.Visibility = Visibility.Collapsed;
            SetButtonsEnabled(false);

            // โหลด summary ก่อนเสมอ เพื่อให้กล่องหมวดสะท้อนของที่มีอยู่จริงในตอนนี้ (หมวดที่เพิ่งหมด
            // ต้องหายไปจากกล่อง ไม่ใช่ค้างให้กดแล้วเจอตารางว่าง)
            var summary = await _apiClient.GetProgramVerdictSummaryAsync(SelectedFilter);
            RebuildCategoryCombo(summary);

            var category = SelectedCategory;
            _items = await _apiClient.GetProgramVerdictsAsync(SelectedFilter, category);
            VerdictsGrid.ItemsSource = _items;

            if (_items.Count == 0)
            {
                SummaryText.Text = string.Empty;
                EmptyText.Visibility = Visibility.Visible;
                return;
            }

            var scope = category is null
                ? $"พบ {summary.Total} รายการ"
                : $"พบ {_items.Count} รายการในหมวด '{ProgramVerdictDto.CategoryLabel(category.Value)}' (ทั้งหมด {summary.Total})";

            // Server ตัดที่ 200 แถว - ต้องบอกครูตรงๆ ว่ากำลังดูไม่ครบ ไม่งั้นจะเข้าใจว่าเห็นหมดแล้ว
            // แล้วสรุปผิดว่าเครื่องในห้องมีของแค่นี้ (เจอจริงตอนที่รายการสะสมเป็นหลักร้อย)
            var truncated = _items.Count >= 200
                ? $" - แสดง {_items.Count} แถวแรกเท่านั้น เลือกหมวดเพื่อดูให้ครบ"
                : string.Empty;

            SummaryText.Text = $"{scope} - เรียงตามจำนวนครั้งที่พบมากที่สุดก่อน{truncated}";
        }
        catch (System.Exception ex)
        {
            SummaryText.Text = string.Empty;
            ToastService.Error($"โหลดคิว Program Review ไม่สำเร็จ: {ex.Message}");
        }
        finally
        {
            SummaryLoadingIcon.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>
    /// สร้างรายการในกล่อง "หมวด" ใหม่จากจำนวนจริงที่ Server นับมา พร้อมรักษาหมวดที่ครูเลือกค้างไว้
    ///
    /// เหตุผลที่ไม่ประกาศ 9 หมวดตายตัวไว้ใน XAML: ห้องหนึ่งมักมีของแค่ 3-4 หมวด การโชว์หมวดเปล่าๆ
    /// ที่กดแล้วไม่มีอะไรเลยทำให้ครูเสียเวลาไล่กดหาว่าหมวดไหนมีของ ซึ่งเป็นปัญหาเดิมที่ตัวกรองนี้
    /// ตั้งใจแก้ตั้งแต่แรก - ตัวเลขในวงเล็บทำให้เลือกได้ถูกตั้งแต่ครั้งแรก
    /// </summary>
    private void RebuildCategoryCombo(ProgramVerdictSummaryDto summary)
    {
        var previous = SelectedCategory;

        _suppressCategoryEvent = true;
        try
        {
            CategoryCombo.Items.Clear();

            var allItem = new ComboBoxItem { Content = $"ทุกหมวด ({summary.Total})", Tag = null };
            CategoryCombo.Items.Add(allItem);

            ComboBoxItem? toSelect = null;
            foreach (var entry in summary.Categories)
            {
                var item = new ComboBoxItem
                {
                    Content = $"{ProgramVerdictDto.CategoryLabel(entry.Category)} ({entry.Count})",
                    Tag = entry.Category
                };
                CategoryCombo.Items.Add(item);

                if (previous is not null && entry.Category == previous.Value)
                {
                    toSelect = item;
                }
            }

            // หมวดที่เคยเลือกไว้อาจไม่มีของแล้ว (ครูเพิ่งตัดสินตัวสุดท้ายไป) - ตกกลับไปที่ "ทุกหมวด"
            CategoryCombo.SelectedItem = toSelect ?? allItem;
        }
        finally
        {
            _suppressCategoryEvent = false;
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
        => await DecideAsync(ProgramActionDto.Allow);

    private async void BlockButton_Click(object sender, RoutedEventArgs e)
        => await DecideAsync(ProgramActionDto.Block);

    private async Task DecideAsync(ProgramActionDto action)
    {
        if (_apiClient is null || VerdictsGrid.SelectedItem is not ProgramVerdictDto selected)
        {
            return;
        }

        try
        {
            SetButtonsEnabled(false);

            var success = await _apiClient.DecideProgramVerdictAsync(selected.Id, (int)action);
            if (!success)
            {
                ToastService.Error($"ตั้งค่า '{selected.FileName}' ไม่สำเร็จ - ลองใหม่อีกครั้ง");
                SetButtonsEnabled(true);
                return;
            }

            await RefreshAsync();
            ToastService.Success(action == ProgramActionDto.Block
                ? $"บล็อก '{selected.FileName}' แล้ว - ทุกเครื่องจะเริ่มบล็อกภายในรอบ poll ถัดไป"
                : $"อนุญาต '{selected.FileName}' แล้ว - จะไม่ถูกถามซ้ำอีก");
        }
        catch (System.Exception ex)
        {
            ToastService.Error($"ตั้งค่า '{selected.FileName}' ไม่สำเร็จ: {ex.Message}");
            SetButtonsEnabled(true);
        }
    }
}
