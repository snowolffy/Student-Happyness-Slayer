using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using OnionProcOparetor.Console.Models;
using OnionProcOparetor.Console.Services;
using QRCoder;

namespace OnionProcOparetor.Console;

/// <summary>
/// "แสดง QR..." ของการ์ดรหัสฉุกเฉิน (WP3-4, 2026-10-31) - รับ settings ที่โหลดไว้แล้วจาก SettingsPage
/// มาเรนเดอร์ QR จาก OtpAuthUri ด้วย QRCoder (ไม่ยิง request ซ้ำ) ปุ่มทดสอบยิง
/// POST /api/settings/emergency-code/test ตรงๆ เพื่อยืนยันว่า Authenticator ที่ครูเพิ่งสแกนคำนวณรหัส
/// ตรงกับที่ Server มี (สูตรเดียวกับ Agent ใช้ตรวจแบบ offline - ดู Server/Services/TotpService.cs)
/// </summary>
public partial class EmergencyCodeQrWindow : UserControl
{
    /// <summary>UI-3b (2026-11-03): หน้าต่างนี้ไม่มีผลลัพธ์ให้อ่าน - raise เมื่อกดปิดเท่านั้น ให้ผู้เปิดเรียก
    /// DialogHost.CloseTop ต่อ</summary>
    public event Action? CloseRequested;

    private readonly ApiClient _apiClient;

    public EmergencyCodeQrWindow(ApiClient apiClient, EmergencyCodeSettingsDto settings)
    {
        InitializeComponent();
        _apiClient = apiClient;

        SecretKeyBox.Text = settings.SecretBase32;
        RenderQrCode(settings.OtpAuthUri);
    }

    private void RenderQrCode(string otpAuthUri)
    {
        try
        {
            using var generator = new QRCodeGenerator();
            using var qrData = generator.CreateQrCode(otpAuthUri, QRCodeGenerator.ECCLevel.M);
            var pngBytes = new PngByteQRCode(qrData).GetGraphic(8);

            var bitmap = new BitmapImage();
            using var stream = new MemoryStream(pngBytes);
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            QrImage.Source = bitmap;
        }
        catch (Exception ex)
        {
            StatusText.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "Brush.Danger.Default");
            StatusText.Text = $"สร้าง QR ไม่สำเร็จ: {ex.Message} (ใช้ key ด้านล่างพิมพ์เองได้)";
        }
    }

    private void CopyKeyButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(SecretKeyBox.Text);
            ToastService.Success("คัดลอก key แล้ว");
        }
        catch (Exception ex)
        {
            ToastService.Error($"คัดลอกไม่สำเร็จ: {ex.Message}");
        }
    }

    private async void TestCodeButton_Click(object sender, RoutedEventArgs e)
    {
        var code = TestCodeBox.Text.Trim();
        if (code.Length != 6 || !code.All(char.IsAsciiDigit))
        {
            StatusText.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "Brush.Danger.Default");
            StatusText.Text = "กรอกรหัส 6 หลักก่อน";
            return;
        }

        TestCodeButton.IsEnabled = false;
        StatusText.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "Brush.Status.Offline");
        StatusText.Text = "กำลังทดสอบ...";

        var (valid, message) = await _apiClient.TestEmergencyCodeAsync(code);

        StatusText.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty,
            valid ? "Brush.Status.Online" : "Brush.Danger.Default");
        StatusText.Text = message;
        TestCodeButton.IsEnabled = true;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => CloseRequested?.Invoke();
}
