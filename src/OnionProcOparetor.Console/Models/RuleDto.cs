namespace OnionProcOparetor.Console.Models;

/// <summary>กฎที่ได้จาก GET /api/rules หรือส่งไปตอน POST /api/rules</summary>
public class RuleDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string PublisherName { get; set; } = string.Empty;

    /// <summary>[OPTIONAL] narrow เพิ่มจาก publisher เช่น "notepad" - เว้นว่างได้ถ้าไม่รู้ชื่อไฟล์ล่วงหน้า</summary>
    public string? ProcessNameContains { get; set; }

    public bool RequireSignedMatch { get; set; } = true;
    public bool KillProcess { get; set; } = false;
    public string? ActionCommand { get; set; }
    public string? ActionArguments { get; set; }
    public bool IsEnabled { get; set; } = true;

    /// <summary>ห้องที่กฎนี้ใช้ด้วย (WP2-3, 2026-10-31) - null = ใช้กับทุกห้อง</summary>
    public int? RoomId { get; set; }

    /// <summary>
    /// ข้อความบนปุ่มสลับสถานะในตาราง Rules - บอก "สิ่งที่จะเกิดถ้ากด" ไม่ใช่คำว่า "TOGGLE"
    ///
    /// ของเดิมทุกแถวเขียนว่า TOGGLE เหมือนกันหมด ครูจึงต้องไปไล่อ่านคอลัมน์ STATUS ก่อนทุกครั้งว่ากด
    /// แล้วกฎจะเปิดหรือปิด - ซึ่งเป็นข้อมูลที่ปุ่มควรบอกเองอยู่แล้ว
    /// </summary>
    public string ToggleActionText => IsEnabled ? "ปิดใช้" : "เปิดใช้";

    /// <summary>
    /// โชว์ในคอลัมน์ "ใช้กับ" ของตาราง (WP2-3) - endpoint ไม่ join ชื่อห้องมาให้ตรงๆ (แค่ RoomId)
    /// DashboardWindow.xaml.cs เป็นคนเซ็ตชื่อจริงให้หลังโหลด room list เสร็จ เหตุผลและ pattern เดียวกับ
    /// WebsiteRuleDto.SourceText
    /// </summary>
    public string ScopeText { get; set; } = "ทุกห้อง";
}
