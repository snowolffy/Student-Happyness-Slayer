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

    /// <summary>
    /// SHA-256 ของไฟล์ (hex ตัวเล็ก) - ว่างได้ถ้าอ่านไฟล์ไม่ได้/ไฟล์ใหญ่เกิน (2026-08-31)
    ///
    /// มีไว้ให้ครูกด "บล็อกทุกเครื่อง" จากรายการนี้ได้ตรงๆ โดยไม่ต้องพึ่ง publisher เลย -
    /// ซึ่งเป็นเคสที่ปุ่ม CREATE RULE เดิมทำไม่ได้ เพราะกฎแบบเดิมผูกกับชื่อ publisher
    /// และโปรแกรมที่อยากบล็อกที่สุดมักไม่มี publisher นั่นแหละ
    /// </summary>
    public string? Sha256 { get; set; }

    /// <summary>
    /// เปิดปุ่ม "BLOCK" ได้ก็ต่อเมื่ออ่านไฟล์จนได้ hash จริง - ไม่มี hash ก็ผูกคำตัดสินกับอะไรไม่ได้
    /// (เกิดได้กับ process ของระบบที่ AgentTray สิทธิ์ไม่พอจะเปิดอ่าน ซึ่งเป็นของที่ไม่ควรบล็อกอยู่แล้ว)
    /// </summary>
    public bool CanBlockByHash => !string.IsNullOrWhiteSpace(Sha256);
}
