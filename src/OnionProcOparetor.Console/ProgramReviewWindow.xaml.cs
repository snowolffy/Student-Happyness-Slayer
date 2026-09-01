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
    private bool _suppressCategoryEvent;

    public ProgramReviewWindow(ApiClient apiClient)
    {
        InitializeComponent();
        _apiClient = apiClient;
        Loaded += async (_, _) => await RefreshAsync();
    }

    private string SelectedFilter =>
        (FilterCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "flagged";

    /// <summary>null = ทุกหมวด (Tag ของตัวเลือก "ทุกหมวด" เป็น null)</summary>
    private int? SelectedCategory =>
        (CategoryCombo.SelectedItem as ComboBoxItem)?.Tag as int?;

    private async void RefreshButton_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

    private async void FilterCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // ยิงตอน InitializeComponent ตั้ง IsSelected ครั้งแรกด้วย ซึ่งตอนนั้น _apiClient ยังเป็น null
        // (constructor ยังไม่ทันได้ assign) - Loaded handler จะ refresh ให้อยู่แล้ว ข้ามรอบนั้นไป
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
        try
        {
            StatusText.Text = "กำลังโหลด...";
            VerdictsGrid.ItemsSource = null;
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
                StatusText.Text = "ไม่มีรายการในหมวดนี้";
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

            StatusText.Text = $"{scope} - เรียงตามจำนวนครั้งที่พบมากที่สุดก่อน{truncated}";
        }
        catch (System.Exception ex)
        {
            StatusText.Text = $"โหลดไม่สำเร็จ: {ex.Message}";
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
            // แทนที่จะปล่อยให้ไม่มีอะไรถูกเลือกซึ่งทำให้ SelectedCategory เป็น null โดยบังเอิญเหมือนกัน
            // แต่กล่องดูเหมือนว่างเปล่าจนครูไม่รู้ว่าตอนนี้กำลังดูอะไรอยู่
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
