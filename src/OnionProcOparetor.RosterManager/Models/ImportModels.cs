namespace OnionProcOparetor.RosterManager.Models;

/// <summary>ตรงกับ ImportStudentsRequest ฝั่ง Server (RosterEndpoints.cs) ทุก field</summary>
public class ImportStudentsRequest
{
    public string CsvText { get; set; } = string.Empty;
    public string StudentCodeColumn { get; set; } = string.Empty;
    public string FullNameColumn { get; set; } = string.Empty;
    public string? EmailColumn { get; set; }
    public string ClassNameColumn { get; set; } = string.Empty;
    public string? BirthDateColumn { get; set; }

    /// <summary>0 = UpdateExisting, 1 = SkipExisting (ตรง ImportDuplicatePolicy ฝั่ง Server)</summary>
    public int DuplicatePolicy { get; set; }

    /// <summary>0 = Random6Digit, 1 = BirthDate, 2 = SameForAll (ตรง ImportPasswordPolicy ฝั่ง Server)</summary>
    public int PasswordPolicy { get; set; }

    public string? FixedPassword { get; set; }
    public bool ForceChangePasswordOnFirstLogin { get; set; } = true;
}

/// <summary>แถวเดียวของผลตรวจ/นำเข้า CSV (nf-import ตาราง) ตรงกับ ImportRowResult ฝั่ง Server</summary>
public class ImportRowResultDto
{
    public int RowNumber { get; set; }
    public string StudentCode { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string ClassName { get; set; } = string.Empty;

    /// <summary>"New" / "DuplicateUpdate" / "DuplicateSkip" / "Error"</summary>
    public string Result { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }

    /// <summary>มีค่าเฉพาะตอน commit=true และแถวเป็น "New" เท่านั้น (ดู ProcessImportAsync ฝั่ง Server)</summary>
    public string? GeneratedPassword { get; set; }

    /// <summary>ป้ายผลตรวจแบบไทยสำหรับตาราง nf-import</summary>
    public string ResultLabel => Result switch
    {
        "New" => "ใหม่",
        "DuplicateUpdate" => "ซ้ำ → อัปเดต",
        "DuplicateSkip" => "ซ้ำ → ข้าม",
        "Error" => ErrorMessage ?? "มีปัญหา",
        _ => Result
    };
}

/// <summary>สรุปผลนำเข้า CSV ทั้งไฟล์ - ตรงกับ ImportSummary ฝั่ง Server</summary>
public class ImportSummaryDto
{
    public int TotalRows { get; set; }
    public int NewCount { get; set; }
    public int DuplicateUpdateCount { get; set; }
    public int DuplicateSkipCount { get; set; }
    public int ErrorCount { get; set; }
    public List<ImportRowResultDto> Rows { get; set; } = new();
}

/// <summary>แถวของ "ประวัติการใช้เครื่อง" (GET /api/students/sessions/history) - ใช้เติม aside "ใช้งานล่าสุด" ของ nf-roster</summary>
public class SessionHistoryRowDto
{
    public int Id { get; set; }
    public string ClientGuid { get; set; } = string.Empty;
    public string? MachineName { get; set; }
    public int? RoomId { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public string? EndReason { get; set; }
}

/// <summary>ใบรหัสผ่านหนึ่งใบ (nf-print / PrintSlipsWindow) - รวมข้อมูลจาก StudentDto + รหัสผ่านที่ Server คืนมาครั้งเดียว</summary>
public class PasswordSlipDto
{
    public required string StudentCode { get; set; }
    public required string FullName { get; set; }
    public required string ClassName { get; set; }
    public string? SchoolEmail { get; set; }
    public required string Password { get; set; }
}
