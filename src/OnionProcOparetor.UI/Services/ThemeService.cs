using System.Diagnostics;
using System.Windows;
using Microsoft.Win32;

namespace OnionProcOparetor.UI.Services;

/// <summary>ธีมที่ผู้ใช้เลือกได้ - System = ตามที่ Windows ตั้งไว้ (ค่า default)</summary>
public enum AppTheme
{
    System = 0,
    Light = 1,
    Dark = 2
}

/// <summary>
/// สลับ palette ตอน runtime โดย "เปลี่ยนทั้ง ResourceDictionary" ไม่ใช่ไล่เปลี่ยนสีทีละตัว - เดิมมี
/// สอง instance ที่ซ้ำกันเกือบทั้งหมดใน Console/Services/ThemeService.cs และ
/// RosterManager/Services/ThemeService.cs ย้ายมารวมเป็นตัวเดียวที่นี่ (WP UI-1a, 2026-11-03) ตามที่
/// ผู้ใช้สั่ง เพราะทั้งสองแอปต้องการพฤติกรรมเดียวกันเป๊ะ ต่างกันแค่ "จะโหลด/บันทึกธีมที่ไหน" และ
/// "สีเน้นชุดไหน" - สองอย่างนี้จึงกลายเป็นพารามิเตอร์ของ <see cref="Initialize"/> แทน
///
/// Theme/Colors.Light.xaml กับ Theme/Colors.Dark.xaml ของไลบรารีนี้ประกาศ x:Key ชุดเดียวกันเป๊ะ และ
/// Theme/Accent.Light.xaml กับ Accent.Dark.xaml (ของไลบรารีเองหรือของแต่ละแอปที่ override เข้ามา) ก็
/// ต้องประกาศ x:Key ชุดเดียวกันเป๊ะเช่นกัน - ดู comment เต็มในไฟล์เหล่านั้น ตัวไหนถูก merge อยู่ก็ชนะ
/// ทุก {DynamicResource} ในแอปจะอัพเดตตามเองทันที
///
/// ทำไมต้อง DynamicResource ทั้งแอป: {StaticResource} resolve ครั้งเดียวตอน parse XAML แล้วจบ -
/// ถ้าใช้ StaticResource กับ Brush.* เอาไว้ ธีมจะค้างอยู่ที่ธีมแรกที่โหลดตลอดไป ไม่มีทางสลับได้เลย
///
/// ผลข้างเคียงที่ต้องระวัง และเป็นเหตุผลที่มี VerifyKeyParity(): DynamicResource ที่หา key ไม่เจอ
/// **ไม่ throw** ต่างจาก StaticResource ที่ระเบิดทันที DynamicResource แค่เงียบแล้ววาดเป็นค่าว่าง -
/// ปลอดภัยกว่าตรงที่แอปไม่ตาย แต่แย่กว่าตรงที่ "จอมืดจุดเดียวในธีมเดียว" จะไม่มีใครเห็นจนกว่าผู้ใช้
/// จะบ่น ฉะนั้นต้องเช็ก key parity ตั้งแต่ตอน startup แทน
/// </summary>
public static class ThemeService
{
    private const string BaseLightUri = "pack://application:,,,/OnionProcOparetor.UI;component/Theme/Colors.Light.xaml";
    private const string BaseDarkUri = "pack://application:,,,/OnionProcOparetor.UI;component/Theme/Colors.Dark.xaml";
    private const string DefaultAccentLightUri = "pack://application:,,,/OnionProcOparetor.UI;component/Theme/Accent.Light.xaml";
    private const string DefaultAccentDarkUri = "pack://application:,,,/OnionProcOparetor.UI;component/Theme/Accent.Dark.xaml";

    /// <summary>
    /// key ที่มีอยู่เฉพาะในไฟล์ palette หลัก/ไฟล์สีเน้นเท่านั้น (คนละ key กัน เพราะทั้งสอง dictionary
    /// ถูก merge เข้า Application.Resources พร้อมกันและต้องสลับแยกจากกันได้) ใช้เป็น "ป้ายชื่อ" หา
    /// dictionary ตัวเดิมเพื่อสลับ - จงใจไม่ใช้ index คงที่ เพราะพอ ThemeMode ถูกตั้งค่า WPF จะ
    /// **แทรก Fluent dictionary ของตัวเองเข้ามาใน Application.Resources.MergedDictionaries เอง**
    /// index ที่เคยถูกจึงเลื่อนได้โดยที่เราไม่รู้ตัว แล้วจะกลายเป็นไปทับ dictionary ของ Fluent แทน
    /// </summary>
    private const string BaseMarkerKey = "Theme.Name";
    private const string AccentMarkerKey = "Theme.AccentName";

    private static AppTheme _current = AppTheme.System;
    private static Func<string?> _loadSavedTheme = () => null;
    private static Action<AppTheme> _saveTheme = _ => { };
    private static Action<string> _log = _ => { };
    private static string _accentLightUri = DefaultAccentLightUri;
    private static string _accentDarkUri = DefaultAccentDarkUri;

    /// <summary>ค่าที่ผู้ใช้เลือก (อาจเป็น System) - ถ้าอยากรู้ว่า "ตอนนี้มืดอยู่ไหม" ให้ใช้ IsDarkActive</summary>
    public static AppTheme Current => _current;

    /// <summary>ธีมที่แสดงผลอยู่จริงตอนนี้ (คลี่ System ออกเป็น Light/Dark แล้ว)</summary>
    public static bool IsDarkActive => Resolve(_current) == AppTheme.Dark;

    /// <summary>ยิงหลังสลับธีมเสร็จ - code-behind ที่ตั้งสีเองต้องฟังตัวนี้แล้วทาสีใหม่</summary>
    public static event Action? ThemeChanged;

    /// <summary>
    /// เรียกครั้งเดียวจาก App.OnStartup ก่อนเปิดหน้าต่างแรก - อ่านค่าที่ผู้ใช้เคยเลือกจาก settings
    /// ของแอป (ผ่าน <paramref name="loadSavedTheme"/>) แล้ว apply ทันที ถ้ายังไม่เคยเลือกจะได้ System
    /// (ตาม Windows)
    /// </summary>
    /// <param name="loadSavedTheme">อ่านค่าธีมที่บันทึกไว้ล่าสุด (เช่น console-settings.json) คืน null/ค่าที่ parse ไม่ได้ = System</param>
    /// <param name="saveTheme">บันทึกธีมที่เลือกใหม่กลับลง settings ของแอป - เรียกจาก <see cref="Set"/> เท่านั้น ไม่เรียกจาก PreviewOverride</param>
    /// <param name="log">ปลายทาง diagnostic log ของแอป (เช่น ConsoleDiagnostics.WriteLine/RosterDiagnostics.WriteLine)</param>
    /// <param name="accentLightUri">URI ของ dictionary สีเน้นธีมสว่าง - ไม่ส่ง = ใช้ค่า default ของไลบรารี (ส้ม)</param>
    /// <param name="accentDarkUri">URI ของ dictionary สีเน้นธีมมืด - ไม่ส่ง = ใช้ค่า default ของไลบรารี (ส้ม)</param>
    public static void Initialize(
        Func<string?> loadSavedTheme,
        Action<AppTheme> saveTheme,
        Action<string> log,
        string? accentLightUri = null,
        string? accentDarkUri = null)
    {
        _loadSavedTheme = loadSavedTheme;
        _saveTheme = saveTheme;
        _log = log;
        _accentLightUri = accentLightUri ?? DefaultAccentLightUri;
        _accentDarkUri = accentDarkUri ?? DefaultAccentDarkUri;

        VerifyKeyParity();

        _current = Parse(_loadSavedTheme());
        ApplyToApplication(Resolve(_current));

        // Application.ThemeMode ไม่ไหลลงไปถึง "แถบหัวหน้าต่าง" ของ Windows เอง - ยืนยันจากภาพจริง
        // (โหมดมืดทั้งหน้า แต่ title bar ยังขาวโพลน) ตัวที่สั่ง DWM ให้ทำ title bar สีเข้มคือ
        // Window.ThemeMode ของแต่ละหน้าต่าง จึงต้องไล่ตั้งให้ทุกหน้าต่างเอง class handler ตรงนี้ทำงาน
        // กับหน้าต่างทุกบานในแอปโดยไม่ต้องไปแก้ทีละไฟล์ และครอบคลุมหน้าต่างที่เปิดทีหลังด้วย
        EventManager.RegisterClassHandler(
            typeof(Window),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler((sender, _) => ApplyToWindow((Window)sender, Resolve(_current))));

        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    /// <summary>สลับธีมและจำค่าไว้ใน settings ของแอป (ผ่าน saveTheme ที่ตั้งไว้ใน Initialize)</summary>
    public static void Set(AppTheme theme)
    {
        _current = theme;
        ApplyToApplication(Resolve(theme));
        _saveTheme(theme);
        ThemeChanged?.Invoke();
    }

    /// <summary>
    /// สลับสว่าง/มืด สำหรับปุ่ม toggle ปุ่มเดียวบน toolbar - ถ้าตอนนี้เป็น System จะกระโดดไป
    /// "ตรงข้ามกับที่เห็นอยู่ตอนนี้" ซึ่งตรงกับที่คนกดคาดหวังมากกว่าการวนสามสถานะ
    /// (สามสถานะยังเลือกได้จากหน้า Settings)
    /// </summary>
    public static void Toggle() => Set(IsDarkActive ? AppTheme.Light : AppTheme.Dark);

#if DEBUG
    /// <summary>
    /// โหมดตรวจ UI เท่านั้น (--theme=light|dark ใน App.OnStartup): สลับธีม **โดยไม่บันทึก** ลง
    /// settings ของแอป - ไฟล์นั้นใช้ร่วมกับตัวจริงบนเครื่องเดียวกัน การถ่ายภาพตรวจทั้งสองธีมจึงต้อง
    /// ไม่ไปเปลี่ยนธีมที่ผู้ใช้เลือกไว้
    /// </summary>
    public static void PreviewOverride(AppTheme theme)
    {
        _current = theme;
        ApplyToApplication(Resolve(theme));
        ThemeChanged?.Invoke();
    }
#endif

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

        // ลำดับสำคัญ: ตั้ง ThemeMode **ก่อน** แล้วค่อยวาง palette ของเรา - ดู comment เดิมที่
        // Console/App.xaml (ย้ายเหตุผลเต็มมาพร้อมโค้ดนี้) การ set ThemeMode ทำให้ WPF **จัดการ
        // MergedDictionaries ของ Application เอง** วางทีหลังจึงชนะเสมอตามที่ต้องการ
#pragma warning disable WPF0001 // ThemeMode ยัง [Experimental] อยู่ใน .NET 10 - รับความเสี่ยงไว้ที่จุดนี้จุดเดียว
        app.ThemeMode = resolved == AppTheme.Dark ? ThemeMode.Dark : ThemeMode.Light;
        var appliedThemeMode = app.ThemeMode.ToString();
#pragma warning restore WPF0001

        var basePalette = new ResourceDictionary
        {
            Source = new Uri(resolved == AppTheme.Dark ? BaseDarkUri : BaseLightUri, UriKind.Absolute)
        };
        var accentPalette = new ResourceDictionary
        {
            Source = new Uri(resolved == AppTheme.Dark ? _accentDarkUri : _accentLightUri, UriKind.RelativeOrAbsolute)
        };

        var merged = app.Resources.MergedDictionaries;
        ReplaceOrAdd(merged, basePalette, BaseMarkerKey);
        ReplaceOrAdd(merged, accentPalette, AccentMarkerKey);

        // หน้าต่างที่เปิดค้างอยู่แล้วตอนผู้ใช้กดสลับธีม - หน้าต่างที่เปิดทีหลังได้จาก class handler ใน Initialize
        foreach (Window window in app.Windows)
        {
            ApplyToWindow(window, resolved);
        }

        _log($"ThemeService: ใช้ธีม {resolved} (ผู้ใช้เลือก {_current}), ThemeMode={appliedThemeMode}, "
            + $"baseIndex={FindIndex(merged, BaseMarkerKey)}, accentIndex={FindIndex(merged, AccentMarkerKey)}/{merged.Count}");
    }

    private static void ReplaceOrAdd(IList<ResourceDictionary> merged, ResourceDictionary dict, string markerKey)
    {
        var existing = FindIndex(merged, markerKey);
        if (existing >= 0)
        {
            merged[existing] = dict;
        }
        else
        {
            merged.Add(dict);
        }
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
        // ต้องสั่ง DWM ตรง ๆ อีกทาง ไม่ throw ถ้าสั่งไม่สำเร็จ: title bar สีผิดน่าเกลียดแต่ไม่ใช่เหตุให้
        // หน้าต่างเปิดไม่ขึ้น
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
            // หน้าต่างไปแล้ว DWM จึงไม่วาดกรอบใหม่ให้เอง SWP_FRAMECHANGED บังคับให้คำนวณและวาด
            // non-client area ใหม่ทั้งหมด โดย NOMOVE/NOSIZE/NOZORDER/NOACTIVATE ทำให้ไม่มีอะไรขยับ
            SetWindowPos(handle, IntPtr.Zero, 0, 0, 0, 0,
                SwpNoSize | SwpNoMove | SwpNoZOrder | SwpNoActivate | SwpFrameChanged);
        }
        catch (Exception ex)
        {
            _log($"ThemeService: ตั้งสี title bar ไม่สำเร็จ: {ex.Message}");
        }
    }

    private static int FindIndex(IList<ResourceDictionary> merged, string markerKey)
    {
        for (var i = 0; i < merged.Count; i++)
        {
            // Contains() ไม่ไล่ลงไปใน MergedDictionaries ลูก จึงเจอเฉพาะไฟล์ palette/accent จริงๆ
            // ไม่ไปโดน Controls.xaml ที่ merge Typography.xaml ต่ออีกทอด
            if (merged[i].Contains(markerKey))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// เช็กว่าไฟล์ palette หลักสองไฟล์ และไฟล์สีเน้นสองไฟล์ (ของแอปที่กำลังเรียก Initialize) มี key
    /// ครบเท่ากันจริง - ทำตอน startup ทุกรอบ (ไม่ใช่เฉพาะ Debug) เพราะราคาถูกมาก เทียบกับความเสียหาย
    /// ของการปล่อยให้ "ปุ่มหายไปทั้งปุ่มเฉพาะตอนโหมดมืด" หลุดไปถึงเครื่องครู
    /// </summary>
    private static void VerifyKeyParity()
    {
        VerifyPairParity(BaseLightUri, BaseDarkUri, UriKind.Absolute, "palette หลัก (Colors)");
        VerifyPairParity(_accentLightUri, _accentDarkUri, UriKind.RelativeOrAbsolute, "สีเน้น (Accent)");
    }

    private static void VerifyPairParity(string lightUri, string darkUri, UriKind kind, string label)
    {
        try
        {
            var light = new ResourceDictionary { Source = new Uri(lightUri, kind) };
            var dark = new ResourceDictionary { Source = new Uri(darkUri, kind) };

            var lightKeys = light.Keys.Cast<object>().Select(k => k.ToString()!).ToHashSet(StringComparer.Ordinal);
            var darkKeys = dark.Keys.Cast<object>().Select(k => k.ToString()!).ToHashSet(StringComparer.Ordinal);

            var missingInDark = lightKeys.Except(darkKeys).OrderBy(k => k, StringComparer.Ordinal).ToList();
            var missingInLight = darkKeys.Except(lightKeys).OrderBy(k => k, StringComparer.Ordinal).ToList();

            if (missingInDark.Count == 0 && missingInLight.Count == 0)
            {
                return;
            }

            var message =
                $"ThemeService: ไฟล์ {label} สองไฟล์ key ไม่ตรงกัน - ธีมใดธีมหนึ่งจะมีจุดที่วาดไม่ออกแบบเงียบๆ. " +
                $"ขาดใน Dark: [{string.Join(", ", missingInDark)}] ขาดใน Light: [{string.Join(", ", missingInLight)}]";

            _log(message);
            Debug.Fail(message);
        }
        catch (Exception ex)
        {
            _log($"ThemeService: ตรวจ key parity ของ {label} ไม่สำเร็จ: {ex.Message}");
        }
    }
}
