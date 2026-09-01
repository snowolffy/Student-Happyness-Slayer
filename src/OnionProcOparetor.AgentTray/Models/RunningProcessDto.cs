namespace OnionProcOparetor.AgentTray.Models;

/// <summary>
/// โปรแกรมหนึ่งตัวที่กำลังรันอยู่และมีหน้าต่างบน desktop จริง (ดู RunningProcessScanner)
///
/// สำคัญ: ไฟล์นี้ "ก๊อปมือ" ไว้คู่กับ OnionProcOparetor.Console/Models/RunningProcessDto.cs (ไม่มี
/// shared class library ระหว่างโปรเจกต์ - pattern เดียวกับ RuleDto/LogEntryDto) Server/Agent ไม่ต้อง
/// รู้จัก type นี้เลย เพราะ relay กันเป็น JSON string ดิบตลอดทาง (เหมือน port/password ของ VNC ที่
/// เป็น primitive ธรรมดา) - แก้ field ที่นี่แล้วต้องไปแก้ฝั่ง Console ด้วยทุกครั้ง
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

    /// <summary>
    /// SHA-256 ของไฟล์ (hex ตัวเล็ก) - ว่างได้ถ้าอ่านไฟล์ไม่ได้/ไฟล์ใหญ่เกิน (2026-08-31)
    ///
    /// มีไว้ให้ครูกด "บล็อกทุกเครื่อง" จากรายการนี้ได้ตรงๆ โดยไม่ต้องพึ่ง publisher เลย -
    /// ซึ่งเป็นเคสที่ปุ่ม CREATE RULE เดิมทำไม่ได้ เพราะกฎแบบเดิมผูกกับชื่อ publisher
    /// และโปรแกรมที่อยากบล็อกที่สุดมักไม่มี publisher นั่นแหละ
    /// </summary>
    public string? Sha256 { get; set; }
}
