using System.Windows;
using OnionProcOparetor.Console.Services;

namespace OnionProcOparetor.Console;

public partial class ConfirmWindow : Window
{
    /// <summary>
    /// ข้อความที่ต้องพิมพ์ให้ตรงก่อนปุ่มยืนยันจะกดได้ - null = ยืนยันแบบธรรมดา (กด YES ได้เลย)
    /// </summary>
    private readonly string? _requiredPhrase;

    private ConfirmWindow(string message, string title, string? requiredPhrase, string? confirmText)
    {
        InitializeComponent();
        Title = title;
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

    private void TypeToConfirmBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        // กันไว้: ถ้าหน้าต่างนี้ถูกเรียกแบบยืนยันธรรมดา (ไม่ต้องพิมพ์ชื่อ) แล้ว WPF ดัน raise
        // TextChanged ระหว่าง InitializeComponent ตอนที่ _requiredPhrase ยังเป็น null อยู่ บรรทัด
        // ข้างล่างจะไป **ปิดปุ่ม YES ทิ้งถาวร** ในกล่องยืนยันทุกใบของแอป (ยืนยันปิดเครื่อง, ปิด
        // enforcement, kill process ฯลฯ) - ทดสอบแล้วว่าตอนนี้ WPF ไม่ raise แต่นั่นเป็นรายละเอียด
        // ภายในที่เปลี่ยนได้ ไม่ใช่สัญญา จึงกันไว้ตรงนี้แทนที่จะพึ่งพฤติกรรมนั้น
        if (_requiredPhrase is null)
        {
            return;
        }

        // ต้องตรงเป๊ะรวมตัวพิมพ์เล็ก-ใหญ่: ทั้งหมดของการให้พิมพ์ชื่อคือการบังคับให้ "อ่านว่ากำลังลบอะไร"
        // ถ้าเทียบแบบไม่สนตัวพิมพ์ ก็ยังพอไหว แต่การเทียบเป๊ะทำให้ต้องมองของจริงก่อนพิมพ์จริง ๆ
        ConfirmButton.IsEnabled = string.Equals(TypeToConfirmBox.Text.Trim(), _requiredPhrase, StringComparison.Ordinal);
    }

    private void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    /// <summary>
    /// Drop-in replacement สำหรับ MessageBox.Show(message, title, YesNo, Warning) เดิม - themed ตาม
    /// design system ของแอพ (Card/ButtonDanger/ButtonSecondary) แทนที่จะเป็น native OS dialog ที่ดูเป็น
    /// คนละแอพ ตรงจุดเดียวที่ครูต้องหยุดคิดก่อนทำ action ที่ทำคืนไม่ได้
    /// </summary>
    public static bool Show(string message, string title, Window? owner = null, string? confirmText = null)
    {
        var window = new ConfirmWindow(message, title, requiredPhrase: null, confirmText) { Owner = owner };
        return window.ShowDialog() == true;
    }

    /// <summary>
    /// เหมือน <see cref="Show"/> แต่ต้องพิมพ์ชื่อของสิ่งที่กำลังจะลบให้ตรงก่อนถึงจะกดยืนยันได้
    ///
    /// ใช้เฉพาะ "ปุ่มลบ" เท่านั้น (ลบกฎ / ลบผู้ใช้) ไม่ใช้กับคำสั่งอย่างปิดเครื่องทั้งห้อง - ตัดสินใจ
    /// ไว้แบบนี้โดยเจตนา เพราะการปิดเครื่องแม้จะกวนแต่ก็ทำใหม่ได้ ส่วนของที่ลบไปแล้วไม่มีทางเอากลับ
    /// แรงเสียดทานที่ใส่ไปทุกที่จนกลายเป็นพิธีกรรมจะถูกคนกดผ่านโดยไม่อ่าน ซึ่งแย่กว่าไม่มีเสียอีก
    /// </summary>
    public static bool ShowWithTypeToConfirm(
        string message, string title, string requiredPhrase, Window? owner = null, string? confirmText = null)
    {
        var window = new ConfirmWindow(message, title, requiredPhrase, confirmText) { Owner = owner };
        return window.ShowDialog() == true;
    }

#if DEBUG
    /// <summary>
    /// สร้างหน้าต่างแบบไม่ modal สำหรับโหมดตรวจ UI (--dialog=confirm) - ShowDialog บล็อกจนกว่าจะปิด
    /// จึงถ่ายภาพระหว่างนั้นไม่ได้ มีเฉพาะ build แบบ Debug
    /// </summary>
    public static ConfirmWindow CreatePreview(bool typeToConfirm) =>
        typeToConfirm
            ? new ConfirmWindow(
                "กำลังจะลบกฎ \"Block Roblox\" ออกจากระบบถาวร การกระทำนี้ย้อนกลับไม่ได้",
                "ยืนยันการลบกฎ", "Block Roblox", "ลบกฎนี้")
            : new ConfirmWindow(
                "จะสั่งปิดเครื่องที่เลือกไว้ทั้งหมด หน่วงเวลา 10 วินาทีก่อนดับจริง",
                "ยืนยันการปิดเครื่อง", requiredPhrase: null, "ปิดเครื่อง");
#endif
}
