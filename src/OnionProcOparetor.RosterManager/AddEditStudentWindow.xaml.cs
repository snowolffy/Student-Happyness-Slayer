using System.Windows;
using OnionProcOparetor.RosterManager.Models;
using OnionProcOparetor.RosterManager.Services;

namespace OnionProcOparetor.RosterManager;

public partial class AddEditStudentWindow : Window
{
    private readonly ApiClient _api;
    private readonly StudentDto? _editing;

    /// <summary>มีค่าเฉพาะตอนเพิ่มนักเรียนใหม่สำเร็จ (ไม่มีตอนแก้ไข - แก้ไขไม่แตะรหัสผ่าน) - RosterWindow
    /// เอาไปเปิด PrintSlipsWindow ต่อได้ทันที (ตรงตาม wireframe nf-print "พิมพ์ใบรหัสตอนนี้ เพราะหลัง
    /// ปิดหน้านี้จะดูรหัสเริ่มต้นไม่ได้อีก")</summary>
    public PasswordSlipDto? CreatedSlip { get; private set; }

    public AddEditStudentWindow(ApiClient api, StudentDto? editing)
    {
        InitializeComponent();
        _api = api;
        _editing = editing;

        if (editing is null)
        {
            TitleText.Text = "เพิ่มนักเรียน";
        }
        else
        {
            TitleText.Text = "แก้ข้อมูลนักเรียน";
            StudentCodeBox.Text = editing.StudentCode;
            FullNameBox.Text = editing.FullName;
            EmailBox.Text = editing.SchoolEmail ?? string.Empty;
            ClassNameBox.Text = editing.ClassName;
            // แก้ไขไม่แตะรหัสผ่านเลย (ตรงกับ PUT /api/students/{id} ฝั่ง Server ที่ไม่มี field รหัสผ่าน)
            PasswordPanel.Visibility = Visibility.Collapsed;
            SaveButton.Content = "บันทึกการแก้ไข";
        }
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        var code = StudentCodeBox.Text.Trim();
        var name = FullNameBox.Text.Trim();
        var email = string.IsNullOrWhiteSpace(EmailBox.Text) ? null : EmailBox.Text.Trim();
        var className = ClassNameBox.Text.Trim();

        if (code.Length == 0 || name.Length == 0 || className.Length == 0)
        {
            SetError("กรุณากรอกรหัสนักเรียน ชื่อ-สกุล และชั้น/ห้องเรียน");
            return;
        }

        SaveButton.IsEnabled = false;

        if (_editing is null)
        {
            var password = string.IsNullOrWhiteSpace(PasswordBox.Text) ? null : PasswordBox.Text.Trim();
            var (success, message, student, generatedPassword) = await _api.CreateStudentAsync(
                code, name, email, className, password, ForceChangeCheckBox.IsChecked == true);

            if (success && student is not null && generatedPassword is not null)
            {
                CreatedSlip = new PasswordSlipDto
                {
                    StudentCode = student.StudentCode,
                    FullName = student.FullName,
                    ClassName = student.ClassName,
                    SchoolEmail = student.SchoolEmail,
                    Password = generatedPassword
                };
                DialogResult = true;
                Close();
            }
            else
            {
                SetError(message);
                SaveButton.IsEnabled = true;
            }
        }
        else
        {
            var (success, message) = await _api.UpdateStudentAsync(_editing.Id, code, name, email, className);
            if (success)
            {
                DialogResult = true;
                Close();
            }
            else
            {
                SetError(message);
                SaveButton.IsEnabled = true;
            }
        }
    }

    private void SetError(string message)
    {
        StatusText.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "Brush.Danger.Default");
        StatusText.Text = message;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
