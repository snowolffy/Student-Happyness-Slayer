using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using OnionProcOparetor.Console.Models;
using OnionProcOparetor.Console.Services;

namespace OnionProcOparetor.Console;

/// <summary>
/// "แจก / เก็บไฟล์" (WP4-3, 2026-11-01) - ดู wireframe docs/wireframes/tc-files.dc.html และ doc comment
/// ใน .xaml สำหรับจุดที่ต่างจาก wireframe ตั้งใจ เปิดจากแถบคำสั่งของ Clients (ดู
/// DashboardWindow.FilesButton_Click)
///
/// ต่างจาก OpenUrlWindow ตรงที่ไม่ปิดตัวเองแล้วฝาก PendingReport ให้ Dashboard เปิดหน้า report ต่อ -
/// หน้าต่างนี้ต้องอยู่ค้างไว้เพื่อโชว่สถานะงานเก็บไฟล์ (ตาราง + "ได้ X/Y") ต่อเนื่อง จึงเปิด
/// ActionReportWindow เองตรงๆ (Show แบบ non-modal ซ้อนขึ้นมาอีกบาน) ทั้งตอนแจกไฟล์และตอนส่ง command
/// "CollectFile" ให้เครื่องเป้าหมาย - pattern เดียวกับที่ DashboardWindow ทำ แค่เรียกจากที่นี่แทน
/// </summary>
public partial class FilesWindow : Window
{
    private readonly ApiClient _apiClient;
    private readonly LabHubClient? _labHubClient;
    private readonly List<ClientMachineDto> _selectedClients;
    private readonly List<ClientMachineDto> _allClients;
    private readonly List<RoomDto> _allRooms;

    private string? _distributeFilePath;
    private int _maxFileSizeMb = 100;
    private int? _currentJobId;

    public FilesWindow(
        ApiClient apiClient,
        LabHubClient? labHubClient,
        List<ClientMachineDto> selectedClients,
        List<ClientMachineDto> allClients,
        List<RoomDto> allRooms,
        int? currentRoomFilterId)
    {
        InitializeComponent();

        _apiClient = apiClient;
        _labHubClient = labHubClient;
        _selectedClients = selectedClients;
        _allClients = allClients;
        _allRooms = allRooms;

        RoomCombo.ItemsSource = _allRooms;
        if (_allRooms.Count > 0)
        {
            RoomCombo.SelectedItem = _allRooms.FirstOrDefault(r => r.Id == currentRoomFilterId) ?? _allRooms[0];
            TargetRoomRadio.IsChecked = true;
        }
        else
        {
            RoomCombo.IsEnabled = false;
            TargetRoomRadio.IsEnabled = false;
            TargetSelectedRadio.IsChecked = true;
        }

        TargetSelectedRadio.Content = _selectedClients.Count > 0
            ? $"เครื่องที่เลือกไว้ ({_selectedClients.Count} เครื่อง)"
            : "เครื่องที่เลือกไว้ (ยังไม่ได้เลือกเครื่องไหน)";
        TargetSelectedRadio.IsEnabled = _selectedClients.Count > 0;

        Loaded += async (_, _) => await LoadFileTransferSettingsAsync();
    }

    private async Task LoadFileTransferSettingsAsync()
    {
        var settings = await _apiClient.GetFileTransferSettingsAsync();
        _maxFileSizeMb = settings.MaxFileSizeMb;
        DistributeMaxSizeText.Text = $"สูงสุด {_maxFileSizeMb} MB ต่อครั้ง";
    }

    private void TargetRadio_Changed(object sender, RoutedEventArgs e)
    {
        // ไม่มี logic เพิ่มเติม - แค่ trigger re-render ของ summary/ปุ่มถ้าต้องมีในอนาคต (ดู
        // OpenUrlWindow.RefreshSummary ที่ทำแบบเดียวกัน) รอบนี้ยังไม่มี summary text แยกจึงเว้นว่างไว้
    }

    private void RoomCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (RoomCombo.SelectedItem is RoomDto room)
        {
            TargetRoomRadio.Content = $"ทั้งห้อง {room.Name} ({room.ClientCount} เครื่อง)";
        }
    }

    /// <summary>เป้าหมายปัจจุบัน - ใช้ร่วมกันทั้งแจกไฟล์และเก็บงาน (ดู comment บนคลาสนี้)</summary>
    private (List<ClientMachineDto> Targets, int? RoomId) ResolveTargets()
    {
        if (TargetRoomRadio.IsChecked == true && RoomCombo.SelectedItem is RoomDto room)
        {
            return (_allClients.Where(c => c.RoomId == room.Id).ToList(), room.Id);
        }

        return (_selectedClients, null);
    }

    // ==================== แจกไฟล์ ====================

    private void ChooseDistributeFileButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "เลือกไฟล์แจก", CheckFileExists = true };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var info = new FileInfo(dialog.FileName);
        var maxBytes = (long)_maxFileSizeMb * 1024 * 1024;
        if (info.Length > maxBytes)
        {
            SetDistributeStatus($"ไฟล์นี้ใหญ่เกินกำหนด ({info.Length / 1024 / 1024} MB > สูงสุด {_maxFileSizeMb} MB)", isError: true);
            _distributeFilePath = null;
            DistributeFileText.Text = "ยังไม่ได้เลือกไฟล์";
            return;
        }

        _distributeFilePath = dialog.FileName;
        DistributeFileText.Text = $"{info.Name} · {info.Length / 1024} KB";
        SetDistributeStatus(string.Empty, isError: false);
    }

    private async void DistributeButton_Click(object sender, RoutedEventArgs e)
    {
        if (_distributeFilePath is null)
        {
            SetDistributeStatus("กรุณาเลือกไฟล์ก่อน", isError: true);
            return;
        }

        var (targets, _) = ResolveTargets();
        if (targets.Count == 0)
        {
            SetDistributeStatus("เป้าหมายที่เลือกไม่มีเครื่องอยู่เลย (ดูตัวเลือก \"ส่งไปที่\" ด้านบน)", isError: true);
            return;
        }

        DistributeButton.IsEnabled = false;
        SetDistributeStatus("กำลังอัปโหลดไฟล์ขึ้น Server...", isError: false);

        var (result, error) = await _apiClient.UploadDistributedFileAsync(_distributeFilePath, OpenAfterDownloadCheck.IsChecked == true);
        DistributeButton.IsEnabled = true;

        if (result is null)
        {
            SetDistributeStatus(error ?? "อัปโหลดไฟล์ไม่สำเร็จ", isError: true);
            return;
        }

        SetDistributeStatus($"อัปโหลด '{result.FileName}' สำเร็จ - กำลังส่งคำสั่งดาวน์โหลดให้ {targets.Count} เครื่อง", isError: false);

        var batchId = Guid.NewGuid().ToString("N");
        var payload = new { distributedFileId = result.Id, openAfterDownload = OpenAfterDownloadCheck.IsChecked == true };

        var request = new ActionReportRequest
        {
            ActionLabel = $"แจกไฟล์ '{result.FileName}'",
            BatchId = batchId,
            Rows = targets.Select(c => new ActionReportRow { ClientGuid = c.ClientGuid, MachineName = c.MachineName }).ToList(),
            SendAsync = async row =>
            {
                var (commandId, err) = await _apiClient.SendCommandTrackedAsync(row.ClientGuid, "DownloadFile", payload, batchId);
                if (commandId is null)
                {
                    return (false, err);
                }

                row.CommandId = commandId;
                return (true, null);
            }
        };

        var report = new ActionReportWindow(_apiClient, _labHubClient, request) { Owner = this };
        report.Show();
    }

    private void SetDistributeStatus(string message, bool isError)
    {
        DistributeStatusText.Text = message;
        DistributeStatusText.SetResourceReference(TextBlock.ForegroundProperty, isError ? "Brush.Danger.Default" : "Brush.Text.Secondary");
    }

    // ==================== เก็บงาน ====================

    private async void CollectNowButton_Click(object sender, RoutedEventArgs e)
    {
        var jobName = JobNameBox.Text.Trim();
        if (string.IsNullOrEmpty(jobName))
        {
            SetCollectSummary("กรุณาตั้งชื่องานก่อน", isError: true);
            return;
        }

        var (targets, roomId) = ResolveTargets();
        if (targets.Count == 0)
        {
            SetCollectSummary("เป้าหมายที่เลือกไม่มีเครื่องอยู่เลย (ดูตัวเลือก \"ส่งไปที่\" ด้านบน)", isError: true);
            return;
        }

        DateTime? closesAt = null;
        var minutesText = ClosesInMinutesBox.Text.Trim();
        if (!string.IsNullOrEmpty(minutesText))
        {
            if (!int.TryParse(minutesText, out var minutes) || minutes <= 0)
            {
                SetCollectSummary("\"ปิดรับใน (นาที)\" ต้องเป็นจำนวนเต็มบวก หรือเว้นว่างไว้", isError: true);
                return;
            }
            closesAt = DateTime.UtcNow.AddMinutes(minutes);
        }

        var fileNamePattern = string.IsNullOrWhiteSpace(FilePatternBox.Text) ? null : FilePatternBox.Text.Trim();

        CollectNowButton.IsEnabled = false;
        var (job, error) = await _apiClient.CreateCollectionJobAsync(jobName, fileNamePattern, closesAt, roomId);
        CollectNowButton.IsEnabled = true;

        if (job is null)
        {
            SetCollectSummary(error ?? "สร้างงานเก็บไฟล์ไม่สำเร็จ", isError: true);
            return;
        }

        _currentJobId = job.Id;
        RefreshStatusButton.IsEnabled = true;
        OpenFolderButton.IsEnabled = true;
        CloseJobButton.IsEnabled = true;
        SetCollectSummary($"สร้างงาน '{job.Name}' แล้ว - กำลังส่งคำขอให้ {targets.Count} เครื่อง", isError: false);

        var batchId = Guid.NewGuid().ToString("N");
        var payload = new { jobId = job.Id };

        var request = new ActionReportRequest
        {
            ActionLabel = $"เก็บไฟล์งาน '{job.Name}'",
            BatchId = batchId,
            Rows = targets.Select(c => new ActionReportRow { ClientGuid = c.ClientGuid, MachineName = c.MachineName }).ToList(),
            SendAsync = async row =>
            {
                var (commandId, err) = await _apiClient.SendCommandTrackedAsync(row.ClientGuid, "CollectFile", payload, batchId);
                if (commandId is null)
                {
                    return (false, err);
                }

                row.CommandId = commandId;
                return (true, null);
            }
        };

        var report = new ActionReportWindow(_apiClient, _labHubClient, request) { Owner = this };
        report.Show();

        await RefreshCollectionStatusAsync();
    }

    private async void RefreshStatusButton_Click(object sender, RoutedEventArgs e) => await RefreshCollectionStatusAsync();

    /// <summary>
    /// ครูกด "ปิดงาน" เอง (WP4-3 fix, 2026-11-03 - บั๊กที่พบจากการทดสอบจริง: endpoint
    /// POST /api/files/collection-jobs/{id}/close และ ApiClient.CloseCollectionJobAsync มีอยู่แล้วตั้งแต่
    /// ตอนสร้าง WP4-3 แต่ไม่มีปุ่มไหนในหน้านี้เรียกมันเลย - ครูเลยปิดรับไฟล์ก่อนเวลาไม่ได้จริงถ้าไม่ได้
    /// ตั้ง "ปิดรับใน (นาที)" ไว้ล่วงหน้าตอนสร้างงาน) ปิดแล้วรีเฟรชสถานะทันทีให้เห็น "ปิดงานแล้ว"
    /// </summary>
    private async void CloseJobButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentJobId is not { } jobId)
        {
            return;
        }

        CloseJobButton.IsEnabled = false;
        var success = await _apiClient.CloseCollectionJobAsync(jobId);
        if (!success)
        {
            SetCollectSummary("ปิดงานไม่สำเร็จ", isError: true);
            CloseJobButton.IsEnabled = true;
            return;
        }

        await RefreshCollectionStatusAsync();
    }

    private async Task RefreshCollectionStatusAsync()
    {
        if (_currentJobId is not { } jobId)
        {
            return;
        }

        var status = await _apiClient.GetCollectionJobStatusAsync(jobId);
        if (status is null)
        {
            SetCollectSummary("ดึงสถานะงานเก็บไฟล์ไม่สำเร็จ", isError: true);
            return;
        }

        StatusGrid.ItemsSource = status.Rows;
        var missing = status.TotalCount - status.CollectedCount;
        SetCollectSummary(
            $"ได้ {status.CollectedCount}/{status.TotalCount}" + (missing > 0 ? $" · ไม่มีไฟล์ {missing} คน" : "") +
            (status.IsOpen ? "" : " · ปิดงานแล้ว"),
            isError: false);
    }

    private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentJobId is null)
        {
            return;
        }

        _ = OpenFolderAsync();
    }

    private async Task OpenFolderAsync()
    {
        if (_currentJobId is not { } jobId)
        {
            return;
        }

        var status = await _apiClient.GetCollectionJobStatusAsync(jobId);
        if (status is null || string.IsNullOrWhiteSpace(status.FolderPath))
        {
            SetCollectSummary("ไม่พบ path โฟลเดอร์ที่เก็บ (ยังไม่มีใครส่งไฟล์มาเลย)", isError: true);
            return;
        }

        try
        {
            // Console อาจรันอยู่คนละเครื่องกับ Server จริง (ดู CLAUDE.md "Server จริงอยู่ที่ 192.168.17.249")
            // เปิด Explorer ได้จริงเฉพาะตอนที่โฟลเดอร์นี้มองเห็นจากเครื่อง Console เอง (เช่นตอน dev ที่ Server
            // ทดลองรันในเครื่องเดียวกัน) ถ้าไม่เจอให้โชว่ path ไว้ให้ครูไปเปิดเองที่เครื่อง Server/ผ่าน UNC share
            if (Directory.Exists(status.FolderPath))
            {
                System.Diagnostics.Process.Start("explorer.exe", $"\"{status.FolderPath}\"");
            }
            else
            {
                SetCollectSummary($"เปิดจากเครื่องนี้ไม่ได้ (path อยู่บนเครื่อง Server) - path: {status.FolderPath}", isError: true);
            }
        }
        catch (Exception ex)
        {
            SetCollectSummary($"เปิดโฟลเดอร์ไม่สำเร็จ: {ex.Message}", isError: true);
        }
    }

    private void SetCollectSummary(string message, bool isError)
    {
        CollectSummaryText.Text = message;
        CollectSummaryText.SetResourceReference(TextBlock.ForegroundProperty, isError ? "Brush.Danger.Default" : "Brush.Text.Secondary");
    }
}
