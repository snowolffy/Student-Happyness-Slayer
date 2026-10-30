using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using OnionProcOparetor.Console.Models;
using OnionProcOparetor.Console.Services;

namespace OnionProcOparetor.Console;

public partial class MainWindow : Window
{
    // ไม่ใช่ readonly โดยเจตนา - สร้างใหม่ทุกครั้งที่กด Connect (ดู ConnectButton_Click) เพราะ HttpClient
    // ห้ามแก้ BaseAddress ซ้ำหลังส่ง request แรกไปแล้ว (throw InvalidOperationException ทันที) การ reuse
    // instance เดิมข้าม Connect attempt เดิมทำให้กด Connect ครั้งที่ 2 (ไม่ว่าจะกรอกรหัสถูกหรือผิด) crash
    // ทั้งโปรแกรมเสมอ เพราะ exception เกิดใน SetServer ก่อนเช็ค password ด้วยซ้ำ (ยืนยันจาก stack trace จริง
    // ใน Event Log ไม่ได้เดา) - instance ที่ login สำเร็จค่อยถูกส่งต่อให้ ForceChangePasswordWindow/DashboardWindow
    private ApiClient _apiClient = new();

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

                var forceChangeWindow = new ForceChangePasswordWindow(_apiClient);
                forceChangeWindow.Show();
                Close();
            }
            else
            {
                StatusText.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "Brush.Status.Online");
                StatusText.Text = "เชื่อมต่อสำเร็จ กำลังเปิด Dashboard...";

                // Toast ของ WP6 - ตอนนี้ยังไม่มี ToastHost อยู่จริง (อยู่ใน DashboardWindow ที่กำลังจะเปิด
                // ด้านล่างนี้) แต่ ToastService.Items เป็น static queue อยู่แล้ว โชว์ทันทีที่ ToastHost
                // ของ Dashboard mount เสร็จ (ดู Services/ToastService.cs ส่วน "เรียกก่อนมี host")
                ToastService.Success($"เข้าสู่ระบบสำเร็จ ({username})");

                var dashboard = new DashboardWindow(_apiClient);
                dashboard.Show();
                Close();
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
}
