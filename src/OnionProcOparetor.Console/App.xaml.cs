using System.Reflection;
using System.Windows;
using OnionProcOparetor.Console.Services;

namespace OnionProcOparetor.Console;

public partial class App : Application
{
    /// <summary>
    /// log บรรทัดแรกสุดของทุก run - เพิ่มไว้เพื่อ diagnose โดยเฉพาะว่า instance ที่กำลังทดสอบอยู่จริง
    /// เป็น build ล่าสุดที่ deploy ไปหรือเปล่า (ProcessPath บอก path ที่รันอยู่จริง, Version บอกว่าตรงกับ
    /// build ล่าสุดไหม) - console.log ไม่มี entry ใหม่เลยหลายรอบก่อนหน้านี้ทั้งที่ควรมี ทำให้สงสัยว่า
    /// instance ที่ทดสอบอยู่อาจไม่ใช่ build ที่เพิ่ง compile/deploy จริง
    /// </summary>
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var version = Assembly.GetExecutingAssembly().GetName().Version;
        ConsoleDiagnostics.WriteLine($"=== Console เริ่มทำงาน === Version: {version}, ProcessPath: {Environment.ProcessPath}, PID: {Environment.ProcessId}");

        // โหลด console-settings.json (หรือ default ถ้ายังไม่มี) แล้ว apply ค่า VNC limit ให้ทันก่อนหน้าต่างไหนเปิดขึ้นมา
        SettingsService.EnsureLoaded();
    }
}
