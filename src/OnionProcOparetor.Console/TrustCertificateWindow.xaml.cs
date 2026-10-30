using System.Windows;
using OnionProcOparetor.Console.Services;

namespace OnionProcOparetor.Console;

/// <summary>
/// หน้าต่าง "ไว้ใจ certificate นี้" (WP1b, 2026-10-30) - เปิดจาก MainWindow หลัง login ด้วย HTTPS พังเพราะ
/// ยังไม่รู้จัก cert ของ host:port นี้ (ดู ServerCertificatePinning สำหรับ flow เต็มและเหตุผล) โชว์
/// thumbprint/subject/issuer ให้ผู้ใช้เทียบกับที่ Server แสดงเอง ไม่ได้ตรวจสอบอะไรเพิ่มในโค้ดนี้ - ความ
/// ปลอดภัยทั้งหมดอยู่ที่ "คนอ่านแล้วเทียบจริง" กับ fail-closed ของ ServerCertificatePinning.Validate
/// </summary>
public partial class TrustCertificateWindow : Window
{
    public bool Trusted { get; private set; }

    /// <summary>isChanged = true: cert ไม่ตรงกับที่เคย pin ไว้ก่อน (อันตรายกว่า) บังคับติ๊ก checkbox ก่อนกดไว้ใจได้</summary>
    public TrustCertificateWindow(
        string host, int port, ServerCertificatePinning.SeenCertificate certificate,
        bool isChanged, string? previousThumbprint = null)
    {
        InitializeComponent();

        HostText.Text = $"{host}:{port}";
        ThumbprintText.Text = FormatThumbprint(certificate.Thumbprint);
        SubjectText.Text = $"Issued to: {certificate.Subject}";
        IssuerText.Text = $"Issuer: {certificate.Issuer}";
        ExpiresText.Text = certificate.NotAfter.ToLocalTime().ToString("d MMM yyyy HH:mm");

        if (isChanged)
        {
            TitleText.Text = "คำเตือน: Certificate เปลี่ยนไปจากที่เคยไว้ใจ";
            ChangedWarningBanner.Visibility = Visibility.Visible;
            VerifiedCheckBox.Visibility = Visibility.Visible;
            TrustButton.IsEnabled = false;

            if (!string.IsNullOrEmpty(previousThumbprint))
            {
                PreviousThumbprintPanel.Visibility = Visibility.Visible;
                PreviousThumbprintText.Text = FormatThumbprint(previousThumbprint);
            }
        }
        else
        {
            TitleText.Text = "ไว้ใจ certificate นี้หรือไม่?";
            StatusText.Text = "ยังไม่เคยต่อ host:port นี้ด้วย HTTPS มาก่อน - เทียบ thumbprint ข้างบนกับที่ Server แสดง (เช่นหน้าจอ/log ของเครื่อง Server) ให้ตรงกันก่อนกดไว้ใจ";
        }
    }

    private static string FormatThumbprint(string thumbprint)
    {
        // ใส่ช่องว่างทุก 2 ตัวอักษร (เหมือนที่ certmgr.msc/browser แสดง) อ่าน/เทียบด้วยตาง่ายกว่า hex ดิบยาว
        var chars = thumbprint.ToUpperInvariant();
        var grouped = new System.Text.StringBuilder();
        for (var i = 0; i < chars.Length; i += 2)
        {
            if (i > 0) grouped.Append(' ');
            grouped.Append(chars, i, Math.Min(2, chars.Length - i));
        }
        return grouped.ToString();
    }

    private void VerifiedCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
    {
        TrustButton.IsEnabled = VerifiedCheckBox.IsChecked == true;
    }

    private void TrustButton_Click(object sender, RoutedEventArgs e)
    {
        Trusted = true;
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        Trusted = false;
        DialogResult = false;
        Close();
    }
}
