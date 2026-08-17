namespace OnionProcOparetor.Agent.Models;

/// <summary>
/// ผลลัพธ์ "จริง" ของการทำ action หนึ่ง command - ต่างจาก ack เดิมที่บอกแค่ว่าได้รับ command แล้ว
/// ส่งกลับไปให้ Server ผ่าน CommandResultReporter แล้ว Console เอาไปแสดงในหน้า report
/// </summary>
/// <param name="Success">true = action ทำได้จริง, false = ทำไม่ได้ (ต้องมี Message บอกสาเหตุเสมอ)</param>
/// <param name="Message">
/// รายละเอียดเพิ่มเติม - บังคับมีเมื่อ Success=false (ครูต้องรู้ว่าทำไมถึงล้มเหลว)
/// กรณี Success=true ใส่ได้ถ้ามีหมายเหตุที่ควรรู้ (เช่น "สั่งดับแล้วแต่โชว์คำเตือนให้ user ไม่ได้")
/// </param>
public record CommandExecutionResult(bool Success, string? Message = null)
{
    public static CommandExecutionResult Ok(string? message = null) => new(true, message);

    public static CommandExecutionResult Fail(string message) => new(false, message);
}
