using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using OnionProcOparetor.Console.Services;

namespace OnionProcOparetor.Console;

/// <summary>
/// วางรายชื่อโดเมนทีละหลายบรรทัด แล้วเรียก /api/website-rules/bulk ทีเดียว (2026-09-12) - ผลลัพธ์
/// (เพิ่มกี่รายการ ข้ามกี่รายการ) เก็บไว้ที่ <see cref="ResultText"/> ให้ WebsiteControlWindow เอาไป
/// โชว์ต่อหลังปิดหน้าต่างนี้ (เหตุผลเดียวกับที่ AddEditWebsiteRuleWindow ใช้ DialogResult - แต่ที่นี่
/// ต้องมีข้อความเพิ่มเติมด้วยเพราะผลลัพธ์เป็นตัวเลขสองค่า ไม่ใช่แค่สำเร็จ/ไม่สำเร็จ)
/// </summary>
public partial class BulkAddWebsiteRulesWindow : UserControl
{
    /// <summary>UI-3b (2026-11-03): true = บันทึกสำเร็จ (อ่าน ResultText ต่อ) false = ยกเลิก</summary>
    public event Action<bool>? ResultChosen;

    private readonly ApiClient _apiClient;

    public string ResultText { get; private set; } = string.Empty;

    public BulkAddWebsiteRulesWindow(ApiClient apiClient)
    {
        InitializeComponent();
        _apiClient = apiClient;
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        var lines = DomainsBox.Text
            .Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .ToList();

        if (lines.Count == 0)
        {
            StatusText.Text = "กรุณาวางรายชื่อโดเมนอย่างน้อยหนึ่งบรรทัด";
            return;
        }

        StatusText.Text = "กำลังบันทึก...";
        var result = await _apiClient.AddWebsiteRulesBulkAsync(lines);

        if (result is null)
        {
            StatusText.Text = "บันทึกไม่สำเร็จ ลองใหม่อีกครั้ง";
            return;
        }

        ResultText = $"เพิ่ม {result.Added} รายการ (ข้าม {result.Skipped} รายการซ้ำ)";
        ResultChosen?.Invoke(true);
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        ResultChosen?.Invoke(false);
    }
}
