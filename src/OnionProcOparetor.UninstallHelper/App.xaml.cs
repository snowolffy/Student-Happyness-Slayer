using System.Diagnostics;
using System.IO;
using System.Windows;

namespace OnionProcOparetor.UninstallHelper;

public partial class App : Application
{
    private const string RelaunchedEnvVar = "ONIONPROC_UNINSTALLHELPER_RELAUNCHED";

    /// <summary>
    /// บั๊กจริงที่เจอ (2026-08-11, live-tested): .exe/.dll ของตัวเองอยู่ใน
    /// [INSTALLFOLDER]\UninstallHelper\ ซึ่งเป็นส่วนหนึ่งของโฟลเดอร์ที่ msiexec /x กำลังจะลบทิ้งทั้งหมด
    /// (RemoveFiles + RemoveInstallFolderExtras) - ถ้า process นี้ยังรันอยู่ที่ path เดิมตอนนั้น (lock
    /// ไฟล์ตัวเองไว้เสมอ ตราบใดที่ยัง process ยังไม่ exit) msiexec ลบไฟล์/โฟลเดอร์ตรงนั้นไม่ได้เลย - ยืนยัน
    /// จากการทดสอบจริงตรงๆ: Windows Installer log บอกว่า "Product removed" (สำเร็จใน MSI database) แต่
    /// ไฟล์ในโฟลเดอร์ UninstallHelper\ (รวมถึงตัว .exe นี้เอง) ยังค้างอยู่บนดิสก์ - ทำให้ folder เก่าไม่ถูก
    /// ลบสมบูรณ์ และอาจกระทบ service registration ที่ตามมา (ดู comment ยาวใน MainWindow.xaml.cs)
    ///
    /// แก้ด้วย pattern มาตรฐานของ self-deleting installer: copy ตัวเองทั้งโฟลเดอร์ไปที่ %TEMP% แล้ว
    /// relaunch จากตรงนั้นแทนก่อนทำอะไรอื่นทั้งหมด (ผ่าน env var marker กันการ relaunch วนไม่รู้จบ) -
    /// instance ต้นฉบับที่ [INSTALLFOLDER]\UninstallHelper\ จะ exit ทันที ไม่ lock ไฟล์ตัวเองอีกต่อไป
    /// พอถึงตอน msiexec /x จริงๆ รันจาก instance ที่ temp แล้ว ไฟล์ที่ [INSTALLFOLDER] จึงลบได้หมดจด
    /// </summary>
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (Environment.GetEnvironmentVariable(RelaunchedEnvVar) != "1")
        {
            RelaunchFromTempAndExit(e.Args);
            return;
        }

        // มาถึงตรงนี้แปลว่าเป็น instance ที่ relaunch จาก temp แล้ว - ทำงานตามปกติ (StartupUri สร้าง
        // MainWindow ต่อเองตามปกติหลังจากนี้)
    }

    private void RelaunchFromTempAndExit(string[] args)
    {
        try
        {
            var sourceDir = AppContext.BaseDirectory;
            var tempDir = Path.Combine(Path.GetTempPath(), "OnionProcOparetorUninstall-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            foreach (var file in Directory.GetFiles(sourceDir))
            {
                File.Copy(file, Path.Combine(tempDir, Path.GetFileName(file)), overwrite: true);
            }

            var tempExePath = Path.Combine(tempDir, "OnionProcOparetor.UninstallHelper.exe");

            // UseShellExecute=false (ไม่ใช่ ShellExecute+runas) เพราะ process นี้ (ตัวต้นฉบับ) ถูกเรียก
            // มาแบบ elevated อยู่แล้วเสมอ (ARP UninstallString ผ่าน Explorer หรือ msiexec เอง) - child
            // process ที่ spawn ตรงๆ inherit elevation ของ parent ทันที ไม่ต้องเจอ UAC prompt ซ้ำสอง
            // (pattern เดียวกับที่ MainWindow.xaml.cs ใช้ยิง msiexec.exe เองอยู่แล้ว) - ใช้ UseShellExecute=false
            // ด้วยเพราะ psi.Environment (ตั้ง marker env var) ใช้ไม่ได้เลยถ้า UseShellExecute=true
            var psi = new ProcessStartInfo
            {
                FileName = tempExePath,
                UseShellExecute = false
            };
            foreach (var arg in args)
            {
                psi.ArgumentList.Add(arg);
            }
            psi.Environment[RelaunchedEnvVar] = "1";

            Process.Start(psi);
        }
        catch
        {
            // Copy/relaunch ไม่สำเร็จ (เช่น %TEMP% เขียนไม่ได้) - fallback: ทำงานต่อจาก instance เดิมตรงนี้
            // เลย (ยังใช้งานได้ปกติทุกอย่าง เพียงแต่จะกลับไปเจอปัญหา lock ไฟล์ตัวเองตอน uninstall เหมือนเดิม
            // ดีกว่าไม่ทำงานเลย)
            return;
        }

        // Environment.Exit (ไม่ใช่ Shutdown()) เพื่อตัดจบ WPF startup pipeline ทันทีแบบไม่มีข้อกำกวม -
        // กัน StartupUri="MainWindow.xaml" สร้างหน้าต่างซ้อนขึ้นมาอีกบาน
        Environment.Exit(0);
    }
}
