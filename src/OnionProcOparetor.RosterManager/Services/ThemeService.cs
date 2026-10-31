using System.Windows;
using Microsoft.Win32;

namespace OnionProcOparetor.RosterManager.Services;

/// <summary>ธีมที่ผู้ใช้เลือกได้ - System = ตามที่ Windows ตั้งไว้ (ค่า default) - เหมือน Console ทุกตัว</summary>
public enum AppTheme
{
    System = 0,
    Light = 1,
    Dark = 2
}

/// <summary>
/// สลับ palette ตอน runtime - คัดลอกพฤติกรรมจาก Console/Services/ThemeService.cs ทั้งหมด (ดู comment
/// เต็มในไฟล์นั้นสำหรับเหตุผลของแต่ละจุด) ตัดส่วนที่ Roster Manager ไม่มี (ปุ่ม preview สำหรับถ่ายภาพ,
/// SettingsService.Changed hook) ออกเพราะไม่มีหน้า Settings ของตัวเอง - ธีมตามด้วย "ตามระบบ" เสมอ
/// ยกเว้นถ้ามีค่าที่เคยบันทึกไว้ (Models/RosterSettings.Theme)
/// </summary>
public static class ThemeService
{
    private const string LightPaletteUri = "/Theme/Colors.Light.xaml";
    private const string DarkPaletteUri = "/Theme/Colors.Dark.xaml";
    private const string PaletteMarkerKey = "Theme.Name";

    private static AppTheme _current = AppTheme.System;

    public static AppTheme Current => _current;
    public static bool IsDarkActive => Resolve(_current) == AppTheme.Dark;
    public static event Action? ThemeChanged;

    public static void Initialize()
    {
        _current = Parse(SettingsService.Current.Theme);
        ApplyToApplication(Resolve(_current));

        EventManager.RegisterClassHandler(
            typeof(Window),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler((sender, _) => ApplyToWindow((Window)sender, Resolve(_current))));

        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    public static void Set(AppTheme theme)
    {
        _current = theme;
        ApplyToApplication(Resolve(theme));

        var settings = SettingsService.Current;
        settings.Theme = theme.ToString();
        var (success, error) = SettingsService.Save(settings);
        if (!success)
        {
            RosterDiagnostics.WriteLine($"ThemeService: บันทึกธีมลง settings ไม่สำเร็จ ({error}) - ธีมยังเปลี่ยนแล้วในรอบนี้");
        }

        ThemeChanged?.Invoke();
    }

    public static void Toggle() => Set(IsDarkActive ? AppTheme.Light : AppTheme.Dark);

    private static void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
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

#pragma warning disable WPF0001 // ThemeMode ยัง [Experimental] อยู่ใน .NET 10 - เหมือน Console
        app.ThemeMode = resolved == AppTheme.Dark ? ThemeMode.Dark : ThemeMode.Light;
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

        foreach (Window window in app.Windows)
        {
            ApplyToWindow(window, resolved);
        }
    }

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

        try
        {
            var handle = new System.Windows.Interop.WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero)
            {
                return;
            }

            var useDark = resolved == AppTheme.Dark ? 1 : 0;
            DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkMode, ref useDark, sizeof(int));
            SetWindowPos(handle, IntPtr.Zero, 0, 0, 0, 0,
                SwpNoSize | SwpNoMove | SwpNoZOrder | SwpNoActivate | SwpFrameChanged);
        }
        catch (Exception ex)
        {
            RosterDiagnostics.WriteLine($"ThemeService: ตั้งสี title bar ไม่สำเร็จ: {ex.Message}");
        }
    }

    private static int FindPaletteIndex(IList<ResourceDictionary> merged)
    {
        for (var i = 0; i < merged.Count; i++)
        {
            if (merged[i].Contains(PaletteMarkerKey))
            {
                return i;
            }
        }

        return -1;
    }
}
