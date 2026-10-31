using System.IO;
using System.Windows;
using Microsoft.Win32;
using OnionProcOparetor.RosterManager.Models;
using OnionProcOparetor.RosterManager.Services;

namespace OnionProcOparetor.RosterManager;

public partial class ImportWizardWindow : Window
{
    private const string NoneOption = "(ไม่มี)";

    private readonly ApiClient _api;
    private string _csvText = string.Empty;
    private string[] _csvHeader = Array.Empty<string>();
    private ImportSummaryDto? _lastDryRun;

    /// <summary>ใบรหัสผ่านที่นำเข้าสำเร็จ (เฉพาะแถว "New" ที่มี GeneratedPassword) - RosterWindow เอาไป
    /// เก็บเป็น "ชุดล่าสุด" สำหรับปุ่ม "พิมพ์ใบรหัสผ่าน…" ได้อีกครั้งถ้าจำเป็น (หน้าต่างนี้เปิด
    /// PrintSlipsWindow ให้เองทันทีหลัง commit สำเร็จแล้ว ตาม wireframe nf-import ขั้น 4)</summary>
    public List<PasswordSlipDto> ImportedSlips { get; } = new();

    public ImportWizardWindow(ApiClient api)
    {
        InitializeComponent();
        _api = api;
    }

    // ==================== ขั้น 1: เลือกไฟล์ ====================

    private void ChooseFileButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*" };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            _csvText = File.ReadAllText(dialog.FileName);
        }
        catch (Exception ex)
        {
            ChosenFileText.Text = $"อ่านไฟล์ไม่สำเร็จ: {ex.Message}";
            return;
        }

        var lines = _csvText.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length < 2)
        {
            ChosenFileText.Text = "ไฟล์นี้ต้องมีแถวหัวคอลัมน์ + ข้อมูลอย่างน้อย 1 แถว";
            return;
        }

        _csvHeader = lines[0].Split(',').Select(h => h.Trim().Trim('"')).ToArray();
        var rowCount = lines.Length - 1;

        ChosenFileText.Text = $"ไฟล์: {Path.GetFileName(dialog.FileName)} · {rowCount} แถว";
        FileSummaryText.Text = $"ไฟล์: {Path.GetFileName(dialog.FileName)} · {rowCount} แถว";

        PopulateColumnCombos();

        Step1Panel.Visibility = Visibility.Collapsed;
        Step2Panel.Visibility = Visibility.Visible;
    }

    private void PopulateColumnCombos()
    {
        StudentCodeColumnCombo.ItemsSource = _csvHeader;
        FullNameColumnCombo.ItemsSource = _csvHeader;
        ClassNameColumnCombo.ItemsSource = _csvHeader;
        EmailColumnCombo.ItemsSource = _csvHeader.Prepend(NoneOption).ToArray();
        BirthDateColumnCombo.ItemsSource = _csvHeader.Prepend(NoneOption).ToArray();

        StudentCodeColumnCombo.SelectedIndex = GuessColumn(["student_id", "studentid", "id", "code", "รหัส", "รหัสนักเรียน"]);
        FullNameColumnCombo.SelectedIndex = GuessColumn(["name", "fullname", "full_name", "ชื่อ", "ชื่อ-สกุล", "ชื่อสกุล"]);
        ClassNameColumnCombo.SelectedIndex = GuessColumn(["class", "classname", "class_name", "room", "ห้อง", "ชั้น", "ชั้น/ห้อง"]);

        var emailIndex = GuessColumn(["email", "mail", "schoolemail", "เมล", "อีเมล"]);
        EmailColumnCombo.SelectedIndex = emailIndex >= 0 ? emailIndex + 1 : 0;

        var birthIndex = GuessColumn(["birth", "birthdate", "dob", "วันเกิด"]);
        BirthDateColumnCombo.SelectedIndex = birthIndex >= 0 ? birthIndex + 1 : 0;
    }

    private int GuessColumn(string[] candidates)
    {
        for (var i = 0; i < _csvHeader.Length; i++)
        {
            var header = _csvHeader[i];
            if (candidates.Any(c => string.Equals(header, c, StringComparison.OrdinalIgnoreCase)))
            {
                return i;
            }
        }

        for (var i = 0; i < _csvHeader.Length; i++)
        {
            var header = _csvHeader[i].ToLowerInvariant();
            if (candidates.Any(c => header.Contains(c.ToLowerInvariant())))
            {
                return i;
            }
        }

        return 0;
    }

    // ==================== ขั้น 2: จับคู่คอลัมน์ + ตรวจ ====================

    private static string? SelectedColumnOrNull(System.Windows.Controls.ComboBox combo) =>
        combo.SelectedItem as string is { } value && value != NoneOption ? value : null;

    private ImportStudentsRequest BuildRequest()
    {
        var passwordPolicy = Random6Radio.IsChecked == true ? 0 : BirthDateRadio.IsChecked == true ? 1 : 2;

        return new ImportStudentsRequest
        {
            CsvText = _csvText,
            StudentCodeColumn = (string)StudentCodeColumnCombo.SelectedItem,
            FullNameColumn = (string)FullNameColumnCombo.SelectedItem,
            EmailColumn = SelectedColumnOrNull(EmailColumnCombo),
            ClassNameColumn = (string)ClassNameColumnCombo.SelectedItem,
            BirthDateColumn = SelectedColumnOrNull(BirthDateColumnCombo),
            DuplicatePolicy = UpdateExistingRadio.IsChecked == true ? 0 : 1,
            PasswordPolicy = passwordPolicy,
            FixedPassword = SameForAllRadio.IsChecked == true ? FixedPasswordBox.Text.Trim() : null,
            ForceChangePasswordOnFirstLogin = ForceChangeCheckBox.IsChecked == true
        };
    }

    private async void CheckButton_Click(object sender, RoutedEventArgs e)
    {
        if (StudentCodeColumnCombo.SelectedItem is null || FullNameColumnCombo.SelectedItem is null || ClassNameColumnCombo.SelectedItem is null)
        {
            return;
        }

        CheckButton.IsEnabled = false;
        var (success, summary, error) = await _api.ImportDryRunAsync(BuildRequest());
        CheckButton.IsEnabled = true;

        if (!success || summary is null)
        {
            ToastService.Error(error ?? "ตรวจไฟล์ไม่สำเร็จ");
            return;
        }

        _lastDryRun = summary;
        NewCountText.Text = summary.NewCount.ToString();
        DupCountText.Text = (summary.DuplicateUpdateCount + summary.DuplicateSkipCount).ToString();
        ErrorCountText.Text = summary.ErrorCount.ToString();
        PreviewGrid.ItemsSource = summary.Rows;
        NextToStep3Button.IsEnabled = summary.NewCount > 0 || summary.DuplicateUpdateCount > 0;
    }

    private void BackToStep1Button_Click(object sender, RoutedEventArgs e)
    {
        Step2Panel.Visibility = Visibility.Collapsed;
        Step1Panel.Visibility = Visibility.Visible;
    }

    private void NextToStep3Button_Click(object sender, RoutedEventArgs e)
    {
        if (_lastDryRun is null) return;

        Step3TitleText.Text = $"รหัสผ่านเริ่มต้นของนักเรียนใหม่ {_lastDryRun.NewCount} คน";
        Step3SummaryText.Text =
            $"เพิ่มใหม่ {_lastDryRun.NewCount} คน · ซ้ำ (อัปเดต) {_lastDryRun.DuplicateUpdateCount} คน · " +
            $"ข้าม {_lastDryRun.DuplicateSkipCount} คน · มีปัญหา {_lastDryRun.ErrorCount} แถว (จะไม่ถูกนำเข้า)";

        Step2Panel.Visibility = Visibility.Collapsed;
        Step3Panel.Visibility = Visibility.Visible;
    }

    private void BackToStep2Button_Click(object sender, RoutedEventArgs e)
    {
        Step3Panel.Visibility = Visibility.Collapsed;
        Step2Panel.Visibility = Visibility.Visible;
    }

    // ==================== ขั้น 3: ตั้งรหัสผ่านเริ่มต้น + commit ====================

    private async void CommitButton_Click(object sender, RoutedEventArgs e)
    {
        if (SameForAllRadio.IsChecked == true && string.IsNullOrWhiteSpace(FixedPasswordBox.Text))
        {
            SetCommitError("กรุณากรอกรหัสผ่านที่ใช้ร่วมกันทั้งชุด");
            return;
        }

        CommitButton.IsEnabled = false;
        var (success, summary, error) = await _api.ImportCommitAsync(BuildRequest());
        CommitButton.IsEnabled = true;

        if (!success || summary is null)
        {
            SetCommitError(error ?? "นำเข้าไม่สำเร็จ");
            return;
        }

        foreach (var row in summary.Rows.Where(r => r.GeneratedPassword is not null))
        {
            ImportedSlips.Add(new PasswordSlipDto
            {
                StudentCode = row.StudentCode,
                FullName = row.FullName,
                ClassName = row.ClassName,
                SchoolEmail = row.Email,
                Password = row.GeneratedPassword!
            });
        }

        ToastService.Success($"นำเข้าสำเร็จ: เพิ่มใหม่ {summary.NewCount} คน, อัปเดต {summary.DuplicateUpdateCount} คน");

        if (ImportedSlips.Count > 0)
        {
            // ตรงตาม nf-import ขั้น 4 "เสร็จ + พิมพ์ใบรหัส" - เปิดหน้าพิมพ์ให้ทันที เพราะรหัสผ่าน
            // แสดงได้ครั้งเดียว (ดู docs/roadmap.md "เก็บแบบ hash ย้อนดูไม่ได้")
            var printWindow = new PrintSlipsWindow(ImportedSlips) { Owner = this };
            printWindow.ShowDialog();
        }

        Close();
    }

    private void SetCommitError(string message)
    {
        CommitStatusText.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "Brush.Danger.Default");
        CommitStatusText.Text = message;
    }
}
