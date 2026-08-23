using System.Diagnostics;
using System.IO;
using OnionProcOparetor.AgentTray.Models;

namespace OnionProcOparetor.AgentTray.Services;

/// <summary>
/// สแกนโปรแกรมที่กำลังรันอยู่และมีหน้าต่างบน desktop จริง (ไม่รวม background/service process) -
/// ต้องรันจาก AgentTray (user session จริง) เพราะ Process.MainWindowHandle/MainWindowTitle มองไม่เห็น
/// หน้าต่างของ session อื่นเลย (เหตุผลเดียวกับที่ VNC ต้อง start จาก AgentTray - ดู AgentTrayPipeMessage)
///
/// ใช้ pattern การ enumerate + publisher-check เดียวกับ Worker.ScanRunningProcessesAsync ฝั่ง Agent
/// (enforcement loop เดิม) ต่างกันแค่ตรงนี้ "คืนผลลัพธ์" แทนที่จะ "ลงมือ action" กับ process ที่เจอ
/// </summary>
public static class RunningProcessScanner
{
    public static List<RunningProcessDto> ScanVisibleProcesses()
    {
        var results = new List<RunningProcessDto>();
        var processes = Process.GetProcesses();

        try
        {
            foreach (var process in processes)
            {
                try
                {
                    // เฉพาะโปรแกรมที่มีหน้าต่างจริงบน desktop เท่านั้น (ไม่รวม background/service) -
                    // .NET คำนวณ MainWindowHandle ผ่าน EnumWindows ภายในซึ่งกรอง IsWindowVisible ให้
                    // อยู่แล้ว ไม่ต้องเพิ่ม user32.dll P/Invoke ซ้อนอีกชั้น
                    if (process.MainWindowHandle == IntPtr.Zero || string.IsNullOrWhiteSpace(process.MainWindowTitle))
                    {
                        continue;
                    }

                    string? path = null;
                    try
                    {
                        path = process.MainModule?.FileName;
                    }
                    catch
                    {
                        // ดึง path ไม่ได้ (สิทธิ์ไม่พอ/process elevated กว่า) - ยังลิสต์ไว้แต่ไม่มี publisher info
                    }

                    var check = !string.IsNullOrEmpty(path) && File.Exists(path)
                        ? PublisherChecker.Check(path)
                        : new PublisherCheckResult(null, IsSignedMatch: false, HasPublisher: false);

                    results.Add(new RunningProcessDto
                    {
                        Pid = process.Id,
                        ProcessName = process.ProcessName,
                        WindowTitle = process.MainWindowTitle,
                        ExecutablePath = path,
                        PublisherName = check.PublisherName,
                        IsSignedMatch = check.IsSignedMatch,
                        HasPublisher = check.HasPublisher
                    });
                }
                catch
                {
                    // process ปิดกลางคันหรือเข้าถึงไม่ได้ระหว่าง enumerate - ข้ามตัวนี้ไป ไม่ทำให้ scan ทั้งหมดล้ม
                }
            }
        }
        finally
        {
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }

        return results;
    }
}
