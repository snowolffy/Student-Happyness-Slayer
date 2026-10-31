namespace OnionProcOparetor.Console.Models;

/// <summary>แถวประวัติเว็บหนึ่งรายการ (WP2-4, 2026-10-31) - ดู Server/Models/WebVisit.cs</summary>
public class WebVisitDto
{
    public int Id { get; set; }
    public string ClientGuid { get; set; } = string.Empty;
    public string MachineName { get; set; } = string.Empty;
    public DateTime VisitedAt { get; set; }
    public string Domain { get; set; } = string.Empty;
    public string? PageTitle { get; set; }
    public bool WasBlocked { get; set; }
    public int? StudentId { get; set; }
    public string? StudentFullName { get; set; }
    public string? StudentClassName { get; set; }

    /// <summary>ไว้แสดงคอลัมน์ "นักเรียน" ตรงกับ wireframe ("[ชื่อ] · {{ClassName}}") - ว่างถ้าไม่มีใคร
    /// login อยู่บนเครื่องนั้นตอนที่เข้าเว็บ (ดู docs/roadmap.md WP2-4 "คอลัมน์นักเรียนยังว่างจนกว่าขั้น 3
    /// จะทำ" - ตอนนี้ขั้น 3/WP3-1 มีอยู่แล้วจึงมีค่าจริงได้ตั้งแต่ WP นี้)</summary>
    public string StudentDisplayText => StudentFullName is null ? string.Empty : $"{StudentFullName} · {StudentClassName}";

    /// <summary>ไว้แสดงคอลัมน์ "ผล" เป็น badge - คู่กับ ResultBadgeTag ด้านล่าง</summary>
    public string ResultText => WasBlocked ? "ถูกบล็อก" : "เปิดได้";

    /// <summary>Tag ของ StatusBadge style (Theme/Controls.xaml) - "Danger" แดงตอนถูกบล็อก</summary>
    public string ResultBadgeTag => WasBlocked ? "Danger" : "Online";

    /// <summary>checkbox ที่เลือกในตาราง (client-side ล้วนๆ ไม่ส่งขึ้น Server) - ใช้กับปุ่ม "บล็อกโดเมนที่เลือก"</summary>
    public bool IsSelected { get; set; }
}

public class WebVisitsPageDto
{
    public List<WebVisitDto> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 200;
}

/// <summary>แถวของ "เว็บที่เข้าบ่อยช่วงนี้" (aside ของ con-website-history.dc.html)</summary>
public class TopWebDomainDto
{
    public string Domain { get; set; } = string.Empty;
    public int Count { get; set; }
    public bool AnyBlocked { get; set; }
}
