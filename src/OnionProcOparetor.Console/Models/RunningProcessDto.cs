namespace OnionProcOparetor.Console.Models;

/// <summary>
/// โปรแกรมหนึ่งตัวที่กำลังรันอยู่และมีหน้าต่างบน desktop จริงของเครื่อง client (ได้จาก
/// ProcessListReady หลังกด "VIEW RUNNING PROCESSES...")
///
/// สำคัญ: ไฟล์นี้ "ก๊อปมือ" ไว้คู่กับ OnionProcOparetor.AgentTray/Models/RunningProcessDto.cs (ไม่มี
/// shared class library ระหว่างโปรเจกต์) - แก้ field ที่นี่แล้วต้องไปแก้ฝั่ง AgentTray ด้วยทุกครั้ง
/// </summary>
public class RunningProcessDto
{
    public int Pid { get; set; }
    public string ProcessName { get; set; } = string.Empty;
    public string WindowTitle { get; set; } = string.Empty;
    public string? ExecutablePath { get; set; }
    public string? PublisherName { get; set; }
    public bool IsSignedMatch { get; set; }
    public bool HasPublisher { get; set; }
}
