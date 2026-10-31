using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using OnionProcOparetor.RosterManager.Models;
using OnionProcOparetor.RosterManager.Services;

namespace OnionProcOparetor.RosterManager;

public partial class RosterWindow : Window
{
    private readonly ApiClient _api;
    private readonly DispatcherTimer _searchDebounce;
    private bool _isInitializing = true;

    /// <summary>ชุดรหัสผ่านที่เพิ่งสร้าง/รีเซ็ตล่าสุด - พิมพ์ซ้ำได้จากปุ่ม "พิมพ์ใบรหัสผ่าน…" จนกว่าจะ
    /// ปิดโปรแกรมหรือมีชุดใหม่มาแทน (รหัสผ่านแสดงได้ครั้งเดียวตาม docs/roadmap.md "เก็บแบบ hash ย้อนดูไม่ได้"
    /// - เก็บ plaintext ไว้ใน memory ของ session นี้เท่านั้น ไม่ลงดิสก์)</summary>
    private List<PasswordSlipDto> _lastPrintableSlips = new();

    private sealed class ClassNavItem
    {
        public required string Label { get; init; }
        public string? ClassName { get; init; }
        public int Count { get; init; }
    }

    public RosterWindow(ApiClient api)
    {
        InitializeComponent();
        _api = api;

        _searchDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _searchDebounce.Tick += (_, _) => { _searchDebounce.Stop(); _ = LoadStudentsAsync(); };

        SessionInfoText.Text = $"{_api.DisplayName} ({_api.Role}) · Server {_api.BaseUrl}";
        StatusFilterCombo.SelectedIndex = 0;

        // Teacher แก้กฎถาวร/ผู้ใช้/ห้องไม่ได้ (docs/roadmap.md) - endpoint ของปุ่มเหล่านี้ฝั่ง Server
        // ตอบ 403 อยู่แล้วถ้าเกิดกดได้ นี่แค่เพื่อ UX (ดู Console's ApiClient.IsAdmin comment เดิม)
        ImportButton.IsEnabled = _api.IsAdmin;
        PromoteButton.IsEnabled = _api.IsAdmin;
        AddButton.IsEnabled = _api.IsAdmin;

        _isInitializing = false;
        _ = InitialLoadAsync();
    }

    private async Task InitialLoadAsync()
    {
        await LoadClassesAsync();
        await LoadStudentsAsync();
    }

    private async Task LoadClassesAsync()
    {
        try
        {
            var counts = await _api.GetClassCountsAsync();
            var items = new List<ClassNavItem>
            {
                new() { Label = "ทั้งหมด", ClassName = null, Count = counts.Sum(c => c.Count) }
            };
            items.AddRange(counts.OrderBy(c => c.ClassName, StringComparer.OrdinalIgnoreCase)
                .Select(c => new ClassNavItem { Label = c.ClassName, ClassName = c.ClassName, Count = c.Count }));

            var previouslySelected = (ClassList.SelectedItem as ClassNavItem)?.ClassName;
            ClassList.ItemsSource = items;
            ClassList.SelectedItem = items.FirstOrDefault(i => i.ClassName == previouslySelected) ?? items[0];

            ClearBanner();
        }
        catch (Exception ex)
        {
            ShowBanner($"โหลดรายชื่อชั้น/ห้องไม่สำเร็จ: {ex.Message}");
        }
    }

    private async Task LoadStudentsAsync()
    {
        if (_isInitializing)
        {
            return;
        }

        var className = (ClassList.SelectedItem as ClassNavItem)?.ClassName;
        var status = (StatusFilterCombo.SelectedItem as ComboBoxItem)?.Tag as string;
        var search = SearchBox.Text.Trim();

        try
        {
            var students = await _api.GetStudentsAsync(className, string.IsNullOrEmpty(search) ? null : search, string.IsNullOrEmpty(status) ? null : status);
            StudentsGrid.ItemsSource = students;

            var label = className ?? "ทั้งหมด";
            HeaderTitleText.Text = $"{label} · {students.Count} คน";

            ClearBanner();
        }
        catch (Exception ex)
        {
            ShowBanner($"โหลดรายชื่อนักเรียนไม่สำเร็จ: {ex.Message}");
        }

        RenderInspector();
    }

    // ==================== sidebar / filter ====================

    private async void ClassList_SelectionChanged(object sender, SelectionChangedEventArgs e) => await LoadStudentsAsync();

    private async void StatusFilterCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) => await LoadStudentsAsync();

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _searchDebounce.Stop();
        _searchDebounce.Start();
    }

    private void StudentsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) => RenderInspector();

    // ==================== inspector ====================

    private StudentDto? SelectedSingleStudent =>
        StudentsGrid.SelectedItems.Count == 1 ? StudentsGrid.SelectedItems[0] as StudentDto : null;

    private List<StudentDto> SelectedStudents => StudentsGrid.SelectedItems.Cast<StudentDto>().ToList();

    private void RenderInspector()
    {
        var selectedCount = StudentsGrid.SelectedItems.Count;

        SelectionCountText.Text = selectedCount > 1 ? $"เลือก {selectedCount} คน →" : string.Empty;
        BulkResetButton.Visibility = selectedCount > 1 ? Visibility.Visible : Visibility.Collapsed;
        BulkMoveButton.Visibility = selectedCount > 1 ? Visibility.Visible : Visibility.Collapsed;

        InspectorEmptyText.Visibility = Visibility.Collapsed;
        InspectorSinglePanel.Visibility = Visibility.Collapsed;
        InspectorMultiPanel.Visibility = Visibility.Collapsed;

        if (selectedCount > 1)
        {
            InspectorMultiPanel.Visibility = Visibility.Visible;
            InspectorMultiCountText.Text = $"เลือก {selectedCount} คน";
            return;
        }

        var student = SelectedSingleStudent;
        if (student is null)
        {
            InspectorEmptyText.Visibility = Visibility.Visible;
            return;
        }

        InspectorSinglePanel.Visibility = Visibility.Visible;
        InspectorName.Text = student.FullName;
        InspectorMeta.Text = $"{student.StudentCode} · {student.ClassName} · {(string.IsNullOrEmpty(student.SchoolEmail) ? "ไม่มีเมล" : student.SchoolEmail)}";
        InspectorStatusBadge.Content = student.StatusLabel;

        InspectorUnlockButton.Visibility = student.IsLocked ? Visibility.Visible : Visibility.Collapsed;
        InspectorUnlockButton.IsEnabled = _api.IsAdmin; // Server ยอม Teacher ด้วยถ้าเปิดสิทธิ์ - ปล่อยให้ 403 บอกเองถ้าปิดสิทธิ์
        InspectorResetButton.IsEnabled = true; // เหมือนกัน - ปุ่มกดได้เสมอ ปล่อยให้ Server ตัดสิน 403
        InspectorEditButton.Visibility = _api.IsAdmin ? Visibility.Visible : Visibility.Collapsed;

        InspectorToggleDisableButton.Content = student.IsDisabled ? "เปิดใช้งานบัญชี" : "ปิดใช้งานบัญชี";
        InspectorToggleDisableButton.SetResourceReference(StyleProperty, student.IsDisabled ? "ButtonSecondary" : "ButtonDanger");
        InspectorToggleDisableButton.Visibility = _api.IsAdmin ? Visibility.Visible : Visibility.Collapsed;

        _ = LoadInspectorHistoryAsync(student.StudentCode);
    }

    private async Task LoadInspectorHistoryAsync(string studentCode)
    {
        InspectorHistoryList.Children.Clear();
        List<SessionHistoryRowDto> history;
        try
        {
            history = await _api.GetSessionHistoryAsync(studentCode, take: 5);
        }
        catch
        {
            return;
        }

        if (history.Count == 0)
        {
            InspectorHistoryList.Children.Add(new TextBlock
            {
                Text = "ยังไม่มีประวัติการใช้เครื่อง",
                FontSize = 12,
                Opacity = 0.7
            });
            return;
        }

        foreach (var row in history)
        {
            var started = row.StartedAt.ToLocalTime();
            var duration = row.EndedAt is { } ended
                ? $"{(ended - row.StartedAt).TotalMinutes:F0} นาที"
                : "กำลังใช้งาน";

            InspectorHistoryList.Children.Add(new TextBlock
            {
                Text = $"{started:dd/MM HH:mm} · {row.MachineName} · {duration}",
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 4),
                TextWrapping = TextWrapping.Wrap
            });
        }
    }

    private async void InspectorUnlockButton_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedSingleStudent is not { } student) return;

        var (success, message) = await _api.UnlockAsync(student.Id);
        if (success)
        {
            ToastService.Success($"ปลดล็อกบัญชี '{student.StudentCode}' แล้ว");
            await LoadStudentsAsync();
        }
        else
        {
            ToastService.Error(message);
        }
    }

    private async void InspectorResetButton_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedSingleStudent is not { } student) return;

        if (!ConfirmWindow.Ask(this, "รีเซ็ตรหัสผ่าน",
                $"สุ่มรหัสผ่านใหม่ให้ '{student.FullName}' ({student.StudentCode}) และบังคับให้เปลี่ยนรหัสตอนล็อกอินครั้งหน้า?"))
        {
            return;
        }

        var (success, message, generatedPassword) = await _api.ResetPasswordAsync(student.Id);
        if (success && generatedPassword is not null)
        {
            _lastPrintableSlips = new List<PasswordSlipDto>
            {
                new()
                {
                    StudentCode = student.StudentCode,
                    FullName = student.FullName,
                    ClassName = student.ClassName,
                    SchoolEmail = student.SchoolEmail,
                    Password = generatedPassword
                }
            };

            ToastService.Success($"รีเซ็ตรหัสผ่านสำเร็จ: {generatedPassword}", "พิมพ์ใบรหัส", () => OpenPrintWindow(_lastPrintableSlips));
            await LoadStudentsAsync();
        }
        else
        {
            ToastService.Error(message);
        }
    }

    private void InspectorEditButton_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedSingleStudent is not { } student) return;

        var editWindow = new AddEditStudentWindow(_api, student) { Owner = this };
        if (editWindow.ShowDialog() == true)
        {
            _ = LoadStudentsAsync();
        }
    }

    private async void InspectorToggleDisableButton_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedSingleStudent is not { } student) return;

        var disabling = !student.IsDisabled;
        var verb = disabling ? "ปิดใช้งาน" : "เปิดใช้งาน";
        if (!ConfirmWindow.Ask(this, $"{verb}บัญชี", $"ยืนยัน{verb}บัญชีของ '{student.FullName}' ({student.StudentCode})?", verb, danger: disabling))
        {
            return;
        }

        var (success, message) = await _api.SetDisabledAsync(student.Id, disabling);
        if (success)
        {
            ToastService.Success($"{verb}บัญชี '{student.StudentCode}' แล้ว");
            await LoadStudentsAsync();
        }
        else
        {
            ToastService.Error(message);
        }
    }

    // ==================== bulk actions ====================

    private async void BulkResetButton_Click(object sender, RoutedEventArgs e)
    {
        var students = SelectedStudents;
        if (students.Count < 2) return;

        if (!ConfirmWindow.Ask(this, "รีเซ็ตรหัสที่เลือก", $"สุ่มรหัสผ่านใหม่ให้นักเรียนที่เลือกไว้ {students.Count} คน?"))
        {
            return;
        }

        var slips = new List<PasswordSlipDto>();
        var failures = 0;

        foreach (var student in students)
        {
            var (success, _, generatedPassword) = await _api.ResetPasswordAsync(student.Id);
            if (success && generatedPassword is not null)
            {
                slips.Add(new PasswordSlipDto
                {
                    StudentCode = student.StudentCode,
                    FullName = student.FullName,
                    ClassName = student.ClassName,
                    SchoolEmail = student.SchoolEmail,
                    Password = generatedPassword
                });
            }
            else
            {
                failures++;
            }
        }

        if (slips.Count > 0)
        {
            _lastPrintableSlips = slips;
            ToastService.Success($"รีเซ็ตรหัสผ่านสำเร็จ {slips.Count} คน" + (failures > 0 ? $" (ล้มเหลว {failures} คน)" : ""),
                "พิมพ์ใบรหัส", () => OpenPrintWindow(_lastPrintableSlips));
        }
        else
        {
            ToastService.Error("รีเซ็ตรหัสผ่านไม่สำเร็จเลย");
        }

        await LoadStudentsAsync();
    }

    private async void BulkMoveButton_Click(object sender, RoutedEventArgs e)
    {
        var students = SelectedStudents;
        if (students.Count < 2) return;

        var newClassName = TextInputWindow.Ask(this, "ย้ายห้อง", $"ย้ายนักเรียนที่เลือกไว้ {students.Count} คน ไปชั้น/ห้องใด?");
        if (string.IsNullOrWhiteSpace(newClassName))
        {
            return;
        }

        var failures = 0;
        foreach (var student in students)
        {
            var (success, _) = await _api.MoveClassAsync(student.Id, newClassName);
            if (!success) failures++;
        }

        ToastService.Success(failures == 0
            ? $"ย้ายห้องสำเร็จ {students.Count} คน"
            : $"ย้ายห้องสำเร็จ {students.Count - failures}/{students.Count} คน");

        await LoadClassesAsync();
        await LoadStudentsAsync();
    }

    // ==================== toolbar ====================

    private async void AddButton_Click(object sender, RoutedEventArgs e)
    {
        var addWindow = new AddEditStudentWindow(_api, null) { Owner = this };
        if (addWindow.ShowDialog() == true)
        {
            if (addWindow.CreatedSlip is { } slip)
            {
                _lastPrintableSlips = new List<PasswordSlipDto> { slip };
                ToastService.Success($"เพิ่มนักเรียนสำเร็จ: {slip.Password}", "พิมพ์ใบรหัส", () => OpenPrintWindow(_lastPrintableSlips));
            }

            await LoadClassesAsync();
            await LoadStudentsAsync();
        }
    }

    private async void ImportButton_Click(object sender, RoutedEventArgs e)
    {
        var importWindow = new ImportWizardWindow(_api) { Owner = this };
        importWindow.ShowDialog();

        if (importWindow.ImportedSlips.Count > 0)
        {
            _lastPrintableSlips = importWindow.ImportedSlips;
        }

        await LoadClassesAsync();
        await LoadStudentsAsync();
    }

    private async void PromoteButton_Click(object sender, RoutedEventArgs e)
    {
        var promoteWindow = new PromoteWindow(_api) { Owner = this };
        if (promoteWindow.ShowDialog() == true)
        {
            await LoadClassesAsync();
            await LoadStudentsAsync();
        }
    }

    private void PrintButton_Click(object sender, RoutedEventArgs e)
    {
        if (_lastPrintableSlips.Count == 0)
        {
            ToastService.Info("ยังไม่มีรหัสผ่านที่พิมพ์ได้ในตอนนี้ - รหัสผ่านแสดงได้ครั้งเดียวตอนเพิ่ม/นำเข้า/รีเซ็ตเท่านั้น (เก็บแบบ hash ย้อนดูไม่ได้)");
            return;
        }

        OpenPrintWindow(_lastPrintableSlips);
    }

    private void OpenPrintWindow(List<PasswordSlipDto> slips)
    {
        var printWindow = new PrintSlipsWindow(slips) { Owner = this };
        printWindow.ShowDialog();
    }

    private void LogoutButton_Click(object sender, RoutedEventArgs e)
    {
        var login = new LoginWindow();
        login.Show();
        Close();
    }

    // ==================== แถบประกาศ ====================

    private void ShowBanner(string message)
    {
        BannerText.Text = message;
        BannerBorder.Visibility = Visibility.Visible;
    }

    private void ClearBanner()
    {
        BannerBorder.Visibility = Visibility.Collapsed;
    }

    private async void BannerFixButton_Click(object sender, RoutedEventArgs e)
    {
        await LoadClassesAsync();
        await LoadStudentsAsync();
    }

    private void RosterWindow_Closed(object sender, EventArgs e)
    {
        RosterDiagnostics.WriteLine("RosterWindow ปิด");
    }
}
