using System.Windows;
using System.Windows.Controls;

namespace OnionProcOparetor.Console;

/// <summary>
/// UI-3a (2026-11-03): เนื้อหาของกล่องยืนยัน - แยกออกจาก <see cref="ConfirmWindow"/> (ที่ยังคงเหลือเป็น
/// Window เปล่าๆ สำหรับ owner ที่ยังไม่มี DialogHost ของตัวเอง เช่น ActionPanelWindow/
/// LaunchableProgramsWindow ที่จะถูกย้ายใน UI-3c) เพื่อให้ใช้เนื้อหาเดียวกันได้ทั้งสองเส้นทาง: ฝังใน
/// DialogHost ตรงๆ (เส้นทางใหม่) หรือเป็น Content ของ Window ลอยแบบเดิม (เส้นทาง legacy)
///
/// ไม่รู้จัก DialogHost เลยโดยเจตนา - raise <see cref="ResultChosen"/> อย่างเดียว ให้ผู้ที่ฝัง control
/// นี้ (ConfirmWindow ของ Console) เป็นคนตัดสินใจว่าจะปิดยังไง (CloseTop ของ DialogHost หรือ
/// DialogResult+Close ของ Window) - รูปแบบนี้คือสิ่งที่ UI-3b ควรทำตามกับ dialog ลอยแบบสั้นที่เหลือ
/// (BroadcastMessage, OpenUrl, ExamMode, ...)
/// </summary>
public partial class ConfirmDialogContent : UserControl
{
    /// <summary>ข้อความที่ต้องพิมพ์ให้ตรงก่อนปุ่มยืนยันจะกดได้ - null = ยืนยันแบบธรรมดา (กดยืนยันได้เลย)</summary>
    private readonly string? _requiredPhrase;

    /// <summary>true (ยืนยันแล้ว) / false (ยกเลิก) - ไม่ raise เลยถ้าปิดด้วย Esc (DialogHost ปิดเองด้วย
    /// ผล default = false ซึ่งตรงกับ "ยกเลิก" พอดีอยู่แล้ว)</summary>
    public event System.Action<bool>? ResultChosen;

    public ConfirmDialogContent(string message, string title, string? requiredPhrase, string? confirmText)
    {
        InitializeComponent();
        TitleText.Text = title;
        MessageText.Text = message;
        _requiredPhrase = requiredPhrase;

        // ค่า default "ยืนยัน" ยังดีกว่า "YES" อยู่แล้ว แต่ทุกจุดที่ทำอะไรกับเครื่องจริงควรส่งกริยา
        // ของตัวเองมา เช่น "ปิดเครื่อง" / "ลบกฎ" - ปุ่มจะได้ตอบคำถาม "กดแล้วเกิดอะไร" ได้ด้วยตัวเอง
        if (!string.IsNullOrWhiteSpace(confirmText))
        {
            ConfirmButton.Content = confirmText;
        }

        if (_requiredPhrase is null)
        {
            return;
        }

        TypeToConfirmPanel.Visibility = Visibility.Visible;
        TypeToConfirmPrompt.Text = $"พิมพ์ \"{_requiredPhrase}\" เพื่อยืนยัน";
        ConfirmButton.IsEnabled = false;
    }

    private void TypeToConfirmBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        // กันไว้: ถ้าหน้าต่างนี้ถูกเรียกแบบยืนยันธรรมดา (ไม่ต้องพิมพ์ชื่อ) แล้ว WPF ดัน raise
        // TextChanged ระหว่าง InitializeComponent ตอนที่ _requiredPhrase ยังเป็น null อยู่ บรรทัด
        // ข้างล่างจะไป **ปิดปุ่มยืนยันทิ้งถาวร** ในกล่องยืนยันทุกใบของแอป (ยืนยันปิดเครื่อง, ปิด
        // enforcement, kill process ฯลฯ) - ทดสอบแล้วว่าตอนนี้ WPF ไม่ raise แต่นั่นเป็นรายละเอียด
        // ภายในที่เปลี่ยนได้ ไม่ใช่สัญญา จึงกันไว้ตรงนี้แทนที่จะพึ่งพฤติกรรมนั้น
        if (_requiredPhrase is null)
        {
            return;
        }

        // ต้องตรงเป๊ะรวมตัวพิมพ์เล็ก-ใหญ่: ทั้งหมดของการให้พิมพ์ชื่อคือการบังคับให้ "อ่านว่ากำลังลบอะไร"
        // ถ้าเทียบแบบไม่สนตัวพิมพ์ ก็ยังพอไหว แต่การเทียบเป๊ะทำให้ต้องมองของจริงก่อนพิมพ์จริง ๆ
        ConfirmButton.IsEnabled = string.Equals(TypeToConfirmBox.Text.Trim(), _requiredPhrase, System.StringComparison.Ordinal);
    }

    private void ConfirmButton_Click(object sender, RoutedEventArgs e) => ResultChosen?.Invoke(true);

    private void CancelButton_Click(object sender, RoutedEventArgs e) => ResultChosen?.Invoke(false);
}
