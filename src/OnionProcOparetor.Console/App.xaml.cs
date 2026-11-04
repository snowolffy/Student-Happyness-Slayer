using System.Windows;
using System.Windows.Input;
using OnionProcOparetor.Console.Services;
using OnionProcOparetor.UI.Services;

namespace OnionProcOparetor.Console;

public partial class App : Application
{
    /// <summary>
    /// log บรรทัดแรกสุดของทุก run - เพิ่มไว้เพื่อ diagnose โดยเฉพาะว่า instance ที่กำลังทดสอบอยู่จริง
    /// เป็น build ล่าสุดที่ deploy ไปหรือเปล่า (ProcessPath บอก path ที่รันอยู่จริง, FileVersion บอกว่า
    /// ตรงกับ build ล่าสุดไหม) - console.log ไม่มี entry ใหม่เลยหลายรอบก่อนหน้านี้ทั้งที่ควรมี ทำให้สงสัยว่า
    /// instance ที่ทดสอบอยู่อาจไม่ใช่ build ที่เพิ่ง compile/deploy จริง
    ///
    /// ใช้ FileVersion (ไม่ใช่ AssemblyVersion เดิม) เพราะ AssemblyVersion ตั้งใจคงที่ตาม
    /// Directory.Build.props เสมอแล้ว (กัน binding-redirect) - FileVersion ต่างหากที่เปลี่ยนทุกรอบ
    /// publish-all.ps1 จริง (ดู comment ที่นั่นสำหรับที่มาของบั๊กที่แก้)
    /// </summary>
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var fileVersion = System.Diagnostics.FileVersionInfo.GetVersionInfo(Environment.ProcessPath!).FileVersion;
        ConsoleDiagnostics.WriteLine($"=== Console เริ่มทำงาน === FileVersion: {fileVersion}, ProcessPath: {Environment.ProcessPath}, PID: {Environment.ProcessId}");

        // โหลด console-settings.json (หรือ default ถ้ายังไม่มี) แล้ว apply ค่า VNC limit ให้ทันก่อนหน้าต่างไหนเปิดขึ้นมา
        SettingsService.EnsureLoaded();

        // ต้องอยู่ *หลัง* EnsureLoaded (อ่านค่าธีมที่ผู้ใช้เลือกไว้จาก settings) และ *ก่อน* หน้าต่างแรกถูกสร้าง
        // (StartupUri ทำงานหลัง OnStartup) ไม่งั้น MainWindow จะวาดด้วยธีมสว่างแล้วค่อยกระพริบเปลี่ยนเป็นมืด
        //
        // ไม่ส่ง accentLightUri/accentDarkUri ให้ - Console ใช้สีเน้นส้ม default ของไลบรารีตรงๆ
        // (WP UI-1a: "Console ใช้สีส้มเดิม หน้าตาต้องไม่เปลี่ยนเลย")
        ThemeService.Initialize(
            loadSavedTheme: () => SettingsService.Current.Ui.Theme,
            saveTheme: theme =>
            {
                var settings = SettingsService.Current;
                settings.Ui.Theme = theme.ToString();
                var (success, error) = SettingsService.Save(settings);
                if (!success)
                {
                    // จำค่าไม่ได้ไม่ใช่เหตุให้ธีมที่เพิ่งสลับไปแล้วต้องเด้งกลับ - แค่บันทึกไว้ว่าครั้งหน้าจะไม่จำ
                    ConsoleDiagnostics.WriteLine($"ThemeService: บันทึกธีมลง settings ไม่สำเร็จ ({error}) - ธีมยังเปลี่ยนแล้วในรอบนี้");
                }
            },
            log: ConsoleDiagnostics.WriteLine);

        // บั๊กจริงที่เจอซ้ำ 2 ครั้ง (2026-08-25): "VIEW RUNNING PROCESSES..." ทำให้ทั้ง Console หายไปเงียบๆ
        // ทันทีที่กด - รอบแรกเข้าใจผิดว่าอยู่ใน RunningProcessesWindow เอง (ครอบ try/catch ไปแล้ว) แต่ crash
        // จริงอยู่ที่ตอน "เปิด" หน้าต่าง (DashboardWindow.RunActionPanelAsync ที่เรียกจาก
        // ClientActionsButton_Click ซึ่งเป็น async void ไม่มี try/catch เหมือนกัน) - ยืนยันว่า pattern นี้
        // (async void event handler ไม่มี try/catch + ไม่มี global handler กันไว้เลย) เป็นความเสี่ยงทั่วทั้ง
        // แอป ไม่ใช่จุดเดียว แก้แบบเจาะจุดต่อไปเรื่อยๆ ไม่จบ - เพิ่ม global handler ตรงนี้แทน กัน exception
        // ที่หลุดจาก event handler ไหนก็ตามทำให้ทั้ง process ตาย เขียน log เต็มๆ ไว้ที่ console.log (มี path
        // เดียวกับที่ ConsoleDiagnostics เขียน startup diagnostic อยู่แล้ว) แทนที่จะหายไปเงียบๆ แบบเดิม
        DispatcherUnhandledException += (_, args) =>
        {
            ConsoleDiagnostics.WriteLine(
                $"UNHANDLED EXCEPTION: {args.Exception.GetType().FullName}: {args.Exception.Message}\n{args.Exception.StackTrace}\n" +
                (args.Exception.InnerException is not null
                    ? $"Inner: {args.Exception.InnerException.GetType().FullName}: {args.Exception.InnerException.Message}\n{args.Exception.InnerException.StackTrace}\n"
                    : string.Empty));

            MessageBox.Show(
                $"เกิดข้อผิดพลาดที่ไม่คาดคิด:\n\n{args.Exception.Message}\n\nรายละเอียดเต็มถูกบันทึกไว้ที่:\n{ConsoleDiagnostics.LogPath}",
                "เกิดข้อผิดพลาด", MessageBoxButton.OK, MessageBoxImage.Error);

            // สำคัญ: บอก WPF ว่า "จัดการแล้ว" กัน process ตายทั้งตัว - โปรแกรมยังทำงานต่อได้ปกติ
            // (หน้าต่างที่กำลังเปิดอยู่ตอนเกิด exception อาจอยู่ในสถานะไม่สมบูรณ์ แต่ดีกว่าทั้งแอปหายไปเงียบๆ)
            args.Handled = true;
        };

#if DEBUG
        // --theme=light|dark บังคับธีมสำหรับการถ่ายภาพตรวจ โดยไม่บันทึกลง console-settings.json
        // (ไฟล์นั้นใช้ร่วมกับ Console ตัวจริงบนเครื่องนี้) - ต้องมาก่อนสร้างหน้าต่างใด ๆ
        var themeArg = e.Args.FirstOrDefault(a => a.StartsWith("--theme=", StringComparison.Ordinal));
        if (themeArg is not null
            && Enum.TryParse<AppTheme>(themeArg["--theme=".Length..], ignoreCase: true, out var forcedTheme))
        {
            ThemeService.PreviewOverride(forcedTheme);
        }


        // เครื่องมือสำหรับตรวจงาน UI เท่านั้น และมีเฉพาะใน build แบบ Debug (installer/publish-all.ps1
        // publish ด้วย -c Release เสมอ ปุ่มนี้จึงไม่มีอยู่จริงในของที่ส่งขึ้นเครื่องครู)
        //
        // มีไว้เพราะเครื่อง dev เครื่องนี้รัน Server ตัวจริงอยู่ด้วย การจะดู Dashboard สักทีจึงต้อง
        // login เข้าเซิร์ฟเวอร์ของจริง ซึ่งไม่ควรทำแค่เพื่อดูว่าปุ่มจัดตำแหน่งตรงไหม - ทางนี้เปิด
        // Dashboard ด้วย ApiClient ที่ยังไม่ได้ login ทุก API call จึงล้มเหลวหมด (ตั้งใจ) เห็นแต่โครง UI
        // --center-test พิสูจน์ว่าหน้าต่างย่อยวางกึ่งกลางหน้าต่างหลักจริง - เป็นสิ่งเดียวที่ภาพนิ่งพิสูจน์
        // ไม่ได้ เพราะ RenderTargetBitmap วาด visual tree ของหน้าต่างเดียว ไม่เห็นตำแหน่งสัมพัทธ์ของสองบาน
        // จึงวัดเป็นตัวเลขแล้วเขียนลง console.log แทน
        if (e.Args.Contains("--center-test"))
        {
            var ownerContent = new DashboardWindow(new Services.ApiClient()) { PreviewMode = true };
            var owner = CreateDashboardPreviewHost(ownerContent);
            owner.Show();

            owner.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, () =>
            {
                var child = ConfirmWindow.CreatePreview(typeToConfirm: false);
                child.Owner = owner;
                child.Show();

                child.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, () =>
                {
                    var ownerCentreX = owner.Left + owner.ActualWidth / 2;
                    var ownerCentreY = owner.Top + owner.ActualHeight / 2;
                    var childCentreX = child.Left + child.ActualWidth / 2;
                    var childCentreY = child.Top + child.ActualHeight / 2;

                    ConsoleDiagnostics.WriteLine(
                        "CENTER-TEST "
                        + $"owner=({owner.Left:F0},{owner.Top:F0} {owner.ActualWidth:F0}x{owner.ActualHeight:F0}) "
                        + $"child=({child.Left:F0},{child.Top:F0} {child.ActualWidth:F0}x{child.ActualHeight:F0}) "
                        + $"offset=({childCentreX - ownerCentreX:F1},{childCentreY - ownerCentreY:F1})");

                    Shutdown();
                });
            });
            return;
        }

        // --focus-test เข้าโหมดดูจอเต็มจอ แล้วเปิดหน้าต่างทับ เพื่อพิสูจน์ว่าหน้าต่างนั้น "ใช้งานได้จริง"
        // overlay เต็มจอเคยดึง keyboard focus กลับมาหาตัวเองทุกครั้งที่ focus หลุด ทำให้หน้าต่างที่เปิดทับ
        // กดปิดไม่ได้ - เช็คด้วยตาไม่ได้ ต้องอ่านว่าใครถือ focus อยู่จริง
        if (e.Args.Contains("--focus-test"))
        {
            var ownerContent = new DashboardWindow(new Services.ApiClient())
            {
                PreviewMode = true,
                PreviewFullscreen = true
            };
            var owner = CreateDashboardPreviewHost(ownerContent);
            owner.Show();

            owner.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, () =>
            {
                var child = ConfirmWindow.CreatePreview(typeToConfirm: false);
                child.Owner = owner;
                child.Show();
                child.Activate();

                child.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, () =>
                {
                    var focused = Keyboard.FocusedElement?.GetType().Name ?? "(none)";
                    ConsoleDiagnostics.WriteLine(
                        $"FOCUS-TEST childIsActive={child.IsActive} ownerIsActive={owner.IsActive} "
                        + $"focusedElement={focused}");
                    Shutdown();
                });
            });
            return;
        }

        // --dialog=<ชื่อ> เปิดหน้าต่างย่อยขึ้นมาถ่ายภาพตรวจ chrome ที่เราวาดเอง (แถบหัว/ปุ่มปิด/
        // การคำนวณความสูงแบบ SizeToContent ซึ่งเป็นจุดที่ Window template ของตัวเองพังได้ง่ายสุด)
        var dialogArg = e.Args.FirstOrDefault(a => a.StartsWith("--dialog=", StringComparison.Ordinal));
        if (dialogArg is not null)
        {
            var api = new Services.ApiClient();
            var sampleClient = new Models.ClientMachineDto
            {
                Id = 1,
                ClientGuid = "a1b2c3d4-preview",
                MachineName = "LAB-PC-01",
                LastKnownIp = "192.168.1.11",
                IsOnline = true,
                IsEnabled = true,
                LastSeenAt = DateTime.UtcNow
            };

            // UI-3b (2026-11-03): BroadcastMessageWindow/AddEditRuleWindow/ChangeMyPasswordWindow/
            // ResetPasswordWindow/EditDisplayNameWindow/ChangeUserRoleWindow/ExamModeWindow/
            // OpenUrlWindow/ScreenBroadcastSetupWindow กลายเป็น UserControl ที่ฝังใน DialogHost จริงของ
            // MainWindow แล้ว - harness นี้แค่ต้องการถ่ายภาพ chrome เฉยๆ เลยห่อด้วย Window เปล่าแบบ
            // เดียวกับที่ "force-password" ทำกับ ForceChangePasswordWindow (UI-3a) ไม่ต้องพึ่ง
            // DialogHost จริงสำหรับภาพนิ่งแบบนี้
            Window WrapAsDialog(string title, FrameworkElement content) => new()
            {
                Title = title,
                SizeToContent = SizeToContent.WidthAndHeight,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                ResizeMode = ResizeMode.NoResize,
                Style = (Style)Current.FindResource("AppDialogWindow"),
                Content = content,
            };

            Window? dialog = dialogArg["--dialog=".Length..] switch
            {
                // หน้า login เป็นหน้าต่างแรกที่ครูเห็น จึงต้องตรวจเหมือนหน้าต่างอื่น - StartupUri จะ
                // เปิดอีกบานตามมาหลัง OnStartup คืนค่า แต่ไม่เป็นไรเพราะเราถ่ายภาพบานที่สร้างเองแล้วปิด
                // โปรแกรมทันที
                "login" => new MainWindow(),
                "confirm" => ConfirmWindow.CreatePreview(typeToConfirm: false),
                "confirm-type" => ConfirmWindow.CreatePreview(typeToConfirm: true),
                "about" => new AboutWindow(),
                "broadcast" => WrapAsDialog("Broadcast Message", new BroadcastMessageWindow(3)),
                "add-rule" => WrapAsDialog("Rule", new AddEditRuleWindow(api)),
                "change-password" => WrapAsDialog("Onion ProcOparetor Console - เปลี่ยน Password", new ChangeMyPasswordWindow(api)),
                // UI-3a (2026-11-03): ForceChangePasswordWindow กลายเป็น UserControl แล้ว (ฝังใน
                // DialogHost จริงของ MainWindow) - harness นี้แค่ต้องการถ่ายภาพ chrome เฉยๆ เลยห่อด้วย
                // Window เปล่าแบบเดียวกับที่ ConfirmWindow.xaml ทำกับ ConfirmDialogContent ไม่ต้องพึ่ง
                // DialogHost จริงสำหรับภาพนิ่งแบบนี้
                "force-password" => new Window
                {
                    Title = "Onion ProcOparetor Console - เปลี่ยน Password",
                    SizeToContent = SizeToContent.WidthAndHeight,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen,
                    ResizeMode = ResizeMode.NoResize,
                    Style = (Style)Current.FindResource("AppDialogWindow"),
                    Content = new ForceChangePasswordWindow(api) { Margin = new Thickness(24) },
                },
                "reset-password" => WrapAsDialog("Reset Password", new ResetPasswordWindow("teacher01")),
                // WP4b (2026-10-30)
                "edit-display-name" => WrapAsDialog("แก้ชื่อที่แสดง", new EditDisplayNameWindow("teacher01", "ครูสมชาย")),
                "change-role" => WrapAsDialog("เปลี่ยน Role", new ChangeUserRoleWindow("teacher01", "Teacher")),
                // WP1b (2026-10-30) - thumbprint/subject/issuer ปลอมล้วนๆ แค่ไว้ถ่ายภาพ chrome ของ
                // หน้าต่าง ไม่ได้ต่อ server จริง
                "trust-cert" => new TrustCertificateWindow(
                    "192.168.17.249", 8843,
                    new Services.ServerCertificatePinning.SeenCertificate(
                        "A1B2C3D4E5F60718293A4B5C6D7E8F9001020304", $"CN={Environment.MachineName}", $"CN={Environment.MachineName}",
                        DateTime.UtcNow.AddYears(10)),
                    isChanged: false),
                "trust-cert-changed" => new TrustCertificateWindow(
                    "192.168.17.249", 8843,
                    new Services.ServerCertificatePinning.SeenCertificate(
                        "FFEEDDCCBBAA99887766554433221100FFEEDD", $"CN={Environment.MachineName}", $"CN={Environment.MachineName}",
                        DateTime.UtcNow.AddYears(10)),
                    isChanged: true, previousThumbprint: "A1B2C3D4E5F60718293A4B5C6D7E8F9001020304"),
                "tailscale" => new TailscaleLoginWindow(),
                // UI-3c (2026-11-03): 9 ตัวข้างล่างกลายเป็น UserControl แล้ว (ฝังใน SidePanelHost/
                // DialogHost จริงของ MainWindow ตอนใช้งานจริง) - harness นี้แค่ต้องการถ่ายภาพ chrome
                // เฉยๆ เลยห่อด้วย Window เปล่าแบบเดียวกับที่ "force-password" ทำ (ดู comment ด้านบน)
                // ไม่ต้องพึ่ง SidePanelHost/DialogHost จริงสำหรับภาพนิ่งแบบนี้
                "client-settings" => WrapAsPreviewWindow(new ClientSettingsWindow(api, sampleClient)),
                "rule-overrides" => WrapAsPreviewWindow(new RuleOverridesWindow(api, sampleClient)),
                // WP2-2 (2026-10-31) - โหลดชุด Allow-list จาก Server จริง (ApiClient ยังไม่ login ใน
                // โหมดนี้) แต่ LoadPresetsAsync จับ exception ไว้แล้วไม่พัง (ดู ExamModeWindow.xaml.cs)
                "exam-mode" => WrapAsDialog("เปิดโหมดสอบ", new ExamModeWindow(
                    api, [sampleClient], [new Models.RoomDto { Id = 1, Name = "ห้องคอม 1", ClientCount = 40 }], currentRoomFilterId: 1)),
                // WP4-1 (2026-11-01) - เหมือน exam-mode เป๊ะๆ (ดู comment ด้านบน): โหลดรายการโปรแกรมจาก
                // Server จริงไม่สำเร็จเพราะ ApiClient ยังไม่ login แต่ LoadProgramsAsync จับ exception
                // ไว้แล้ว ไม่พัง (ดู OpenUrlWindow.xaml.cs)
                "open-url" => WrapAsDialog("เปิดให้ทั้งห้อง", new OpenUrlWindow(
                    api, [sampleClient], [sampleClient], [new Models.RoomDto { Id = 1, Name = "ห้องคอม 1", ClientCount = 40 }], currentRoomFilterId: 1)),
                // WP4-3 (2026-11-01) - เหมือน open-url เป๊ะๆ (ดู comment ด้านบน): ApiClient ยังไม่ login
                // แต่ GetFileTransferSettingsAsync จับ exception ไว้แล้ว ไม่พัง (คืน default 100 MB)
                "files" => WrapAsPreviewWindow(new FilesWindow(
                    api, labHubClient: null, [sampleClient], [sampleClient], [new Models.RoomDto { Id = 1, Name = "ห้องคอม 1", ClientCount = 40 }], currentRoomFilterId: 1)),
                "launchable-programs" => WrapAsPreviewWindow(new LaunchableProgramsWindow(api)),
                "website-catalog-sources" => WrapAsPreviewWindow(new WebsiteCatalogSourcesWindow(api)),
                // ActionReportRequest ปลอม - แค่ไว้ถ่ายภาพ chrome/ตาราง ไม่ส่งคำสั่งจริง (SendAsync ไม่
                // ถูกเรียกเพราะ harness ปิดก่อนที่ Loaded จะยิง SendAllAsync จริง - ยังพอให้เห็นหน้าได้)
                "action-report" => WrapAsPreviewWindow(new ActionReportWindow(api, labHubClient: null, new Models.ActionReportRequest
                {
                    ActionLabel = "Restart",
                    BatchId = "preview",
                    SendAsync = _ => Task.FromResult<(bool, string?)>((true, null)),
                    Rows = [new Models.ActionReportRow { ClientGuid = sampleClient.ClientGuid, MachineName = sampleClient.MachineName }]
                })),
                "pending-commands" => WrapAsPreviewWindow(new PendingCommandsWindow(api, sampleClient)),
                "running-processes" => WrapAsPreviewWindow(new RunningProcessesWindow(api, new Services.LabHubClient(), sampleClient)),
                "action-panel" => WrapAsPreviewWindow(new ActionPanelWindow(api, sampleClient)),
                // โหมด bulk โดย "ไม่ได้เลือกเครื่องเลย" - เคสที่เพิ่งแก้ (2026-09-10) ต้องเห็นว่า
                // ตัวเลือก "เครื่องที่เลือกไว้ (0 เครื่อง)" ถูกปิด และ "ทุกเครื่อง" ถูกเลือกไว้ให้แล้ว
                "action-panel-room" => WrapAsPreviewWindow(new ActionPanelWindow(
                    api, [], [sampleClient, sampleClient, sampleClient], defaultToAll: true)),
                // "ส่งจอครู" (WP4-4, 2026-11-01) - เหมือน open-url/exam-mode: เรียก API จริงไม่สำเร็จ
                // เพราะ ApiClient ยังไม่ login แต่ไม่พัง (ไม่มี async load ตอนเปิดหน้าต่างเหมือนสองอันนั้น
                // อยู่แล้ว - โหลดแค่ room/client ที่ส่งเข้ามาตรงๆ)
                "screen-broadcast-setup" => WrapAsDialog("ส่งจอครู", new ScreenBroadcastSetupWindow(
                    api, [sampleClient], [sampleClient], [new Models.RoomDto { Id = 1, Name = "ห้องคอม 1", ClientCount = 40 }], currentRoomFilterId: 1)),
                // แถบแดง (ขั้นตอน 2) - session ปลอมไม่มีเครื่องจริงให้ส่ง StopScreenBroadcast ถึง (ไม่
                // เป็นไร - clientGuid ปลอมแค่ได้ 404/timeout จาก Server ปลอม ไม่ throw) Loaded จะเริ่ม
                // capture จอจริงของเครื่อง dev ตามปกติ แต่ไม่กระทบภาพที่ถ่าย (ถ่ายแค่ chrome ของแถบแดงเอง)
                "broadcast-warning-bar" or "broadcast-warning-bar-collapsed" => new BroadcastWarningBarWindow(
                    api, new Services.LabHubClient(),
                    new ScreenBroadcastSession
                    {
                        Targets = Enumerable.Repeat(sampleClient, 36).ToList(), // 34 สำเร็จ + 2 ไม่สำเร็จ ตามตัวอย่างใน wireframe tc-broadcast.dc.html
                        ViewerMode = "FullscreenLocked",
                        TeacherDisplayName = "สมชาย ใจดี",
                        SuccessCount = 34,
                        FailCount = 2
                    }),
                // คำขอเพิ่ม 3 พ.ย. 2569 - กรอบแดงรอบจอครู ถ่ายภาพได้ตรงๆ (ไม่ต้องมี session/API จริง
                // เลย เพราะไม่พึ่ง ApiClient/LabHubClient อะไรทั้งนั้น)
                "teacher-broadcast-border" => new TeacherBroadcastBorderWindow(),
                // แผงเครื่องมือเขียน - สร้าง canvas เปล่าไว้ให้ toolbar อ้างถึง (ไม่โชว์ canvas เองในภาพนี้
                // เพราะพื้นหลัง transparent ทั้งจอไม่มีอะไรให้ดูต่างจากพื้นหลัง - ถ่ายแค่ toolbar)
                "teacher-draw-toolbar" => new TeacherDrawToolbarWindow(new TeacherDrawCanvasWindow()),
                _ => null
            };

            // ชื่อ dialog ผิดต้อง "จบเงียบ ๆ" ไม่ใช่ throw - เพราะ throw จะไปโดน global handler ที่เด้ง
            // MessageBox ค้างรอให้คนมากดปิด ซึ่งสำหรับเครื่องมือที่สคริปต์เรียกวนเป็นสิบรอบ แปลว่ามี
            // หน้าต่างค้างเต็มจอผู้ใช้ (เจอมาแล้วจริง ๆ ตอนสคริปต์ส่ง argument ติดกันจนชื่อเพี้ยน)
            if (dialog is null)
            {
                ConsoleDiagnostics.WriteLine($"--dialog: ไม่รู้จักชื่อ '{dialogArg}' - ปิดโปรแกรม");
                Shutdown(2);
                return;
            }

            // คำขอเพิ่ม 3 พ.ย. 2569 - "broadcast-warning-bar-collapsed" ถ่ายภาพสถานะหุบ (ปุ่มกลม) แทนแถบเต็ม
            if (dialog is BroadcastWarningBarWindow warningBar && dialogArg == "--dialog=broadcast-warning-bar-collapsed")
            {
                warningBar.PreviewStartCollapsed = true;
            }

            // ถ่ายภาพอย่างเดียว: อย่าแย่ง focus และอย่าโผล่กลางจอที่ผู้ใช้กำลังทำงานอยู่
            // RenderTargetBitmap ต้องการหน้าต่างที่ realize แล้วก็จริง แต่ไม่ต้องให้คนเห็นด้วยตา
            if (e.Args.Any(a => a.StartsWith("--shot=", StringComparison.Ordinal)))
            {
                dialog.ShowActivated = false;
                dialog.WindowStartupLocation = WindowStartupLocation.Manual;
                dialog.Left = -32000;
                dialog.Top = -32000;
            }
            else
            {
                dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }

            dialog.Show();
            CapturePreview(dialog, e.Args);
            return;
        }

        if (e.Args.Contains("--ui-preview"))
        {
            var preview = new DashboardWindow(new Services.ApiClient())
            {
                PreviewMode = true,
                PreviewSelectFirst = e.Args.Contains("--select"),
                PreviewGlobalOff = e.Args.Contains("--global-off"),
                PreviewFullscreen = e.Args.Contains("--fullscreen"),
                PreviewToasts = e.Args.Contains("--toasts"),
                PreviewAnnouncements = e.Args.Contains("--announce"),
                PreviewInspectorCollapsed = e.Args.Contains("--inspector-collapsed"),
                PreviewSidebarExpanded = e.Args.Contains("--sidebar-expanded"),
                // WP4b (2026-10-30)
                PreviewUsers = e.Args.Contains("--users-sample"),
                PreviewAsTeacher = e.Args.Contains("--as-teacher"),
                // WP4-2 (2026-11-01)
                PreviewRaiseHand = e.Args.Contains("--raisehand-sample")
            };

            // --tab=<ชื่อแท็บ> เลือกแท็บก่อนถ่ายภาพ ใช้ไล่ตรวจทุกหน้าจอในโหมดมืดรอบเดียว
            var tab = e.Args.FirstOrDefault(a => a.StartsWith("--tab=", StringComparison.Ordinal));
            if (tab is not null)
            {
                preview.PreviewTab = tab["--tab=".Length..];
            }

            // --subview=<ชื่อ> (orchestrator review 2026-11-04) - เปิด sub-view ทับ ClientInspector ใน SidePanel
            var subView = e.Args.FirstOrDefault(a => a.StartsWith("--subview=", StringComparison.Ordinal));
            if (subView is not null)
            {
                preview.PreviewSubView = subView["--subview=".Length..];
            }

            // --dialog-preview=<ชื่อ> (orchestrator review 2026-11-04) - เปิด dialog ผ่าน DialogHost จริง
            var dialogPreview = e.Args.FirstOrDefault(a => a.StartsWith("--dialog-preview=", StringComparison.Ordinal));
            if (dialogPreview is not null)
            {
                preview.PreviewDialog = dialogPreview["--dialog-preview=".Length..];
            }

            // UI-2 (2026-11-03): DashboardWindow เป็น UserControl แล้ว (ฝังใน MainWindow ตอนใช้งานจริง)
            // - เครื่องมือ --ui-preview นี้ยังต้องเห็นมันเป็นหน้าต่างลอยแยกเพื่อถ่ายภาพ/ทดสอบโดยไม่ต้อง
            // ผ่าน login จริง
            //
            // orchestrator review 2026-11-04: เดิมห่อด้วย Window เปล่าๆ (CreateDashboardPreviewHost)
            // ซึ่งไม่มี SidePanelHost/DialogHost จริง - ตั้งแต่ ClientInspector ย้ายไปแสดงผ่าน
            // MainWindow.SidePanel แล้ว (UI-4) ภาพตรวจ --select --tab=clients จึงไม่เห็นแผงข้อมูลเครื่อง
            // อีกต่อไป เปลี่ยนมาใช้ MainWindow จริงเป็น preview host แทน (ดู ShowPreviewDashboard - ข้าม
            // login flow ทั้งหมด สลับไป DashboardHost ตรงๆ) ได้ SidePanel/DialogHost จริงมาใช้ฟรี
            var previewHost = new MainWindow();
            previewHost.ShowPreviewDashboard(preview);

            // --maximize (orchestrator review 2026-11-04) - ตรวจแผงเต็มความสูงตอนหน้าต่างขยายเต็มจอด้วย
            // ตั้งใจ "ไม่" ใช้ trick ย้ายไปนอกจอ (Left=-32000) คู่กับโหมดนี้ - เจอจริงว่า WPF คำนวณจอที่
            // จะ maximize ไปจากตำแหน่งตอน Show() ถ้าตำแหน่งนั้นไม่ตรงกับจอไหนเลย ขนาดที่ maximize ได้จะ
            // ผิดเพี้ยน (ActualWidth/Height ไม่ถูกต้อง) ทำให้ RenderTargetBitmap ด้านล่าง throw แล้วค้าง
            // รอ MessageBox ของ DispatcherUnhandledException ที่มองไม่เห็น (อยู่นอกจอเหมือนกัน) - ยอมให้
            // หน้าต่างโผล่บนจอจริงสั้นๆ ตอน --maximize แทน
            var isMaximizePreview = e.Args.Contains("--maximize");

            // ถ่ายภาพอย่างเดียว: อย่าไปแย่ง focus และอย่าโผล่กลางจอที่ผู้ใช้กำลังทำงานอยู่
            if (!isMaximizePreview && e.Args.Any(a => a.StartsWith("--shot=", StringComparison.Ordinal)))
            {
                previewHost.ShowActivated = false;
                previewHost.WindowStartupLocation = WindowStartupLocation.Manual;
                previewHost.Left = -32000;
                previewHost.Top = -32000;
            }

            previewHost.Show();

            if (isMaximizePreview)
            {
                previewHost.WindowState = WindowState.Maximized;
            }

            // --shot=<path> เรนเดอร์หน้าต่างเป็น PNG แล้วปิดโปรแกรม
            //
            // ที่ต้องเรนเดอร์เองในแอปแทนที่จะให้สคริปต์ข้างนอกจับภาพหน้าจอ: ทั้ง CopyFromScreen และ
            // PrintWindow พึ่งสิ่งที่อยู่บนจอจริงและ DWM ต้องมี redirection surface ให้ - พอหน้าต่าง
            // ถูกเบราว์เซอร์บังอยู่ CopyFromScreen จะจับภาพเบราว์เซอร์มาแทน (เจอมาแล้ว) ส่วน
            // PrintWindow จะคืนเฟรมขาวเปล่า ๆ (เจอมาแล้วเหมือนกัน) และ Windows ไม่ยอมให้ process
            // เบื้องหลังแย่ง foreground มาแก้ RenderTargetBitmap วาดจาก visual tree ตรง ๆ จึงได้ภาพ
            // ที่ถูกต้องเสมอไม่ว่าหน้าต่างจะถูกบัง ย่อ หรือมีอะไรทับอยู่
            CapturePreview(previewHost, e.Args);
            // StartupUri ถูกประมวลผลหลัง OnStartup คืนค่า (และ set เป็น null จะ throw) เลยต้องปิด
            // หน้าต่าง login ที่มันเปิดให้ ทีหลังแทน - BeginInvoke จึงจำเป็น ไม่ใช่แค่สไตล์
            //
            // orchestrator review 2026-11-04: previewHost เองเป็น MainWindow แล้ว (ดูด้านบน) - ต้อง
            // กันไม่ให้ลูปนี้ปิด previewHost ผิดตัวแทนหน้าต่าง login เดิมจาก StartupUri (ReferenceEquals
            // ไม่ใช่แค่ "is MainWindow" เฉยๆ เหมือนเดิม ไม่งั้นลำดับ enumerate ของ Windows ไม่แน่นอนว่า
            // จะเจอตัวไหนก่อน - ถ้าปิด previewHost ไปก่อน CapturePreview's ApplicationIdle callback
            // จะ render หน้าต่างที่ปิดไปแล้ว)
            Dispatcher.BeginInvoke(() =>
            {
                foreach (Window window in Windows)
                {
                    if (window is MainWindow login && !ReferenceEquals(login, previewHost))
                    {
                        login.Close();
                        break;
                    }
                }
            });
        }
#endif
    }

#if DEBUG
    /// <summary>
    /// UI-2 (2026-11-03): DashboardWindow เปลี่ยนจาก Window เป็น UserControl ที่ฝังใน MainWindow แล้ว
    /// (ดู DashboardWindow.xaml) แต่เครื่องมือ debug สามตัวข้างบน (--center-test/--focus-test/
    /// --ui-preview) ยังต้องการเห็น Dashboard เป็นหน้าต่างลอยเดี่ยวๆ เพื่อถ่ายภาพ/ทดสอบโดยไม่ต้องผ่าน
    /// login จริง - ห่อด้วย Window เปล่าๆ ขนาด/สไตล์เดียวกับที่ DashboardWindow เคยตั้งบนตัวเองตอนยัง
    /// เป็น Window (1000x732, MinWidth/MinHeight 1000x560, AppDialogWindow)
    /// </summary>
    /// <summary>
    /// UI-3c (2026-11-03): ห่อ UserControl ที่เดิมเป็น Window ของตัวเอง (ตอนนี้ฝังใน SidePanelHost/
    /// DialogHost จริงของ MainWindow แทน) ด้วย Window เปล่าสำหรับถ่ายภาพ chrome เฉยๆ - pattern เดียวกับ
    /// ที่ "force-password" ใช้มาตั้งแต่ UI-3a ไม่ต้องพึ่ง host จริงสำหรับภาพนิ่งแบบนี้ (เนื้อหาเองมี
    /// Border Style="Card" ของตัวเองกำหนดขนาด/หน้าตาแล้ว ไม่ต้องให้ Window วาด chrome ซ้ำ)
    /// </summary>
    private static Window WrapAsPreviewWindow(FrameworkElement content) => new()
    {
        SizeToContent = SizeToContent.WidthAndHeight,
        WindowStartupLocation = WindowStartupLocation.CenterScreen,
        ResizeMode = ResizeMode.NoResize,
        Style = (Style)Current.FindResource("AppDialogWindow"),
        Content = content,
    };

    private static Window CreateDashboardPreviewHost(DashboardWindow content) => new()
    {
        Title = "Onion ProcOparetor Console - Dashboard",
        Width = 1000,
        Height = 732,
        MinWidth = 1000,
        MinHeight = 560,
        WindowStartupLocation = WindowStartupLocation.CenterScreen,
        ResizeMode = ResizeMode.CanResize,
        Style = (Style)Current.FindResource("AppDialogWindow"),
        Content = content
    };

    /// <summary>
    /// --shot=&lt;path&gt; เรนเดอร์หน้าต่างเป็น PNG แล้วปิดโปรแกรม (โหมดตรวจ UI เท่านั้น)
    ///
    /// ที่ต้องเรนเดอร์เองในแอปแทนที่จะให้สคริปต์ข้างนอกจับภาพหน้าจอ: ทั้ง CopyFromScreen และ
    /// PrintWindow พึ่งสิ่งที่อยู่บนจอจริงและ DWM ต้องมี redirection surface ให้ - พอหน้าต่างถูก
    /// เบราว์เซอร์บังอยู่ CopyFromScreen จะจับภาพเบราว์เซอร์มาแทน (เจอมาแล้ว) ส่วน PrintWindow จะคืน
    /// เฟรมขาวเปล่า ๆ (เจอมาแล้วเหมือนกัน) และ Windows ไม่ยอมให้ process เบื้องหลังแย่ง foreground
    /// มาแก้ RenderTargetBitmap วาดจาก visual tree ตรง ๆ จึงได้ภาพที่ถูกต้องเสมอไม่ว่าจะถูกอะไรบัง
    /// </summary>
    private void CapturePreview(Window window, string[] args)
    {
        var shot = args.FirstOrDefault(a => a.StartsWith("--shot=", StringComparison.Ordinal));
        if (shot is null)
        {
            return;
        }

        var path = shot["--shot=".Length..];
        window.Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.ApplicationIdle,
            () =>
            {
                // --scroll=bottom (WP3-4, 2026-10-31) - หน้ายาวๆ อย่าง "ตั้งค่า" มีการ์ดอยู่ใต้ fold ที่
                // ScrollViewer ของหน้าไม่โชว์แต่แรก ใช้ตรวจการ์ดที่อยู่ล่างสุดโดยไม่ต้อง resize จอ/แก้ไฟล์
                // ชั่วคราว - เลื่อน ScrollViewer ตัวแรกที่เจอในหน้าต่างไปสุดท้ายก่อนเรนเดอร์
                if (args.Contains("--scroll=bottom"))
                {
                    FindMostScrollableViewer(window)?.ScrollToEnd();
                    window.UpdateLayout();
                }

                // --scroll-to=<x:Name> (WP3-4, 2026-10-31) - เจาะจงกว่า --scroll=bottom สำหรับหน้า
                // ยาวๆ ที่การ์ดที่ต้องตรวจอยู่กลางหน้า ไม่ใช่ท้ายสุด (เช่น การ์ด "รหัสฉุกเฉิน" ใน
                // SettingsPage) ใช้ FrameworkElement.BringIntoView() แทนการเดา ScrollViewer offset เอง
                var scrollToArg = args.FirstOrDefault(a => a.StartsWith("--scroll-to=", StringComparison.Ordinal));
                if (scrollToArg is not null)
                {
                    var elementName = scrollToArg["--scroll-to=".Length..];
                    if (FindElementByName(window, elementName) is FrameworkElement target)
                    {
                        target.BringIntoView();
                        window.UpdateLayout();
                    }
                }
                // วัดตำแหน่ง/ขนาดตาราง Clients ลง console.log (WP6) - เกณฑ์ "ตารางไม่ขยับตอนแผงเปิดหรือ
                // ตอนประกาศโผล่" พิสูจน์ด้วยตัวเลขเทียบกันระหว่างภาพ ไม่ใช่ด้วยการเพ่งภาพสองใบ
                //
                // UI-2 (2026-11-03): DashboardWindow เป็น UserControl แล้ว - window ที่ส่งเข้ามาตรงนี้คือ
                // previewHost ที่ห่ออยู่ (ดู CreateDashboardPreviewHost) จึงต้องมองหา DashboardWindow ผ่าน
                // window.Content แทนที่จะ cast window ตรงๆ
                if (window.Content as DashboardWindow is { } dashboard && dashboard.ClientsGrid.IsVisible)
                {
                    var origin = dashboard.ClientsGrid.TranslatePoint(new Point(0, 0), dashboard);
                    ConsoleDiagnostics.WriteLine(
                        $"LAYOUT-TEST ClientsGrid=({origin.X:F1},{origin.Y:F1} "
                        + $"{dashboard.ClientsGrid.ActualWidth:F1}x{dashboard.ClientsGrid.ActualHeight:F1}) "
                        + $"args={string.Join(' ', args)}");
                }

                var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(
                    (int)window.ActualWidth, (int)window.ActualHeight, 96, 96,
                    System.Windows.Media.PixelFormats.Pbgra32);
                bitmap.Render(window);

                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                using (var stream = System.IO.File.Create(path))
                {
                    encoder.Save(stream);
                }

                Shutdown();
            });
    }

    /// <summary>
    /// ใช้กับ --scroll=bottom เท่านั้น - หน้าต่างมี ScrollViewer หลายตัวพร้อมกันได้ (sidebar, แท็บอื่นที่
    /// ยังไม่ unload, ฯลฯ) ตัวแรกที่เจอแบบ depth-first ไม่ใช่ตัวที่ต้องการเสมอไป - เลือกตัวที่ "เหลือให้
    /// เลื่อนได้มากที่สุด" (ExtentHeight - ViewportHeight) แทน ซึ่งคือเนื้อหาของแท็บที่กำลังเปิดดูอยู่จริง
    /// </summary>
    private static System.Windows.Controls.ScrollViewer? FindMostScrollableViewer(DependencyObject root)
    {
        System.Windows.Controls.ScrollViewer? best = null;
        var bestScrollable = 0.0;
        CollectScrollViewers(root, ref best, ref bestScrollable);
        return best;
    }

    /// <summary>ใช้กับ --scroll-to=&lt;x:Name&gt; - หา FrameworkElement ที่ชื่อนี้แบบ depth-first ทั้งหน้าต่าง (รวมแท็บที่ไม่ได้โชว์อยู่ด้วย ถ้า visual tree ยัง realize อยู่)</summary>
    private static FrameworkElement? FindElementByName(DependencyObject node, string name)
    {
        if (node is FrameworkElement fe && fe.Name == name)
        {
            return fe;
        }

        var count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(node);
        for (var i = 0; i < count; i++)
        {
            var found = FindElementByName(System.Windows.Media.VisualTreeHelper.GetChild(node, i), name);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    private static void CollectScrollViewers(DependencyObject node, ref System.Windows.Controls.ScrollViewer? best, ref double bestScrollable)
    {
        if (node is System.Windows.Controls.ScrollViewer sv)
        {
            var scrollable = sv.ExtentHeight - sv.ViewportHeight;
            if (scrollable > bestScrollable)
            {
                bestScrollable = scrollable;
                best = sv;
            }
        }

        var count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(node);
        for (var i = 0; i < count; i++)
        {
            CollectScrollViewers(System.Windows.Media.VisualTreeHelper.GetChild(node, i), ref best, ref bestScrollable);
        }
    }
#endif
}
