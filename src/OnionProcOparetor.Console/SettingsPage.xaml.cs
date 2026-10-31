using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using OnionProcOparetor.Console.Models;
using OnionProcOparetor.Console.Services;

namespace OnionProcOparetor.Console;

/// <summary>
/// หน้าตั้งค่าใน shell (WP6) - เดิมคือ SettingsWindow ค่าที่ตั้งได้และการตรวจค่า (SettingsService.Save)
/// เหมือนเดิมทุกอย่าง ผลการบันทึก/error ไปที่ toast
///
/// WP3-5 (2026-10-31): เพิ่มคอลัมน์ขวา "ทั้งระบบ (Admin)" - Sign-in นักเรียน, Staff session (อ่านอย่าง
/// เดียว - ไม่มี API ตั้งค่า), รหัสฉุกเฉิน (placeholder ว่าง - WP3-4), ความปลอดภัย (HTTPS - thumbprint
/// เท่านั้น), สำรองข้อมูล ทุกคอลัมน์ขวาโหลด/บันทึกแยกจากคอลัมน์ซ้ายโดยสิ้นเชิง (เรียก API คนละชุด)
/// </summary>
public partial class SettingsPage : UserControl
{
    private ApiClient? _apiClient;

    public SettingsPage()
    {
        InitializeComponent();

        // เติมใหม่จากค่าที่บันทึกไว้จริงทุกครั้งที่สลับมาหน้านี้ - ค่าบางตัว (ธีม/sidebar) ถูกเปลี่ยนจาก
        // ที่อื่นได้ระหว่างที่ไม่ได้อยู่หน้านี้ และของที่พิมพ์ค้างไว้โดยไม่กดบันทึกไม่ควรดูเหมือนถูกบันทึกแล้ว
        // ใช้ IsVisibleChanged ไม่ใช่ Loaded - Loaded ของเนื้อหาแท็บยิงตั้งแต่เปิด Dashboard ครั้งเดียว
        // (ดู ProgramReviewPage) ไม่ได้ยิงทุกครั้งที่สลับกลับมา
        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is true)
            {
                LoadFromSettings();
                _ = LoadAdminSettingsAsync();
            }
        };
    }

    /// <summary>เรียกครั้งเดียวจาก DashboardWindow หลัง InitializeComponent</summary>
    public void Initialize(ApiClient apiClient) => _apiClient = apiClient;

    private void LoadFromSettings()
    {
        var settings = SettingsService.Current;
        VncLimitBox.Text = settings.Vnc.MaxConcurrentSessions.ToString();
        KeepLiveViewOpenCheckBox.IsChecked = settings.Vnc.KeepLiveViewOpenOnTabLeave;
        ShowTailscaleActionsCheckBox.IsChecked = settings.Ui.ShowTailscaleActions;
        PollIntervalBox.Text = settings.Connection.DashboardPollIntervalSeconds.ToString();
        ServerHostBox.Text = settings.Connection.ServerHost;
        ServerPortBox.Text = settings.Connection.ServerPort;
        ThaiTimeRadio.IsChecked = !settings.Ui.ShowTimesInUtc;
        UtcTimeRadio.IsChecked = settings.Ui.ShowTimesInUtc;
    }

    private void ChangeMyPasswordButton_Click(object sender, RoutedEventArgs e)
    {
        if (_apiClient is null)
        {
            return;
        }

        var dialog = new ChangeMyPasswordWindow(_apiClient) { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() == true)
        {
            ToastService.Success("เปลี่ยนรหัสผ่านของคุณแล้ว");
        }
    }

    private void AboutButton_Click(object sender, RoutedEventArgs e) =>
        new AboutWindow { Owner = Window.GetWindow(this) }.ShowDialog();

    private void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        LoadFromSettings();
        ToastService.Info("คืนค่าที่บันทึกไว้แล้ว");
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(VncLimitBox.Text.Trim(), out var vncLimit))
        {
            ToastService.Error("จำนวนหน้าจอที่เปิดดูพร้อมกันต้องเป็นตัวเลข");
            return;
        }

        if (!int.TryParse(PollIntervalBox.Text.Trim(), out var pollInterval))
        {
            ToastService.Error("รอบรีเฟรชอัตโนมัติต้องเป็นตัวเลข (วินาที)");
            return;
        }

        var current = SettingsService.Current;
        var updated = new ConsoleSettings
        {
            Vnc = new VncSettings
            {
                MaxConcurrentSessions = vncLimit,
                KeepLiveViewOpenOnTabLeave = KeepLiveViewOpenCheckBox.IsChecked ?? false
            },
            Connection = new ConnectionSettings
            {
                ServerHost = ServerHostBox.Text.Trim(),
                ServerPort = ServerPortBox.Text.Trim(),
                DashboardPollIntervalSeconds = pollInterval
            },
            // คัดลอกกลุ่ม Ui เดิมมาทั้งก้อนแล้วทับเฉพาะช่องที่หน้านี้แก้ - ธีมกับสถานะ sidebar ถูกตั้ง
            // จากที่อื่น (ปุ่มบน sidebar) ถ้าสร้าง UiSettings ใหม่เปล่า ๆ ตรงนี้ กดบันทึกหน้านี้ทีเดียว
            // ค่าพวกนั้นจะถูกล้างกลับเป็น default ทันที
            Ui = new UiSettings
            {
                Theme = current.Ui.Theme,
                SidebarCollapsed = current.Ui.SidebarCollapsed,
                ShowTailscaleActions = ShowTailscaleActionsCheckBox.IsChecked ?? false,
                ShowTimesInUtc = UtcTimeRadio.IsChecked == true
            }
        };

        var (success, error) = SettingsService.Save(updated);
        if (!success)
        {
            ToastService.Error(error ?? "บันทึกการตั้งค่าไม่สำเร็จ");
            return;
        }

        ToastService.Success("บันทึกการตั้งค่าแล้ว");
    }

    // ==================== คอลัมน์ขวา: ทั้งระบบ (Admin) - WP3-5 ====================

    private SignInSettingsDto? _signInSettings;

    /// <summary>โหลดทั้ง 3 อย่างที่มี API จริง (Sign-in, thumbprint cert, backup) - Staff session/
    /// รหัสฉุกเฉินไม่มี API ให้โหลด (ดู XAML) ซ่อนคอลัมน์ทั้งหมดให้ Teacher แทนปล่อยให้เจอ 403</summary>
    private async Task LoadAdminSettingsAsync()
    {
        if (_apiClient is null)
        {
            return;
        }

        if (!_apiClient.IsAdmin)
        {
            AdminOnlyNoticeCard.Visibility = Visibility.Visible;
            AdminSettingsPanel.Visibility = Visibility.Collapsed;
            return;
        }

        AdminOnlyNoticeCard.Visibility = Visibility.Collapsed;
        AdminSettingsPanel.Visibility = Visibility.Visible;

        await LoadSignInSettingsAsync();
        await LoadEmergencyCodeSettingsAsync();
        await LoadCertificateThumbprintAsync();
        await LoadBackupSettingsAsync();
    }

    private async Task LoadSignInSettingsAsync()
    {
        _signInSettings = await _apiClient!.GetSignInSettingsAsync();
        if (_signInSettings is null)
        {
            ToastService.Error("โหลดตั้งค่า Sign-in ไม่สำเร็จ");
            return;
        }

        RequireLoginModeCombo.SelectedIndex = _signInSettings.RequireLoginMode;
        GuestModeCheckBox.IsChecked = _signInSettings.GuestModeEnabled;
        EmergencyCodeEnabledCheckBox.IsChecked = _signInSettings.EmergencyCodeEnabled;
        PasswordMinLengthBox.Text = _signInSettings.PasswordMinLength.ToString();
        ForceChangePasswordCheckBox.IsChecked = _signInSettings.ForceChangePasswordOnFirstLogin;
        LockoutFailedAttemptsBox.Text = _signInSettings.LockoutFailedAttempts.ToString();
        MultiDeviceModeCombo.SelectedIndex = _signInSettings.MultiDeviceMode;
        AutoLogoutIdleMinutesBox.Text = _signInSettings.AutoLogoutIdleMinutes.ToString();
        LogoutOnShutdownCheckBox.IsChecked = _signInSettings.LogoutOnShutdown;
        TeacherCanResetPasswordCheckBox.IsChecked = _signInSettings.TeacherCanResetPassword;
        ShowStudentNameCheckBox.IsChecked = _signInSettings.ShowStudentNameInConsole;
        LoginScreenMessageBox.Text = _signInSettings.LoginScreenMessage ?? string.Empty;
        LockScreenDefaultMessageBox.Text = _signInSettings.LockScreenDefaultMessage;
    }

    private async void ReloadSignInSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        await LoadSignInSettingsAsync();
        ToastService.Info("โหลดตั้งค่า Sign-in ใหม่แล้ว");
    }

    private async void SaveSignInSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        if (_apiClient is null || _signInSettings is null)
        {
            return;
        }

        if (!int.TryParse(PasswordMinLengthBox.Text.Trim(), out var passwordMinLength) ||
            !int.TryParse(LockoutFailedAttemptsBox.Text.Trim(), out var lockoutAttempts) ||
            !int.TryParse(AutoLogoutIdleMinutesBox.Text.Trim(), out var idleMinutes))
        {
            ToastService.Error("ความยาวรหัสผ่าน / จำนวนครั้งที่ล็อก / นาทีไม่ได้ใช้งาน ต้องเป็นตัวเลข");
            return;
        }

        _signInSettings.RequireLoginMode = RequireLoginModeCombo.SelectedIndex;
        _signInSettings.GuestModeEnabled = GuestModeCheckBox.IsChecked ?? false;
        _signInSettings.EmergencyCodeEnabled = EmergencyCodeEnabledCheckBox.IsChecked ?? false;
        _signInSettings.PasswordMinLength = passwordMinLength;
        _signInSettings.ForceChangePasswordOnFirstLogin = ForceChangePasswordCheckBox.IsChecked ?? false;
        _signInSettings.LockoutFailedAttempts = lockoutAttempts;
        _signInSettings.MultiDeviceMode = MultiDeviceModeCombo.SelectedIndex;
        _signInSettings.AutoLogoutIdleMinutes = idleMinutes;
        _signInSettings.LogoutOnShutdown = LogoutOnShutdownCheckBox.IsChecked ?? false;
        _signInSettings.TeacherCanResetPassword = TeacherCanResetPasswordCheckBox.IsChecked ?? false;
        _signInSettings.ShowStudentNameInConsole = ShowStudentNameCheckBox.IsChecked ?? false;
        _signInSettings.LoginScreenMessage = LoginScreenMessageBox.Text.Trim();
        _signInSettings.LockScreenDefaultMessage = LockScreenDefaultMessageBox.Text.Trim();

        var (success, message) = await _apiClient.UpdateSignInSettingsAsync(_signInSettings);
        if (success)
        {
            ToastService.Success(message);
        }
        else
        {
            ToastService.Error(message);
        }
    }

    // ==================== รหัสฉุกเฉิน (WP3-4, 2026-10-31) ====================

    private EmergencyCodeSettingsDto? _emergencyCodeSettings;

    private async Task LoadEmergencyCodeSettingsAsync()
    {
        _emergencyCodeSettings = await _apiClient!.GetEmergencyCodeSettingsAsync();
        if (_emergencyCodeSettings is null)
        {
            EmergencyCodeStatusText.Text = "โหลดสถานะรหัสฉุกเฉินไม่สำเร็จ";
            return;
        }

        var rotated = _emergencyCodeSettings.RotatedAt is { } rotatedAt
            ? $"เปลี่ยนล่าสุด {TimeDisplay.Text(rotatedAt)} โดย {_emergencyCodeSettings.RotatedByUsername}"
            : $"สร้างตอนติดตั้งระบบ {TimeDisplay.Text(_emergencyCodeSettings.CreatedAt)} (ยังไม่เคยเปลี่ยน)";

        var usage = _emergencyCodeSettings.LastUsageAt is { } lastUsageAt
            ? $"ใช้แล้ว {_emergencyCodeSettings.UsageCount} ครั้ง - ล่าสุด: {_emergencyCodeSettings.LastUsageMachineName} · {TimeDisplay.Text(lastUsageAt)}"
            : "ยังไม่เคยถูกใช้";

        EmergencyCodeStatusText.Text = $"{rotated}\n{usage}";
    }

    private async void ShowEmergencyCodeQrButton_Click(object sender, RoutedEventArgs e)
    {
        if (_apiClient is null)
        {
            return;
        }

        // โหลดใหม่เสมอก่อนเปิด (ไม่ใช้ค่าที่ cache ไว้ตั้งแต่เปิดหน้า) เผื่อมีคนเปลี่ยน secret จากเครื่องอื่นระหว่างนั้น
        var settings = await _apiClient.GetEmergencyCodeSettingsAsync();
        if (settings is null)
        {
            ToastService.Error("โหลดข้อมูลรหัสฉุกเฉินไม่สำเร็จ");
            return;
        }

        _emergencyCodeSettings = settings;
        new EmergencyCodeQrWindow(_apiClient, settings) { Owner = Window.GetWindow(this) }.ShowDialog();
    }

    private void ShowEmergencyCodeUsageButton_Click(object sender, RoutedEventArgs e)
    {
        if (_apiClient is null)
        {
            return;
        }

        new EmergencyCodeUsageWindow(_apiClient) { Owner = Window.GetWindow(this) }.ShowDialog();
    }

    private async void RotateEmergencyCodeButton_Click(object sender, RoutedEventArgs e)
    {
        if (_apiClient is null)
        {
            return;
        }

        var confirmed = System.Windows.MessageBox.Show(
            "เปลี่ยน secret รหัสฉุกเฉินใหม่ทั้งหมด?\n\nรหัสเก่าจะใช้ไม่ได้อีกทันที ต้องลง Agent MSI ใหม่ทุกเครื่องและแก้ Script Properties ของ Apps Script ด้วย ไม่งั้นเครื่องนักเรียนจะยังตรวจด้วย secret เก่าที่ติดตั้งไว้",
            "ยืนยันเปลี่ยน secret",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);

        if (confirmed != System.Windows.MessageBoxResult.Yes)
        {
            return;
        }

        RotateEmergencyCodeButton.IsEnabled = false;
        var (settings, message) = await _apiClient.RotateEmergencyCodeAsync();

        if (settings is not null)
        {
            ToastService.Success(message);
            await LoadEmergencyCodeSettingsAsync();
            // เปิด QR ใหม่ให้ครูสแกนต่อทันที - ไม่งั้นต้องกด "แสดง QR..." แยกอีกรอบเพื่อเอา secret ใหม่ไปใส่ MSI/Apps Script
            new EmergencyCodeQrWindow(_apiClient, settings) { Owner = Window.GetWindow(this) }.ShowDialog();
        }
        else
        {
            ToastService.Error(message);
        }

        RotateEmergencyCodeButton.IsEnabled = true;
    }

    private async Task LoadCertificateThumbprintAsync()
    {
        var thumbprint = await _apiClient!.GetCertificateThumbprintAsync();
        CertificateThumbprintText.Text = string.IsNullOrWhiteSpace(thumbprint)
            ? "อ่านไม่ได้ (Server อาจยังไม่พร้อม)"
            : thumbprint;
    }

    private async void ExportCertificateButton_Click(object sender, RoutedEventArgs e)
    {
        if (_apiClient is null)
        {
            return;
        }

        var bytes = await _apiClient.DownloadCertificateAsync();
        if (bytes is null)
        {
            ToastService.Error("ดึงใบรับรองจาก Server ไม่สำเร็จ");
            return;
        }

        var dialog = new SaveFileDialog
        {
            FileName = "onionprocoparetor-server.cer",
            Filter = "Certificate (*.cer)|*.cer|All files (*.*)|*.*"
        };

        if (dialog.ShowDialog(Window.GetWindow(this)) == true)
        {
            try
            {
                File.WriteAllBytes(dialog.FileName, bytes);
                ToastService.Success($"ส่งออกใบรับรองไปที่ {dialog.FileName} แล้ว");
            }
            catch (Exception ex)
            {
                ToastService.Error($"บันทึกไฟล์ไม่สำเร็จ: {ex.Message}");
            }
        }
    }

    private BackupSettingsDto? _backupSettings;

    private async Task LoadBackupSettingsAsync()
    {
        _backupSettings = await _apiClient!.GetBackupSettingsAsync();
        if (_backupSettings is null)
        {
            ToastService.Error("โหลดตั้งค่าสำรองข้อมูลไม่สำเร็จ");
            return;
        }

        BackupFrequencyCombo.SelectedIndex = _backupSettings.Frequency;
        BackupRetentionCountBox.Text = _backupSettings.RetentionCount.ToString();
        BackupStoragePathBox.Text = _backupSettings.StoragePath ?? string.Empty;
        BackupLastRunText.Text = _backupSettings.LastBackupAt is { } last
            ? $"ล่าสุด: {TimeDisplay.Text(last)} · {(_backupSettings.LastBackupSizeBytes ?? 0) / 1024.0 / 1024.0:F1} MB"
            : "ยังไม่เคยสำรองข้อมูล";
    }

    private async void SaveBackupSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        if (_apiClient is null)
        {
            return;
        }

        if (!int.TryParse(BackupRetentionCountBox.Text.Trim(), out var retentionCount))
        {
            ToastService.Error("จำนวนชุดที่เก็บย้อนหลังต้องเป็นตัวเลข");
            return;
        }

        var storagePath = string.IsNullOrWhiteSpace(BackupStoragePathBox.Text) ? null : BackupStoragePathBox.Text.Trim();
        var (success, message) = await _apiClient.UpdateBackupSettingsAsync(BackupFrequencyCombo.SelectedIndex, retentionCount, storagePath);
        if (success)
        {
            ToastService.Success(message);
            await LoadBackupSettingsAsync();
        }
        else
        {
            ToastService.Error(message);
        }
    }

    private async void BackupNowButton_Click(object sender, RoutedEventArgs e)
    {
        if (_apiClient is null)
        {
            return;
        }

        var (success, message) = await _apiClient.BackupNowAsync();
        if (success)
        {
            ToastService.Success(message);
            await LoadBackupSettingsAsync();
        }
        else
        {
            ToastService.Error(message);
        }
    }
}
