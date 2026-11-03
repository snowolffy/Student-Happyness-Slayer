using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace OnionProcOparetor.Console;

/// <summary>เปลี่ยน role ของบัญชีหนึ่ง (WP4b, 2026-10-30) - เรียกจากแท็บ "ผู้ใช้" ของ DashboardWindow
/// เฉพาะ Admin (ปุ่มที่เปิดหน้าต่างนี้ถูกซ่อนไว้ก่อนแล้วถ้า login เป็น Teacher - ดู ApplyRolePermissions)</summary>
public partial class ChangeUserRoleWindow : UserControl
{
    /// <summary>UI-3b (2026-11-03): true = บันทึกแล้ว (อ่าน NewRole ต่อ) false = ยกเลิก</summary>
    public event Action<bool>? ResultChosen;

    /// <summary>0 = Admin, 1 = Teacher (ตรง enum AdminRole ฝั่ง Server)</summary>
    public int NewRole { get; private set; }

    public ChangeUserRoleWindow(string username, string currentRole)
    {
        InitializeComponent();
        TitleText.Text = $"เปลี่ยน Role - {username}";

        var currentItem = RoleCombo.Items.Cast<ComboBoxItem>()
            .FirstOrDefault(i => string.Equals((string)i.Content, currentRole, System.StringComparison.OrdinalIgnoreCase));
        RoleCombo.SelectedItem = currentItem ?? RoleCombo.Items[0];
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (RoleCombo.SelectedItem is not ComboBoxItem selected)
        {
            StatusText.Text = "กรุณาเลือก role";
            StatusText.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Danger.Default");
            return;
        }

        NewRole = int.Parse((string)selected.Tag);
        ResultChosen?.Invoke(true);
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        ResultChosen?.Invoke(false);
    }
}
