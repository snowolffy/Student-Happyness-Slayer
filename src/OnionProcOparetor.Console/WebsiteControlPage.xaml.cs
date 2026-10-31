using System.Windows.Controls;
using OnionProcOparetor.Console.Services;

namespace OnionProcOparetor.Console;

/// <summary>
/// Website Control (2026-09-11) - แยกจากหน้ากฎโปรแกรม ไม่ปนกฎบล็อกเว็บกับกฎบล็อกโปรแกรม ใช้ global
/// enforcement toggle ตัวเดียวกับ Program Control (ไม่มี toggle แยกในนี้)
///
/// WP6 (2026-10-30): ย้ายจากหน้าต่างลอย (WebsiteControlWindow) มาเป็นหน้าใน shell
///
/// WP2-1 (2026-10-31): กลายเป็นแค่ "โครงแท็บ" - เนื้อหาจริงทั้งหมดอยู่ใน 4 UserControl แยกไฟล์
/// (WebsiteBlockTab / WebsiteAllowListPlaceholderTab / WebsiteAntiEvasionTab /
/// WebsiteHistoryPlaceholderTab) หน้านี้แค่ส่ง ApiClient ต่อให้แท็บที่ต้องใช้จริง (2 แท็บที่ยังว่าง
/// ไม่ต้องใช้ ApiClient เลย)
/// </summary>
public partial class WebsiteControlPage : UserControl
{
    public WebsiteControlPage()
    {
        InitializeComponent();
    }

    /// <summary>เรียกครั้งเดียวจาก DashboardWindow หลัง InitializeComponent</summary>
    public void Initialize(ApiClient apiClient)
    {
        BlockTab.Initialize(apiClient);
        AntiEvasionTab.Initialize(apiClient);
        // WP2-2 (2026-10-31): แท็บนี้ไม่ว่างแล้ว (เนื้อหาจริงของ WP2-2 มาแทน EmptyState เดิม) ต้องมี
        // ApiClient เหมือน BlockTab/AntiEvasionTab - จุดเดียวที่ WP2-2 ต้องแตะไฟล์นี้ (x:Name +
        // บรรทัดนี้) นอกเหนือจาก WebsiteAllowListPlaceholderTab.xaml(.cs) เอง ดู comment เดิมในไฟล์นั้น
        AllowListTab.Initialize(apiClient);
    }

    /// <summary>
    /// เลือกแท็บย่อยตาม Header (เช่น "ป้องกันการเลี่ยง") - ใช้เฉพาะโหมดตรวจ UI (--ui-preview --tab=...)
    /// ของ DashboardWindow ให้จับภาพแท็บย่อยที่ต้องการได้โดยตรง ไม่ต้องคลิกเองทีละแท็บ คืน true ถ้าเจอ
    /// และเลือกสำเร็จ (ดู DashboardWindow.cs ที่ลองเรียกต่อจากการหาแท็บหลักไม่เจอ)
    /// </summary>
    public bool TrySelectTab(string header)
    {
        foreach (TabItem item in InnerTabControl.Items)
        {
            if (string.Equals(item.Header as string, header, StringComparison.OrdinalIgnoreCase))
            {
                InnerTabControl.SelectedItem = item;
                return true;
            }
        }
        return false;
    }
}
