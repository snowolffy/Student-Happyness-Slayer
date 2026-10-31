using System.Windows.Controls;

namespace OnionProcOparetor.Console;

/// <summary>
/// แท็บ "ประวัติเว็บ" ของหน้า Website Control (WP2-1, 2026-10-31) - หน้าว่างรอ WP2-4
/// ไม่มี logic อะไรเลย (ไม่ต้องมี Initialize(ApiClient) เพราะยังไม่เรียก API ไหน) - WP2-4 มาเติม
/// field/constructor/Initialize ของตัวเองได้ในไฟล์นี้โดยไม่กระทบ WebsiteControlPage
/// </summary>
public partial class WebsiteHistoryPlaceholderTab : UserControl
{
    public WebsiteHistoryPlaceholderTab()
    {
        InitializeComponent();
    }
}
