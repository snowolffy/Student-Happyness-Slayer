using System.Text.Json;

namespace OnionProcOparetor.OnlineConsole.Models;

/// <summary>Ported from OnionProcOparetor.Console/Models/ProgramVerdictDto.cs (identical) - ต้องตรงกับ ProgramAction ฝั่ง Server</summary>
public enum ProgramActionDto
{
    Allow = 0,
    Flag = 1,
    Block = 2
}

/// <summary>ต้องตรงกับ ProgramCategory ฝั่ง Server ทั้งชื่อและตัวเลข</summary>
public enum ProgramCategoryDto
{
    Unknown = 0,
    Game = 1,
    GameLauncher = 2,
    Emulator = 3,
    Streaming = 4,
    Chat = 5,
    RemoteAccess = 6,
    SystemOrProductivity = 7,
    Inappropriate = 8
}

/// <summary>จำนวนแถวแยกตามหมวด - ผลจาก GET /api/program-verdicts/summary</summary>
public class ProgramVerdictSummaryDto
{
    public int Total { get; set; }
    public List<CategoryCount> Categories { get; set; } = new();

    public class CategoryCount
    {
        public int Category { get; set; }
        public int Count { get; set; }
    }
}

/// <summary>ผลจำแนกโปรแกรมหนึ่งตัวที่ Server ส่งมาให้แสดงในคิวตรวจสอบ</summary>
public class ProgramVerdictDto
{
    public int Id { get; set; }
    public string Sha256 { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string? ProductName { get; set; }
    public string? Publisher { get; set; }
    public bool IsSignedPublisher { get; set; }
    public int Score { get; set; }
    public int Category { get; set; }
    public int Action { get; set; }
    public string ReasonsJson { get; set; } = "[]";
    public bool IsManual { get; set; }
    public string? DecidedBy { get; set; }
    public string? OnlineMatchName { get; set; }
    public DateTime FirstSeenAt { get; set; }
    public DateTime LastSeenAt { get; set; }
    public string? LastSeenOnMachine { get; set; }
    public int SightingCount { get; set; }

    public string ActionText => Action switch
    {
        0 => "ALLOW",
        1 => "REVIEW",
        2 => "BLOCK",
        _ => "?"
    };

    public static string CategoryLabel(int category) => category switch
    {
        1 => "เกม",
        2 => "ตัวเปิดเกม",
        3 => "อีมูเลเตอร์",
        4 => "สตรีมมิ่ง",
        5 => "แชท",
        6 => "รีโมท",
        7 => "ระบบ/งานทั่วไป",
        8 => "ไม่เหมาะสม",
        _ => "ไม่ทราบ"
    };

    public string CategoryText => CategoryLabel(Category);

    public string SourceText => IsManual ? $"ครู ({DecidedBy})" : "อัตโนมัติ";

    public string DisplayName => string.IsNullOrWhiteSpace(ProductName) ? FileName : $"{FileName}  ({ProductName})";

    public string ReasonsText
    {
        get
        {
            try
            {
                var signals = JsonSerializer.Deserialize<List<SignalDto>>(ReasonsJson);
                if (signals is null || signals.Count == 0)
                {
                    return "(ไม่มีสัญญาณที่บันทึกไว้)";
                }

                return string.Join(Environment.NewLine, signals.Select(s => $"  {s.Weight,+4}  {s.Description}"));
            }
            catch
            {
                return ReasonsJson;
            }
        }
    }

    public class SignalDto
    {
        public string Code { get; set; } = string.Empty;
        public int Weight { get; set; }
        public string Description { get; set; } = string.Empty;
    }
}
