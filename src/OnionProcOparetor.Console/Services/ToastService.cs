using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;

namespace OnionProcOparetor.Console.Services;

/// <summary>ชนิดของ toast - กำหนดสี ไอคอน และว่าหายเองหรือค้างจนกดปิด</summary>
public enum ToastKind
{
    Success,
    Info,
    Error
}

/// <summary>
/// toast หนึ่งอัน - immutable ยกเว้นตัวจับเวลา ซึ่ง <see cref="ToastService"/> เป็นเจ้าของคนเดียว
/// </summary>
public sealed class ToastItem
{
    public required ToastKind Kind { get; init; }
    public required string Message { get; init; }

    /// <summary>ข้อความของลิงก์ เช่น "ดูผล" - null = ไม่มีลิงก์</summary>
    public string? ActionText { get; init; }

    /// <summary>สิ่งที่ทำเมื่อกดลิงก์ - toast ปิดตัวเองหลังเรียกเสมอ</summary>
    public Action? Action { get; init; }

    public bool HasAction => !string.IsNullOrEmpty(ActionText) && Action is not null;

    /// <summary>ชื่อไอคอน (kebab-case แบบ IconPresenter) ตามชนิด</summary>
    public string IconName => Kind switch
    {
        ToastKind.Success => "check-circle",
        ToastKind.Error => "x-circle",
        _ => "info"
    };

    internal DispatcherTimer? Timer { get; set; }
}

/// <summary>
/// Toast มุมขวาล่าง (WP6, 2026-10-30) - ใช้แทนข้อความผลลัพธ์/error ที่เคยแทรกอยู่ในหน้า
/// (StatusText ของ Dashboard ฯลฯ) ซึ่งมีปัญหาสองอย่าง: ข้อความใหม่ทับข้อความเก่าทันทีจนอ่านไม่ทัน
/// และอยู่คนละมุมกับที่ครูกำลังมองอยู่
///
/// กติกา (ตาม docs/roadmap.md หัวข้อ Console):
/// - สำเร็จ/ข้อมูล หายเองใน 4 วินาที · error ค้างจนกดปิด (error ที่หายเองคือ error ที่ครูอาจไม่ทันเห็น)
/// - ซ้อนกันสูงสุด 3 อัน - อันที่ 4 ดันอันเก่าสุดออก
/// - มีลิงก์ action ได้ เช่น "ดูผล"
/// - ข้อความซ้ำกับอันที่ยังโชว์อยู่ ไม่เพิ่มอันใหม่ แค่นับเวลาใหม่ (กดปุ่มรัว ๆ แล้ว toast ไม่ล้นจอ)
///
/// **สิ่งที่ toast ไม่ได้มาแทน:** อะไรที่ต้องให้ครูตัดสินใจก่อน (ลบ/ปิดทั้งระบบ) ยังเป็น ConfirmWindow
/// เหมือนเดิม และ "สถานะของทั้งระบบ" ที่ต้องเห็นตลอดเวลา (gate ปิด / Server หลุด) อยู่ที่แถบหัว
/// (ดู AnnouncementCenter) ไม่ใช่ toast เพราะ toast หายไปได้ แต่ปัญหายังอยู่
///
/// เป็น static เพราะทั้งแอปมีที่แสดง toast ที่เดียว (ToastHost ใน DashboardWindow) และหน้าย่อยกับ
/// หน้าต่างย่อยต้องเรียกได้โดยไม่ต้องส่ง reference ต่อกันเป็นทอด ๆ - toast ที่ถูกเรียกก่อนจะมี host
/// (เช่นตอน login เสร็จ) ค้างอยู่ใน <see cref="Items"/> แล้วโผล่ทันทีที่ host ถูกสร้าง
/// </summary>
public static class ToastService
{
    public const int MaxVisible = 3;
    public static readonly TimeSpan AutoDismissAfter = TimeSpan.FromSeconds(4);

    /// <summary>รายการที่แสดงอยู่ เรียงเก่า → ใหม่ (ToastHost วาดอันใหม่สุดไว้ล่างสุด)</summary>
    public static ObservableCollection<ToastItem> Items { get; } = new();

    public static ToastItem? Success(string message, string? actionText = null, Action? action = null) =>
        Show(ToastKind.Success, message, actionText, action);

    public static ToastItem? Info(string message, string? actionText = null, Action? action = null) =>
        Show(ToastKind.Info, message, actionText, action);

    public static ToastItem? Error(string message, string? actionText = null, Action? action = null) =>
        Show(ToastKind.Error, message, actionText, action);

    /// <summary>
    /// แสดง toast - เรียกจาก thread ไหนก็ได้ (marshal เข้า UI thread ให้เอง) คืน null ถ้าไม่มี
    /// Application (เช่นตอน process กำลังปิด)
    /// </summary>
    public static ToastItem? Show(ToastKind kind, string message, string? actionText = null, Action? action = null)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            return null;
        }

        if (!dispatcher.CheckAccess())
        {
            return dispatcher.Invoke(() => Show(kind, message, actionText, action));
        }

        // ซ้ำกับอันที่ยังโชว์อยู่ = นับเวลาใหม่แทนการซ้อนเพิ่ม
        var duplicate = Items.FirstOrDefault(t => t.Kind == kind && t.Message == message && t.ActionText == actionText);
        if (duplicate is not null && duplicate.Action is null && action is null)
        {
            RestartTimer(duplicate);
            return duplicate;
        }

        var item = new ToastItem { Kind = kind, Message = message, ActionText = actionText, Action = action };
        Items.Add(item);

        while (Items.Count > MaxVisible)
        {
            Dismiss(Items[0]);
        }

        RestartTimer(item);
        ConsoleDiagnostics.WriteLine($"[Toast] {kind}: {message}");
        return item;
    }

    public static void Dismiss(ToastItem item)
    {
        item.Timer?.Stop();
        item.Timer = null;
        Items.Remove(item);
    }

    /// <summary>กดลิงก์ action - ทำงานแล้วปิด toast เสมอ (ลิงก์ที่กดแล้ว toast ยังค้างดูเหมือนกดไม่ติด)</summary>
    public static void InvokeAction(ToastItem item)
    {
        Dismiss(item);
        item.Action?.Invoke();
    }

    /// <summary>เมาส์ชี้อยู่ - หยุดนับถอยหลัง ไม่ให้ toast หายไปตอนกำลังอ่านหรือกำลังจะกดลิงก์</summary>
    public static void Hold(ToastItem item) => item.Timer?.Stop();

    /// <summary>เมาส์ออก - เริ่มนับ 4 วินาทีใหม่ (ไม่ใช่นับต่อจากที่ค้าง ซึ่งอาจเหลือไม่ถึงวินาที)</summary>
    public static void Release(ToastItem item) => RestartTimer(item);

    private static void RestartTimer(ToastItem item)
    {
        item.Timer?.Stop();

        // error ค้างจนกดปิดเท่านั้น
        if (item.Kind == ToastKind.Error)
        {
            item.Timer = null;
            return;
        }

        var timer = new DispatcherTimer { Interval = AutoDismissAfter };
        timer.Tick += (_, _) => Dismiss(item);
        item.Timer = timer;
        timer.Start();
    }
}
