using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using OnionProcOparetor.Console.Models;
using OnionProcOparetor.Console.Services;

namespace OnionProcOparetor.Console;

public partial class DashboardWindow : Window
{
    private static readonly TimeSpan VncSessionReadyTimeout = TimeSpan.FromSeconds(15);

    private readonly ApiClient _apiClient;
    private readonly DispatcherTimer _autoRefreshTimer;
    private readonly LabHubClient _labHubClient = new();

    // ==================== Step 4d: embedded VNC screen view (SCREEN VIEW tab) ====================
    // ทุก field ตรงนี้แตะ/แก้ได้จาก UI thread เท่านั้น (ไม่มี lock ป้องกัน race เพราะทุกจุดที่แก้เป็น
    // UI-thread event handler ทั้งหมด - RfbClient framebuffer loop เองรันบน background thread แยก
    // แต่ callback ของมันเขียนกลับผ่าน Dispatcher.Invoke เท่านั้น) _vncSessionGeneration ใช้กันกรณี
    // await ที่ค้างอยู่ (เช่นกำลังรอ RequestVncSessionAsync/handshake ของเครื่องเก่า) resume มาแล้วโลก
    // เปลี่ยนไปแล้ว (ผู้ใช้สลับเครื่อง/ปิดแท็บระหว่างรอ) - ต้องรู้ตัวว่าเป็นของเก่าแล้วไม่เขียนทับ UI/state
    // ปัจจุบัน (ดู ShowVncViewAsync/StreamVncAsync/OnVncRectangleDecoded)
    private int _vncSessionGeneration;
    private RfbClient? _vncClient;
    private WriteableBitmap? _vncBitmap;
    private CancellationTokenSource? _vncStreamCts;

    // ==================== Step 4f: Live View grid (LIVE VIEW tab) ====================
    // pool แยกต่างหากจาก Step 4d โดยตั้งใจ (ไม่ migrate SCREEN VIEW tab มาใช้ pool นี้ - เก็บของเดิมไว้
    // ทำงานเหมือนเดิมทุกประการ ลดความเสี่ยง regression) เหมือน Step 4d ทุก field ตรงนี้แตะจาก UI thread
    // เท่านั้น (background thread ของแต่ละ handle เขียนกลับผ่าน Dispatcher.Invoke เสมอ)
    private readonly VncSessionPool _liveViewPool;
    private readonly Dictionary<string, LiveViewCell> _liveViewCells = new();

    // ไม่ null = เครื่องนี้กำลังแสดง full-screen overlay อยู่ (ดู ExpandLiveViewCell/BackToGridButton_Click)
    // - session อื่นใน grid ยังรันเบื้องหลังต่อปกติ แค่ถูกโอเวอร์เลย์บังไว้เฉยๆ ไม่ pause/reconnect
    private string? _liveViewFullscreenClientGuid;
    private WriteableBitmap? _liveViewFullscreenBitmap;
    private VncViewMode? _liveViewFullscreenBitmapMode;

    // Toggle target input (block/unblock physical keyboard/mouse บนเครื่อง target ระหว่าง fullscreen -
    // ดู ToggleTargetInputButton_Click) - default false (ไม่บล็อก) เสมอทุกครั้งที่เข้า fullscreen ใหม่
    // (ดู ExpandLiveViewCell) และต้องคืนเป็น false ทุกครั้งที่ออกจาก fullscreen ไม่ว่าทางไหน (ดู
    // ReleaseFullscreenInputState) กัน state ค้าง "บล็อกอยู่" ข้าม session ผิดเครื่อง
    private bool _targetInputBlocked;

    // ==================== Step 4h: Live View full-screen input capture ====================
    // ปุ่มเมาส์ที่กำลังกดค้างอยู่บน target (ผ่าน overlay นี้) - null = ไม่มีปุ่มไหนค้าง ใช้สอง field นี้คู่กัน
    // ตอนต้อง force-release ปุ่มถ้าออกจาก fullscreen (กด BACK TO GRID/ปิด tab/ปิด window) กลางที่ยังลากอยู่
    // พอดี (ดู ReleaseFullscreenInputState) - ไม่งั้น target จะค้างคิดว่าปุ่มยังกดอยู่ตลอดไป (stuck
    // mouse-down) เพราะไม่มีทางส่ง MouseUp event ทีหลังได้อีกแล้วหลังออกจาก fullscreen ไปแล้ว
    private VncMouseButton? _fullscreenMouseButtonDown;
    private ushort _fullscreenLastMouseX;
    private ushort _fullscreenLastMouseY;

    // throttle MouseMove ไม่ให้เกิน ~60 event/วิ (กัน network flood - WPF ยิง MouseMove ถี่กว่านี้มากทุก
    // pixel ที่ขยับ) ไม่ throttle MouseDown/MouseUp/Wheel เลย (ต้องส่งทุกครั้งแม่นยำ - พลาดแม้แค่ครั้งเดียว
    // = คลิกหาย/ปุ่มค้าง)
    private DateTime _fullscreenLastMouseMoveSentAt = DateTime.MinValue;
    private static readonly TimeSpan FullscreenMouseMoveMinInterval = TimeSpan.FromSeconds(1.0 / 60);

    /// <summary>หน่วยมาตรฐานของ Win32 WM_MOUSEWHEEL (WHEEL_DELTA) - WPF MouseWheelEventArgs.Delta ใช้หน่วยเดียวกัน (1 "notch" ของล้อเมาส์ = ±120)</summary>
    private const int WheelDeltaPerNotch = 120;

    private sealed class LiveViewCell
    {
        public required Border Border { get; init; }
        public required Image Image { get; init; }
        public required TextBlock StatusOverlayText { get; init; }
        public required ClientMachineDto Client { get; init; }
        public WriteableBitmap? Bitmap { get; set; }
        public VncViewMode? BitmapMode { get; set; }
        public VncSessionHandle? Handle { get; set; }
    }

#if DEBUG
    /// <summary>
    /// โหมดตรวจงาน UI (App.OnStartup, --ui-preview) มีเฉพาะ build แบบ Debug - publish-all.ps1 ใช้
    /// -c Release เสมอ จึงไม่มีอยู่จริงในของที่ส่งขึ้นเครื่องครู
    /// </summary>
    public bool PreviewMode { get; set; }

    /// <summary>ชื่อแท็บที่จะเลือกไว้ตอนเปิดในโหมดตรวจ UI (ตรงกับ TabItem.Header เช่น "Rules")</summary>
    public string? PreviewTab { get; set; }

    /// <summary>เลือกแถวแรกไว้ไหมตอนเปิดในโหมดตรวจ UI - ใช้เทียบภาพ "เลือก/ไม่เลือก" ว่าตารางขยับไหม</summary>
    public bool PreviewSelectFirst { get; set; }

    /// <summary>ตั้ง gate ทั้งห้องเป็นปิดในโหมดตรวจ UI เพื่อให้แถบเตือนโผล่ขึ้นมาให้ดู</summary>
    public bool PreviewGlobalOff { get; set; }

    /// <summary>
    /// ใส่ข้อมูลปลอมลงตารางเพื่อให้เห็น badge / การ์ดตัวเลข / แถบเลือก ตอนตรวจงาน UI โดยไม่ต้อง
    /// login เข้า Server ตัวจริงที่รันอยู่บนเครื่อง dev เครื่องนี้ - ค่าถูกเลือกให้ครบทุกสถานะที่
    /// UI ต้องรับมือ (ออนไลน์/ออฟไลน์, กฎทำงาน/ไม่ทำงาน, ต่อผ่าน LAN/Tailscale) ไม่ใช่แค่แถวสวย ๆ
    /// </summary>
    private void SeedPreviewData()
    {
        var now = DateTime.UtcNow;
        _globalState = new GlobalStateDto { IsEnabled = !PreviewGlobalOff };
        _allClients =
        [
            new() { Id = 1, ClientGuid = "a1", MachineName = "LAB-PC-01", LastKnownIp = "192.168.1.11", IsOnline = true,  IsEnabled = true,  LastSeenAt = now },
            new() { Id = 2, ClientGuid = "b2", MachineName = "LAB-PC-02", LastKnownIp = "192.168.1.12", IsOnline = true,  IsEnabled = false, LastSeenAt = now },
            new() { Id = 3, ClientGuid = "c3", MachineName = "LAB-PC-03", LastKnownIp = "100.84.2.7",   IsOnline = true,  IsEnabled = true,  UsingFallback = true, LastSeenAt = now },
            new() { Id = 4, ClientGuid = "d4", MachineName = "LAB-PC-04", LastKnownIp = "192.168.1.14", IsOnline = false, IsEnabled = true,  LastSeenAt = now.AddHours(-3) },
            new() { Id = 5, ClientGuid = "e5", MachineName = "LAB-PC-05", LastKnownIp = null,           IsOnline = false, IsEnabled = false, LastSeenAt = now.AddDays(-2) },
        ];

        RefreshGlobalStateUi();
        ApplyClientsFilter();
        ApplyLiveViewClientList();

        // แท็บอื่นตั้งใจปล่อยว่าง เพื่อให้เห็น empty state ของจริงตอนไล่ตรวจ
        RulesEmptyText.Visibility = Visibility.Visible;
        LogsEmptyText.Visibility = Visibility.Visible;
        AuditLogEmptyText.Visibility = Visibility.Visible;
        UsersEmptyText.Visibility = Visibility.Visible;

        // เลือกแถวแรกเฉพาะเมื่อสั่ง (--select) - ต้องถ่ายได้ทั้งสองแบบเพื่อเทียบว่าตารางขยับไหม
        // ตอนแถบ "ที่เลือก" โผล่ขึ้นมา
        if (PreviewSelectFirst && ClientsGrid.Items.Count > 0)
        {
            ClientsGrid.SelectedIndex = 0;
        }

        if (!string.IsNullOrEmpty(PreviewTab))
        {
            foreach (TabItem item in MainTabControl.Items)
            {
                if (string.Equals(item.Header as string, PreviewTab, StringComparison.OrdinalIgnoreCase))
                {
                    MainTabControl.SelectedItem = item;
                    break;
                }
            }
        }

        StatusText.Text = "โหมดตรวจ UI (ข้อมูลปลอม)";
    }
#endif

    // ==================== Sidebar / ธีม (2026-09-09) ====================

    /// <summary>
    /// ปุ่มสองตัวนี้อยู่ใน ControlTemplate ของ MainTabControl จึง **ไม่มี field ที่ x:Name สร้างให้**
    /// (element ใน template อยู่คนละ namescope กับหน้าต่าง) ต้องดึงออกมาเองด้วย Template.FindName
    /// หลัง template ถูก apply แล้วเท่านั้น - ทำใน Loaded ซึ่งการันตีว่าเลยจุดนั้นไปแล้ว
    ///
    /// ที่ต้องถือ reference ไว้เพราะต้องตั้ง "สถานะเริ่มต้น" ของทั้งสองปุ่มให้ตรงกับค่าที่จำไว้ตอน
    /// เปิดหน้าต่าง (ไอคอนลูกศรชี้ถูกด้าน / ไอคอนพระอาทิตย์-พระจันทร์ตรงกับธีมปัจจุบัน) - ถ้าอัพเดต
    /// จาก sender ใน Click อย่างเดียวจะถูกก็ต่อเมื่อผู้ใช้กดแล้วเท่านั้น
    /// </summary>
    private Button? _sidebarCollapseButton;
    private Button? _themeToggleButton;

    private void ApplySidebarAndThemeState()
    {
        _sidebarCollapseButton = MainTabControl.Template.FindName("SidebarCollapseButton", MainTabControl) as Button;
        _themeToggleButton = MainTabControl.Template.FindName("ThemeToggleButton", MainTabControl) as Button;

        SetSidebarCollapsed(SettingsService.Current.Ui.SidebarCollapsed, persist: false);
        UpdateThemeToggleButton();

        // ธีมเปลี่ยนได้จากทางอื่นด้วย (ผู้ใช้สลับธีมของ Windows ทั้งเครื่องขณะที่ตั้งไว้เป็น "ตามระบบ")
        // ไม่ใช่แค่จากปุ่มนี้ จึงต้องฟัง event แทนที่จะอัพเดตเฉพาะตอนกดปุ่ม
        ThemeService.ThemeChanged += UpdateThemeToggleButton;
        Closed += (_, _) => ThemeService.ThemeChanged -= UpdateThemeToggleButton;
    }

    private void SetSidebarCollapsed(bool collapsed, bool persist)
    {
        // ตั้งที่ TabControl ตัวเดียว - Nav.IsCollapsed ประกาศเป็น Inherits จึงไหลลงไปถึงแถวเมนูทุกแถว
        // หัวข้อกลุ่ม ปุ่มท้าย sidebar และ trigger ที่ย่อความกว้างของ sidebar เองพร้อมกันทั้งหมด
        Controls.Nav.SetIsCollapsed(MainTabControl, collapsed);

        if (_sidebarCollapseButton is not null)
        {
            Controls.Nav.SetIcon(_sidebarCollapseButton, collapsed ? "chevron-right" : "chevron-left");
            _sidebarCollapseButton.Content = collapsed ? string.Empty : "ย่อเมนู";
            _sidebarCollapseButton.ToolTip = collapsed ? "ขยายเมนู" : "ย่อเมนู";
        }

        if (!persist)
        {
            return;
        }

        var settings = SettingsService.Current;
        settings.Ui.SidebarCollapsed = collapsed;
        var (success, error) = SettingsService.Save(settings);
        if (!success)
        {
            // จำไม่ได้ก็ไม่เป็นไร sidebar ในรอบนี้ย่อ/ขยายไปแล้วตามที่กด
            ConsoleDiagnostics.WriteLine($"DashboardWindow: จำสถานะ sidebar ไม่สำเร็จ ({error})");
        }
    }

    private void UpdateThemeToggleButton()
    {
        if (_themeToggleButton is null)
        {
            return;
        }

        // ไอคอนบอก "สิ่งที่จะได้ถ้ากด" ไม่ใช่ "สถานะตอนนี้" - ตอนอยู่ธีมสว่างจึงเป็นพระจันทร์ (กดแล้วมืด)
        var dark = ThemeService.IsDarkActive;
        Controls.Nav.SetIcon(_themeToggleButton, dark ? "sun" : "moon");
        _themeToggleButton.Content = dark ? "ธีมสว่าง" : "ธีมมืด";
    }

    private void SidebarCollapseButton_Click(object sender, RoutedEventArgs e) =>
        SetSidebarCollapsed(!Controls.Nav.GetIsCollapsed(MainTabControl), persist: true);

    private void ThemeToggleButton_Click(object sender, RoutedEventArgs e) => ThemeService.Toggle();

    public DashboardWindow(ApiClient apiClient)
    {
        InitializeComponent();
        _apiClient = apiClient;
        ServerLabel.Text = $"connected to {_apiClient.BaseUrl}";
        FooterServerText.Text = _apiClient.BaseUrl ?? "-";
        FooterUserText.Text = _apiClient.Username ?? "-";
        Loaded += async (_, _) =>
        {
            ApplySidebarAndThemeState();
#if DEBUG
            if (PreviewMode)
            {
                SeedPreviewData();
                return;
            }
#endif
            await LoadAllDataAsync();
        };

        _liveViewPool = new VncSessionPool(_apiClient, _labHubClient);

        // auto refresh ทุกกี่วิตาม Settings (ค่า default เดิมคือ 10 วิ) จะได้ไม่ต้องกด REFRESH เอง -
        // หยุดเองตอนปิดหน้าต่างกันเรียก LoadAllDataAsync ทับ ApiClient ที่ปิดไปแล้ว
        _autoRefreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(SettingsService.Current.Connection.DashboardPollIntervalSeconds)
        };
        _autoRefreshTimer.Tick += async (_, _) =>
        {
            await LoadAllDataAsync();

            // AUDIT LOG ไม่รวมอยู่ใน LoadAllDataAsync (volume สูง โหลดเฉพาะตอนแท็บ active เท่านั้น) -
            // รีเฟรชหน้าปัจจุบันเพิ่มเฉพาะตอนแท็บนี้เปิดอยู่จริง
            if (ReferenceEquals(MainTabControl.SelectedItem, AuditLogTabItem))
            {
                await LoadActivityLogPageAsync();
            }
        };
        _autoRefreshTimer.Start();

        // debounce ตอนพิมพ์ filter client GUID ของ Audit Log - กันยิง request ทุกตัวอักษรที่พิมพ์
        _auditLogFilterDebounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _auditLogFilterDebounceTimer.Tick += async (_, _) =>
        {
            _auditLogFilterDebounceTimer.Stop();
            _auditLogPage = 1;
            await LoadActivityLogPageAsync();
        };

        // อัพเดต interval แบบ live ทันทีที่บันทึก Settings ใหม่ - ไม่ต้อง restart Console (ดู SettingsWindow)
        SettingsService.Changed += OnSettingsChanged;

        // SignalR เป็นชั้นเสริมคู่ขนานกับ DispatcherTimer ข้างบน (ไม่แทนที่) - ทำให้ตารางเครื่องอัปเดต
        // ไวขึ้นโดยไม่ต้องรอรอบ poll ถ้า SignalR หลุด/ต่อไม่ได้ ตาราง ยังอัปเดตปกติผ่าน auto refresh เดิม
        _labHubClient.ClientStatusChanged += OnClientStatusChangedFromHub;
        _ = _labHubClient.StartAsync(_apiClient.BaseUrl!, CancellationToken.None);

        // Step 4h: กัน Alt+F4 (หรือทางอื่นที่ทำให้ Window พยายามปิด เช่นปุ่ม X บน title bar) ปิด Console
        // ทั้งที่ครูกำลังคุมเครื่อง target อยู่เต็มจอ - **ตั้งใจไม่ใช้วิธี "ดัก PreviewKeyDown แล้ว
        // e.Handled=true" เพื่อกัน Alt+F4 โดยตรง** เพราะ verify แล้วจาก 2 แหล่งอิสระ (WPF community
        // discussion) ว่าวิธีนั้น "fragile/incomplete" - Alt+F4 ถูก Windows แปลงเป็น WM_SYSCOMMAND
        // (SC_CLOSE) ที่อาจไปถึง window โดยไม่ผ่าน routed keyboard event pipeline ปกติเสมอไป วิธีที่
        // เชื่อถือได้จริงตามคำแนะนำที่ verify มาคือดักที่ Window.Closing (ครอบคลุมทุกทางที่ทำให้ Window
        // ปิดพร้อมกันหมด ไม่ใช่แค่ Alt+F4) แล้ว cancel ถ้ายังอยู่ใน fullscreen input capture
        Closing += (_, e) =>
        {
            if (_liveViewFullscreenClientGuid is not null)
            {
                e.Cancel = true;
                StatusText.Text = "กด \"กลับไปจอรวม\" ก่อนปิดโปรแกรม (กำลังควบคุมเครื่อง target แบบเต็มจออยู่)";
            }
        };

        Closed += async (_, _) =>
        {
            _autoRefreshTimer.Stop();
            _auditLogFilterDebounceTimer.Stop();
            SettingsService.Changed -= OnSettingsChanged;
            DisconnectVncView();
            await CloseAllLiveViewSessionsAsync();
            _labHubClient.ClientStatusChanged -= OnClientStatusChangedFromHub;
            await _labHubClient.DisposeAsync();
        };
    }

    private void OnSettingsChanged(ConsoleSettings settings)
    {
        Dispatcher.Invoke(() => _autoRefreshTimer.Interval = TimeSpan.FromSeconds(settings.Connection.DashboardPollIntervalSeconds));
    }

    /// <summary>
    /// เรียกจาก SignalR background thread ตอนได้ ClientStatusChanged - update item ที่มีอยู่ใน
    /// _allClients ตาม ClientGuid ที่ match แล้วรีเฟรชตารางผ่าน ApplyClientsFilter เดิม
    /// (ไม่สร้าง binding ใหม่ - ใช้ List + ItemsSource reassign แบบเดียวกับที่ LoadAllDataAsync ทำอยู่แล้ว)
    /// </summary>
    private void OnClientStatusChangedFromHub(ClientMachineDto updated)
    {
        Dispatcher.Invoke(() =>
        {
            var existing = _allClients.FirstOrDefault(c => c.ClientGuid == updated.ClientGuid);
            if (existing is not null)
            {
                existing.MachineName = updated.MachineName;
                existing.LastKnownIp = updated.LastKnownIp;
                existing.IsEnabled = updated.IsEnabled;
                existing.LastSeenAt = updated.LastSeenAt;
                existing.RegisteredAt = updated.RegisteredAt;
                existing.PollIntervalOverrideSeconds = updated.PollIntervalOverrideSeconds;
                existing.IsOnline = updated.IsOnline;
            }
            else
            {
                _allClients.Add(updated);
            }

            ApplyClientsFilter();
        });
    }

    /// <summary>
    /// สถานะ enforcement ทั้งห้อง - null = ยังอ่านค่าไม่ได้ ซึ่งต้องต่างจาก "รู้ว่าเปิดอยู่" เสมอ
    /// (ดู RefreshGlobalStateUi) โหลดใหม่ทุกครั้งที่ LoadAllDataAsync ทำงาน เพราะครูอีกคนอาจปิด
    /// ทั้งระบบจาก Online Console อยู่คนละเครื่องกัน
    /// </summary>
    private GlobalStateDto? _globalState;

    private async Task LoadAllDataAsync()
    {
        StatusText.Text = "กำลังโหลดข้อมูล...";
        try
        {
            // อ่านก่อน _allClients เสมอ - ApplyClientsFilter ต้องใช้ค่านี้เติมลง DTO แต่ละตัว
            _globalState = await _apiClient.GetGlobalStateAsync();
            RefreshGlobalStateUi();

            _allClients = await _apiClient.GetClientsAsync();
            ApplyClientsFilter();
            ApplyLiveViewClientList();

            _allRules = await _apiClient.GetRulesAsync();
            ApplyRulesFilter();

            _allLogs = await _apiClient.GetLogsAsync();
            ApplyLogsFilter();

            var users = await _apiClient.GetUsersAsync();
            UsersGrid.ItemsSource = users;
            UsersEmptyText.Visibility = users.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            StatusText.Text = $"อัพเดตล่าสุด: {DateTime.Now:HH:mm:ss}";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"โหลดข้อมูลไม่สำเร็จ: {ex.Message}";
        }
    }

    /// <summary>
    /// เปิดคิวตรวจสอบของ Program Control - เก็บ reference ไว้กันเปิดซ้อนหลายบาน (ครูมักกดปุ่มซ้ำ
    /// ตอนหน้าต่างยังโหลดไม่เสร็จ) ถ้าเปิดอยู่แล้วให้ดึงบานเดิมขึ้นมาแทน
    /// </summary>
    private ProgramReviewWindow? _programReviewWindow;

    private void ProgramControlButton_Click(object sender, RoutedEventArgs e)
    {
        if (_programReviewWindow is { IsLoaded: true })
        {
            _programReviewWindow.Activate();
            return;
        }

        _programReviewWindow = new ProgramReviewWindow(_apiClient) { Owner = this };
        _programReviewWindow.Closed += (_, _) => _programReviewWindow = null;
        _programReviewWindow.Show();
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        await LoadAllDataAsync();
    }

    /// <summary>ปุ่ม "เปิดกลับ" บนแถบเตือน - ทางออกอยู่ตรงที่เห็นปัญหา ไม่ต้องไปหาในเมนู</summary>
    private async void GlobalTurnOnButton_Click(object sender, RoutedEventArgs e)
    {
        await SetGlobalEnforcementAsync(true);
    }

    /// <summary>
    /// ตั้งค่า gate ทั้งห้องแบบระบุค่าตรงๆ - การปิดต้องยืนยันก่อน (มีผลทุกเครื่องพร้อมกัน)
    /// ส่วนการเปิดกลับไม่ต้องถาม เพราะนั่นคือการกู้คืนจากสถานะที่ทำให้ทั้งห้องไม่บังคับใช้กฎ
    /// </summary>
    private async Task SetGlobalEnforcementAsync(bool enabled)
    {
        if (!enabled && !ConfirmWindow.Show(
                "กฎทั้งหมดจะหยุดทำงานบนทุกเครื่องในห้องแล็บพร้อมกัน จนกว่าจะเปิดกลับ - "
                    + "การเปิด/ปิด enforcement รายเครื่องจะไม่มีผลใดๆ ตลอดช่วงที่ปิดอยู่",
                "ปิดการบังคับใช้ทั้งระบบ?", this, "ปิดทั้งระบบ"))
        {
            return;
        }

        var success = await _apiClient.SetGlobalEnabledAsync(enabled);
        StatusText.Text = success
            ? enabled ? "เปิดการบังคับใช้ทั้งระบบแล้ว" : "ปิดการบังคับใช้ทั้งระบบแล้ว"
            : "เปลี่ยนการบังคับใช้ทั้งระบบไม่สำเร็จ";

        // โหลดใหม่ทั้งชุดเสมอ ไม่ใช่แค่เดาค่าใหม่ลง _globalState เอง - แถบเตือนกับคอลัมน์
        // ENFORCEMENT ต้องสะท้อนสิ่งที่ Server ตอบจริง ไม่ใช่สิ่งที่ Console เพิ่งกดไป
        await LoadAllDataAsync();
    }

    /// <summary>
    /// แสดง/ซ่อนแถบเตือน "ปิดการบังคับใช้ทั้งระบบ" ตาม _globalState
    ///
    /// _globalState เป็น null (อ่านค่าไม่ได้ เช่น Server เวอร์ชันเก่าที่ยังไม่มี GET /api/global)
    /// ให้ซ่อนแถบไว้ - ขึ้นเตือนว่า "ปิดอยู่" ทั้งที่ยังไม่รู้ว่าปิดจริงไหม แย่กว่าไม่ขึ้นเลย
    /// (เมนูเกียร์จะขึ้นว่า "อ่านค่าไม่ได้" ให้เห็นแทน)
    /// </summary>
    private void RefreshGlobalStateUi()
    {
        GlobalOffBanner.Visibility = _globalState is { IsEnabled: false }
            ? Visibility.Visible
            : Visibility.Collapsed;

        // ปุ่มบอก "สิ่งที่จะเกิดถ้ากด" ไม่ใช่ "สถานะตอนนี้" - ปุ่มที่เขียนว่า Toggle เฉย ๆ คือครึ่งหนึ่ง
        // ของบั๊กเดิมปี 2026-09-04 ที่ครูไม่รู้ว่า gate เปิดหรือปิดอยู่
        //
        // ระหว่างที่ยังอ่านค่าไม่ได้ (_globalState == null เช่น ตอน Server ล่ม) ปุ่มถูกปิดไว้ ไม่ใช่เดา
        // ว่าเปิดอยู่ - เดาผิดแล้วครูกดหนึ่งครั้งอาจกลายเป็นปิดกฎทั้งห้องโดยไม่ได้ตั้งใจ
        if (_globalState is null)
        {
            GlobalEnforcementButton.Content = "การบังคับใช้ทั้งระบบ: อ่านค่าไม่ได้";
            GlobalEnforcementButton.IsEnabled = false;
            return;
        }

        GlobalEnforcementButton.IsEnabled = true;
        GlobalEnforcementButton.Content = _globalState.IsEnabled
            ? "ปิดการบังคับใช้ทั้งระบบ"
            : "เปิดการบังคับใช้ทั้งระบบ";
        GlobalEnforcementButton.ToolTip = _globalState.IsEnabled
            ? "ตอนนี้บังคับใช้กฎอยู่ทั้งห้อง - กดเพื่อปิดทั้งระบบ (จะถามยืนยันก่อน)"
            : "ตอนนี้ปิดการบังคับใช้อยู่ทั้งห้อง กฎไม่ทำงานเลยแม้เครื่องจะขึ้น ENABLED - กดเพื่อเปิดกลับ";
        GlobalEnforcementButton.Style = (Style)FindResource(
            _globalState.IsEnabled ? "ButtonSecondary" : "ButtonPrimary");
    }

    /// <summary>
    /// สวิตช์ gate ทั้งห้องบนแถบคำสั่งของแท็บ Clients - อยู่คู่กับ BROADCAST เพราะทั้งคู่คือ
    /// "สิ่งที่ทำกับทั้งห้อง" และทำได้โดยไม่ต้องเลือกเครื่องก่อน
    ///
    /// ยังคงปุ่ม "เปิดกลับ" บนแบนเนอร์เตือนไว้ด้วย ไม่ใช่ของซ้ำซ้อน: แบนเนอร์อยู่บนแถบบนซึ่งเห็นจาก
    /// ทุกแท็บ ส่วนปุ่มนี้อยู่เฉพาะแท็บ Clients - ถ้ามีแต่ปุ่มนี้ คนที่ค้างอยู่แท็บอื่นจะเห็นคำเตือน
    /// แต่กดแก้ไม่ได้ ซึ่งเป็นบั๊กเดิมเป๊ะ ๆ (12d774a "visible and reachable") แบนเนอร์เปิดได้ทางเดียว
    /// (เปิดกลับ) ส่วนปุ่มนี้สลับได้สองทาง
    /// </summary>
    private async void GlobalEnforcementButton_Click(object sender, RoutedEventArgs e)
    {
        if (_globalState is not { } state)
        {
            return;
        }

        await SetGlobalEnforcementAsync(!state.IsEnabled);
    }

    /// <summary>
    /// ปุ่มเกียร์บน toolbar - เปิด ContextMenu ให้เลือก Settings/About แทนที่จะแยกปุ่มคนละจุด
    ///
    /// รวมสวิตช์ "การบังคับใช้ทั้งระบบ" กลับเข้ามาที่นี่ด้วย (2026-09-04): ปุ่ม TOGGLE GLOBAL เดิม
    /// เคยถูกลบทิ้งด้วยเหตุผลที่ผิด - เข้าใจว่า "ACTIONS > ทุกเครื่อง > TOGGLE ENFORCEMENT ให้ผล
    /// เหมือนกัน" ซึ่งไม่จริงเลย: อันนั้นพลิก IsEnabled ราย "เครื่อง" ส่วนอันนี้คือ GlobalState ที่เป็น
    /// AND gate คร่อมทุกเครื่องอีกชั้น (ดู /api/poll) พอลบไปโดยที่ gate ยังอยู่ Console ตัวนี้จึงไม่มี
    /// ทางทั้ง "เห็น" และ "แก้" ค่านั้นได้อีกเลย = ถ้ามันถูกปิดไว้ ทั้งห้องหยุดบังคับใช้กฎถาวรโดยที่
    /// ครูหาสาเหตุไม่เจอ (เจอจริงตอน field test)
    /// </summary>
    private void SettingsMenuButton_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu();

        // สวิตช์ "การบังคับใช้ทั้งระบบ" ถูกย้ายออกจากเมนูนี้แล้ว (2026-09-10) ไปเป็นปุ่มที่มีป้ายกำกับ
        // อยู่บนแถบคำสั่งของแท็บ Clients คู่กับ BROADCAST บวกกับปุ่ม "เปิดกลับ" บนแถบเตือนซึ่งเห็นจาก
        // ทุกแท็บ - ทั้งสองทางนั้นมองเห็นได้โดยไม่ต้องเปิดเมนูก่อน ซึ่งเป็นหัวใจของบั๊กเดิม
        // (12d774a "visible and reachable") การเก็บไว้ในเมนูนี้ด้วยมีแต่จะทำให้มีสามที่ที่ทำสิ่งเดียวกัน

        var settingsItem = new MenuItem { Header = "Settings..." };
        settingsItem.Click += (_, _) => new SettingsWindow(_apiClient) { Owner = this }.ShowDialog();
        menu.Items.Add(settingsItem);

        var aboutItem = new MenuItem { Header = "About..." };
        aboutItem.Click += (_, _) => new AboutWindow { Owner = this }.ShowDialog();
        menu.Items.Add(aboutItem);

        menu.PlacementTarget = (Button)sender;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    /// <summary>
    /// ปุ่มเดียวใน DataGrid row - เปิด ActionPanelWindow โหมด single-client รวม action ทั้งหมดของ
    /// เครื่องนั้น (toggle/settings/lock-unlock/restart/shutdown/delete) แทนที่ ContextMenu เดิม
    /// เพื่อใช้ panel เดียวกับที่ปุ่ม ACTIONS แบบ bulk บน header เปิด (ดู BulkActionsButton_Click)
    /// </summary>
    private async void ClientActionsButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ClientMachineDto client })
        {
            return;
        }

        var panel = new ActionPanelWindow(_apiClient, client) { Owner = this };
        await RunActionPanelAsync(panel);
    }

    /// <summary>
    /// เปิด ActionPanelWindow แบบ modal แล้วจัดการสิ่งที่ต้องทำต่อหลังมันปิด - ใช้ร่วมกันทั้ง
    /// โหมด single-client (ปุ่มต่อแถว) และ bulk (ปุ่มบน header)
    ///
    /// หน้า report ถูกเปิด "หลัง" panel ปิดสนิทแล้วเสมอ ไม่ใช่ตอนที่ panel ยังอยู่ - ตรงตาม
    /// โจทย์ที่ว่าต้องเปิดแทนที่กัน ไม่ซ้อนกัน (ดู ActionPanelWindow.PendingReport)
    /// </summary>
    private async Task RunActionPanelAsync(ActionPanelWindow panel)
    {
        panel.ShowDialog();

        if (panel.PendingReport is not null)
        {
            // ส่ง LabHubClient ตัวเดิมของ Dashboard เข้าไปใช้ต่อ (join group "console" ไว้แล้ว)
            // ไม่เปิด connection ใหม่ให้เปลือง - หน้า report unsubscribe ตัวเองตอนปิด
            var report = new ActionReportWindow(_apiClient, _labHubClient, panel.PendingReport) { Owner = this };
            report.Show();
        }

        if (panel.DataChanged)
        {
            await LoadAllDataAsync();
        }

        if (panel.ViewScreenRequested is not null)
        {
            await ShowVncViewAsync(panel.ViewScreenRequested);
        }

        if (panel.ViewProcessesRequested is not null)
        {
            var processesWindow = new RunningProcessesWindow(_apiClient, _labHubClient, panel.ViewProcessesRequested) { Owner = this };
            processesWindow.Show();
        }

        if (panel.ViewPendingCommandsRequested is not null)
        {
            var pendingCommandsWindow = new PendingCommandsWindow(_apiClient, panel.ViewPendingCommandsRequested) { Owner = this };
            pendingCommandsWindow.Show();
        }
    }

    private async void ToggleRuleButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: int ruleId })
        {
            var success = await _apiClient.ToggleRuleAsync(ruleId);
            StatusText.Text = success ? "Toggle กฎสำเร็จ" : "Toggle กฎไม่สำเร็จ";
            await LoadAllDataAsync();
        }
    }

    private async void DeleteRuleButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: int ruleId })
        {
            // ต้องพิมพ์ชื่อกฎให้ตรงก่อนถึงจะลบได้ - ลบกฎเป็นหนึ่งในสองอย่างในแอปนี้ที่เอาคืนไม่ได้จริง ๆ
            // (อีกอย่างคือลบผู้ใช้) และแถวในตารางกฎหน้าตาคล้ายกันมากจนกดผิดแถวได้ง่าย การพิมพ์ชื่อบังคับ
            // ให้ต้องอ่านก่อนว่ากำลังลบตัวไหนอยู่ ไม่ใช่แค่กดผ่านกล่องยืนยันอีกกล่อง
            var ruleName = _allRules.FirstOrDefault(r => r.Id == ruleId)?.Name;
            var confirmed = string.IsNullOrWhiteSpace(ruleName)
                ? ConfirmWindow.Show("ยืนยันการลบกฎนี้? การกระทำนี้ย้อนกลับไม่ได้", "ยืนยันการลบ", this)
                : ConfirmWindow.ShowWithTypeToConfirm(
                    $"กำลังจะลบกฎ \"{ruleName}\" ออกจากระบบถาวร การกระทำนี้ย้อนกลับไม่ได้",
                    "ยืนยันการลบกฎ", ruleName, this, "ลบกฎนี้");
            if (!confirmed)
            {
                return;
            }

            var success = await _apiClient.DeleteRuleAsync(ruleId);
            StatusText.Text = success ? "ลบกฎสำเร็จ" : "ลบกฎไม่สำเร็จ";
            await LoadAllDataAsync();
        }
    }

    private async void AddRuleButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new AddEditRuleWindow(_apiClient) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            StatusText.Text = "เพิ่มกฎสำเร็จ";
            await LoadAllDataAsync();
        }
    }

    private async void EditRuleButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: RuleDto rule })
        {
            var dialog = new AddEditRuleWindow(_apiClient, rule) { Owner = this };
            if (dialog.ShowDialog() == true)
            {
                StatusText.Text = "แก้ไขกฎสำเร็จ";
                await LoadAllDataAsync();
            }
        }
    }

    private async void AddUserButton_Click(object sender, RoutedEventArgs e)
    {
        var username = NewUserUsernameBox.Text.Trim();
        var password = NewUserPasswordBox.Password;

        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
        {
            StatusText.Text = "กรุณากรอก Username และ Password";
            return;
        }

        if (password.Length < 8)
        {
            StatusText.Text = "Password ต้องมีความยาวอย่างน้อย 8 ตัวอักษร";
            return;
        }

        var (success, message) = await _apiClient.CreateUserAsync(username, password);
        StatusText.Text = message;

        if (success)
        {
            NewUserUsernameBox.Clear();
            NewUserPasswordBox.Clear();
        }

        await LoadAllDataAsync();
    }

    private async void DeleteUserButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: int userId })
        {
            // เหตุผลเดียวกับการลบกฎ - ชื่อผู้ใช้ที่ต้องพิมพ์ดึงมาจากแถวที่กด ไม่ใช่จากที่อื่น
            var userName = (UsersGrid.ItemsSource as IEnumerable<UserDto>)?.FirstOrDefault(u => u.Id == userId)?.Username;
            var confirmed = string.IsNullOrWhiteSpace(userName)
                ? ConfirmWindow.Show("ยืนยันการลบ user นี้? การกระทำนี้ย้อนกลับไม่ได้", "ยืนยันการลบ", this)
                : ConfirmWindow.ShowWithTypeToConfirm(
                    $"กำลังจะลบผู้ใช้ \"{userName}\" ออกจากระบบถาวร การกระทำนี้ย้อนกลับไม่ได้",
                    "ยืนยันการลบผู้ใช้", userName, this, "ลบผู้ใช้นี้");
            if (!confirmed)
            {
                return;
            }

            var (success, message) = await _apiClient.DeleteUserAsync(userId);
            StatusText.Text = message;
            await LoadAllDataAsync();
        }
    }

    private void ClientsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var count = ClientsGrid.SelectedItems.Count;
        SelectedClientsCountText.Text = $"เลือก {count} เครื่อง";

        // แถบจะโผล่มาเมื่อมีเครื่องถูกเลือกเท่านั้น - เป็นตัวที่ทำให้ "เลือกหลายเครื่องแล้วสั่งทีเดียว"
        // มองเห็นได้จริง แทนที่จะซ่อนอยู่หลังปุ่ม ACTIONS ที่นิ่งอยู่ตลอดเวลาแบบเดิม
        // Hidden ไม่ใช่ Collapsed - Hidden ยังกินที่เท่าเดิม ตารางข้างล่างจึงไม่ขยับตอนเลือกเครื่อง
        // ของเดิมใช้ Collapsed ทำให้แถบนี้แทรกเข้ามาแล้วดันตารางลง คลิกถัดไปเลยไปโดนเครื่องคนละตัว
        ClientsSelectionBar.Visibility = count > 0 ? Visibility.Visible : Visibility.Hidden;
        UpdateClientInspector();
    }

    private void ClearClientSelectionButton_Click(object sender, RoutedEventArgs e) => ClientsGrid.UnselectAll();

    // ==================== แผงรายละเอียดเครื่องฝั่งขวา (2026-09-09) ====================

    /// <summary>
    /// เครื่องที่แผงขวากำลังแสดงอยู่ - เก็บไว้เองแทนที่จะอ่าน ClientsGrid.SelectedItem ตอนกดปุ่ม
    /// เพราะ auto-refresh ทุก 2 วิสร้าง DTO ชุดใหม่แล้วผูกเข้าตารางใหม่ทั้งหมด ระหว่างที่แผงยังเปิดค้าง
    /// อยู่ ถ้าอ่านสด ๆ ตอนกดอาจได้ instance คนละตัวกับที่ผู้ใช้เห็นอยู่ตรงหน้า
    /// </summary>
    private ClientMachineDto? _inspectedClient;

    /// <summary>
    /// แผงขวาแสดงเมื่อเลือก "เครื่องเดียว" เท่านั้น - เลือกหลายเครื่องคือคนละโหมด (สั่งงานหมู่)
    /// ซึ่งมีแถบเลือกด้านบนรับหน้าที่อยู่แล้ว การโชว์รายละเอียดของเครื่องใดเครื่องหนึ่งตอนเลือก 7 เครื่อง
    /// จะทำให้เข้าใจผิดว่าคำสั่งจะไปลงที่เครื่องนั้นเครื่องเดียว
    /// </summary>
    private void UpdateClientInspector()
    {
        var selected = ClientsGrid.SelectedItems.Cast<ClientMachineDto>().ToList();
        if (selected.Count != 1)
        {
            _inspectedClient = null;
            ClientInspector.Visibility = Visibility.Collapsed;
            return;
        }

        var client = selected[0];
        _inspectedClient = client;
        ClientInspector.Visibility = Visibility.Visible;

        InspectorMachineName.Text = client.MachineName;

        InspectorStatusBadge.Content = client.IsOnline ? "ONLINE" : "OFFLINE";
        InspectorStatusBadge.Tag = client.IsOnline ? "Online" : "Offline";

        InspectorEnforcementBadge.Content = client.EnforcementBadgeText;
        InspectorEnforcementBadge.Tag = client.EnforcementBadgeTag;

        InspectorConnectionBadge.Content = client.UsingFallback ? "TAILSCALE" : "LAN";
        InspectorConnectionBadge.Tag = client.UsingFallback ? "Warning" : "Online";

        InspectorIp.Text = string.IsNullOrWhiteSpace(client.LastKnownIp) ? "-" : client.LastKnownIp;
        InspectorLastSeen.Text = client.LastSeenAt.ToString("yyyy-MM-dd HH:mm:ss");
        InspectorGuid.Text = client.ClientGuid;
        InspectorPollInterval.Text = client.PollIntervalOverrideSeconds is { } seconds
            ? $"{seconds} วินาที (ตั้งเฉพาะเครื่องนี้)"
            : "ตามค่าเริ่มต้นของระบบ";
    }

    private async void InspectorViewScreenButton_Click(object sender, RoutedEventArgs e)
    {
        if (_inspectedClient is { } client)
        {
            await ShowVncViewAsync(client);
        }
    }

    private void InspectorProcessesButton_Click(object sender, RoutedEventArgs e)
    {
        if (_inspectedClient is { } client)
        {
            new RunningProcessesWindow(_apiClient, _labHubClient, client) { Owner = this }.Show();
        }
    }

    private void InspectorPendingCommandsButton_Click(object sender, RoutedEventArgs e)
    {
        if (_inspectedClient is { } client)
        {
            new PendingCommandsWindow(_apiClient, client) { Owner = this }.Show();
        }
    }

    private async void InspectorClientSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        if (_inspectedClient is not { } client)
        {
            return;
        }

        new ClientSettingsWindow(_apiClient, client) { Owner = this }.ShowDialog();
        await LoadAllDataAsync();
    }

    private async void InspectorRuleOverridesButton_Click(object sender, RoutedEventArgs e)
    {
        if (_inspectedClient is not { } client)
        {
            return;
        }

        new RuleOverridesWindow(_apiClient, client) { Owner = this }.ShowDialog();
        await LoadAllDataAsync();
    }

    private async void InspectorActionsButton_Click(object sender, RoutedEventArgs e)
    {
        if (_inspectedClient is { } client)
        {
            await RunActionPanelAsync(new ActionPanelWindow(_apiClient, client) { Owner = this });
        }
    }

    private async void BroadcastMessageButton_Click(object sender, RoutedEventArgs e)
    {
        var selected = ClientsGrid.SelectedItems.Cast<ClientMachineDto>().ToList();

        var dialog = new BroadcastMessageWindow(selected.Count) { Owner = this };
        var result = dialog.ShowDialog();

        if (result != true)
        {
            return;
        }

        var targets = dialog.SendToAll ? _allClients : selected;

        if (targets.Count == 0)
        {
            StatusText.Text = "ไม่มีเครื่องให้ส่งข้อความ";
            return;
        }

        StatusText.Text = $"กำลังส่งข้อความไป {targets.Count} เครื่อง...";

        var successCount = 0;
        foreach (var client in targets)
        {
            var payload = new
            {
                message = dialog.MessageText,
                title = string.IsNullOrEmpty(dialog.MessageTitle) ? null : dialog.MessageTitle
            };

            var success = await _apiClient.SendCommandAsync(client.ClientGuid, "BroadcastMessage", payload);
            if (success) successCount++;
        }

        StatusText.Text = $"ส่งข้อความสำเร็จ {successCount}/{targets.Count} เครื่อง";
    }

    /// <summary>
    /// ปุ่ม ACTIONS เดียวบน header (แทนที่ Toggle/Shutdown/Restart Selected เดิม 3 ปุ่มแยก) - เปิด
    /// ActionPanelWindow เดียวกับที่ปุ่ม ACTIONS ต่อแถวใช้ (ดู ClientActionsButton_Click) แต่ปรับเป็น
    /// bulk mode ครอบคลุมทั้ง selected-multiple และ all-clients (เลือกได้ใน panel เอง)
    /// </summary>
    private async void BulkActionsButton_Click(object sender, RoutedEventArgs e)
    {
        var selected = ClientsGrid.SelectedItems.Cast<ClientMachineDto>().ToList();
        var panel = new ActionPanelWindow(_apiClient, selected, _allClients, defaultToAll: false) { Owner = this };
        await RunActionPanelAsync(panel);
    }

    /// <summary>
    /// ปุ่ม ACTIONS (ทุกเครื่อง) บนแถบคำสั่งระดับห้อง - แผงเดียวกับปุ่มในแถบเลือก ต่างกันแค่ตั้งเป้า
    /// เริ่มต้นเป็น "ทุกเครื่อง" และ **เปิดได้โดยไม่ต้องเลือกเครื่องก่อน**
    ///
    /// เดิมทางเข้าเดียวคือปุ่มในแถบเลือก ซึ่งโผล่เมื่อมี selection เท่านั้น และตัว handler ก็ปฏิเสธถ้า
    /// เลือก 0 เครื่อง - แปลว่าจะสั่งทั้งห้องต้องไปเลือกเครื่องที่ไม่ได้ตั้งใจจะสั่งก่อนหนึ่งเครื่อง
    /// แล้วค่อยสลับเป็น "ทุกเครื่อง" ในแผง
    /// </summary>
    private async void RoomActionsButton_Click(object sender, RoutedEventArgs e)
    {
        var selected = ClientsGrid.SelectedItems.Cast<ClientMachineDto>().ToList();
        var panel = new ActionPanelWindow(_apiClient, selected, _allClients, defaultToAll: true) { Owner = this };
        await RunActionPanelAsync(panel);
    }

    private async void ResetUserPasswordButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: UserDto user })
        {
            if (user.IsBuiltIn)
            {
                StatusText.Text = "ไม่แนะนำให้ reset password ของ built-in Administrator ด้วยวิธีนี้ ใช้ change password ปกติแทน";
                return;
            }

            var resetWindow = new ResetPasswordWindow(user.Username);
            var result = resetWindow.ShowDialog();

            if (result == true && !string.IsNullOrEmpty(resetWindow.NewPassword))
            {
                var (success, message) = await _apiClient.ResetUserPasswordAsync(user.Id, resetWindow.NewPassword);
                StatusText.Text = message;
                await LoadAllDataAsync();
            }
        }
    }

    private List<ClientMachineDto> _allClients = new();
    private List<RuleDto> _allRules = new();
    private List<LogEntryDto> _allLogs = new();

    // ==================== Audit Log tab ====================
    // ตั้งใจไม่ผูกกับ LoadAllDataAsync/_allLogs pattern (โหลดทั้งหมดทุก tick) เพราะ volume คาดว่าสูง
    // กว่ามาก - โหลดเฉพาะหน้าปัจจุบันตอนแท็บ active เท่านั้น (ดู MainTabControl_SelectionChanged
    // และ _autoRefreshTimer.Tick ใน constructor)
    private const int AuditLogPageSize = 100;
    private int _auditLogPage = 1;
    private int _auditLogTotalCount;
    private readonly DispatcherTimer _auditLogFilterDebounceTimer;

    private void ApplyClientsFilter()
    {
        // จำ selection เดิมไว้ก่อนเปลี่ยน ItemsSource (ไม่งั้นจะหลุดทุกครั้งที่ reload ข้อมูล
        // เช่นตอน auto-refresh ทุก 10 วิ หรือตอนพิมพ์ค้นหา) แล้วเลือกคืนให้ตาม Id หลังโหลดเสร็จ
        var selectedIds = ClientsGrid.SelectedItems.Cast<ClientMachineDto>().Select(c => c.Id).ToHashSet();

        // เติม gate ทั้งห้องลงทุกแถวก่อนผูกเข้าตาราง - คอลัมน์ ENFORCEMENT แสดง "ผลจริง" ที่ Agent
        // ทำอยู่ ไม่ใช่แค่ค่ารายเครื่อง (ดู ClientMachineDto.EnforcementBadgeText) ถ้าอ่านค่า global
        // ไม่ได้เลย ให้ถือว่าเปิด = แสดงผลแบบเดิมทุกประการ ไม่ใช่ขึ้น OFF (GLOBAL) ผิดๆ ทั้งตาราง
        var globalEnforcementEnabled = _globalState?.IsEnabled ?? true;
        foreach (var client in _allClients)
        {
            client.GlobalEnforcementEnabled = globalEnforcementEnabled;
        }

        // ตัวเลขบนการ์ดคิดจาก _allClients เสมอ ไม่ใช่จากผลที่กรองแล้ว - ไม่งั้นพอกด "ออนไลน์" การ์ด
        // "ออฟไลน์" จะกลายเป็น 0 ทันที ซึ่งอ่านแล้วเหมือนเครื่องหายไปจากระบบจริง ๆ การ์ดต้องบอก
        // "ห้องนี้มีอะไรอยู่บ้าง" คงที่ ส่วนตารางข้างล่างต่างหากที่เป็นตัวตอบ "ตอนนี้กำลังดูอะไรอยู่"
        ClientStatAll.Tag = _allClients.Count.ToString();
        ClientStatOnline.Tag = _allClients.Count(c => c.IsOnline).ToString();
        ClientStatOffline.Tag = _allClients.Count(c => !c.IsOnline).ToString();
        ClientStatNotEnforcing.Tag = _allClients.Count(c => !c.EffectiveEnabled).ToString();

        var keyword = ClientsSearchBox.Text.Trim();
        var byStatus = _clientStatusFilter switch
        {
            ClientStatusFilter.Online => _allClients.Where(c => c.IsOnline),
            ClientStatusFilter.Offline => _allClients.Where(c => !c.IsOnline),
            ClientStatusFilter.NotEnforcing => _allClients.Where(c => !c.EffectiveEnabled),
            _ => _allClients
        };

        var filtered = (string.IsNullOrEmpty(keyword)
            ? byStatus
            : byStatus.Where(c => c.MachineName.Contains(keyword, StringComparison.OrdinalIgnoreCase))).ToList();

        ClientsGrid.ItemsSource = filtered;

        // แยก "ยังไม่มีเครื่องเลย" ออกจาก "กรองแล้วไม่เจอ" - คนละสถานการณ์ และทางออกคนละอย่าง
        // (อันแรกคือไปติดตั้ง Agent อันหลังคือล้างตัวกรอง) ของเดิมขึ้นข้อความเดียวกันทั้งสองกรณี
        var filteringSomething = !string.IsNullOrEmpty(keyword) || _clientStatusFilter != ClientStatusFilter.All;
        if (filtered.Count > 0)
        {
            ClientsEmptyText.Visibility = Visibility.Collapsed;
        }
        else
        {
            ClientsEmptyText.Visibility = Visibility.Visible;
            if (filteringSomething)
            {
                ClientsEmptyText.Content = "ไม่มีเครื่องที่ตรงกับตัวกรอง";
                ClientsEmptyText.Tag = _allClients.Count > 0
                    ? $"ในระบบมีทั้งหมด {_allClients.Count} เครื่อง - ลองล้างคำค้นหรือกดการ์ด \"ทั้งหมด\""
                    : null;
            }
            else
            {
                ClientsEmptyText.Content = "ยังไม่มีเครื่องในระบบ";
                ClientsEmptyText.Tag = "เครื่องจะขึ้นมาเองเมื่อ Agent ติดตั้งแล้ว poll เข้ามาครั้งแรก";
            }
        }

        foreach (var client in filtered.Where(c => selectedIds.Contains(c.Id)))
        {
            ClientsGrid.SelectedItems.Add(client);
        }
    }

    /// <summary>ตัวกรองสถานะที่การ์ดตัวเลขด้านบนตาราง Clients เลือกอยู่</summary>
    private enum ClientStatusFilter
    {
        All,
        Online,
        Offline,
        NotEnforcing
    }

    private ClientStatusFilter _clientStatusFilter = ClientStatusFilter.All;

    private void ClientStatFilter_Checked(object sender, RoutedEventArgs e)
    {
        // ยิงตั้งแต่ตอน InitializeComponent จาก IsChecked="True" ที่ตั้งไว้ใน XAML ซึ่งเป็นจังหวะที่
        // ClientsGrid/ClientsSearchBox ยังไม่พร้อม - เช็คก่อนเสมอ (pattern เดียวกับ AuditLog filter
        // ที่มี comment เรื่องนี้อยู่แล้ว)
        if (!IsLoaded)
        {
            _clientStatusFilter = ClientStatusFilter.All;
            return;
        }

        _clientStatusFilter =
            ReferenceEquals(sender, ClientStatOnline) ? ClientStatusFilter.Online :
            ReferenceEquals(sender, ClientStatOffline) ? ClientStatusFilter.Offline :
            ReferenceEquals(sender, ClientStatNotEnforcing) ? ClientStatusFilter.NotEnforcing :
            ClientStatusFilter.All;

        ApplyClientsFilter();
    }

    private void ApplyRulesFilter()
    {
        var keyword = RulesSearchBox.Text.Trim();
        var filtered = string.IsNullOrEmpty(keyword)
            ? _allRules
            : _allRules.Where(r =>
                r.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                r.PublisherName.Contains(keyword, StringComparison.OrdinalIgnoreCase)).ToList();
        RulesGrid.ItemsSource = filtered;
        RulesEmptyText.Visibility = filtered.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ApplyLogsFilter()
    {
        var keyword = LogsSearchBox.Text.Trim();
        var filtered = string.IsNullOrEmpty(keyword)
            ? _allLogs
            : _allLogs.Where(l =>
                l.MachineName.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                (l.ProcessPath?.Contains(keyword, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (l.DetectedPublisher?.Contains(keyword, StringComparison.OrdinalIgnoreCase) ?? false)).ToList();
        LogsGrid.ItemsSource = filtered;
        LogsEmptyText.Visibility = filtered.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ClientsSearchBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyClientsFilter();
    private void RulesSearchBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyRulesFilter();
    private void LogsSearchBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyLogsFilter();

    /// <summary>
    /// โหลด audit log หน้าปัจจุบัน (_auditLogPage) ตาม filter ที่เลือกอยู่ตอนนี้ - เรียกตอนแท็บถูกเลือก,
    /// filter เปลี่ยน, กด Prev/Next, หรือ auto-refresh tick ขณะแท็บนี้ active เท่านั้น (ไม่โหลดทั้งตาราง
    /// ทีเดียวแบบแท็บอื่น เพราะ volume คาดว่าสูงกว่ามาก - ดู comment field ด้านบน)
    /// </summary>
    private async Task LoadActivityLogPageAsync()
    {
        // _apiClient ยังเป็น null ตอน InitializeComponent() กำลังรัน (constructor เรียก
        // InitializeComponent ก่อนแล้วค่อย assign _apiClient - ComboBox ที่ตั้ง SelectedIndex="0"
        // ใน XAML ยิง SelectionChanged ทันทีตอน parse ซึ่งเร็วกว่านั้น) ป้องกันไว้เผื่อถูกเรียกเร็วเกินไป
        if (_apiClient is null)
        {
            return;
        }

        try
        {
            var category = AuditLogCategoryFilter.SelectedIndex switch
            {
                1 => "AdminAction",
                2 => "AgentConnectivity",
                3 => "SystemEvent",
                _ => (string?)null
            };
            var minSeverity = AuditLogSeverityFilter.SelectedIndex switch
            {
                1 => "Info",
                2 => "Warning",
                3 => "Error",
                _ => (string?)null
            };
            var clientGuid = string.IsNullOrWhiteSpace(AuditLogClientGuidFilter.Text)
                ? null
                : AuditLogClientGuidFilter.Text.Trim();

            var pageResult = await _apiClient.GetActivityLogAsync(category, minSeverity, clientGuid, _auditLogPage, AuditLogPageSize);

            AuditLogGrid.ItemsSource = pageResult.Items;
            AuditLogEmptyText.Visibility = pageResult.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            _auditLogTotalCount = pageResult.TotalCount;
            var totalPages = Math.Max(1, (int)Math.Ceiling(pageResult.TotalCount / (double)AuditLogPageSize));
            AuditLogPageInfoText.Text = $"หน้า {pageResult.Page}/{totalPages} ({pageResult.TotalCount} รายการ)";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"โหลด audit log ไม่สำเร็จ: {ex.Message}";
        }
    }

    private async void AuditLogFilter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_apiClient is null)
        {
            return; // ถูกยิงระหว่าง InitializeComponent จาก SelectedIndex="0" ที่ตั้งไว้ใน XAML
        }

        _auditLogPage = 1;
        await LoadActivityLogPageAsync();
    }

    /// <summary>debounce 400ms กันยิง request ทุกตัวอักษรที่พิมพ์ในช่อง client GUID filter</summary>
    private void AuditLogClientGuidFilter_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_apiClient is null)
        {
            return;
        }

        _auditLogFilterDebounceTimer.Stop();
        _auditLogFilterDebounceTimer.Start();
    }

    private async void AuditLogPrevButton_Click(object sender, RoutedEventArgs e)
    {
        if (_auditLogPage <= 1)
        {
            return;
        }

        _auditLogPage--;
        await LoadActivityLogPageAsync();
    }

    private async void AuditLogNextButton_Click(object sender, RoutedEventArgs e)
    {
        var totalPages = Math.Max(1, (int)Math.Ceiling(_auditLogTotalCount / (double)AuditLogPageSize));
        if (_auditLogPage >= totalPages)
        {
            return;
        }

        _auditLogPage++;
        await LoadActivityLogPageAsync();
    }

    // ==================== Step 4d: embedded VNC screen view ====================

    /// <summary>
    /// เรียกจากปุ่ม VIEW SCREEN ใน ActionPanelWindow (single-client mode) - ตัด session เดิม (ถ้ามี) ทิ้ง
    /// ก่อนเสมอ แล้วสลับไปแท็บ SCREEN VIEW เริ่มขอ session ใหม่ ทุก await ข้างล่างเช็ค myGeneration ก่อน
    /// แตะ UI/state เสมอ กันกรณีผู้ใช้สลับไปดูเครื่องอื่น/ปิดแท็บระหว่างที่ยังรออยู่ (เช่น รอ Agent ตอบ
    /// หรือรอ handshake) ทำให้ผลลัพธ์ที่มาช้าของ session เก่าไปเขียนทับ session ใหม่กว่า
    /// </summary>
    private async Task ShowVncViewAsync(ClientMachineDto client)
    {
        DisconnectVncView();
        var myGeneration = ++_vncSessionGeneration;

        VncHeaderText.Text = $"Screen View - {client.MachineName}";
        VncStatusText.Text = "Connecting...";
        VncScreenImage.Source = null;
        MainTabControl.SelectedItem = ScreenViewTabItem;

        if (string.IsNullOrWhiteSpace(client.LastKnownIp))
        {
            VncStatusText.Text = "Failed to connect: ไม่ทราบ IP เป้าหมาย (LastKnownIp ว่าง - รอ Agent poll เข้ามาอีกครั้ง)";
            return;
        }

        var sessionResult = await RequestVncSessionAsync(client);
        if (myGeneration != _vncSessionGeneration)
        {
            return; // ถูกแทนที่ไปแล้วระหว่างรอ Agent ตอบ (สลับเครื่อง/ปิดแท็บ) - ทิ้งผลลัพธ์นี้
        }

        if (!sessionResult.Success)
        {
            VncStatusText.Text = $"Failed to connect: {sessionResult.FailureReason}";
            return;
        }

        var rfbClient = new RfbClient();
        var handshakeResult = await rfbClient.ConnectAndHandshakeAsync(
            client.LastKnownIp, sessionResult.Port, sessionResult.Password!);

        if (myGeneration != _vncSessionGeneration)
        {
            rfbClient.Dispose(); // ถูกแทนที่ไปแล้วระหว่างรอ handshake - ปิด connection ที่เพิ่ง connect ทิ้งกัน socket ค้าง
            return;
        }

        if (!handshakeResult.Success)
        {
            VncStatusText.Text = $"Failed to connect: {handshakeResult.FailureReason}";
            rfbClient.Dispose();
            return;
        }

        var bitmap = new WriteableBitmap(
            handshakeResult.FramebufferWidth, handshakeResult.FramebufferHeight, 96, 96, PixelFormats.Bgr32, null);
        VncScreenImage.Source = bitmap;
        VncStatusText.Text = "Connected";

        _vncClient = rfbClient;
        _vncBitmap = bitmap;
        var cts = new CancellationTokenSource();
        _vncStreamCts = cts;

        // Task.Run เพื่อให้ loop ข้างในรันบน thread pool thread จริง (เหมือน VncTestWindow เดิม) - กัน
        // await ข้างในถูก marshal กลับ UI thread เองอัตโนมัติจนบล็อก UI ระหว่างรอ network I/O
        _ = Task.Run(() => StreamVncAsync(rfbClient, bitmap, cts.Token));
    }

    /// <summary>
    /// ตัด session ปัจจุบันทิ้ง (ถ้ามี) - เรียกได้จากหลายจุด: ก่อนเริ่ม session ใหม่, กดปุ่ม DISCONNECT,
    /// สลับออกจากแท็บ SCREEN VIEW (MainTabControl_SelectionChanged), หรือปิดหน้าต่าง Dashboard เพิ่ม
    /// generation ก่อนเสมอ (แม้ไม่มี session ค้างอยู่เลย) เพื่อให้ ShowVncViewAsync ที่อาจกำลัง await
    /// ค้างอยู่ (ยังไม่ทันตั้ง _vncClient/_vncBitmap) รู้ตัวว่าถูกยกเลิกไปแล้วตอน resume เหมือนกัน
    /// </summary>
    private void DisconnectVncView()
    {
        _vncSessionGeneration++;

        _vncStreamCts?.Cancel();
        _vncStreamCts = null;

        _vncClient?.Dispose();
        _vncClient = null;

        _vncBitmap = null;
        VncScreenImage.Source = null;
    }

    private void VncDisconnectButton_Click(object sender, RoutedEventArgs e)
    {
        DisconnectVncView();
        VncHeaderText.Text = "ยังไม่ได้เลือกเครื่อง - เลือกเครื่องในแท็บ Clients แล้วกด \"ดูหน้าจอเครื่องนี้\"";
        VncStatusText.Text = "ตัดการเชื่อมต่อแล้ว";
    }

    /// <summary>
    /// SelectionChanged ของ Selector (TabControl/DataGrid/ComboBox ทุกตัวใช้ RoutedEvent ร่วมกัน) bubble
    /// ขึ้นมาจาก control ข้างในแท็บได้ (เช่น ClientsGrid) - เช็ค OriginalSource ตรงกับ MainTabControl เอง
    /// เท่านั้นถึงจะถือว่าเป็นการสลับแท็บจริง กันเรียก DisconnectVncView ผิดจังหวะ (เช่นแค่คลิกแถวใน DataGrid)
    /// </summary>
    private async void MainTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, MainTabControl))
        {
            return;
        }

        if (e.RemovedItems.Contains(ScreenViewTabItem))
        {
            DisconnectVncView();
            VncHeaderText.Text = "ยังไม่ได้เลือกเครื่อง - เลือกเครื่องในแท็บ Clients แล้วกด \"ดูหน้าจอเครื่องนี้\"";
            VncStatusText.Text = "";
        }

        if (e.RemovedItems.Contains(LiveViewTabItem) && !SettingsService.Current.Vnc.KeepLiveViewOpenOnTabLeave)
        {
            await CloseAllLiveViewSessionsAsync();
        }

        if (e.AddedItems.Contains(AuditLogTabItem))
        {
            await LoadActivityLogPageAsync();
        }
    }

    private async Task StreamVncAsync(RfbClient rfbClient, WriteableBitmap bitmap, CancellationToken ct)
    {
        try
        {
            await rfbClient.RunFramebufferUpdateLoopAsync(rect => OnVncRectangleDecoded(bitmap, rect), ct);
        }
        catch (OperationCanceledException)
        {
            // disconnect ปกติ (สลับเครื่อง/ปิดแท็บ/ปิดหน้าต่าง) - ไม่ต้องรายงาน
        }
        catch (Exception ex)
        {
            if (ct.IsCancellationRequested)
            {
                return; // เพิ่งถูก cancel พอดี (เช่น dispose ระหว่าง read) ไม่ใช่ error จริงของ session นี้
            }

            Dispatcher.Invoke(() =>
            {
                if (!ReferenceEquals(_vncBitmap, bitmap))
                {
                    return; // session นี้ถูกแทนที่ไปแล้ว - เพิกเฉย กัน UI เก่าทับ session ใหม่
                }

                VncStatusText.Text = $"Failed to connect: {ex.Message}";
            });
        }
    }

    /// <summary>เรียกจาก background thread ของ StreamVncAsync (ดู comment ที่จุดเรียก Task.Run ใน ShowVncViewAsync) - ต้อง marshal เข้า UI thread เองผ่าน Dispatcher เสมอ</summary>
    private void OnVncRectangleDecoded(WriteableBitmap bitmap, RfbRectangle rect)
    {
        if (rect.Width == 0 || rect.Height == 0)
        {
            return;
        }

        Dispatcher.Invoke(() =>
        {
            if (!ReferenceEquals(_vncBitmap, bitmap))
            {
                return; // session นี้ถูกแทนที่ไปแล้ว - เพิกเฉย
            }

            // ใช้ helper ตัวเดียวกับ Live View grid (2026-08-31) - เดิม path นี้ (SCREEN VIEW) เขียนตรง
            // ไม่มี bounds check ทั้งที่ Live View มีอยู่แล้ว เป็นความไม่สอดคล้องที่เหลือไว้ไม่มีประโยชน์:
            // ทั้งสอง path รับข้อมูลจาก winvnc.exe ตัวเดียวกันบนเครื่องนักเรียนเหมือนกันเป๊ะ
            WriteLiveViewRectSafely(bitmap, rect);
        });
    }

    /// <summary>
    /// ส่ง command StartVncSession แล้วรอผลผ่าน LabHubClient event เดียวกับที่ DashboardWindow subscribe
    /// อยู่แล้ว (SessionId เดียวกันตลอดอายุ Dashboard) มี timeout กันค้างถ้า Agent ไม่ตอบ (offline/SignalR
    /// หลุด) - subscribe/unsubscribe event ชั่วคราวเฉพาะช่วงรอนี้เท่านั้น (เหมือน VncTestWindow เดิม)
    /// </summary>
    private async Task<(bool Success, int Port, string? Password, string? FailureReason)> RequestVncSessionAsync(ClientMachineDto client)
    {
        var tcs = new TaskCompletionSource<(bool, int, string?, string?)>(TaskCreationOptions.RunContinuationsAsynchronously);

        // ตั้งแต่ Step 4e event มี clientGuid มาด้วย (VncSessionPool อาจมีหลาย request ค้างรอพร้อมกัน) -
        // Dashboard เปิดได้ทีละเครื่องเท่านั้นตอนนี้ (Step 4d) เลยไม่มีผลต่างเชิงพฤติกรรม แต่ต้องกรองให้
        // ถูกต้องตาม client ที่ request ไปเสมอ ไม่ใช่รับทุก event ที่เข้ามา
        void OnReady(string clientGuid, int port, string password)
        {
            if (clientGuid == client.ClientGuid)
            {
                tcs.TrySetResult((true, port, password, null));
            }
        }

        void OnFailed(string clientGuid, string reason)
        {
            if (clientGuid == client.ClientGuid)
            {
                tcs.TrySetResult((false, 0, null, reason));
            }
        }

        _labHubClient.VncSessionReady += OnReady;
        _labHubClient.VncSessionFailed += OnFailed;

        try
        {
            var sent = await _apiClient.SendCommandAsync(client.ClientGuid, "StartVncSession",
                new { requestingConsoleSessionId = _labHubClient.SessionId });

            if (!sent)
            {
                return (false, 0, null, "ส่ง command StartVncSession ไปยัง Server ไม่สำเร็จ (HTTP)");
            }

            var completed = await Task.WhenAny(tcs.Task, Task.Delay(VncSessionReadyTimeout));
            if (completed != tcs.Task)
            {
                return (false, 0, null, $"รอ Agent ตอบกลับเกิน {VncSessionReadyTimeout.TotalSeconds} วิ (Agent อาจ offline หรือ SignalR ไม่ connected)");
            }

            return await tcs.Task;
        }
        finally
        {
            _labHubClient.VncSessionReady -= OnReady;
            _labHubClient.VncSessionFailed -= OnFailed;
        }
    }

    // ==================== Step 4f: Live View grid (CCTV wall) ====================

    /// <summary>เรียกจาก LoadAllDataAsync ทุกครั้งที่ _allClients เปลี่ยน (poll 10 วิ/SignalR/REFRESH) - จำ selection เดิมไว้ก่อนเปลี่ยน ItemsSource เหมือน ApplyClientsFilter กันเลือกหลุดกลางคันตอนกำลังจะกด ADD</summary>
    private void ApplyLiveViewClientList()
    {
        var selectedIds = LiveViewClientListBox.SelectedItems.Cast<ClientMachineDto>().Select(c => c.Id).ToHashSet();

        LiveViewClientListBox.ItemsSource = _allClients;

        foreach (var client in _allClients.Where(c => selectedIds.Contains(c.Id)))
        {
            LiveViewClientListBox.SelectedItems.Add(client);
        }
    }

    /// <summary>
    /// เปิด session แบบ Thumbnail mode เฉพาะเครื่องที่เลือกไว้ (lazy - ไม่ auto-connect ทั้งหมดตั้งแต่เปิด
    /// หน้า ตามที่ตกลง) ข้ามเครื่องที่เปิดอยู่ใน grid แล้ว เช็ค MaxConcurrentSessions ก่อนเสมอ (single
    /// source - ดู VncSessionPool.MaxConcurrentSessions) ไม่ปล่อยให้เงียบๆ ล้มเหลวถ้าเกิน limit
    /// </summary>
    private void AddSelectedToGridButton_Click(object sender, RoutedEventArgs e)
    {
        var selected = LiveViewClientListBox.SelectedItems.Cast<ClientMachineDto>()
            .Where(c => !_liveViewCells.ContainsKey(c.ClientGuid))
            .ToList();

        if (selected.Count == 0)
        {
            LiveViewStatusText.Text = _liveViewCells.Count == 0
                ? "กรุณาเลือกเครื่องอย่างน้อย 1 เครื่อง"
                : "เครื่องที่เลือกเปิดอยู่ใน grid แล้วทั้งหมด";
            return;
        }

        var remaining = VncSessionPool.MaxConcurrentSessions - _liveViewCells.Count;
        if (selected.Count > remaining)
        {
            LiveViewStatusText.Text = remaining <= 0
                ? $"Grid เต็มแล้ว (สูงสุด {VncSessionPool.MaxConcurrentSessions} เครื่อง) - เอาเครื่องออกก่อนเพิ่มใหม่"
                : $"เลือกเกินจำนวนที่เพิ่มได้ (เหลือที่ว่างอีก {remaining} เครื่อง จากทั้งหมด {VncSessionPool.MaxConcurrentSessions}) - เลือกน้อยลง";
            return;
        }

        foreach (var client in selected)
        {
            AddCellToGrid(client);
        }

        UpdateLiveViewStatusAndButtonState();
    }

    private void AddCellToGrid(ClientMachineDto client)
    {
        var cell = CreateLiveViewCell(client);
        LiveViewGrid.Children.Add(cell.Border);
        _liveViewCells[client.ClientGuid] = cell;

        _ = OpenLiveViewSessionAsync(client, cell);
    }

    /// <summary>
    /// เรียกจาก UI-thread continuation เสมอ (AddCellToGrid ไม่ผ่าน Task.Run) - เหมือน Step 4d
    /// ShowVncViewAsync เลยแตะ UI ตรงๆ ได้หลัง await โดยไม่ต้อง Dispatcher.Invoke เช็ค _liveViewCells
    /// ก่อนเสมอหลัง await กันกรณีผู้ใช้กด X เอา cell นี้ออกไปแล้วระหว่างที่ยังรอ connect อยู่ (ทิ้งผลลัพธ์
    /// ที่มาช้าไป ไม่งั้นจะไปแก้ cell ที่หลุดจาก grid ไปแล้ว)
    /// </summary>
    private async Task OpenLiveViewSessionAsync(ClientMachineDto client, LiveViewCell cell)
    {
        var (success, handle, failureReason) = await _liveViewPool.OpenAsync(client, VncViewMode.Thumbnail);

        if (!_liveViewCells.TryGetValue(client.ClientGuid, out var currentCell) || !ReferenceEquals(currentCell, cell))
        {
            return; // ถูกเอาออกจาก grid ไปแล้วระหว่างรอ (หรือถูกเพิ่มใหม่กลายเป็นคนละ cell object) - ทิ้งผลลัพธ์นี้
        }

        if (!success || handle is null)
        {
            cell.StatusOverlayText.Text = $"Failed: {failureReason}";
            cell.StatusOverlayText.Visibility = Visibility.Visible;
            return;
        }

        cell.Handle = handle;
        handle.StatusChanged += () => Dispatcher.Invoke(() => OnLiveViewStatusChanged(cell, handle));
        handle.RectangleDecoded += rect => OnLiveViewRectangleDecoded(cell, handle, rect);

        OnLiveViewStatusChanged(cell, handle);
    }

    private void OnLiveViewStatusChanged(LiveViewCell cell, VncSessionHandle handle)
    {
        cell.StatusOverlayText.Text = handle.Status switch
        {
            VncSessionStatus.Connecting => "Connecting...",
            VncSessionStatus.Connected => "",
            VncSessionStatus.Failed => $"Failed: {handle.FailureReason}",
            VncSessionStatus.Closed => "Closed",
            _ => ""
        };
        cell.StatusOverlayText.Visibility = string.IsNullOrEmpty(cell.StatusOverlayText.Text) ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>
    /// เรียกจาก background thread ของ VncSessionHandle (RfbClient framebuffer loop) เสมอ - ต้อง
    /// marshal เข้า UI thread เองผ่าน Dispatcher (เหมือน Step 4d/4e ทุกจุด) ตัดสินใจว่าจะเขียนลง
    /// thumbnail bitmap ของ cell ใน grid หรือ fullscreen bitmap จาก _liveViewFullscreenClientGuid ปัจจุบัน
    /// - สร้าง/resize WriteableBitmap ใหม่ถ้ายังไม่มีหรือ mode เปลี่ยนไปจากตอนสร้างครั้งล่าสุด (สลับ
    /// Thumbnail/FullRes เปลี่ยนขนาดภาพเสมอ)
    /// </summary>
    private void OnLiveViewRectangleDecoded(LiveViewCell cell, VncSessionHandle handle, RfbRectangle rect)
    {
        if (rect.Width == 0 || rect.Height == 0)
        {
            return;
        }

        Dispatcher.Invoke(() =>
        {
            if (_liveViewFullscreenClientGuid == handle.Client.ClientGuid)
            {
                if (_liveViewFullscreenBitmap is null || _liveViewFullscreenBitmapMode != handle.Mode)
                {
                    var width = handle.DisplayWidth ?? rect.Width;
                    var height = handle.DisplayHeight ?? rect.Height;
                    _liveViewFullscreenBitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgr32, null);
                    _liveViewFullscreenBitmapMode = handle.Mode;
                    FullscreenImage.Source = _liveViewFullscreenBitmap;
                }

                WriteLiveViewRectSafely(_liveViewFullscreenBitmap, rect);
            }
            else
            {
                if (cell.Bitmap is null || cell.BitmapMode != handle.Mode)
                {
                    var width = handle.DisplayWidth ?? rect.Width;
                    var height = handle.DisplayHeight ?? rect.Height;
                    cell.Bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgr32, null);
                    cell.BitmapMode = handle.Mode;
                    cell.Image.Source = cell.Bitmap;
                }

                WriteLiveViewRectSafely(cell.Bitmap, rect);
            }
        });
    }

    private static void WriteLiveViewRectSafely(WriteableBitmap bitmap, RfbRectangle rect)
    {
        if (rect.X + rect.Width > bitmap.PixelWidth || rect.Y + rect.Height > bitmap.PixelHeight)
        {
            return; // ขนาดยังไม่ตรงกับ mode ปัจจุบันพอดี (ไม่ควรเกิดได้จริงในทางทฤษฎี - กันไว้เฉยๆ)
        }

        var area = new Int32Rect(rect.X, rect.Y, rect.Width, rect.Height);
        bitmap.WritePixels(area, rect.PixelData, rect.Width * 4, 0);
    }

    /// <summary>สร้าง UI ของ cell หนึ่งช่องใน grid แบบ code-behind (เหมือน pattern เดียวกับ VncPoolTestWindow) - layer ซ้อนกันใน Grid เดียว: ภาพ -> ข้อความสถานะกลางจอ -> ป้ายชื่อเครื่องมุมล่าง -> ปุ่ม X มุมบน คลิกที่ไหนก็ได้ใน Border (ยกเว้นปุ่ม X) เพื่อขยายเต็มจอ</summary>
    private LiveViewCell CreateLiveViewCell(ClientMachineDto client)
    {
        var image = new Image { Stretch = Stretch.Uniform };

        var statusText = new TextBlock
        {
            Text = "Connecting...",
            Foreground = Brushes.White,
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 200
        };

        var nameLabel = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(160, 0, 0, 0)),
            VerticalAlignment = VerticalAlignment.Bottom,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Child = new TextBlock
            {
                Text = client.MachineName,
                Foreground = Brushes.White,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(6, 3, 6, 3),
                TextTrimming = TextTrimming.CharacterEllipsis
            }
        };

        var removeButton = new Button
        {
            // IconPresenter ไม่ใช่ตัวอักษร "✕": ตัวอักษรจะขึ้นกับ font ที่ระบบ fallback มาให้ ซึ่ง
            // ขนาดและน้ำหนักเส้นไม่ตรงกับไอคอนตัวอื่นในแอปเลย - ชุดเดียวกันทั้งหมดดีกว่า
            Content = new Controls.IconPresenter { Icon = "x", IconSize = 12 },
            Width = 22,
            Height = 22,
            Padding = new Thickness(0),
            FontSize = 11,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 4, 4, 0),
            Tag = client.ClientGuid
        };
        removeButton.Click += RemoveGridCellButton_Click;

        var overlayGrid = new Grid();
        overlayGrid.Children.Add(image);
        overlayGrid.Children.Add(statusText);
        overlayGrid.Children.Add(nameLabel);
        overlayGrid.Children.Add(removeButton);

        var border = new Border
        {
            // Black คงไว้เป็นค่าตายตัวโดยเจตนา ไม่ใช่ token: นี่คือพื้นหลังของ "จอวิดีโอ" ที่ภาพ VNC letterbox
            // อยู่ข้างใน เหมือนขอบดำของจอทีวี - มันไม่ใช่ surface ของแอปและไม่ควรเปลี่ยนตามธีม เช่นเดียวกับ
            // ข้อความสีขาวที่ทับอยู่บนภาพด้านบน มีแต่ "ขอบ" เท่านั้นที่เป็นส่วนของ UI จริงจึงผูกกับธีม
            Background = Brushes.Black,
            BorderThickness = new Thickness(1),
            Margin = new Thickness(3),
            Child = overlayGrid,
            Cursor = Cursors.Hand
        };
        border.SetResourceReference(Border.BorderBrushProperty, "Brush.Border.Default");

        // Button.Click กิน MouseLeftButtonUp ที่ trigger มันไปแล้ว (ButtonBase mark e.Handled ให้เอง) เลย
        // ไม่ bubble มาถึง handler นี้ตอนกดปุ่ม X - คลิกที่ไหนอื่นใน cell จะ trigger ขยายเต็มจอ
        border.MouseLeftButtonUp += (_, _) => ExpandLiveViewCell(client.ClientGuid);

        return new LiveViewCell
        {
            Border = border,
            Image = image,
            StatusOverlayText = statusText,
            Client = client
        };
    }

    /// <summary>คลิกที่ thumbnail ใน grid - สลับ session นั้นเป็น FullRes mode (ไม่ reconnect) แล้วแสดง full-screen overlay ทับทั้งหน้า Dashboard - session อื่นใน grid ยังรันเบื้องหลังต่อปกติ ไม่ pause/reconnect (ตามที่ตกลง)</summary>
    private void ExpandLiveViewCell(string clientGuid)
    {
        if (!_liveViewCells.TryGetValue(clientGuid, out var cell) || cell.Handle is not { Status: VncSessionStatus.Connected } handle)
        {
            return; // ยังไม่ connect เสร็จ (ยัง Connecting/Failed) - ขยายไม่ได้
        }

        _liveViewFullscreenClientGuid = clientGuid;
        _liveViewFullscreenBitmap = null;
        _liveViewFullscreenBitmapMode = null;
        FullscreenImage.Source = null;
        FullscreenHeaderText.Text = cell.Client.MachineName;
        FullscreenStatusText.Text = "";

        // default ปิด (ไม่บล็อก) เสมอทุกครั้งที่เข้า fullscreen ใหม่ - ไม่สืบทอด state จากเครื่องก่อนหน้า
        _targetInputBlocked = false;
        SetToggleTargetInputButtonVisual(blocked: false);

        LiveViewFullscreenOverlay.Visibility = Visibility.Visible;

        // ต้อง focus หลัง Visibility=Visible เท่านั้น (ตอน Collapsed element ไม่มี layout/ไม่ใช่ส่วนหนึ่ง
        // ของ focus scope จริง) ใช้ Dispatcher.InvokeAsync (Input priority) แทนเรียก Focus() ตรงๆ เพื่อรอ
        // ให้ layout pass ของการเปลี่ยน Visibility เสร็จก่อนจริงๆ - ดู
        // LiveViewFullscreenOverlay_IsKeyboardFocusWithinChanged ที่ดึง focus กลับมาอัตโนมัติถ้าโดน
        // popup/element อื่นแย่ง focus ไประหว่างที่ยังอยู่ใน fullscreen mode
        Dispatcher.InvokeAsync(() => Keyboard.Focus(LiveViewFullscreenOverlay), DispatcherPriority.Input);

        handle.RequestModeSwitch(VncViewMode.FullRes);
    }

    /// <summary>กลับไป grid - สลับ mode คืนเป็น Thumbnail (ไม่ reconnect) แล้วซ่อน overlay เครื่องอื่นใน grid ไม่ถูกแตะเลยตลอดช่วงที่อยู่ full-screen (ตามที่ตกลง)</summary>
    private void BackToGridButton_Click(object sender, RoutedEventArgs e)
    {
        if (_liveViewFullscreenClientGuid is not null &&
            _liveViewCells.TryGetValue(_liveViewFullscreenClientGuid, out var cell) &&
            cell.Handle is { } handle)
        {
            handle.RequestModeSwitch(VncViewMode.Thumbnail);
        }

        ReleaseFullscreenInputState();

        _liveViewFullscreenClientGuid = null;
        _liveViewFullscreenBitmap = null;
        _liveViewFullscreenBitmapMode = null;
        FullscreenImage.Source = null;

        LiveViewFullscreenOverlay.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// Toggle target input: กันนักเรียนแย่งเมาส์/คีย์บอร์ดขณะครูควบคุมผ่าน full-screen VNC (ดู
    /// PhysicalInputBlocker ฝั่ง AgentTray) - ไม่ optimistic update ปุ่ม รอผลจริงของ SendCommandAsync
    /// ก่อนสลับ state/ปุ่ม เพราะถ้า command ส่งไม่ถึงจริง (เช่น server หลุด) ต้องรู้ตัวว่ายัง
    /// unblocked/blocked อยู่เหมือนเดิม ไม่ใช่เข้าใจผิดว่าสำเร็จไปแล้ว
    /// </summary>
    private async void ToggleTargetInputButton_Click(object sender, RoutedEventArgs e)
    {
        if (_liveViewFullscreenClientGuid is null)
        {
            return; // ไม่ได้อยู่ใน fullscreen แล้ว (เช่น ออกไปพอดีระหว่างรอ event ก่อนหน้า) - เป็น no-op
        }

        var clientGuid = _liveViewFullscreenClientGuid;
        var wantBlocked = !_targetInputBlocked;

        ToggleTargetInputButton.IsEnabled = false;
        var success = await _apiClient.SendCommandAsync(
            clientGuid, wantBlocked ? "BlockTargetInput" : "UnblockTargetInput", null);
        ToggleTargetInputButton.IsEnabled = true;

        if (!success)
        {
            FullscreenStatusText.Text = wantBlocked
                ? "บล็อก input ไม่สำเร็จ - ลองใหม่อีกครั้ง"
                : "คืนสิทธิ์ input ไม่สำเร็จ - ลองใหม่อีกครั้ง";
            return;
        }

        // เช็คซ้ำว่ายังอยู่เครื่องเดิม/fullscreen อยู่ไหม (กันกรณีครูออกจาก fullscreen ไปแล้วระหว่างรอ
        // SendCommandAsync - ReleaseFullscreenInputState จัดการ reset state ไปแล้ว ไม่ควรทับกลับ)
        if (_liveViewFullscreenClientGuid != clientGuid)
        {
            return;
        }

        _targetInputBlocked = wantBlocked;
        FullscreenStatusText.Text = "";
        SetToggleTargetInputButtonVisual(_targetInputBlocked);
    }

    private void SetToggleTargetInputButtonVisual(bool blocked)
    {
        // ป้ายบอกสิ่งที่จะเกิดถ้ากด ไม่ใช่สถานะปัจจุบัน - ตอนกำลังบล็อกอยู่ ปุ่มจึงเขียนว่า "คืน..."
        ToggleTargetInputButton.Content = blocked
            ? "คืนเมาส์/คีย์บอร์ดให้นักเรียน"
            : "บล็อกเมาส์/คีย์บอร์ดนักเรียน";
        ToggleTargetInputButton.Style = (Style)FindResource(blocked ? "ButtonDanger" : "ButtonSecondary");
    }

    /// <summary>
    /// เรียกทุกครั้งก่อนออกจาก fullscreen mode ไม่ว่าทางไหน (BACK TO GRID, เอา cell ออกจาก grid ระหว่าง
    /// fullscreen, ปิดแท็บ LIVE VIEW, ปิด Dashboard) - เคลียร์ mouse capture ที่อาจค้างอยู่ + force-release
    /// ปุ่มเมาส์ที่กำลังกดค้างอยู่บน target ก่อนเสมอ (กัน "stuck mouse-down" ถ้าครูออกจาก fullscreen กลางที่
    /// ยังลาก-วางไฟล์อยู่พอดี - หลังจากนี้จะไม่มีทางส่ง MouseUp ให้ target รู้ตัวได้อีกแล้ว) เทียบเท่ากับการ
    /// "unbind input event" ที่ขอ - แต่ทำผ่านการเคลียร์ state (GetFullscreenHandle คืน null ทันทีหลัง
    /// _liveViewFullscreenClientGuid = null) แทนการ +=/-= event handler จริง เพราะ handler ทุกตัวผูกแบบ
    /// static ไว้ใน XAML ครั้งเดียวอยู่แล้ว (ไม่ subscribe ใหม่ต่อ session) - ปลอดภัยกว่า (ไม่มีจุดไหนลืม
    /// unsubscribe ได้เลย) และให้ผลลัพธ์เดียวกันทุกประการ: หลังเรียกจุดนี้แล้ว event ทุกตัวที่ยิงมาจะเป็น
    /// no-op ทันทีเพราะ resolve handle ไม่ได้อีกต่อไป
    /// </summary>
    private void ReleaseFullscreenInputState()
    {
        if (FullscreenImage.IsMouseCaptured)
        {
            FullscreenImage.ReleaseMouseCapture();
        }

        if (_fullscreenMouseButtonDown is { } stuckButton &&
            _liveViewFullscreenClientGuid is not null &&
            _liveViewCells.TryGetValue(_liveViewFullscreenClientGuid, out var stuckCell) &&
            stuckCell.Handle is { } stuckHandle)
        {
            _ = stuckHandle.SendMouseButtonAsync(stuckButton, isDown: false, _fullscreenLastMouseX, _fullscreenLastMouseY);
        }

        _fullscreenMouseButtonDown = null;

        // ออกจาก fullscreen ต้องคืนสิทธิ์ physical input ให้นักเรียนเสมอ ไม่ว่าจะออกทางไหน (ทางเดียวกับ
        // path นี้ครอบคลุมอยู่แล้ว - BACK TO GRID/เอา cell ออก/ปิดแท็บ/ปิด Dashboard) fire-and-forget
        // เพราะเป็น cleanup ตอนกำลังจะออกจาก fullscreen แล้ว ไม่ควร block UI รอผลลัพธ์ - ยังมี failsafe
        // ฝั่ง AgentTray (VncProcessManager.Stopped ตอน RequestModeSwitch(Thumbnail) ไม่ได้ปิด VNC session
        // จริงๆ ก็จริง แต่ path หลักที่ปิด VNC session จริงคือ CloseAsync/CloseAllAsync ซึ่งเรียกหลัง
        // ReleaseFullscreenInputState เสมอ - ดู RemoveGridCellButton_Click/CloseAllLiveViewSessionsAsync)
        // เผื่อ command นี้ส่งไม่ถึงจริงๆ
        if (_targetInputBlocked && _liveViewFullscreenClientGuid is not null)
        {
            _ = _apiClient.SendCommandAsync(_liveViewFullscreenClientGuid, "UnblockTargetInput", null);
        }

        _targetInputBlocked = false;
        SetToggleTargetInputButtonVisual(blocked: false);
    }

    /// <summary>ปุ่ม X บน cell - เอาออกจาก grid + dispose session นั้นทิ้ง ไม่กระทบ session อื่นเลย (ตามที่ตกลงใน Step 4e)</summary>
    private async void RemoveGridCellButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string clientGuid })
        {
            return;
        }

        if (_liveViewFullscreenClientGuid == clientGuid)
        {
            BackToGridButton_Click(sender, e); // กำลังขยายเครื่องนี้อยู่พอดี - กลับ grid ก่อนเอาออก
        }

        if (_liveViewCells.Remove(clientGuid, out var cell))
        {
            LiveViewGrid.Children.Remove(cell.Border);
        }

        await _liveViewPool.CloseAsync(clientGuid);
        UpdateLiveViewStatusAndButtonState();
    }

    private void UpdateLiveViewStatusAndButtonState()
    {
        LiveViewStatusText.Text = $"เปิดอยู่ {_liveViewCells.Count}/{VncSessionPool.MaxConcurrentSessions} เครื่อง";
        AddSelectedToGridButton.IsEnabled = _liveViewCells.Count < VncSessionPool.MaxConcurrentSessions;
    }

    /// <summary>เรียกตอนออกจากแท็บ LIVE VIEW หรือปิด Dashboard - dispose ทุก session ใน grid ให้ครบ (ตามที่ตกลง) รวมถึงปิด full-screen overlay ถ้ากำลังเปิดอยู่พอดี</summary>
    private async Task CloseAllLiveViewSessionsAsync()
    {
        if (_liveViewFullscreenClientGuid is not null)
        {
            ReleaseFullscreenInputState();
            _liveViewFullscreenClientGuid = null;
            _liveViewFullscreenBitmap = null;
            _liveViewFullscreenBitmapMode = null;
            FullscreenImage.Source = null;
            LiveViewFullscreenOverlay.Visibility = Visibility.Collapsed;
        }

        LiveViewGrid.Children.Clear();
        _liveViewCells.Clear();

        await _liveViewPool.CloseAllAsync();
        UpdateLiveViewStatusAndButtonState();
    }

    // ==================== Step 4h: Live View full-screen input capture ====================
    // เมาส์/คีย์บอร์ดจริงของครูควบคุมเครื่อง target ที่กำลัง fullscreen อยู่ - handler ทุกตัวผูกแบบ static
    // ไว้ใน XAML ครั้งเดียว (ไม่ subscribe/unsubscribe ต่อ session) แล้ว resolve handle ปัจจุบันจาก
    // _liveViewFullscreenClientGuid ทุกครั้งที่ event ยิง (เหมือน GetFullscreenHandle ด้านล่าง) - ถ้า
    // clientGuid เป็น null หรือ session ไม่ Connected แล้ว handler กลายเป็น no-op ทันที (ดู
    // ReleaseFullscreenInputState สำหรับ cleanup ตอนออกจาก fullscreen)

    /// <summary>คืน handle ของเครื่องที่กำลัง fullscreen อยู่ตอนนี้ - null ถ้าไม่ได้อยู่ใน fullscreen mode หรือ session หลุดไปแล้ว (Connecting/Failed/Closed)</summary>
    private VncSessionHandle? GetFullscreenHandle() =>
        _liveViewFullscreenClientGuid is not null &&
        _liveViewCells.TryGetValue(_liveViewFullscreenClientGuid, out var cell) &&
        cell.Handle is { Status: VncSessionStatus.Connected } handle
            ? handle
            : null;

    /// <summary>
    /// แปลงพิกัดคลิก/ขยับเมาส์บน FullscreenImage (พิกัดจอ Console) กลับเป็นพิกัดจริงของ target -
    /// Stretch="Uniform" ทำให้ภาพถูกวาดแบบรักษาสัดส่วนอยู่กึ่งกลางของ Image control เสมอ (WPF default
    /// behavior) เกิด letterbox แถบดำถ้าอัตราส่วนไม่ตรงกัน (เช่น target 1920x1080 แต่พื้นที่แสดงผลเป็น
    /// สี่เหลี่ยมจัตุรัสกว่า) - ต้องคำนวณ offset ของ letterbox ด้วย ไม่ใช่แค่ scale เฉยๆ
    ///
    /// clampToImage=true: บังคับพิกัดให้อยู่ในขอบเขตภาพเสมอ (ไม่มีทาง return false) ใช้กับ MouseMove/MouseUp
    /// เพราะระหว่างลาก (drag) เมาส์อาจขยับ/ปล่อยออกนอกขอบภาพเล็กน้อยได้ (โดยเฉพาะเมื่อ capture ไว้ - ดู
    /// FullscreenImage_PreviewMouseDown) ต้องยังส่งพิกัดที่ clamp ไว้ที่ขอบแทนที่จะ drop event ทิ้งไปเฉยๆ
    /// (ไม่งั้น drag/scroll จะรู้สึกกระตุกหรือ drag ค้างไม่จบ)
    ///
    /// clampToImage=false: return false ถ้าคลิกอยู่ในแถบดำ (นอกภาพจริง) ใช้กับ MouseDown เท่านั้น - ไม่อยาก
    /// ให้คลิกที่แถบดำ (ซึ่งไม่ตรงกับตำแหน่งใดบน target เลย) กลายเป็นคลิกที่ขอบจอ target โดยไม่ตั้งใจ
    /// </summary>
    private bool TryMapFullscreenPointToTarget(Point consolePoint, VncSessionHandle handle, bool clampToImage, out ushort targetX, out ushort targetY)
    {
        targetX = 0;
        targetY = 0;

        var displayWidth = handle.DisplayWidth;
        var displayHeight = handle.DisplayHeight;
        if (displayWidth is not > 0 || displayHeight is not > 0)
        {
            return false; // ยังไม่รู้ resolution จริงของ target (ยังไม่ได้ ServerInit/ยังไม่ connected)
        }

        var sourceWidth = displayWidth.Value;
        var sourceHeight = displayHeight.Value;

        var containerWidth = FullscreenImage.ActualWidth;
        var containerHeight = FullscreenImage.ActualHeight;
        if (containerWidth <= 0 || containerHeight <= 0)
        {
            return false; // ยังไม่ผ่าน layout pass (เพิ่ง Visibility=Visible ไปหมาดๆ)
        }

        var scale = Math.Min(containerWidth / sourceWidth, containerHeight / sourceHeight);
        if (scale <= 0)
        {
            return false;
        }

        var renderedWidth = sourceWidth * scale;
        var renderedHeight = sourceHeight * scale;
        var offsetX = (containerWidth - renderedWidth) / 2;
        var offsetY = (containerHeight - renderedHeight) / 2;

        var relX = consolePoint.X - offsetX;
        var relY = consolePoint.Y - offsetY;

        if (!clampToImage && (relX < 0 || relY < 0 || relX >= renderedWidth || relY >= renderedHeight))
        {
            return false; // คลิกอยู่ในแถบ letterbox - ไม่ใช่ตำแหน่งใดบนภาพจริง (เฉพาะ MouseDown)
        }

        relX = Math.Clamp(relX, 0, renderedWidth - 1);
        relY = Math.Clamp(relY, 0, renderedHeight - 1);

        targetX = (ushort)Math.Clamp((int)Math.Round(relX / scale), 0, sourceWidth - 1);
        targetY = (ushort)Math.Clamp((int)Math.Round(relY / scale), 0, sourceHeight - 1);
        return true;
    }

    private static bool TryMapMouseButton(MouseButton wpfButton, out VncMouseButton vncButton)
    {
        switch (wpfButton)
        {
            case MouseButton.Left:
                vncButton = VncMouseButton.Left;
                return true;
            case MouseButton.Middle:
                vncButton = VncMouseButton.Middle;
                return true;
            case MouseButton.Right:
                vncButton = VncMouseButton.Right;
                return true;
            default:
                // XButton1/XButton2 (ปุ่มเสริม back/forward) - RFC 6143 §7.5.5 นิยาม button 1-5 ชัดเจน
                // (left/middle/right/wheel-up/wheel-down) เท่านั้น ไม่มีความหมายมาตรฐานสำหรับ bit 5-7 เลย
                // (แค่บอกว่า "button 1 to 8" มีที่ทางให้ใช้ได้ แต่ไม่ได้นิยามว่า target ตีความยังไง) - ไม่
                // implement ต่อเพราะจะเป็นการเดา ไม่ใช่ทำตาม spec ที่ verify แล้วจริง
                vncButton = default;
                return false;
        }
    }

    private void FullscreenImage_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        var handle = GetFullscreenHandle();
        if (handle is null)
        {
            return;
        }

        var now = DateTime.UtcNow;
        if (now - _fullscreenLastMouseMoveSentAt < FullscreenMouseMoveMinInterval)
        {
            return;
        }

        if (!TryMapFullscreenPointToTarget(e.GetPosition(FullscreenImage), handle, clampToImage: true, out var x, out var y))
        {
            return;
        }

        _fullscreenLastMouseMoveSentAt = now;
        _fullscreenLastMouseX = x;
        _fullscreenLastMouseY = y;
        _ = handle.SendMouseMoveAsync(x, y);
    }

    /// <summary>Mouse.Capture ไว้ตั้งแต่ MouseDown เพื่อให้ยังได้รับ MouseMove/MouseUp ต่อเนื่องแม้เมาส์ขยับออกนอกขอบเขต FullscreenImage ระหว่างลาก (drag) - ตรงกับพฤติกรรม drag ปกติของ Windows</summary>
    private void FullscreenImage_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        var handle = GetFullscreenHandle();
        if (handle is null)
        {
            return;
        }

        if (!TryMapMouseButton(e.ChangedButton, out var button))
        {
            return;
        }

        if (!TryMapFullscreenPointToTarget(e.GetPosition(FullscreenImage), handle, clampToImage: false, out var x, out var y))
        {
            return; // คลิกในแถบ letterbox - ไม่ส่งอะไรไป target เลย
        }

        Keyboard.Focus(LiveViewFullscreenOverlay); // Image ไม่ได้ถือ keyboard focus เอง (Focusable=false ปกติของ Image) - คืน focus ให้ overlay ที่รับ key event จริง เผื่อโดนคลิกแล้วหลุดไปจาก popup อื่นก่อนหน้า
        Mouse.Capture(FullscreenImage);
        e.Handled = true;

        _fullscreenMouseButtonDown = button;
        _fullscreenLastMouseX = x;
        _fullscreenLastMouseY = y;
        _ = handle.SendMouseButtonAsync(button, isDown: true, x, y);
    }

    private void FullscreenImage_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        var handle = GetFullscreenHandle();
        if (handle is null)
        {
            return;
        }

        if (!TryMapMouseButton(e.ChangedButton, out var button))
        {
            return;
        }

        // clampToImage=true เสมอตรงนี้ (ต่างจาก MouseDown) - ต้องปล่อยปุ่มให้สำเร็จเสมอไม่ว่าตำแหน่งปล่อย
        // จะอยู่ตรงไหน (แม้หลุดนอกภาพเล็กน้อยระหว่างลาก) ไม่งั้นปุ่มจะค้างเป็น stuck mouse-down บน target
        TryMapFullscreenPointToTarget(e.GetPosition(FullscreenImage), handle, clampToImage: true, out var x, out var y);

        Mouse.Capture(null);
        e.Handled = true;

        _fullscreenMouseButtonDown = null;
        _fullscreenLastMouseX = x;
        _fullscreenLastMouseY = y;
        _ = handle.SendMouseButtonAsync(button, isDown: false, x, y);
    }

    /// <summary>Scroll wheel - RFC 6143 §7.5.5 core spec เอง ไม่ใช่ extension (verify แล้ว - ดู comment ที่ VncMouseButton.WheelUp/WheelDown) ส่งเป็น click (กด+ปล่อยทันที) ซ้ำตามจำนวน notch ที่ขยับจริง ไม่ throttle (wheel ไม่ถี่เท่า MouseMove และการ drop notch จะทำให้ scroll ระยะไม่ตรง)</summary>
    private void FullscreenImage_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var handle = GetFullscreenHandle();
        if (handle is null)
        {
            return;
        }

        if (!TryMapFullscreenPointToTarget(e.GetPosition(FullscreenImage), handle, clampToImage: true, out var x, out var y))
        {
            return;
        }

        e.Handled = true;
        var notches = Math.Max(1, Math.Abs(e.Delta) / WheelDeltaPerNotch);
        var button = e.Delta > 0 ? VncMouseButton.WheelUp : VncMouseButton.WheelDown;

        _ = ForwardWheelNotchesAsync(handle, button, x, y, notches);
    }

    private static async Task ForwardWheelNotchesAsync(VncSessionHandle handle, VncMouseButton button, ushort x, ushort y, int notches)
    {
        for (var i = 0; i < notches; i++)
        {
            await handle.SendMouseClickAsync(button, x, y);
        }
    }

    /// <summary>
    /// popup/tooltip/element อื่นแย่ง keyboard focus ไประหว่างอยู่ใน fullscreen mode (เช่น ContextMenu ค้าง
    /// จาก SettingsMenuButton แวบมาโผล่พอดี) - ดึง focus กลับมาที่ overlay ทันทีที่มันหลุดไป ตราบใดที่ยัง
    /// อยู่ใน fullscreen mode จริง (เช็ค _liveViewFullscreenClientGuid ไม่ใช่แค่ IsVisible เพราะตอนกำลังจะ
    /// ปิด overlay เอง IsKeyboardFocusWithin ก็จะเปลี่ยนเป็น false ด้วยเหมือนกัน ต้องแยกให้ออกจากกรณีนั้น)
    /// </summary>
    private void LiveViewFullscreenOverlay_IsKeyboardFocusWithinChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_liveViewFullscreenClientGuid is not null && !LiveViewFullscreenOverlay.IsKeyboardFocusWithin)
        {
            Dispatcher.InvokeAsync(() => Keyboard.Focus(LiveViewFullscreenOverlay), DispatcherPriority.Input);
        }
    }

    private void LiveViewFullscreenOverlay_PreviewKeyDown(object sender, KeyEventArgs e) => ForwardFullscreenKeyEvent(e, isDown: true);
    private void LiveViewFullscreenOverlay_PreviewKeyUp(object sender, KeyEventArgs e) => ForwardFullscreenKeyEvent(e, isDown: false);

    /// <summary>
    /// e.Handled=true ตั้งไว้ก่อนเช็คอะไรทั้งหมด (แม้ handle จะเป็น null หรือ key ไม่มี mapping ก็ตาม) -
    /// สำคัญมาก: ต้องกัน WPF/Windows ประมวลผล key นี้เอง "ทุกกรณี" ไม่ใช่แค่กรณีที่ส่งสำเร็จ ไม่งั้น Tab จะ
    /// ขยับ focus ใน Console เอง, Escape อาจปิด dialog ผิดตัว ฯลฯ ระหว่างที่ตั้งใจจะพิมพ์/กดปุ่มบน target
    ///
    /// key == Key.System (WPF จัดกลุ่มนี้ให้ทุก key ที่กดพร้อม Alt ค้างอยู่ รวม Alt+F4 เอง) ต้องอ่านจาก
    /// e.SystemKey แทน e.Key ตรงๆ (ดู pattern เดียวกันใน VncPoolTestWindow.KeyTestBox_PreviewKey - Step 4g)
    ///
    /// **หมายเหตุสำคัญเรื่อง Alt+Tab/ปุ่ม Windows**: e.Handled=true ตรงนี้ป้องกันไม่ให้ WPF/Console เอง
    /// ประมวลผล key เหล่านี้ก็จริง แต่ "ไม่สามารถ" กัน Windows shell (Explorer) ไม่ให้แสดง task
    /// switcher/Start menu ของมันเองได้ - Alt+Tab/ปุ่ม Windows เป็น global hotkey ที่ shell ดักจับที่ระดับ
    /// ต่ำกว่า foreground application ใดๆ ทั้งสิ้น (คนละกลไกกับ routed keyboard event ของ WPF) ไม่มี
    /// WPF-level API ใดกันได้ ต้องใช้ SetWindowsHookEx(WH_KEYBOARD_LL) แบบ system-wide เท่านั้น - **ไม่ได้
    /// implement เพราะเป็นการยกระดับ scope ใหญ่มาก (แทรกแซงทั้งเครื่องครู ไม่ใช่แค่ apply กับ Console)
    /// ต้องถามยืนยันก่อนถ้าต้องการจริงๆ** ต่างจาก Alt+F4 ที่กันได้แน่นอนด้วยวิธีอื่นแล้ว (ดู Window.Closing
    /// ใน constructor) เพราะ Alt+F4 แค่ทำให้ "Console เองปิด" (แก้ที่ Window.Closing ได้ตรงจุด) แต่
    /// Alt+Tab/Windows key แค่ทำให้ "shell UI ของครูโผล่ขึ้นมาชั่วคราว" (ไม่มีผลเสียร้ายแรงเท่า - keysym ยัง
    /// ถูกส่งไป target ตามปกติเหมือนเดิมไม่ว่ากรณีไหน เพียงแต่โผล่ UI ฝั่งครูมาด้วยเฉยๆ)
    /// </summary>
    private void ForwardFullscreenKeyEvent(KeyEventArgs e, bool isDown)
    {
        e.Handled = true;

        var handle = GetFullscreenHandle();
        if (handle is null)
        {
            return;
        }

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var vkCode = KeyInterop.VirtualKeyFromKey(key);

        try
        {
            _ = handle.SendKeyPressAsync(vkCode, isDown);
            FullscreenStatusText.Text = "";
        }
        catch (NotSupportedException ex)
        {
            FullscreenStatusText.Text = ex.Message;
        }
    }

    private void NewUserUsernameBox_TextChanged(object sender, TextChangedEventArgs e)
    {

    }
}
