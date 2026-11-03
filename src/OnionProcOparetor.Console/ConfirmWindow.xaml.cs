using System.Windows;
using System.Windows.Threading;

namespace OnionProcOparetor.Console;

/// <summary>
/// Drop-in replacement สำหรับ MessageBox.Show(message, title, YesNo, Warning) เดิม - themed ตาม
/// design system ของแอพ (Card/ButtonDanger/ButtonSecondary) แทนที่จะเป็น native OS dialog
///
/// UI-3a (2026-11-03): migrate ไปใช้ DialogHost ของ MainWindow แล้ว **เมื่อ owner เป็น MainWindow**
/// (ดู ShowViaHost ด้านล่าง) - owner อื่นที่ยังไม่มี DialogHost ของตัวเอง (ActionPanelWindow,
/// LaunchableProgramsWindow, RunningProcessesWindow, WebsiteCatalogSourcesWindow - ทั้งหมดจะถูกย้าย
/// เป็น UserControl ใน UI-3c) ยัง fallback ไปเปิดเป็น Window ลอยแบบเดิมเหมือนก่อน UI-3a ทุกอย่าง -
/// **public call site (Show/ShowWithTypeToConfirm) เหมือนเดิมทุกตัวโดยไม่ต้องแก้ผู้เรียกเลย** เพราะ
/// ส่วนที่ต่างกัน (DialogHost vs Window.ShowDialog) ถูกซ่อนอยู่หลัง static method นี้ทั้งหมด
///
/// เนื้อหาจริงของกล่องยืนยัน (ข้อความ, ปุ่ม, ช่องพิมพ์ชื่อยืนยัน) อยู่ใน ConfirmDialogContent.xaml/.cs
/// ใช้ร่วมกันทั้งสองเส้นทาง - ดูที่นั่นสำหรับ validation logic เดิมทั้งหมด
/// </summary>
public partial class ConfirmWindow : Window
{
    private ConfirmWindow(string message, string title, string? requiredPhrase, string? confirmText)
    {
        InitializeComponent();
        Title = title;

        var content = new ConfirmDialogContent(message, title, requiredPhrase, confirmText) { Margin = new Thickness(24) };
        content.ResultChosen += result =>
        {
            DialogResult = result;
            Close();
        };
        Content = content;
    }

    public static bool Show(string message, string title, Window? owner = null, string? confirmText = null)
    {
        if (owner is MainWindow mainWindow)
        {
            return ShowViaHost(mainWindow, message, title, requiredPhrase: null, confirmText);
        }

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
        if (owner is MainWindow mainWindow)
        {
            return ShowViaHost(mainWindow, message, title, requiredPhrase, confirmText);
        }

        var window = new ConfirmWindow(message, title, requiredPhrase, confirmText) { Owner = owner };
        return window.ShowDialog() == true;
    }

    /// <summary>
    /// UI-3a (2026-11-03): bridge แบบ "ดูเหมือน synchronous" สำหรับ call site เดิม 18 ที่ในแอปที่เรียก
    /// ConfirmWindow.Show(...)/ShowWithTypeToConfirm(...) แบบ <c>if (ConfirmWindow.Show(...)) { ... }</c>
    /// ตรงๆ ไม่ await (เขียนเป็น async method ไม่ได้โดยไม่แก้ผู้เรียกทุกจุด) - ใช้ลูป
    /// Dispatcher.PushFrame เดียวกับที่ Window.ShowDialog() ใช้ภายในจริงๆ: สูบ message loop ของ UI
    /// thread ต่อไปจนกว่า dialog ใน DialogHost จะถูกปิด (ปุ่มยืนยัน/ยกเลิก หรือ Esc) แล้วค่อย return -
    /// ไม่ใช่การ block UI thread เฉยๆ (ซึ่งจะ deadlock เพราะ DialogHost ปิดผ่าน event ของปุ่มที่ต้องให้
    /// message loop เดินต่อถึงจะยิงได้)
    /// </summary>
    private static bool ShowViaHost(
        MainWindow owner, string message, string title, string? requiredPhrase, string? confirmText)
    {
        var content = new ConfirmDialogContent(message, title, requiredPhrase, confirmText);
        var frame = new DispatcherFrame();
        var result = false;

        // ปุ่ม Confirm/Cancel ของ content แค่บอกผลผ่าน ResultChosen - ให้ DialogHost.CloseTop เป็นคน
        // ปิดจริง แล้ว Push ด้านล่างรับผลสุดท้าย (ไม่ว่าจะมาจากปุ่ม หรือจาก Esc ที่ DialogHost เรียก
        // CloseTop(null) เองโดยไม่ผ่าน ResultChosen เลย) ไว้จุดเดียวที่นี่ ไม่ซ้ำสองที่
        content.ResultChosen += r => owner.DialogHost.CloseTop(r);

        owner.DialogHost.Push(content, raw =>
        {
            result = raw is true;
            frame.Continue = false;
        });

        Dispatcher.PushFrame(frame);
        return result;
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
