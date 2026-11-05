using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace OnionProcOparetor.UI.Services;

/// <summary>
/// UI-15: token ของ motion ทั้งแอป (ตัวเลขชุดเดียวกับ Duration ใน Theme/Controls.xaml: Motion.Fast / Motion.Normal)
/// ทุก animation ที่เขียนด้วยโค้ดต้องใช้ helper ในคลาสนี้ ไม่ตั้งเวลา/easing เอง
///
/// หลักประกัน "จบที่สถานะสุดท้ายเป๊ะ": animation ขาเข้าใช้ FillBehavior.Stop - ค่าจริง (local value) ของ
/// property ถูกตั้งเป็นค่าปลายทางก่อนเริ่ม animation เสมอ แล้วเมื่อ animation จบ/ถูกแทนที่/ถูกยกเลิก WPF จะ
/// คืนค่าเป็นค่าปลายทางนั้นเอง จึงไม่มีทางค้างที่ Opacity น้อยกว่า 1 หรือ RenderTransform ที่เลื่อนอยู่
/// ไม่แตะ IsHitTestVisible ของ element ที่กำลังเล่น (ถ้า caller ต้องปิดรับคลิกตอนปิด ให้จัดการเอง)
/// ถ้าผู้ใช้ปิด animation ใน Windows (SystemParameters.ClientAreaAnimation = false) จะข้ามไปตั้งค่าปลายทางทันที
/// </summary>
public static class Motion
{
    public const int FastMs = 120;
    public const int NormalMs = 180;

    /// <summary>เวลาของ slide เข้าแผงข้าง (SidePanelHost) - ใช้ซ้ำกับการสลับเนื้อหาในแผง</summary>
    public const int PanelMs = 220;

    public static readonly IEasingFunction EaseOut = CreateEase();

    /// <summary>true = ปิด animation ทั้งหมด (ใช้ตอนจับภาพ --shot ของ preview เพื่อให้ได้เฟรมสุดท้ายไม่ใช่เฟรมกลางทาง)</summary>
    public static bool ForceDisabled { get; set; }

    public static bool Enabled => !ForceDisabled && SystemParameters.ClientAreaAnimation;

    private static IEasingFunction CreateEase()
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        ease.Freeze();
        return ease;
    }

    private static DoubleAnimation Make(double from, double to, int ms) => new(from, to, TimeSpan.FromMilliseconds(ms))
    {
        EasingFunction = EaseOut,
        FillBehavior = FillBehavior.Stop,
    };

    /// <summary>ค่า Opacity ปลายทางเป็น 1 เสมอ - เล่น fade จาก <paramref name="from"/> ขึ้นมา 1</summary>
    public static void FadeIn(UIElement element, int ms = NormalMs, double from = 0)
    {
        element.BeginAnimation(UIElement.OpacityProperty, null);
        element.Opacity = 1;
        if (!Enabled)
        {
            return;
        }

        element.BeginAnimation(UIElement.OpacityProperty, Make(from, 1, ms));
    }

    /// <summary>fade + ยกขึ้นจากล่าง <paramref name="rise"/> px (ใช้ตอนเปลี่ยนหน้า)</summary>
    public static void FadeRise(FrameworkElement element, double rise = 8, int ms = NormalMs)
    {
        var translate = EnsureTranslate(element);
        if (translate is not null)
        {
            translate.BeginAnimation(TranslateTransform.YProperty, null);
            translate.Y = 0;
        }

        FadeIn(element, ms);
        if (Enabled && translate is not null)
        {
            translate.BeginAnimation(TranslateTransform.YProperty, Make(rise, 0, ms));
        }
    }

    /// <summary>fade + ขยายจาก <paramref name="fromScale"/> เป็น 1 รอบจุดกึ่งกลาง (ใช้ตอน dialog เปิด)</summary>
    public static void FadeScaleIn(FrameworkElement element, double fromScale = 0.98, int ms = NormalMs)
    {
        var scale = EnsureScale(element);
        if (scale is not null)
        {
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            scale.ScaleX = 1;
            scale.ScaleY = 1;
        }

        FadeIn(element, ms);
        if (Enabled && scale is not null)
        {
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, Make(fromScale, 1, ms));
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, Make(fromScale, 1, ms));
        }
    }

    /// <summary>
    /// เล่น Opacity 1 -> 0 (ค้างที่ 0 จนกว่าจะเรียก <see cref="ResetOpacity"/>) แล้วเรียก
    /// <paramref name="completed"/> ตอนจบ · ถ้าปิด animation เรียก completed ทันที
    /// completed ต้องซ่อน element แล้วเรียก ResetOpacity เสมอ
    /// </summary>
    public static void FadeOut(UIElement element, Action completed, int ms = NormalMs)
    {
        if (!Enabled)
        {
            completed();
            return;
        }

        var animation = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(ms))
        {
            EasingFunction = EaseOut,
            FillBehavior = FillBehavior.HoldEnd,
        };
        animation.Completed += (_, _) => completed();
        element.BeginAnimation(UIElement.OpacityProperty, animation);
    }

    public static void ResetOpacity(UIElement element)
    {
        element.BeginAnimation(UIElement.OpacityProperty, null);
        element.Opacity = 1;
    }

    /// <summary>animation ทั่วไปตาม token (ขาเข้า = Stop · hold=true สำหรับขาออกที่ caller จัดการจบเอง)</summary>
    public static DoubleAnimation Animation(double from, double to, int ms = NormalMs, bool hold = false)
    {
        var animation = Make(from, to, ms);
        if (hold)
        {
            animation.FillBehavior = FillBehavior.HoldEnd;
        }

        return animation;
    }

    private static TranslateTransform? EnsureTranslate(FrameworkElement element)
    {
        if (element.RenderTransform is TranslateTransform { IsFrozen: false } existing)
        {
            return existing;
        }

        if (element.RenderTransform is null || element.RenderTransform == Transform.Identity)
        {
            var created = new TranslateTransform();
            element.RenderTransform = created;
            return created;
        }

        return null;
    }

    private static ScaleTransform? EnsureScale(FrameworkElement element)
    {
        if (element.RenderTransform is ScaleTransform { IsFrozen: false } existing)
        {
            return existing;
        }

        if (element.RenderTransform is null || element.RenderTransform == Transform.Identity)
        {
            var created = new ScaleTransform(1, 1);
            element.RenderTransform = created;
            element.RenderTransformOrigin = new Point(0.5, 0.5);
            return created;
        }

        return null;
    }
}
