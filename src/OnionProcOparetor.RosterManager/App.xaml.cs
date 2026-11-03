using System.Windows;
using OnionProcOparetor.RosterManager.Services;
using OnionProcOparetor.UI.Services;

namespace OnionProcOparetor.RosterManager;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var fileVersion = System.Diagnostics.FileVersionInfo.GetVersionInfo(Environment.ProcessPath!).FileVersion;
        RosterDiagnostics.WriteLine($"=== Roster Manager เริ่มทำงาน === FileVersion: {fileVersion}, ProcessPath: {Environment.ProcessPath}, PID: {Environment.ProcessId}");

        // ส่ง Theme/Accent.Light.xaml, Theme/Accent.Dark.xaml ของตัวเอง (สีฟ้า #2A6FD6) แทนสีส้ม
        // default ของไลบรารี - URI สัมพัทธ์เพราะไฟล์ยังอยู่ในแอสเซมบลีนี้เอง (WP UI-1a, 2026-11-03)
        ThemeService.Initialize(
            loadSavedTheme: () => SettingsService.Current.Theme,
            saveTheme: theme =>
            {
                var settings = SettingsService.Current;
                settings.Theme = theme.ToString();
                var (success, error) = SettingsService.Save(settings);
                if (!success)
                {
                    RosterDiagnostics.WriteLine($"ThemeService: บันทึกธีมลง settings ไม่สำเร็จ ({error}) - ธีมยังเปลี่ยนแล้วในรอบนี้");
                }
            },
            log: RosterDiagnostics.WriteLine,
            accentLightUri: "/Theme/Accent.Light.xaml",
            accentDarkUri: "/Theme/Accent.Dark.xaml");

        // global handler กัน exception ที่หลุดจาก event handler ไหนก็ตามทำให้ทั้ง process ตาย -
        // pattern เดียวกับ Console/App.xaml.cs (บั๊กจริงที่เคยเจอ: async void event handler ไม่มี
        // try/catch ทำให้ทั้งแอปหายไปเงียบๆ)
        DispatcherUnhandledException += (_, args) =>
        {
            RosterDiagnostics.WriteLine(
                $"UNHANDLED EXCEPTION: {args.Exception.GetType().FullName}: {args.Exception.Message}\n{args.Exception.StackTrace}");

            MessageBox.Show(
                $"เกิดข้อผิดพลาดที่ไม่คาดคิด:\n\n{args.Exception.Message}\n\nรายละเอียดเต็มถูกบันทึกไว้ที่:\n{RosterDiagnostics.LogPath}",
                "เกิดข้อผิดพลาด", MessageBoxButton.OK, MessageBoxImage.Error);

            args.Handled = true;
        };

#if DEBUG
        // เครื่องมือจับภาพตรวจ UI เท่านั้น (WP3-2) - pattern เดียวกับ Console/App.xaml.cs's --dialog=/
        // --shot=/--theme= แต่เล็กกว่ามาก (ไม่มี catalog หน้าต่างย่อยครบทุกตัวแบบ Console) มีเฉพาะ
        // --theme=light|dark (สลับโดยไม่บันทึกลง roster-settings.json), --window=login|roster
        // (เลือกหน้าที่จะถ่าย) และสำหรับ roster ต้องมี --server=host:port --user=... --pass=...
        // (ล็อกอินกับ Server ทดสอบจริงก่อนเปิดหน้า - ไม่มี preview mode ปลอมข้อมูลแบบ Console เพราะ
        // Roster Manager ไม่มี PreviewMode property ใน RosterWindow) ไม่มีใน build Release เพราะ
        // publish-all.ps1 publish ด้วย -c Release เสมอ
        var themeArg = e.Args.FirstOrDefault(a => a.StartsWith("--theme=", StringComparison.Ordinal));
        if (themeArg is not null &&
            Enum.TryParse<AppTheme>(themeArg["--theme=".Length..], ignoreCase: true, out var forcedTheme))
        {
            ThemeService.Set(forcedTheme);
        }

        var windowArg = e.Args.FirstOrDefault(a => a.StartsWith("--window=", StringComparison.Ordinal));
        var shotArg = e.Args.FirstOrDefault(a => a.StartsWith("--shot=", StringComparison.Ordinal));
        if (windowArg is not null && shotArg is not null)
        {
            _ = RunCaptureAsync(windowArg["--window=".Length..], shotArg["--shot=".Length..], e.Args);
            return;
        }
#endif
    }

#if DEBUG
    private async Task RunCaptureAsync(string windowName, string shotPath, string[] args)
    {
        Window window;

        if (windowName == "login")
        {
            window = new LoginWindow();
        }
        else if (windowName == "roster")
        {
            var server = Arg(args, "--server=") ?? "localhost:18787";
            var user = Arg(args, "--user=") ?? "Administrator";
            var pass = Arg(args, "--pass=") ?? "";

            var api = new Services.ApiClient();
            api.SetServer(server, useHttps: false);
            var (success, message) = await api.LoginAsync(user, pass);
            if (!success)
            {
                RosterDiagnostics.WriteLine($"--window=roster: login ไม่สำเร็จ ({message}) - ถ่ายภาพไม่ได้");
                Shutdown(3);
                return;
            }

            window = new RosterWindow(api);
        }
        else
        {
            RosterDiagnostics.WriteLine($"--window: ไม่รู้จักชื่อ '{windowName}' - ปิดโปรแกรม");
            Shutdown(2);
            return;
        }

        window.ShowActivated = false;
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = -32000;
        window.Top = -32000;
        window.Show();

        // รอให้ InitialLoadAsync (roster) หรือภาพ hero (login) โหลดเสร็จก่อนถ่าย - ไม่มี event ที่
        // บอกว่า "โหลดเสร็จแล้ว" ตรงๆ ง่ายกว่าคือรอเวลาคงที่ให้พอ (เครื่องมือตรวจ UI เท่านั้น ไม่ใช่โค้ด
        // ที่ใช้งานจริง จึงไม่คุ้มที่จะเพิ่ม event/Task ให้รอตรงจุดเป๊ะ)
        await Task.Delay(1200);
        await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);

        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(
            (int)window.ActualWidth, (int)window.ActualHeight, 96, 96,
            System.Windows.Media.PixelFormats.Pbgra32);
        bitmap.Render(window);

        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using (var stream = System.IO.File.Create(shotPath))
        {
            encoder.Save(stream);
        }

        Shutdown();
    }

    private static string? Arg(string[] args, string prefix) =>
        args.FirstOrDefault(a => a.StartsWith(prefix, StringComparison.Ordinal))?[prefix.Length..];
#endif
}
