using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using OnionProcOparetor.Console.Models;
using OnionProcOparetor.Console.Services;

namespace OnionProcOparetor.Console;

/// <summary>
/// UI-2 (2026-11-03): MainWindow ไม่ใช่แค่หน้าล็อกอินอีกต่อไป - เป็น "เปลือกหน้าต่างเดียว" ตลอดอายุ
/// โปรแกรม (host window) ที่สลับเนื้อหาระหว่างหน้าล็อกอิน (LoginRoot, เดิมทั้งหมด) กับหน้าหลัก
/// (DashboardHost ที่ใส่ DashboardWindow UserControl เข้าไป) โดยไม่เปิดหน้าต่างใหม่เลย ตามที่ roadmap
/// WP UI-2 สั่ง ("ล็อกอินเสร็จแสดงตัวหมุนโหลดสั้นๆ แล้ว fade เข้าหน้าหลักในหน้าต่างเดิม") - ดู
/// ShowDashboardAsync/ReturnToLoginAsync ด้านล่างสำหรับลำดับขั้นเต็ม
/// </summary>
public partial class MainWindow : Window
{
    // ไม่ใช่ readonly โดยเจตนา - สร้างใหม่ทุกครั้งที่กด Connect (ดู ConnectButton_Click) เพราะ HttpClient
    // ห้ามแก้ BaseAddress ซ้ำหลังส่ง request แรกไปแล้ว (throw InvalidOperationException ทันที) การ reuse
    // instance เดิมข้าม Connect attempt เดิมทำให้กด Connect ครั้งที่ 2 (ไม่ว่าจะกรอกรหัสถูกหรือผิด) crash
    // ทั้งโปรแกรมเสมอ เพราะ exception เกิดใน SetServer ก่อนเช็ค password ด้วยซ้ำ (ยืนยันจาก stack trace จริง
    // ใน Event Log ไม่ได้เดา) - instance ที่ login สำเร็จค่อยถูกส่งต่อให้ ForceChangePasswordWindow/DashboardWindow
    private ApiClient _apiClient = new();

    /// <summary>UI-2 (2026-11-03): DashboardWindow ที่กำลังแสดงอยู่ (null = ยังอยู่หน้าล็อกอิน) - สร้างใหม่
    /// ทุกครั้งที่ login สำเร็จ (เหมือนพฤติกรรมเดิมก่อน UI-2) ทิ้งตอน logout ดู ShowDashboardAsync/
    /// ReturnToLoginAsync</summary>
    private DashboardWindow? _dashboard;

    public MainWindow()
    {
        InitializeComponent();

        // เติมค่า default จาก Settings แทน hardcode "localhost"/"8787" ใน XAML เดิม
        var connection = SettingsService.Current.Connection;
        ServerIpBox.Text = connection.ServerHost;
        PortBox.Text = connection.ServerPort;
        // WP1b (2026-10-30) - จำตัวเลือก HTTP/HTTPS ไว้เหมือน IP/Port (ค่าเริ่มต้นของ ConnectionSettings
        // คือ false/HTTP จนกว่าจะย้ายระบบจริง)
        UseHttpsCheckBox.IsChecked = connection.UseHttps;

        LoadHeroImage();

#if DEBUG
        // --dialog=login --login-error[=long] (UI-6, 2026-11-03) - จำลองข้อความ error สีแดงใต้ปุ่ม
        // เชื่อมต่อ (เหมือนตอนรหัสผ่านผิด/ต่อ Server ไม่ได้) เพื่อถ่ายภาพตรวจว่าการ์ดที่ย่อลง 75% แล้วไม่มี
        // อะไรล้น/ถูกตัดตอนมีข้อความ error แสดงอยู่ (roadmap WP UI-6 สั่งให้ตรวจสถานะนี้ด้วย) =long ใช้
        // ข้อความยาวจำลอง exception จริงของ cert/network (2 บรรทัด) ตามที่ orchestrator ขอให้ตรวจเพิ่ม
        // อ่าน args เองตรงนี้แทนการแก้ App.xaml.cs เพราะไฟล์นั้นอยู่นอกขอบเขตไฟล์ที่ WP UI-6 แตะได้
        var loginErrorArg = Environment.GetCommandLineArgs().FirstOrDefault(a => a.StartsWith("--login-error", StringComparison.Ordinal));
        if (loginErrorArg is not null)
        {
            StatusText.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "Brush.Danger.Default");
            StatusText.Text = loginErrorArg == "--login-error=long"
                ? "เชื่อมต่อไม่สำเร็จ: certificate ของ Server (CN=192.168.17.249) ไม่ตรงกับที่เคยไว้ใจไว้ กรุณาตรวจสอบกับผู้ดูแลระบบก่อนลองใหม่อีกครั้ง"
                : "เชื่อมต่อไม่สำเร็จ: Username หรือ Password ไม่ถูกต้อง";
        }

        // วัด overflow จริงด้วย TransformToAncestor แทนการเพ่งภาพ (ตามที่ orchestrator ขอ 2026-11-03) -
        // รันทุกครั้งที่มี --shot= (ไม่ใช่แค่ตอน --login-error) เพื่อจับ regression ของทุกสถานะ เทียบขอบล่าง
        // ของ StatusText (ธาตุที่อยู่ล่างสุดของฟอร์มเสมอ) กับความสูงจริงของ LoginCard แล้ว log ผลลง
        // console.log (ConsoleDiagnostics.LogPath) ให้ตรวจสอบได้โดยไม่ต้องเพ่งภาพ
        if (Environment.GetCommandLineArgs().Any(a => a.StartsWith("--shot=", StringComparison.Ordinal)))
        {
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, () =>
            {
                var bottomOfStatusText = StatusText.TransformToAncestor(LoginCard)
                    .Transform(new Point(0, StatusText.ActualHeight)).Y;
                var overflow = bottomOfStatusText - LoginCard.ActualHeight;
                ConsoleDiagnostics.WriteLine(
                    $"UI6-OVERFLOW-CHECK LoginCard.ActualHeight={LoginCard.ActualHeight:F1} "
                    + $"StatusText.bottom={bottomOfStatusText:F1} overflow={overflow:F1} "
                    + (overflow > 0 ? "CLIPPED" : "OK"));
            });
        }
#endif

        // UI-2 (2026-11-03): เดิม "กัน Alt+F4 ปิด Console ตอนอยู่ fullscreen live-view" อยู่ใน
        // DashboardWindow.Closing ของตัวเอง (ตอนยังเป็น Window แยก) - ตอนนี้ MainWindow เป็น host
        // window เดียวตลอด process จึงต้องรับ Closing ของตัวเองแทน แล้วถาม DashboardWindow (ถ้ากำลัง
        // แสดงอยู่) ว่าปิดได้ไหม (ดู DashboardWindow.CanCloseHost)
        Closing += (_, e) =>
        {
            if (_dashboard is not null && !_dashboard.CanCloseHost())
            {
                e.Cancel = true;
            }
        };

        // ปิดโปรแกรมจริง (ไม่ใช่ logout) - cleanup timer/SignalR ของ Dashboard ถ้ายังเปิดอยู่ (เดิมอยู่ใน
        // DashboardWindow.Closed ของตัวเอง)
        Closed += async (_, _) =>
        {
            if (_dashboard is not null)
            {
                await _dashboard.ShutdownAsync();
            }
        };
    }

    /// <summary>
    /// WP7 (2026-10-30): ภาพฝั่งซ้ายของการ์ดล็อกอิน - ลองโหลดจากไฟล์ "ข้างแอป" (Assets\LoginHero.png
    /// ถัดจาก .exe) ก่อนเสมอ เพื่อให้เปลี่ยนภาพได้โดยไม่ต้อง build ใหม่ ถ้าไฟล์นั้นหายไป (ลบทิ้ง/ติดตั้ง
    /// พลาด/อ่านไม่ได้) fallback ไปใช้ภาพเดียวกันที่ฝังอยู่ในตัว exe เอง (pack resource - ดู .csproj ที่มี
    /// ทั้ง None CopyToOutputDirectory และ Resource ชี้ไฟล์เดียวกัน) **ต้องไม่ throw ขึ้นไปเด็ดขาด** -
    /// เกณฑ์เสร็จของ WP7 คือ "ลบไฟล์ภาพ LoginHero ทิ้งแล้วแอปต้องไม่ crash"
    /// </summary>
    private void LoadHeroImage()
    {
        var externalPath = Path.Combine(AppContext.BaseDirectory, "Assets", "LoginHero.png");
        try
        {
            if (File.Exists(externalPath))
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                // OnLoad = อ่านทั้งไฟล์เข้า memory ทันทีตอน EndInit แล้วปล่อย file handle - ถ้าใช้
                // ค่า default (OnDemand) ไฟล์จะยังถูกถือล็อกอยู่ตราบใดที่ BitmapImage นี้มีชีวิต ซึ่งแปลว่า
                // ลบไฟล์ทิ้งตอน Console ยังเปิดอยู่จะทำไม่ได้ (ไม่ตรงกับเกณฑ์ทดสอบที่ต้องลบไฟล์ได้)
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(externalPath, UriKind.Absolute);
                bitmap.EndInit();
                HeroImage.Source = bitmap;
                return;
            }
        }
        catch (Exception ex)
        {
            // ไฟล์เสีย/อ่านไม่ได้ (permission, PNG พัง ฯลฯ) - log ไว้แล้วไหลลงไป fallback ด้านล่าง
            // เหมือนกรณีไม่มีไฟล์เลย ไม่ throw ขึ้นไปทำให้หน้าล็อกอินเปิดไม่ขึ้น
            ConsoleDiagnostics.WriteLine($"MainWindow: โหลด {externalPath} ไม่สำเร็จ ({ex.Message}) - ใช้ภาพที่ฝังไว้แทน");
        }

        // ไม่มีไฟล์ข้างแอป (หรือโหลดพัง) - ใช้ภาพที่ฝังในตัว exe เอง (pack resource) ซึ่งมีอยู่แน่นอนเสมอ
        // เพราะถูก build เข้าไปกับ assembly ไม่ได้พึ่งไฟล์แยกบนดิสก์
        HeroImage.Source = new BitmapImage(new Uri("pack://application:,,,/Assets/LoginHero.png"));
    }

    private async void ConnectButton_Click(object sender, RoutedEventArgs e)
    {
        var serverIp = ServerIpBox.Text.Trim();
        var port = PortBox.Text.Trim();
        var username = UsernameBox.Text.Trim();
        var password = PasswordBox.Password;
        var useHttps = UseHttpsCheckBox.IsChecked == true;

        // หมายเหตุ: ไม่เช็ค password ว่างเปล่าที่นี่โดยเจตนา
        // เพราะ built-in "Administrator" ใช้ password ว่างเปล่าเป็นค่า default ตอนยังไม่เคยเปลี่ยน
        // (server จะบังคับให้เปลี่ยน password หลัง login สำเร็จอยู่แล้วผ่าน hasDefaultPassword flag)
        if (string.IsNullOrEmpty(serverIp) || string.IsNullOrEmpty(username))
        {
            StatusText.Text = "กรุณากรอก Server IP และ Username";
            return;
        }

        var serverAddress = string.IsNullOrEmpty(port) ? serverIp : $"{serverIp}:{port}";

        ConnectButton.IsEnabled = false;
        // SetResourceReference ไม่ใช่ FindResource: FindResource คืน "ค่า ณ ตอนนั้น" ครั้งเดียว ซึ่งจะค้างอยู่กับธีมเดิมตลอดไปเมื่อผู้ใช้สลับธีม - SetResourceReference ผูกเป็น DynamicResource จริงเหมือนที่เขียนใน XAML จึงตามธีมใหม่ให้เอง
        StatusText.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "Brush.Status.Offline");
        StatusText.Text = "กำลังเชื่อมต่อ...";

        _apiClient = new ApiClient();
        _apiClient.SetServer(serverAddress, useHttps);
        var (success, message) = await _apiClient.LoginAsync(username, password);

        // WP1b (2026-10-30): login พังตอนเลือก HTTPS อาจเป็นเพราะ cert ของ host:port นี้ยังไม่รู้จัก
        // (ครั้งแรก) หรือเปลี่ยนไปจากที่เคย pin ไว้ - ไม่ใช่เพราะ server ปิดอยู่หรือรหัสผ่านผิด เช็คว่า TLS
        // handshake ไปถึงชั้นเห็น certificate จริงไหม (TryGetLastSeenCertificate มีค่า = ไปถึงแล้ว - ถ้า
        // server ปิดอยู่/ต่อ TCP ไม่ติด จะไม่มีค่านี้เลย) ถ้าใช่ เปิดหน้าต่าง "ไว้ใจ certificate นี้" แล้วลอง
        // login ซ้ำอีกครั้งเดียวถ้าผู้ใช้กดไว้ใจ (ตั้งใจไม่ลูปไม่สิ้นสุด - พังอีกรอบก็แค่โชว์ error ปกติ)
        if (!success && useHttps)
        {
            var portNum = ResolvePort(port, useHttps: true);
            var certInfo = ServerCertificatePinning.TryGetLastSeenCertificate(serverIp, portNum);
            if (certInfo is { } cert)
            {
                var pinned = ServerCertificatePinning.GetPinnedThumbprint(serverIp, portNum);
                var isChanged = pinned is not null &&
                    !string.Equals(pinned, cert.Thumbprint, StringComparison.OrdinalIgnoreCase);

                var trustWindow = new TrustCertificateWindow(serverIp, portNum, cert, isChanged, pinned) { Owner = this };
                if (trustWindow.ShowDialog() == true && trustWindow.Trusted)
                {
                    var (trustOk, trustError) = ServerCertificatePinning.TrustCertificate(serverIp, portNum, cert.Thumbprint);
                    if (trustOk)
                    {
                        StatusText.Text = "ไว้ใจ certificate แล้ว กำลังลองเชื่อมต่อใหม่...";
                        (success, message) = await _apiClient.LoginAsync(username, password);
                    }
                    else
                    {
                        success = false;
                        message = $"บันทึก certificate ที่ไว้ใจไม่สำเร็จ: {trustError}";
                    }
                }
                else
                {
                    message = "ยกเลิกการเชื่อมต่อ - ไม่ได้ไว้ใจ certificate นี้";
                }
            }
        }

        if (success)
        {
            SaveConnectionAsDefault(serverIp, port, useHttps);

            if (_apiClient.HasDefaultPassword)
            {
                StatusText.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "Brush.Status.Online");
                StatusText.Text = "เชื่อมต่อสำเร็จ กรุณาเปลี่ยน password ก่อนใช้งาน...";

                // UI-2 (2026-11-03): เดิมหน้านี้เปิดเป็นหน้าต่างของตัวเองแล้ว Close() MainWindow ทิ้ง
                // (ตอนนั้น DashboardWindow/ForceChangePasswordWindow ต่างก็เป็น Window อิสระ) ตอนนี้
                // MainWindow ต้องอยู่ต่อตลอด (host เดียว) จึงเปิดเป็น modal dialog แทน (Owner=this,
                // ShowDialog) แล้วอ่านผลจาก DialogResult - เปลี่ยนสำเร็จ (true) ค่อยเปิดหน้าหลักต่อใน
                // หน้าต่างเดิมนี้เอง ถ้าผู้ใช้ปิดหน้าต่างเปลี่ยนรหัสเอง (กดปุ่ม X) ก็แค่กลับมาที่ฟอร์ม
                // ล็อกอินเดิม ยังไม่ login จริง (ตรงกับพฤติกรรมเดิมที่ต้องเปลี่ยนรหัสให้จบก่อนถึงจะเห็น
                // Dashboard)
                var forceChangeWindow = new ForceChangePasswordWindow(_apiClient) { Owner = this };
                var passwordChanged = forceChangeWindow.ShowDialog() == true;
                if (passwordChanged)
                {
                    ToastService.Success($"เข้าสู่ระบบสำเร็จ ({username})");
                    await ShowDashboardAsync(_apiClient);
                }
                else
                {
                    StatusText.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "Brush.Danger.Default");
                    StatusText.Text = "ยกเลิกการเปลี่ยน password - ยังไม่ได้เข้าสู่ระบบ";
                    ConnectButton.IsEnabled = true;
                }
            }
            else
            {
                StatusText.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "Brush.Status.Online");
                StatusText.Text = "เชื่อมต่อสำเร็จ กำลังเปิด Dashboard...";

                // Toast ของ WP6 - ตอนนี้ยังไม่มี ToastHost อยู่จริง (อยู่ใน DashboardWindow ที่กำลังจะเปิด
                // ด้านล่างนี้) แต่ ToastService.Items เป็น static queue อยู่แล้ว โชว์ทันทีที่ ToastHost
                // ของ Dashboard mount เสร็จ (ดู Services/ToastService.cs ส่วน "เรียกก่อนมี host")
                ToastService.Success($"เข้าสู่ระบบสำเร็จ ({username})");

                await ShowDashboardAsync(_apiClient);
            }
        }
        else
        {
            StatusText.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "Brush.Danger.Default");
            StatusText.Text = message;
            ConnectButton.IsEnabled = true;
        }
    }

    /// <summary>
    /// จำ Server IP/Port ที่ login สำเร็จไว้เป็นค่า default ครั้งถัดไปที่เปิด Console (ดู Settings > DEFAULT
    /// SERVER HOST/PORT) - ไม่ critical ถ้าบันทึกไม่สำเร็จ (เช่น เขียนไฟล์ไม่ได้) เลยไม่โชว์ error ให้ผู้ใช้
    /// ตรงนี้ แค่ล้มเหลวเงียบๆ แล้วครั้งถัดไปยังใช้ค่าเดิมที่เคยบันทึกไว้ (หรือ default ของ ConsoleSettings)
    /// </summary>
    private static void SaveConnectionAsDefault(string serverHost, string serverPort, bool useHttps)
    {
        var current = SettingsService.Current;
        var updated = new ConsoleSettings
        {
            Vnc = current.Vnc,
            Ui = current.Ui,
            Connection = new ConnectionSettings
            {
                ServerHost = serverHost,
                ServerPort = string.IsNullOrEmpty(serverPort) ? current.Connection.ServerPort : serverPort,
                DashboardPollIntervalSeconds = current.Connection.DashboardPollIntervalSeconds,
                UseHttps = useHttps,
                // WP1b (2026-10-30): ต้องคัดลอกมาด้วยเสมอ - ไม่งั้น SaveConnectionAsDefault ที่สร้าง
                // ConnectionSettings ใหม่ทั้งก้อนนี้จะเขียนทับ pin ที่ TrustCertificate บันทึกไว้ก่อนหน้า
                // ในการ login ครั้งเดียวกัน (เรียกหลัง login สำเร็จ ซึ่งอาจเพิ่งไว้ใจ cert ไปหมาดๆ)
                TrustedCertificateThumbprints = current.Connection.TrustedCertificateThumbprints
            }
        };

        SettingsService.Save(updated);
    }

    /// <summary>WP1b (2026-10-30) - แปลง Port box (อาจว่าง) เป็นเลขพอร์ตจริงให้ตรงกับที่ Uri ของ .NET
    /// จะใช้จริง (default 443 สำหรับ https, 80 สำหรับ http) ใช้คำนวณ key "host:port" ให้ตรงกับที่
    /// ServerCertificatePinning เห็นจาก request.RequestUri.Port จริง (ดู ServerCertificatePinning.Validate)</summary>
    private static int ResolvePort(string port, bool useHttps) =>
        !string.IsNullOrEmpty(port) && int.TryParse(port, out var parsed) ? parsed : (useHttps ? 443 : 80);

    private void ServerIpBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {

    }

    /// <summary>
    /// UI-2 (2026-11-03): login สำเร็จ -> ตัวหมุนโหลดสั้นๆ -> fade เข้าหน้าหลัก "ในหน้าต่างเดิม" ตามที่
    /// roadmap WP UI-2 สั่งตรงๆ สร้าง DashboardWindow (UserControl) ใหม่ทุกครั้ง (เหมือนพฤติกรรมเดิมก่อน
    /// UI-2 ที่ `new DashboardWindow(...)` ทุกครั้งที่ login สำเร็จ) แล้วฝังเข้า DashboardHost
    ///
    /// เรื่องขนาดหน้าต่าง: ขนาดล็อกอิน (1000x732, ตาย Width/Height ที่ตั้งไว้บน Window) กับขนาดเริ่มต้น
    /// เดิมของ DashboardWindow ตอนยังเป็น Window ของตัวเอง (Height=732 Width=1000 เป๊ะ) เท่ากันอยู่แล้ว -
    /// จึงไม่มีอะไรให้ "ขยายแบบนุ่มนวล" จริงๆ (animate Width/Height จะไม่เห็นผลต่าง) แค่เปิด resize
    /// (ResizeMode=CanResize) และกำหนด MinWidth/MinHeight ให้ตรงกับที่ DashboardWindow เดิมเคยตั้งไว้บน
    /// ตัวเอง (1000x560) หลัง fade เสร็จ
    /// </summary>
    private async Task ShowDashboardAsync(ApiClient apiClient)
    {
        LoadingOverlayText.Text = "กำลังเข้าสู่ระบบ...";
        LoadingOverlay.Opacity = 0;
        LoadingOverlay.Visibility = Visibility.Visible;
        StartSpinner();
        AnimateOpacity(LoadingOverlay, 1, 150);
        await Task.Delay(500);

        var dashboard = new DashboardWindow(apiClient);
        // UI-2 (2026-11-03): ปุ่ม "ออกจากระบบ" ใน footer ของ Dashboard - สลับกลับหน้าล็อกอินในหน้าต่าง
        // เดิม (ไม่ใช่ async void ตรงๆ ที่นี่เพราะ event handler ของ Button ไม่ await ได้ - ครอบด้วย _ =
        // ตามแบบที่โค้ดเดิมในไฟล์นี้ใช้กับ async void pattern อยู่แล้ว)
        dashboard.LogoutRequested += () => _ = ReturnToLoginAsync();
        _dashboard = dashboard;

        DashboardHost.Content = dashboard;
        DashboardHost.Opacity = 0;
        DashboardHost.Visibility = Visibility.Visible;

        AnimateOpacity(DashboardHost, 1, 250);
        AnimateOpacity(LoadingOverlay, 0, 250);
        await Task.Delay(260);

        LoadingOverlay.Visibility = Visibility.Collapsed;
        StopSpinner();
        LoginRoot.Visibility = Visibility.Collapsed;

        ResizeMode = ResizeMode.CanResize;
        MinWidth = 1000;
        MinHeight = 560;
        Title = "Onion ProcOparetor Console - Dashboard";
    }

    /// <summary>
    /// UI-2 (2026-11-03): logout (หรือ token หมดอายุในอนาคต) -> fade กลับไปหน้าล็อกอิน "ในหน้าต่างเดิม"
    /// ตามที่ roadmap WP UI-2 สั่ง - ไม่เรียก API ใดๆ (ยังไม่มี endpoint invalidate token ของ Server ฝั่งนี้
    /// - ดู comment บน LogoutButton ใน DashboardWindow.xaml) แค่เคลียร์ ApiClient/Dashboard ฝั่ง client
    /// </summary>
    private async Task ReturnToLoginAsync()
    {
        if (_dashboard is null)
        {
            return;
        }

        LoadingOverlayText.Text = "กำลังออกจากระบบ...";
        LoadingOverlay.Opacity = 0;
        LoadingOverlay.Visibility = Visibility.Visible;
        StartSpinner();
        AnimateOpacity(LoadingOverlay, 1, 150);
        await Task.Delay(200);

        var dashboard = _dashboard;
        _dashboard = null;
        await dashboard.ShutdownAsync();

        DashboardHost.Content = null;
        DashboardHost.Visibility = Visibility.Collapsed;

        // แก้ตามคำสั่ง orchestrator (2026-11-03): ครูอาจ maximize หรือลาก resize หน้าหลักไว้ก่อน logout
        // (ResizeMode=CanResize ตอนนั้น) - ถ้าแค่สลับ ResizeMode กลับเป็น NoResize เฉยๆ โดยไม่รีเซ็ต
        // WindowState/ขนาด การ์ดล็อกอินจะไปโผล่กลางหน้าต่างยักษ์ที่ resize ไม่ได้แล้ว ต้องคืนกลับเป็น
        // Normal + ขนาดล็อกอินเดิม (1000x732) + จัดกลางจอใหม่เองทุกครั้ง (WindowStartupLocation=
        // CenterScreen มีผลแค่ตอนสร้างหน้าต่างครั้งแรกเท่านั้น ไม่ทำงานซ้ำตอนนี้) - ทำตอนนี้ขณะที่
        // LoadingOverlay ยังทึบเต็มจอพอดี (fade-in 150ms + รอ 200ms ข้างบนเสร็จแล้วแน่นอน) ผู้ใช้จึงไม่
        // เห็นหน้าต่างกระตุกขนาด/ตำแหน่งระหว่างเปลี่ยน
        //
        // clamp ด้วย SystemParameters.WorkArea (บทเรียนจาก docs/handoff-2026-11-03.md) เผื่อจอเล็กกว่า
        // 1000x732 จริงๆ (เช่น remote desktop ความละเอียดต่ำ) และคำนวณ Left/Top จาก WorkArea.Left/Top
        // เสมอ ไม่ใช่ 0,0 ตรงๆ เพราะจอรองที่อยู่ทางซ้าย/บนจอหลักจะมีค่าติดลบได้
        WindowState = WindowState.Normal;
        var workArea = SystemParameters.WorkArea;
        Width = Math.Min(1000, workArea.Width);
        Height = Math.Min(732, workArea.Height);
        Left = workArea.Left + Math.Max(0, (workArea.Width - Width) / 2);
        Top = workArea.Top + Math.Max(0, (workArea.Height - Height) / 2);

        ResizeMode = ResizeMode.NoResize;
        MinWidth = 0;
        MinHeight = 0;
        Title = "Onion ProcOparetor Console";

        // เคลียร์ฟอร์มล็อกอิน - ล้าง password เสมอ (ความปลอดภัย) แต่คง Server IP/Port/Username ไว้ให้
        // กรอกรหัสใหม่ได้เร็ว ไม่ต้องพิมพ์ทุกช่องใหม่
        PasswordBox.Clear();
        StatusText.Text = string.Empty;
        ConnectButton.IsEnabled = true;
        _apiClient = new ApiClient();

        LoginRoot.Opacity = 0;
        LoginRoot.Visibility = Visibility.Visible;
        AnimateOpacity(LoginRoot, 1, 250);
        AnimateOpacity(LoadingOverlay, 0, 250);
        await Task.Delay(260);

        LoadingOverlay.Visibility = Visibility.Collapsed;
        StopSpinner();
    }

    private void StartSpinner()
    {
        var animation = new DoubleAnimation(0, 360, TimeSpan.FromSeconds(0.9))
        {
            RepeatBehavior = RepeatBehavior.Forever
        };
        SpinnerRotate.BeginAnimation(RotateTransform.AngleProperty, animation);
    }

    private void StopSpinner()
    {
        SpinnerRotate.BeginAnimation(RotateTransform.AngleProperty, null);
    }

    private static void AnimateOpacity(UIElement element, double to, int durationMs)
    {
        var animation = new DoubleAnimation(to, TimeSpan.FromMilliseconds(durationMs))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
        };
        element.BeginAnimation(UIElement.OpacityProperty, animation);
    }
}
