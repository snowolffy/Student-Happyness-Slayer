using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using OnionProcOparetor.AgentTray.Services;

namespace OnionProcOparetor.AgentTray;

/// <summary>
/// การ์ด "ขอความช่วยเหลือจากครู" (WP4-2, 2026-11-01) - ดู doc comment ใน .xaml สำหรับที่มาของ wireframe
///
/// เลือกเหตุผลสำเร็จรูปได้ 1 ใน 3 ปุ่ม (ไฮไลต์เองด้วยโค้ดตรงนี้ ไม่ใช้ ToggleButton/RadioButton เพราะ
/// AgentTray ไม่มี style "pill" ที่ไฮไลต์ปุ่มที่เลือกแบบ Console - ไม่อยากเพิ่ม key ใหม่ลง Theme/AppTheme.xaml
/// ที่ใช้ร่วมกับหน้าต่างอื่นทั้งหมดเพื่อ UI เล็กๆ จุดเดียวนี้) ต้องเลือกเหตุผลก่อนกดยกมือได้เสมอ
/// </summary>
public partial class RaiseHandWindow : Window
{
    private static readonly Brush SelectedBackground = new SolidColorBrush(Color.FromRgb(0x2B, 0x2F, 0x36));
    private static readonly Brush SelectedForeground = Brushes.White;

    private readonly ClientApiClient _apiClient;
    private Button? _selectedReasonButton;
    private string? _selectedReason;

    /// <summary>ยิงตอนยกมือสำเร็จจริง (Server ยืนยันแล้ว) - App.xaml.cs ฟังไว้เปิด RaiseHandBadgeWindow พร้อมตำแหน่งในคิว</summary>
    public event Action<int?>? Raised;

    public RaiseHandWindow(ClientApiClient apiClient)
    {
        InitializeComponent();
        _apiClient = apiClient;
    }

    private void ReasonButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button clicked) return;

        // คืนปุ่มเดิมกลับเป็นค่าเริ่มต้นก่อนไฮไลต์ปุ่มใหม่ (เลือกได้ทีละปุ่มเท่านั้น)
        if (_selectedReasonButton is not null)
        {
            _selectedReasonButton.ClearValue(BackgroundProperty);
            _selectedReasonButton.ClearValue(ForegroundProperty);
        }

        clicked.Background = SelectedBackground;
        clicked.Foreground = SelectedForeground;
        _selectedReasonButton = clicked;
        _selectedReason = clicked.Content?.ToString();
        StatusText.Text = string.Empty;
    }

    private async void RaiseButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_selectedReason))
        {
            StatusText.Text = "กรุณาเลือกเหตุผลก่อน";
            return;
        }

        RaiseButton.IsEnabled = false;
        StatusText.Text = string.Empty;

        var note = string.IsNullOrWhiteSpace(NoteBox.Text) ? null : NoteBox.Text.Trim();
        var result = await _apiClient.RaiseHandAsync(_selectedReason, note);

        if (result is null)
        {
            StatusText.Text = "ติดต่อ Agent บนเครื่องนี้ไม่ได้";
            RaiseButton.IsEnabled = true;
            return;
        }

        if (!result.Success)
        {
            StatusText.Text = result.Error ?? "ยกมือไม่สำเร็จ";
            RaiseButton.IsEnabled = true;
            return;
        }

        Raised?.Invoke(result.QueuePosition);
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => Close();
}
