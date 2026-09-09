using System.Diagnostics;
using System.Windows;
using Microsoft.Win32;
using OnionProcOparetor.Console.Models;

namespace OnionProcOparetor.Console.Services;

/// <summary>ธีมที่ผู้ใช้เลือกได้ - System = ตามที่ Windows ตั้งไว้ (ค่า default)</summary>
public enum AppTheme
{
    System = 0,
    Light = 1,
    Dark = 2
}

/// <summary>
/// สลับ palette ตอน runtime โดย "เปลี่ยนทั้ง ResourceDictionary" ไม่ใช่ไล่เปลี่ยนสีทีละตัว -
/// Theme/Colors.Light.xaml กับ Theme/Colors.Dark.xaml ประกาศ x:Key ชุดเดียวกันเป๊ะ (ดู comment
/// ในไฟล์นั้น) ตัวไหนถูก merge อยู่ก็ชนะ ทุก {DynamicResource} ในแอปจะอัพเดตตามเองทันที
///
/// ทำไมต้อง DynamicResource ทั้งแอป: {StaticResource} resolve ครั้งเดียวตอน parse XAML แล้วจบ -
/// ถ้าใช้ StaticResource กับ Brush.* เอาไว้ ธีมจะค้างอยู่ที่ธีมแรกที่โหลดตลอดไป ไม่มีทางสลับได้เลย
/// จึงต้องกวาดทั้ง 19 ไฟล์ XAML เป็น DynamicResource (ทำไปแล้ว 2026-09-09)
///
/// ผลข้างเคียงที่ต้องระวัง และเป็นเหตุผลที่มี VerifyKeyParity(): DynamicResource ที่หา key ไม่เจอ
/// **ไม่ throw** ต่างจาก StaticResource ที่ระเบิดทันที (บั๊ก "VIEW RUNNING PROCESSES" ปี 2026-08-25
/// คือ StaticResource หา key ข้าม Window ไม่เจอแล้ว throw ทั้งหน้าต่าง) DynamicResource แค่เงียบแล้ว
/// วาดเป็นค่าว่าง - ปลอดภัยกว่าตรงที่แอปไม่ตาย แต่แย่กว่าตรงที่ "จอมืดจุดเดียวในธีมเดียว" จะไม่มีใคร
/// เห็นจนกว่าผู้ใช้จะบ่น ฉะนั้นต้องเช็ก key parity ตั้งแต่ตอน startup แทน
/// </summary>
public static class ThemeService
{
    private const string LightPaletteUri = "/Theme/Colors.Light.xaml";
    private const string DarkPaletteUri = "/Theme/Colors.Dark.xaml";

    /// <summary>
    /// key ที่มีอยู่เฉพาะในไฟล์ palette เท่านั้น ใช้เป็น "ป้ายชื่อ" หา dictionary ตัวเดิมเพื่อสลับ -
    /// จงใจไม่ใช้ index คงที่ (เช่น MergedDictionaries[0]) เพราะพอ ThemeMode ถูกตั้งค่า WPF จะ
    /// **แทรก Fluent dictionary ของตัวเองเข้ามาใน Application.Resources.MergedDictionaries เอง**
    /// index ที่เคยถูกจึงเลื่อนได้โดยที่เราไม่รู้ตัว แล้วจะกลายเป็นไปทับ dictionary ของ Fluent แทน
    /// </summary>
    private const string PaletteMarkerKey = "Theme.Name";

    private static AppTheme _current = AppTheme.System;

    /// <summary>ค่าที่ผู้ใช้เลือก (อาจเป็น System) - ถ้าอยากรู้ว่า "ตอนนี้มืดอยู่ไหม" ให้ใช้ IsDarkActive</summary>
    public static AppTheme Current => _current;

    /// <summary>ธีมที่แสดงผลอยู่จริงตอนนี้ (คลี่ System ออกเป็น Light/Dark แล้ว)</summary>
    public static bool IsDarkActive => Resolve(_current) == AppTheme.Dark;

    /// <summary>ยิงหลังสลับธีมเสร็จ - code-behind ที่ตั้งสีเองต้องฟังตัวนี้แล้วทาสีใหม่</summary>
    public static event Action? ThemeChanged;

    /// <summary>
    /// เรียกครั้งเดียวจาก App.OnStartup ก่อนเปิดหน้าต่างแรก - อ่านค่าที่ผู้ใช้เคยเลือกจาก
    /// console-settings.json แล้ว apply ทันที ถ้ายังไม่เคยเลือกจะได้ System (ตาม Windows)
    /// </summary>
    public static void Initialize()
    {
        VerifyKeyParity();

        _current = Parse(SettingsService.Current.Ui.Theme);
        ApplyToApplication(Resolve(_current));

        // Application.ThemeMode ไม่ไหลลงไปถึง "แถบหัวหน้าต่าง" ของ Windows เอง - ยืนยันจากภาพจริง
        // (โหมดมืดทั้งหน้า แต่ title bar ยังขาวโพลน) ตัวที่สั่ง DWM ให้ทำ title bar สีเข้มคือ
        // Window.ThemeMode ของแต่ละหน้าต่าง จึงต้องไล่ตั้งให้ทุกหน้าต่างเอง class handler ตรงนี้ทำงาน
        // กับหน้าต่างทุกบานในแอปโดยไม่ต้องไปแก้ทีละไฟล์ (มี 19 บาน) และครอบคลุมหน้าต่างที่เปิดทีหลังด้วย
        EventManager.RegisterClassHandler(
            typeof(Window),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler((sender, _) => ApplyToWindow((Window)sender, Resolve(_current))));

        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    /// <summary>สลับธีมและจำค่าไว้ใน console-settings.json</summary>
    public static void Set(AppTheme theme)
    {
        _current = theme;
        ApplyToApplication(Resolve(theme));

        var settings = SettingsService.Current;
        settings.Ui.Theme = theme.ToString();
        var (success, error) = SettingsService.Save(settings);
        if (!success)
        {
            // จำค่าไม่ได้ไม่ใช่เหตุให้ธีมที่เพิ่งสลับไปแล้วต้องเด้งกลับ - แค่บันทึกไว้ว่าครั้งหน้าจะไม่จำ
            ConsoleDiagnostics.WriteLine($"ThemeService: บันทึกธีมลง settings ไม่สำเร็จ ({error}) - ธีมยังเปลี่ยนแล้วในรอบนี้");
        }

        ThemeChanged?.Invoke();
    }

    /// <summary>
    /// สลับสว่าง/มืด สำหรับปุ่ม toggle ปุ่มเดียวบน toolbar - ถ้าตอนนี้เป็น System จะกระโดดไป
    /// "ตรงข้ามกับที่เห็นอยู่ตอนนี้" ซึ่งตรงกับที่คนกดคาดหวังมากกว่าการวนสามสถานะ
    /// (สามสถานะยังเลือกได้จากหน้า Settings)
    /// </summary>
    public static void Toggle() => Set(IsDarkActive ? AppTheme.Light : AppTheme.Dark);

    private static void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        // สนใจเฉพาะตอนที่ผู้ใช้เลือก "ตามระบบ" ไว้ - ถ้าเขาเลือก Light/Dark ตายตัว Windows จะเปลี่ยน
        // ยังไงก็ไม่เกี่ยว
        if (_current != AppTheme.System || e.Category != UserPreferenceCategory.General)
        {
            return;
        }

        ApplyToApplication(Resolve(AppTheme.System));
        ThemeChanged?.Invoke();
    }

    private static AppTheme Resolve(AppTheme theme) => theme switch
    {
        AppTheme.Light => AppTheme.Light,
        AppTheme.Dark => AppTheme.Dark,
        _ => ReadWindowsAppsUseLightTheme() ? AppTheme.Light : AppTheme.Dark
    };

    /// <summary>
    /// AppsUseLightTheme = 0 คือ Windows ตั้งเป็นโหมดมืดสำหรับ "แอป" (แยกกับ SystemUsesLightTheme
    /// ที่คุมทาสก์บาร์/Start เท่านั้น - ตัวที่ตรงกับหน้าต่างแอปคือตัวนี้) อ่านไม่ได้เมื่อไหร่ให้ถือว่าสว่าง
    /// ซึ่งเป็นค่า default ของ Windows เอง
    /// </summary>
    private static bool ReadWindowsAppsUseLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is not int value || value != 0;
        }
        catch
        {
            return true;
        }
    }

    private static AppTheme Parse(string? stored) =>
        Enum.TryParse<AppTheme>(stored, ignoreCase: true, out var parsed) ? parsed : AppTheme.System;

    private static void ApplyToApplication(AppTheme resolved)
    {
        var app = Application.Current;
        if (app is null)
        {
            return;
        }

        // ลำดับสำคัญ: ตั้ง ThemeMode **ก่อน** แล้วค่อยวาง palette ของเรา
        //
        // ThemeMode คุม chrome ของคอนโทรลที่เราไม่ได้ re-template เอง (ComboBox, CheckBox, ScrollBar,
        // DataGrid chrome, แถบหัวหน้าต่างของ Windows เอง) ถ้าไม่สลับตัวนี้ด้วยจะได้ dark mode ครึ่งใบ -
        // การ์ดมืดแต่ scrollbar/ComboBox ยังขาว ซึ่งเป็นเหตุผลที่ App.xaml เคยตั้ง ThemeMode="Light"
        // ตายตัวไว้ (ดู comment ที่นั่น) จนกว่า token จะ theme-aware จริง - ตอนนี้เป็นแล้ว
        //
        // ที่ต้องมาก่อน: การ set ThemeMode ทำให้ WPF **จัดการ MergedDictionaries ของ Application เอง**
        // (สลับ Fluent dictionary เข้า/ออก) ถ้าเราวาง palette ลงไปก่อนแล้ว WPF ค่อยมายุ่งกับ collection
        // ทีหลัง ตำแหน่งที่เราวางไว้จะเลื่อน และ Fluent dictionary อาจไปอยู่ *หลัง* palette ของเรา
        // ซึ่งแปลว่า brush ของ Fluent จะชนะ token ของเราในคีย์ที่ชนกัน วางทีหลังจึงชนะเสมอตามที่ต้องการ
#pragma warning disable WPF0001 // ThemeMode ยัง [Experimental] อยู่ใน .NET 10 - รับความเสี่ยงไว้ที่จุดนี้จุดเดียว
        app.ThemeMode = resolved == AppTheme.Dark ? ThemeMode.Dark : ThemeMode.Light;
        var appliedThemeMode = app.ThemeMode.ToString();
#pragma warning restore WPF0001

        var palette = new ResourceDictionary
        {
            Source = new Uri(resolved == AppTheme.Dark ? DarkPaletteUri : LightPaletteUri, UriKind.Relative)
        };

        var merged = app.Resources.MergedDictionaries;
        var existing = FindPaletteIndex(merged);
        if (existing >= 0)
        {
            merged[existing] = palette;
        }
        else
        {
            merged.Add(palette);
        }

        // หน้าต่างที่เปิดค้างอยู่แล้วตอนผู้ใช้กดสลับธีม - หน้าต่างที่เปิดทีหลังได้จาก class handler ใน Initialize
        foreach (Window window in app.Windows)
        {
            ApplyToWindow(window, resolved);
        }

        ConsoleDiagnostics.WriteLine($"ThemeService: ใช้ธีม {resolved} (ผู้ใช้เลือก {_current}), ThemeMode={appliedThemeMode}, palette index={FindPaletteIndex(merged)}/{merged.Count}");
    }

    /// <summary>
    /// DWMWA_USE_IMMERSIVE_DARK_MODE - บอก Desktop Window Manager ให้วาดแถบหัวหน้าต่าง (title bar,
    /// ปุ่มปิด/ย่อ/ขยาย, ขอบ) เป็นสีเข้ม ส่วนนี้ WPF วาดเองไม่ได้เลย เพราะเป็น non-client area ที่
    /// Windows เป็นคนวาด
    /// </summary>
    private const int DwmwaUseImmersiveDarkMode = 20;

    [System.Runtime.InteropServices.DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr hwndInsertAfter, int x, int y, int cx, int cy, uint flags);

    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;

    private static void ApplyToWindow(Window window, AppTheme resolved)
    {
#pragma warning disable WPF0001
        window.ThemeMode = resolved == AppTheme.Dark ? ThemeMode.Dark : ThemeMode.Light;
#pragma warning restore WPF0001

        // Window.ThemeMode เพียงอย่างเดียว **ไม่ทำให้ title bar เป็นสีเข้ม** - ยืนยันด้วยภาพจริงแล้ว
        // (เนื้อหาในหน้าต่างมืดครบ แต่แถบหัวยังขาวโพลนเหมือนเดิม) ต้องสั่ง DWM ตรง ๆ อีกทาง
        // ไม่ throw ถ้าสั่งไม่สำเร็จ: title bar สีผิดน่าเกลียดแต่ไม่ใช่เหตุให้หน้าต่างเปิดไม่ขึ้น
        try
        {
            var handle = new System.Windows.Interop.WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero)
            {
                return;
            }

            var useDark = resolved == AppTheme.Dark ? 1 : 0;
            DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkMode, ref useDark, sizeof(int));

            // จำเป็น ไม่ใช่ของแถม: เราตั้ง attribute นี้ตอน Loaded ซึ่งเป็นเวลา *หลัง* Windows วาดกรอบ
            // หน้าต่างไปแล้ว DWM จึงไม่วาดกรอบใหม่ให้เอง ผลที่เห็นจากภาพจริงคือแถบหัวครึ่งเก่าครึ่งใหม่
            // (ข้อความมืดแต่พื้นยังขาว ปุ่มปิดหาย) SWP_FRAMECHANGED บังคับให้คำนวณและวาด non-client
            // area ใหม่ทั้งหมด โดย NOMOVE/NOSIZE/NOZORDER/NOACTIVATE ทำให้ไม่มีอะไรอย่างอื่นขยับ
            SetWindowPos(handle, IntPtr.Zero, 0, 0, 0, 0,
                SwpNoSize | SwpNoMove | SwpNoZOrder | SwpNoActivate | SwpFrameChanged);
        }
        catch (Exception ex)
        {
            ConsoleDiagnostics.WriteLine($"ThemeService: ตั้งสี title bar ไม่สำเร็จ: {ex.Message}");
        }
    }

    private static int FindPaletteIndex(IList<ResourceDictionary> merged)
    {
        for (var i = 0; i < merged.Count; i++)
        {
            // Contains() ไม่ไล่ลงไปใน MergedDictionaries ลูก จึงเจอเฉพาะไฟล์ palette จริงๆ
            // ไม่ไปโดน Controls.xaml ที่ merge Typography.xaml ต่ออีกทอด
            if (merged[i].Contains(PaletteMarkerKey))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// เช็กว่าไฟล์ palette สองไฟล์มี key ครบเท่ากันจริง - ทำตอน startup ทุกรอบ (ไม่ใช่เฉพาะ Debug)
    /// เพราะราคาถูกมาก (โหลด dictionary สองตัวเทียบ HashSet) เทียบกับความเสียหายของการปล่อยให้
    /// "ปุ่มหายไปทั้งปุ่มเฉพาะตอนโหมดมืด" หลุดไปถึงเครื่องครู - เขียนลง console.log ให้ไล่ย้อนได้
    /// </summary>
    private static void VerifyKeyParity()
    {
        try
        {
            var light = new ResourceDictionary { Source = new Uri(LightPaletteUri, UriKind.Relative) };
            var dark = new ResourceDictionary { Source = new Uri(DarkPaletteUri, UriKind.Relative) };

            var lightKeys = light.Keys.Cast<object>().Select(k => k.ToString()!).ToHashSet(StringComparer.Ordinal);
            var darkKeys = dark.Keys.Cast<object>().Select(k => k.ToString()!).ToHashSet(StringComparer.Ordinal);

            var missingInDark = lightKeys.Except(darkKeys).OrderBy(k => k, StringComparer.Ordinal).ToList();
            var missingInLight = darkKeys.Except(lightKeys).OrderBy(k => k, StringComparer.Ordinal).ToList();

            if (missingInDark.Count == 0 && missingInLight.Count == 0)
            {
                return;
            }

            var message =
                $"ThemeService: palette สองไฟล์ key ไม่ตรงกัน - ธีมใดธีมหนึ่งจะมีจุดที่วาดไม่ออกแบบเงียบๆ. " +
                $"ขาดใน Dark: [{string.Join(", ", missingInDark)}] ขาดใน Light: [{string.Join(", ", missingInLight)}]";

            ConsoleDiagnostics.WriteLine(message);
            Debug.Fail(message);
        }
        catch (Exception ex)
        {
            ConsoleDiagnostics.WriteLine($"ThemeService: ตรวจ key parity ไม่สำเร็จ: {ex.Message}");
        }
    }
}
