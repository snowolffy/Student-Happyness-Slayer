using System;
using System.Collections.Generic;
using System.Text.Json;

namespace OnionProcOparetor.Console.Models;

/// <summary>ต้องตรงกับ ProgramAction ฝั่ง Server</summary>
public enum ProgramActionDto
{
    Allow = 0,
    Flag = 1,
    Block = 2
}

/// <summary>
/// หมวดที่ classifier เดาได้ - ต้องตรงกับ ProgramCategory ฝั่ง Server ทั้งชื่อและตัวเลข
/// ใช้เป็นตัวเลือกในกล่อง "หมวด" ของหน้าคิวตรวจสอบ (2026-09-01)
/// </summary>
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

/// <summary>
/// ผลจำแนกโปรแกรมหนึ่งตัวที่ Server ส่งมาให้ Console แสดงในคิวตรวจสอบ
/// ตรงกับ ProgramVerdict ฝั่ง Server (ดูเหตุผลของแต่ละ field ที่นั่น)
/// </summary>
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

    // ---- property สำหรับ binding ใน DataGrid โดยเฉพาะ (ไม่มีฝั่ง Server) ----

    public string ActionText => Action switch
    {
        0 => "ALLOW",
        1 => "REVIEW",
        2 => "BLOCK",
        _ => "?"
    };

    /// <summary>
    /// ชื่อหมวดภาษาไทย - static เพราะกล่องเลือกหมวดใน ProgramReviewWindow ต้องใช้ชื่อชุดเดียวกัน
    /// กับคอลัมน์ในตาราง ถ้าแยกกันเขียนแล้วชื่อไม่ตรง ครูจะเลือก "อีมูเลเตอร์" แล้วเห็นแถวที่คอลัมน์
    /// เขียนคนละคำ ซึ่งดูเหมือนตัวกรองพัง
    /// </summary>
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

    /// <summary>
    /// แปลง ReasonsJson เป็นข้อความอ่านง่ายสำหรับแสดงในหน้าต่าง - เป็นส่วนที่สำคัญที่สุดของหน้านี้
    /// จริงๆ: ครูต้องเห็นว่า "ทำไม" ระบบถึงคิดว่าเป็นเกม ก่อนจะกดยืนยันหรือยกเลิก ไม่ใช่เห็นแค่คะแนน
    /// </summary>
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

                var lines = new List<string>();
                foreach (var s in signals)
                {
                    lines.Add($"  {s.Weight,+4}  {s.Description}");
                }
                return string.Join(Environment.NewLine, lines);
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
