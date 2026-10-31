using OnionProcOparetor.Agent.Models;

namespace OnionProcOparetor.Agent.Services;

/// <summary>
/// สถานะ "โหมดสอบ" ปัจจุบันของเครื่องนี้ (WP2-2, 2026-10-31) - เก็บใน memory ของ Agent process เท่านั้น
/// (เหมือน AgentBypassState) ตั้งใจให้หายไปเองทุกครั้งที่ Agent restart (ไม่ persist) เพราะ Worker จะ
/// sync ค่าจาก poll รอบแรกให้ใหม่เสมอถ้า Server ยังเห็นว่าโหมดสอบกำลังทำงานอยู่
///
/// หัวใจของ "หมดเวลาเองแม้ Server ล่ม" (ดู docs/roadmap.md WP2-2): <see cref="IsActive"/> คำนวณจาก
/// นาฬิกาของเครื่องนี้เองทุกครั้งที่อ่าน ไม่ใช่ flag ที่ถูกตั้งครั้งเดียว - แปลว่าต่อให้ Worker ไม่ได้
/// poll สำเร็จอีกเลยหลังจากนี้ (Server ล่ม/เน็ตหลุด) โหมดสอบก็จะหยุดเอง ณ ExpiresAtUtc ที่เคยรับมา
/// ครั้งล่าสุดพอดี เพราะทุก enforcement tick (ทุก 10 วิ - ดู Worker.ScanRunningProcessesAsync) อ่าน
/// property นี้สดใหม่เสมอ
///
/// UpdateFromServer ถูกเรียกเฉพาะตอน poll "สำเร็จ" เท่านั้น (ดู Worker.cs) - ส่ง null = Server บอกว่า
/// ไม่มีโหมดสอบทำงานอยู่กับเครื่องนี้แล้ว (หมดเวลาตามปกติ หรือครูยกเลิกก่อนเวลา) ต้องเคลียร์ทันที
/// ไม่ต้องรอให้ IsActive หมดเองฝั่งนี้ - ให้ Server ชนะเสมอเมื่อ Server ยังติดต่อได้ (fresher กว่าเสมอ)
/// </summary>
public class ExamModeState
{
    private readonly object _lock = new();
    private DateTime? _expiresAtUtc;
    private List<string> _allowedDomains = new();

    /// <summary>กำลังทำงานอยู่ไหม ณ ตอนนี้ - คำนวณจากนาฬิกาเครื่องนี้เองทุกครั้ง (ดู doc comment บนคลาส)</summary>
    public bool IsActive
    {
        get { lock (_lock) { return _expiresAtUtc.HasValue && DateTime.UtcNow < _expiresAtUtc.Value; } }
    }

    /// <summary>ค่าดิบล่าสุดที่ได้จาก Server - null = ไม่มีโหมดสอบ (ไม่ได้แปลว่า IsActive เสมอ ดู IsActive)</summary>
    public DateTime? ExpiresAtUtc
    {
        get { lock (_lock) { return _expiresAtUtc; } }
    }

    /// <summary>โดเมนที่อนุญาตของโหมดสอบรอบล่าสุด - ไม่มีความหมายถ้า IsActive เป็น false</summary>
    public IReadOnlyList<string> AllowedDomains
    {
        get { lock (_lock) { return _allowedDomains; } }
    }

    /// <summary>เรียกทุกครั้งที่ poll สำเร็จ (ดู Worker.cs) - examMode เป็น null = Server บอกว่าไม่มีโหมดสอบ</summary>
    public void UpdateFromServer(ExamModeDto? examMode)
    {
        lock (_lock)
        {
            _expiresAtUtc = examMode?.ExpiresAtUtc;
            _allowedDomains = examMode?.AllowedDomains ?? new List<string>();
        }
    }
}
