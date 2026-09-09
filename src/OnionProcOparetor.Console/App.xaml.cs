using System.Windows;
using OnionProcOparetor.Console.Services;

namespace OnionProcOparetor.Console;

public partial class App : Application
{
    /// <summary>
    /// log บรรทัดแรกสุดของทุก run - เพิ่มไว้เพื่อ diagnose โดยเฉพาะว่า instance ที่กำลังทดสอบอยู่จริง
    /// เป็น build ล่าสุดที่ deploy ไปหรือเปล่า (ProcessPath บอก path ที่รันอยู่จริง, FileVersion บอกว่า
    /// ตรงกับ build ล่าสุดไหม) - console.log ไม่มี entry ใหม่เลยหลายรอบก่อนหน้านี้ทั้งที่ควรมี ทำให้สงสัยว่า
    /// instance ที่ทดสอบอยู่อาจไม่ใช่ build ที่เพิ่ง compile/deploy จริง
    ///
    /// ใช้ FileVersion (ไม่ใช่ AssemblyVersion เดิม) เพราะ AssemblyVersion ตั้งใจคงที่ตาม
    /// Directory.Build.props เสมอแล้ว (กัน binding-redirect) - FileVersion ต่างหากที่เปลี่ยนทุกรอบ
    /// publish-all.ps1 จริง (ดู comment ที่นั่นสำหรับที่มาของบั๊กที่แก้)
    /// </summary>
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var fileVersion = System.Diagnostics.FileVersionInfo.GetVersionInfo(Environment.ProcessPath!).FileVersion;
        ConsoleDiagnostics.WriteLine($"=== Console เริ่มทำงาน === FileVersion: {fileVersion}, ProcessPath: {Environment.ProcessPath}, PID: {Environment.ProcessId}");

        // โหลด console-settings.json (หรือ default ถ้ายังไม่มี) แล้ว apply ค่า VNC limit ให้ทันก่อนหน้าต่างไหนเปิดขึ้นมา
        SettingsService.EnsureLoaded();

        // ต้องอยู่ *หลัง* EnsureLoaded (อ่านค่าธีมที่ผู้ใช้เลือกไว้จาก settings) และ *ก่อน* หน้าต่างแรกถูกสร้าง
        // (StartupUri ทำงานหลัง OnStartup) ไม่งั้น MainWindow จะวาดด้วยธีมสว่างแล้วค่อยกระพริบเปลี่ยนเป็นมืด
        ThemeService.Initialize();

        // บั๊กจริงที่เจอซ้ำ 2 ครั้ง (2026-08-25): "VIEW RUNNING PROCESSES..." ทำให้ทั้ง Console หายไปเงียบๆ
        // ทันทีที่กด - รอบแรกเข้าใจผิดว่าอยู่ใน RunningProcessesWindow เอง (ครอบ try/catch ไปแล้ว) แต่ crash
        // จริงอยู่ที่ตอน "เปิด" หน้าต่าง (DashboardWindow.RunActionPanelAsync ที่เรียกจาก
        // ClientActionsButton_Click ซึ่งเป็น async void ไม่มี try/catch เหมือนกัน) - ยืนยันว่า pattern นี้
        // (async void event handler ไม่มี try/catch + ไม่มี global handler กันไว้เลย) เป็นความเสี่ยงทั่วทั้ง
        // แอป ไม่ใช่จุดเดียว แก้แบบเจาะจุดต่อไปเรื่อยๆ ไม่จบ - เพิ่ม global handler ตรงนี้แทน กัน exception
        // ที่หลุดจาก event handler ไหนก็ตามทำให้ทั้ง process ตาย เขียน log เต็มๆ ไว้ที่ console.log (มี path
        // เดียวกับที่ ConsoleDiagnostics เขียน startup diagnostic อยู่แล้ว) แทนที่จะหายไปเงียบๆ แบบเดิม
        DispatcherUnhandledException += (_, args) =>
        {
            ConsoleDiagnostics.WriteLine(
                $"UNHANDLED EXCEPTION: {args.Exception.GetType().FullName}: {args.Exception.Message}\n{args.Exception.StackTrace}\n" +
                (args.Exception.InnerException is not null
                    ? $"Inner: {args.Exception.InnerException.GetType().FullName}: {args.Exception.InnerException.Message}\n{args.Exception.InnerException.StackTrace}\n"
                    : string.Empty));

            MessageBox.Show(
                $"เกิดข้อผิดพลาดที่ไม่คาดคิด:\n\n{args.Exception.Message}\n\nรายละเอียดเต็มถูกบันทึกไว้ที่:\n{ConsoleDiagnostics.LogPath}",
                "เกิดข้อผิดพลาด", MessageBoxButton.OK, MessageBoxImage.Error);

            // สำคัญ: บอก WPF ว่า "จัดการแล้ว" กัน process ตายทั้งตัว - โปรแกรมยังทำงานต่อได้ปกติ
            // (หน้าต่างที่กำลังเปิดอยู่ตอนเกิด exception อาจอยู่ในสถานะไม่สมบูรณ์ แต่ดีกว่าทั้งแอปหายไปเงียบๆ)
            args.Handled = true;
        };

#if DEBUG
        // เครื่องมือสำหรับตรวจงาน UI เท่านั้น และมีเฉพาะใน build แบบ Debug (installer/publish-all.ps1
        // publish ด้วย -c Release เสมอ ปุ่มนี้จึงไม่มีอยู่จริงในของที่ส่งขึ้นเครื่องครู)
        //
        // มีไว้เพราะเครื่อง dev เครื่องนี้รัน Server ตัวจริงอยู่ด้วย การจะดู Dashboard สักทีจึงต้อง
        // login เข้าเซิร์ฟเวอร์ของจริง ซึ่งไม่ควรทำแค่เพื่อดูว่าปุ่มจัดตำแหน่งตรงไหม - ทางนี้เปิด
        // Dashboard ด้วย ApiClient ที่ยังไม่ได้ login ทุก API call จึงล้มเหลวหมด (ตั้งใจ) เห็นแต่โครง UI
        if (e.Args.Contains("--ui-preview"))
        {
            new DashboardWindow(new Services.ApiClient()).Show();
            // StartupUri ถูกประมวลผลหลัง OnStartup คืนค่า (และ set เป็น null จะ throw) เลยต้องปิด
            // หน้าต่าง login ที่มันเปิดให้ ทีหลังแทน - BeginInvoke จึงจำเป็น ไม่ใช่แค่สไตล์
            Dispatcher.BeginInvoke(() =>
            {
                foreach (Window window in Windows)
                {
                    if (window is MainWindow login)
                    {
                        login.Close();
                        break;
                    }
                }
            });
        }
#endif
    }
}
