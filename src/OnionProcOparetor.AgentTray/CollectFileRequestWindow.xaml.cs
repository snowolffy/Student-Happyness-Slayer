using System.IO;
using System.Windows;
using Microsoft.Win32;
using OnionProcOparetor.AgentTray.Models;
using OnionProcOparetor.AgentTray.Services;

namespace OnionProcOparetor.AgentTray;

/// <summary>
/// การ์ด "ครูขอเก็บไฟล์งาน" (WP4-3, 2026-11-01) - ดู doc comment ใน .xaml สำหรับที่มา เปิดจาก
/// App.xaml.cs ตอนได้รับ BroadcastPipeListener.CollectFileRequested (ครูกดสั่งจาก Console)
///
/// เลือกไฟล์ผ่าน OpenFileDialog ธรรมดา (filter มาจาก FileNamePattern ของงาน - ใบ้เท่านั้น ไม่บังคับ
/// เพราะมี "ทุกไฟล์ (*.*)" ให้เลือกเสมอ) แล้วอัปโหลดผ่าน ClientApiClient ไปที่ Agent local API ซึ่ง
/// relay ต่อขึ้น Server อีกที (ดู ClientApiClient.UploadCollectedFileAsync)
/// </summary>
public partial class CollectFileRequestWindow : Window
{
    private readonly ClientApiClient _apiClient;
    private readonly CollectFileRequestDto _request;
    private string? _selectedFilePath;

    public CollectFileRequestWindow(ClientApiClient apiClient, CollectFileRequestDto request)
    {
        InitializeComponent();
        _apiClient = apiClient;
        _request = request;

        JobNameText.Text = request.JobName;

        var hints = new List<string> { $"ขนาดสูงสุด {request.MaxFileSizeMb} MB" };
        if (!string.IsNullOrWhiteSpace(request.FileNamePattern))
        {
            hints.Add($"ชนิดไฟล์ที่รับ: {request.FileNamePattern}");
        }
        if (request.ClosesAtUtc is { } closesAtUtc)
        {
            hints.Add($"ปิดรับ: {closesAtUtc.ToLocalTime():dd/MM/yyyy HH:mm}");
        }
        HintText.Text = string.Join(" · ", hints);
    }

    private void ChooseFileButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "เลือกไฟล์ส่งครู",
            Filter = BuildDialogFilter(_request.FileNamePattern),
            CheckFileExists = true
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var info = new FileInfo(dialog.FileName);
        var maxBytes = (long)_request.MaxFileSizeMb * 1024 * 1024;
        if (info.Length > maxBytes)
        {
            StatusText.Text = $"ไฟล์นี้ใหญ่เกินกำหนด ({info.Length / 1024 / 1024} MB > สูงสุด {_request.MaxFileSizeMb} MB) - เลือกไฟล์อื่น";
            SendButton.IsEnabled = false;
            _selectedFilePath = null;
            SelectedFileText.Text = "ยังไม่ได้เลือกไฟล์";
            return;
        }

        _selectedFilePath = dialog.FileName;
        SelectedFileText.Text = $"{info.Name} · {info.Length / 1024} KB";
        SendButton.IsEnabled = true;
        StatusText.Text = string.Empty;
    }

    private async void SendButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedFilePath is null)
        {
            return;
        }

        SendButton.IsEnabled = false;
        ChooseFileButton.IsEnabled = false;
        StatusText.Foreground = System.Windows.Media.Brushes.Gray;
        StatusText.Text = "กำลังส่ง...";

        var (success, error) = await _apiClient.UploadCollectedFileAsync(_request.JobId, _selectedFilePath, CancellationToken.None);

        if (!success)
        {
            StatusText.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "AccentRedBrush");
            StatusText.Text = error ?? "ส่งไฟล์ไม่สำเร็จ";
            SendButton.IsEnabled = true;
            ChooseFileButton.IsEnabled = true;
            return;
        }

        StatusText.Foreground = System.Windows.Media.Brushes.Green;
        StatusText.Text = "ส่งไฟล์สำเร็จแล้ว";
        await Task.Delay(1200);
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => Close();

    /// <summary>
    /// แปลง "*.sb3, *.docx" (รูปแบบเดียวกับที่ครูพิมพ์ใน Console) เป็น WPF OpenFileDialog Filter string
    /// ("ชื่อ|รูปแบบ1;รูปแบบ2|...") เติม "ทุกไฟล์ (*.*)" ไว้เสมอ (ใบ้เท่านั้น ไม่บังคับ - Server เป็นคน
    /// บังคับขนาดจริง ไม่บังคับชนิดไฟล์เข้มที่ชั้นไหนเลยตามที่ตัดสินใจไว้ใน Models/FileCollectionJob.cs)
    /// </summary>
    private static string BuildDialogFilter(string? pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return "ทุกไฟล์ (*.*)|*.*";
        }

        var patterns = pattern.Split(',', ';')
            .Select(p => p.Trim())
            .Where(p => p.Length > 0)
            .ToList();

        if (patterns.Count == 0)
        {
            return "ทุกไฟล์ (*.*)|*.*";
        }

        var joined = string.Join(";", patterns);
        return $"ไฟล์ที่ครูรับ ({joined})|{joined}|ทุกไฟล์ (*.*)|*.*";
    }
}
