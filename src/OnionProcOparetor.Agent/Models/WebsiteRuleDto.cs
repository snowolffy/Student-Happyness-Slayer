namespace OnionProcOparetor.Agent.Models;

/// <summary>
/// กฎบล็อกเว็บที่รับมาจาก Server (โครงสร้างต้องตรงกับ WebsiteRule ฝั่ง Server) - เป็น list ที่กรอง
/// เฉพาะ IsEnabled มาแล้วจาก /api/poll (ดู Server/Program.cs) Agent ไม่ต้องกรองซ้ำ
/// </summary>
public class WebsiteRuleDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string UrlPattern { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
}
