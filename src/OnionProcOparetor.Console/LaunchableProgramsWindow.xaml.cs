using System.Windows;
using System.Windows.Controls;
using OnionProcOparetor.Console.Models;
using OnionProcOparetor.Console.Services;

namespace OnionProcOparetor.Console;

/// <summary>
/// "จัดการรายการโปรแกรม" (Admin เท่านั้น) (WP4-1, 2026-11-01) - เปิดจากหน้าตั้งค่า (ดู
/// SettingsPage.xaml.cs ManageLaunchableProgramsButton_Click) ให้ Admin เพิ่ม/แก้ไข/ลบโปรแกรมที่
/// Teacher เลือกเปิดได้จาก OpenUrlWindow Server เป็นคนกัน 403 จริงถ้า Teacher ยิง API เอง - หน้าต่างนี้
/// ไม่เช็ค role ซ้ำ (เหมือน AddAllowListPresetWindow ที่ไม่เช็คเองเหมือนกัน)
/// </summary>
public partial class LaunchableProgramsWindow : Window
{
    private readonly ApiClient _apiClient;
    private List<LaunchableProgramDto> _programs = new();
    private int? _editingId;

    /// <summary>true = มีการเปลี่ยนแปลงรายการจริง - ผู้เรียก (SettingsPage) ไม่ต้องรีโหลดอะไรต่อในรอบนี้
    /// เพราะ OpenUrlWindow โหลด GetLaunchableProgramsAsync() ใหม่ทุกครั้งที่เปิดอยู่แล้ว แต่เก็บไว้เผื่อ
    /// ผู้เรียกในอนาคตต้องใช้</summary>
    public bool DataChanged { get; private set; }

    public LaunchableProgramsWindow(ApiClient apiClient)
    {
        InitializeComponent();
        _apiClient = apiClient;

        Loaded += async (_, _) => await LoadProgramsAsync();
    }

    private async Task LoadProgramsAsync()
    {
        try
        {
            _programs = await _apiClient.GetLaunchableProgramsAsync();
            ProgramsGrid.ItemsSource = _programs;
        }
        catch (Exception)
        {
            SetFormStatus("โหลดรายการจาก Server ไม่สำเร็จ", isError: true);
        }
    }

    private async void SaveProgramButton_Click(object sender, RoutedEventArgs e)
    {
        var name = NameTextBox.Text.Trim();
        var path = PathTextBox.Text.Trim();
        var arguments = string.IsNullOrWhiteSpace(ArgumentsTextBox.Text) ? null : ArgumentsTextBox.Text.Trim();

        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(path))
        {
            SetFormStatus("ต้องระบุชื่อและ path", isError: true);
            return;
        }

        SaveProgramButton.IsEnabled = false;
        var success = _editingId is { } id
            ? await _apiClient.UpdateLaunchableProgramAsync(id, name, path, arguments)
            : await _apiClient.AddLaunchableProgramAsync(name, path, arguments);
        SaveProgramButton.IsEnabled = true;

        if (!success)
        {
            SetFormStatus("บันทึกไม่สำเร็จ - อาจมีชื่อนี้อยู่แล้ว หรือต่อ Server ไม่ได้", isError: true);
            return;
        }

        DataChanged = true;
        ResetForm();
        await LoadProgramsAsync();
    }

    private void EditProgramButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: LaunchableProgramDto program })
        {
            return;
        }

        _editingId = program.Id;
        NameTextBox.Text = program.Name;
        PathTextBox.Text = program.Path;
        ArgumentsTextBox.Text = program.Arguments ?? string.Empty;
        FormTitleText.Text = $"แก้ไขโปรแกรม '{program.Name}'";
        SaveProgramButton.Content = "บันทึก";
        CancelEditButton.Visibility = Visibility.Visible;
        SetFormStatus(string.Empty, isError: false);
    }

    private async void DeleteProgramButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: LaunchableProgramDto program })
        {
            return;
        }

        if (!ConfirmWindow.Show(
                $"ยืนยันลบโปรแกรม '{program.Name}'? Teacher จะเลือกโปรแกรมนี้จากรายการเปิด URL/โปรแกรมไม่ได้อีก",
                "ยืนยันการลบ", this))
        {
            return;
        }

        var success = await _apiClient.DeleteLaunchableProgramAsync(program.Id);
        if (!success)
        {
            SetFormStatus("ลบไม่สำเร็จ - ต่อ Server ไม่ได้", isError: true);
            return;
        }

        DataChanged = true;
        if (_editingId == program.Id)
        {
            ResetForm();
        }

        await LoadProgramsAsync();
    }

    private void CancelEditButton_Click(object sender, RoutedEventArgs e) => ResetForm();

    private void ResetForm()
    {
        _editingId = null;
        NameTextBox.Text = string.Empty;
        PathTextBox.Text = string.Empty;
        ArgumentsTextBox.Text = string.Empty;
        FormTitleText.Text = "เพิ่มโปรแกรมใหม่";
        SaveProgramButton.Content = "เพิ่ม";
        CancelEditButton.Visibility = Visibility.Collapsed;
        SetFormStatus(string.Empty, isError: false);
    }

    private void SetFormStatus(string message, bool isError)
    {
        FormStatusText.Text = message;
        FormStatusText.SetResourceReference(TextBlock.ForegroundProperty, isError ? "Brush.Danger.Default" : "Brush.Text.Secondary");
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = DataChanged;
        Close();
    }
}
