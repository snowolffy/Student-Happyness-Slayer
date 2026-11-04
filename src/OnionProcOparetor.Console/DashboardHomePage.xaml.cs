using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using OnionProcOparetor.Console.Models;
using OnionProcOparetor.Console.Services;

namespace OnionProcOparetor.Console;

/// <summary>
/// UI-4 (2026-11-04): หน้า Dashboard - ดู comment เต็มใน DashboardHomePage.xaml (สิ่งที่หน้านี้ทำ/ไม่ทำ
/// และที่มาของข้อมูลแต่ละก้อน) หน้านี้ "อ่านอย่างเดียว" ไม่มีปุ่มที่แก้ข้อมูลตรงๆ ยกเว้น 3 ปุ่มลัดท้ายหน้า
/// ที่ส่งสัญญาณกลับให้ DashboardWindow เปิด dialog เดิมจาก UI-3b เอง
/// </summary>
public partial class DashboardHomePage : UserControl
{
    private ApiClient? _apiClient;

    /// <summary>ครูกดปุ่มลัด "ล็อกทั้งห้อง"</summary>
    public event Action? LockWholeRoomRequested;

    /// <summary>ครูกดปุ่มลัด "ส่งข้อความขึ้นจอ"</summary>
    public event Action? SendMessageRequested;

    /// <summary>ครูกดปุ่มลัด "เปิดโหมดสอบ"</summary>
    public event Action? ExamModeRequested;

    /// <summary>ครูกด "ดูทั้งหมด" ของคิวยกมือ - ให้ DashboardWindow สลับไปแท็บ "คิวยกมือ"</summary>
    public event Action? ViewRaiseHandQueueRequested;

    public DashboardHomePage()
    {
        InitializeComponent();

        // โหลดตอน "เห็น" ไม่ใช่ตอน Loaded - เหตุผลเดียวกับ ProgramReviewPage (TabControl ยิง Loaded ให้
        // ทุกแท็บตั้งแต่เปิด Dashboard แม้ยังไม่ถูกเลือก)
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

#if DEBUG
    /// <summary>
    /// โหมดตรวจงาน UI (--ui-preview) - orchestrator review 2026-11-04: หน้านี้เคยเรียก ApiClient จริง
    /// ตรงๆ ตอน IsVisibleChanged แม้ใน preview mode (ApiClient ยังไม่ login/ไม่มี BaseAddress) ทำให้
    /// throw แล้ว --shot=... ค้างรอ MessageBox ของ global handler ไม่ยอมปิดเอง - ตอนนี้ true = ข้าม
    /// การยิง API จริงใน RefreshAsync ไปเลย (ดู ApplyPreviewSampleData ที่ DashboardWindow.SeedPreviewData
    /// เรียกแทน) ตั้งก่อน IsVisibleChanged จะได้ผลเสมอ (DashboardWindow ตั้งค่านี้ใน Loaded ก่อนเรียก
    /// ApplyPreviewSampleData - ถ้า RefreshAsync ดันทำงานไปก่อนหน้านั้นแล้วก็ไม่เป็นไร เพราะ
    /// ApplyPreviewSampleData เขียนทับค่าทุกช่องอยู่ดี)
    /// </summary>
    public bool PreviewMode { get; set; }

    /// <summary>
    /// ข้อมูลตัวอย่างสำหรับ --ui-preview (ไม่ยิง API จริง) - เรียกจาก DashboardWindow.SeedPreviewData()
    /// ตัวเลข/รายการให้พอเห็นทุก section ของหน้านี้ในภาพเดียว (ตามที่ orchestrator ขอ: online/offline
    /// count, คิวยกมือ 2 แถว, โหมดสอบ 1 รายการ, เหตุการณ์ล่าสุดไม่กี่แถว)
    /// </summary>
    public void ApplyPreviewSampleData()
    {
        PreviewMode = true;
        LoadErrorBanner.Visibility = Visibility.Collapsed;

        OnlineCountText.Text = "3";
        OfflineCountText.Text = "2";
        StudentsLoggedInCountText.Text = "2";

        var now = DateTime.UtcNow;
        var raiseHandSample = new List<RaiseHandQueueEntryDto>
        {
            new() { Id = 1, ClientGuid = "a1", MachineName = "LAB-PC-01", StudentName = "สมชาย ใจดี", Reason = "โปรแกรมค้าง", RaisedAt = now.AddMinutes(-2), QueuePosition = 1 },
            new() { Id = 2, ClientGuid = "b2", MachineName = "LAB-PC-02", StudentName = null, Reason = "ทำไม่เป็น", Note = "ไม่รู้จะกดตรงไหนต่อ", RaisedAt = now.AddMinutes(-1), QueuePosition = 2 },
        };
        RaiseHandHeaderText.Text = $"คิวยกมือ ({raiseHandSample.Count})";
        RaiseHandList.ItemsSource = raiseHandSample;
        RaiseHandEmptyText.Visibility = Visibility.Collapsed;

        var examSample = new List<ExamSessionRow>
        {
            new() { TargetDescription = "ห้อง ห้อง 1 (2 เครื่อง)", ExpiresAtText = $"ถึง {now.AddMinutes(25):HH:mm}" }
        };
        ExamSessionList.ItemsSource = examSample;
        ExamSessionEmptyText.Visibility = Visibility.Collapsed;

        var eventsSample = new List<RecentEventRow>
        {
            new() { Timestamp = now.AddMinutes(-3), Icon = "shield", Text = "LAB-PC-01: ปิดโปรแกรม \"Minecraft.exe\" (Blocked)" },
            new() { Timestamp = now.AddMinutes(-6), Icon = "wifi", Text = "LAB-PC-02: บล็อกเว็บ \"roblox.com\"" },
            new() { Timestamp = now.AddMinutes(-10), Icon = "monitor", Text = "LAB-PC-04: เครื่องหลุดการเชื่อมต่อ" },
        };
        RecentEventsList.ItemsSource = eventsSample;
        RecentEventsEmptyText.Visibility = Visibility.Collapsed;
    }
#endif

    private void LockWholeRoomButton_Click(object sender, RoutedEventArgs e) => LockWholeRoomRequested?.Invoke();
    private void SendMessageButton_Click(object sender, RoutedEventArgs e) => SendMessageRequested?.Invoke();
    private void ExamModeButton_Click(object sender, RoutedEventArgs e) => ExamModeRequested?.Invoke();
    private void ViewRaiseHandQueueButton_Click(object sender, RoutedEventArgs e) => ViewRaiseHandQueueRequested?.Invoke();
    private async void RetryButton_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

    /// <summary>แถวหนึ่งของ "เหตุการณ์ล่าสุด" - รวมมาจาก 3 แหล่ง (ดู BuildRecentEventsAsync)</summary>
    private sealed class RecentEventRow
    {
        public required DateTime Timestamp { get; init; }
        public required string Icon { get; init; }
        public required string Text { get; init; }
        public string TimeText => Timestamp.ToLocalTime().ToString("HH:mm:ss");
    }

    /// <summary>แถวหนึ่งของ "โหมดสอบที่ทำงานอยู่" - ห่อ ExamSessionDto เพิ่มข้อความวันที่หมดอายุให้พร้อมแสดง</summary>
    private sealed class ExamSessionRow
    {
        public required string TargetDescription { get; init; }
        public required string ExpiresAtText { get; init; }
    }

    /// <summary>
    /// โหลดข้อมูลทั้งหน้าใหม่ - เรียกตอนแท็บนี้ถูกเลือกครั้งแรก/ทุกครั้งที่สลับกลับมา (ไม่มี auto-refresh
    /// เป็นระยะเหมือนแท็บ Clients เพราะหน้านี้เป็นแค่สรุปภาพรวม ไม่ใช่หน้าที่ต้องเห็นเรียลไทม์วินาทีต่อวินาที)
    ///
    /// orchestrator review 2026-11-04 (บั๊กจริง): GetLogsAsync/GetClientsAsync **ไม่ได้** มี try/catch
    /// ภายในตัวเอง (ต่างจากที่ comment เดิมอ้างผิด) - ตอนยังไม่ login (เช่น --ui-preview) หรือ Server
    /// หลุดกลางคัน ApiClient โยน InvalidOperationException/HttpRequestException ตรงๆ ซึ่งหลุดออกจาก
    /// IsVisibleChanged (async void) กลายเป็น unhandled exception จริง (เห็นใน console.log) ครอบทั้งก้อน
    /// ด้วย try/catch ที่นี่แทน แสดง LoadErrorBanner ให้กดลองใหม่แทนที่จะปล่อยให้ global handler เด้ง
    /// MessageBox ค้าง (ซึ่งทำให้ --shot=... ของโหมดตรวจ UI ค้างรอคนกดปิดตลอดไป)
    /// </summary>
    private async System.Threading.Tasks.Task RefreshAsync()
    {
        if (_apiClient is null)
        {
            return;
        }

#if DEBUG
        // --ui-preview: DashboardWindow.SeedPreviewData เรียก ApplyPreviewSampleData เอง (ไม่ยิง API
        // จริงเพราะ ApiClient ยังไม่ login/ไม่มี BaseAddress) - ข้ามไปเลยกัน exception ซ้ำถ้า
        // IsVisibleChanged ดันทำงานมาก่อน
        if (PreviewMode)
        {
            return;
        }
#endif

        try
        {
            LoadErrorBanner.Visibility = Visibility.Collapsed;

            var clients = await _apiClient.GetClientsAsync();
            OnlineCountText.Text = clients.Count(c => c.IsOnline).ToString();
            OfflineCountText.Text = clients.Count(c => !c.IsOnline).ToString();
            StudentsLoggedInCountText.Text = clients.Count(c => c.HasActiveStudent).ToString();

            var raiseHandQueue = await _apiClient.GetRaiseHandQueueAsync();
            RaiseHandHeaderText.Text = $"คิวยกมือ ({raiseHandQueue.Count})";
            RaiseHandList.ItemsSource = raiseHandQueue.Take(5).ToList();
            RaiseHandEmptyText.Visibility = raiseHandQueue.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            var examSessions = await _apiClient.GetActiveExamSessionsAsync();
            var examRows = examSessions.Select(s => new ExamSessionRow
            {
                TargetDescription = s.TargetDescription,
                ExpiresAtText = $"ถึง {s.ExpiresAt.ToLocalTime():HH:mm}"
            }).ToList();
            ExamSessionList.ItemsSource = examRows;
            ExamSessionEmptyText.Visibility = examRows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            var recentEvents = await BuildRecentEventsAsync();
            RecentEventsList.ItemsSource = recentEvents;
            RecentEventsEmptyText.Visibility = recentEvents.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            ConsoleDiagnostics.WriteLine($"DashboardHomePage.RefreshAsync: โหลดไม่สำเร็จ - {ex.GetType().Name}: {ex.Message}");
            LoadErrorText.Text = $"โหลดข้อมูลหน้าแรกไม่สำเร็จ: {ex.Message}";
            LoadErrorBanner.Visibility = Visibility.Visible;
        }
    }

    /// <summary>
    /// รวม "เหตุการณ์ล่าสุด" จาก 3 แหล่งที่มีอยู่แล้ว (ตามที่ roadmap WP UI-4 สั่ง: ปิดโปรแกรม/บล็อกเว็บ/
    /// เครื่องหลุด) แล้วเรียงเวลาล่าสุดก่อน ตัด 8 รายการบนสุด - ไม่มี endpoint รวม 3 แหล่งนี้ไว้ที่เดียว
    /// (และ "เปิด endpoint ใหม่" อยู่นอกขอบเขตของ WP นี้) จึงยิง 3 คำขอแล้วรวมฝั่ง client เอง
    /// </summary>
    private async System.Threading.Tasks.Task<List<object>> BuildRecentEventsAsync()
    {
        var rows = new List<RecentEventRow>();

        // ปิดโปรแกรม (GET /api/logs) - ActionTaken ที่ไม่ใช่ "Allowed" ถือว่าเป็นเหตุการณ์ที่ครูอยากเห็น
        var logs = await _apiClient!.GetLogsAsync(take: 30);
        rows.AddRange(logs
            .Where(l => !string.Equals(l.ActionTaken, "Allowed", StringComparison.OrdinalIgnoreCase))
            .Select(l => new RecentEventRow
            {
                Timestamp = l.Timestamp,
                Icon = "shield",
                Text = $"{l.MachineName}: ปิดโปรแกรม \"{l.ProcessPath}\" ({l.ActionTaken})"
            }));

        // บล็อกเว็บ (GET /api/web-visits?blockedOnly=true) - WP2-4
        var blockedVisits = await _apiClient.GetWebVisitsAsync(blockedOnly: true, pageSize: 10);
        rows.AddRange(blockedVisits.Items.Select(v => new RecentEventRow
        {
            Timestamp = v.VisitedAt,
            Icon = "wifi",
            Text = $"{v.MachineName}: บล็อกเว็บ \"{v.Domain}\""
        }));

        // เครื่องหลุด (GET /api/activitylog?category=AgentConnectivity)
        var connectivity = await _apiClient.GetActivityLogAsync(category: "AgentConnectivity", pageSize: 10);
        rows.AddRange(connectivity.Items.Select(a => new RecentEventRow
        {
            Timestamp = a.Timestamp,
            Icon = "monitor",
            Text = $"{a.TargetMachineName ?? "เครื่อง"}: {a.Description}"
        }));

        return rows
            .OrderByDescending(r => r.Timestamp)
            .Take(8)
            .Cast<object>()
            .ToList();
    }
}
