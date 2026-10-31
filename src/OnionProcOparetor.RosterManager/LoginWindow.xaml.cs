using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using OnionProcOparetor.RosterManager.Models;
using OnionProcOparetor.RosterManager.Services;

namespace OnionProcOparetor.RosterManager;

public partial class LoginWindow : Window
{
    private ApiClient _apiClient = new();

    public LoginWindow()
    {
        InitializeComponent();

        var connection = SettingsService.Current.Connection;
        ServerIpBox.Text = connection.ServerHost;
        PortBox.Text = connection.ServerPort;
        UseHttpsCheckBox.IsChecked = connection.UseHttps;
        UsernameBox.Text = connection.Username;

        LoadHeroImage();
    }

    /// <summary>ภาพฝั่งซ้ายของการ์ด - ลองโหลดจากไฟล์ "ข้างแอป" ก่อน ถ้าไม่มีให้ fallback ไปใช้ภาพที่
    /// ฝังในตัว exe เอง (pack resource) - pattern เดียวกับ Console/MainWindow.xaml.cs LoadHeroImage
    /// ต้องไม่ throw ขึ้นไปเด็ดขาด (ลบไฟล์ภาพทิ้งแล้วแอปต้องไม่ crash)</summary>
    private void LoadHeroImage()
    {
        var externalPath = Path.Combine(AppContext.BaseDirectory, "Assets", "LoginHero.png");
        try
        {
            if (File.Exists(externalPath))
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(externalPath, UriKind.Absolute);
                bitmap.EndInit();
                HeroImage.Source = bitmap;
                return;
            }
        }
        catch (Exception ex)
        {
            RosterDiagnostics.WriteLine($"LoginWindow: โหลด {externalPath} ไม่สำเร็จ ({ex.Message}) - ใช้ภาพที่ฝังไว้แทน");
        }

        HeroImage.Source = new BitmapImage(new Uri("pack://application:,,,/Assets/LoginHero.png"));
    }

    private async void ConnectButton_Click(object sender, RoutedEventArgs e)
    {
        var serverIp = ServerIpBox.Text.Trim();
        var port = PortBox.Text.Trim();
        var username = UsernameBox.Text.Trim();
        var password = PasswordBox.Password;
        var useHttps = UseHttpsCheckBox.IsChecked == true;

        if (string.IsNullOrEmpty(serverIp) || string.IsNullOrEmpty(username))
        {
            StatusText.Text = "กรุณากรอก Server IP และ Username";
            return;
        }

        var serverAddress = string.IsNullOrEmpty(port) ? serverIp : $"{serverIp}:{port}";

        ConnectButton.IsEnabled = false;
        StatusText.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "Brush.Status.Offline");
        StatusText.Text = "กำลังเชื่อมต่อ...";

        _apiClient = new ApiClient();
        _apiClient.SetServer(serverAddress, useHttps);
        var (success, message) = await _apiClient.LoginAsync(username, password);

        if (success)
        {
            SaveConnectionAsDefault(serverIp, port, useHttps, username);

            if (_apiClient.HasDefaultPassword)
            {
                StatusText.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "Brush.Status.Online");
                StatusText.Text = "เชื่อมต่อสำเร็จ กรุณาเปลี่ยน password ก่อนใช้งาน...";

                var changeWindow = new ChangeMyPasswordWindow(_apiClient);
                changeWindow.Show();
                Close();
                return;
            }

            StatusText.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "Brush.Status.Online");
            StatusText.Text = "เชื่อมต่อสำเร็จ กำลังเปิด Roster Manager...";

            ToastService.Success($"เข้าสู่ระบบสำเร็จ ({username})");

            var roster = new RosterWindow(_apiClient);
            roster.Show();
            Close();
        }
        else
        {
            StatusText.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "Brush.Danger.Default");
            StatusText.Text = message;
            ConnectButton.IsEnabled = true;
        }
    }

    private static void SaveConnectionAsDefault(string serverHost, string serverPort, bool useHttps, string username)
    {
        var current = SettingsService.Current;
        var updated = new RosterSettings
        {
            Theme = current.Theme,
            Connection = new RosterConnectionSettings
            {
                ServerHost = serverHost,
                ServerPort = string.IsNullOrEmpty(serverPort) ? current.Connection.ServerPort : serverPort,
                UseHttps = useHttps,
                Username = username
            }
        };

        SettingsService.Save(updated);
    }
}
