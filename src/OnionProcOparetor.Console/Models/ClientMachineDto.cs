namespace OnionProcOparetor.Console.Models;

/// <summary>ข้อมูลเครื่อง client ที่ได้จาก GET /api/clients</summary>
public class ClientMachineDto
{
    public int Id { get; set; }
    public string ClientGuid { get; set; } = string.Empty;
    public string MachineName { get; set; } = string.Empty;
    public string? LastKnownIp { get; set; }
    public bool IsEnabled { get; set; }
    public DateTime LastSeenAt { get; set; }
    public DateTime RegisteredAt { get; set; }
    public int? PollIntervalOverrideSeconds { get; set; }

    /// <summary>
    /// คำนวณฝั่ง Server (ClientMachine.IsOnline) ส่งมาเป็น field นี้ตรงๆ ทั้งจาก GET /api/clients และ
    /// SignalR ClientStatusChanged - ไม่คำนวณเองฝั่ง Console อีกต่อไป (เดิมมี IsOffline คำนวณจาก
    /// LastSeenAt ที่นี่แยกกับ IsMissingUnexpectedly ที่คำนวณฝั่ง Server คนละเกณฑ์กัน สับสน และเสี่ยง
    /// clock skew ระหว่าง 2 เครื่อง - ยุบเหลือ IsOnline ตัวเดียว คำนวณที่ Server แหล่งเดียว 2026-08-20)
    ///
    /// เป็นคนละเรื่องกับ IsEnabled โดยสิ้นเชิง - IsOnline บอกว่า "เครื่องติดต่อได้ไหม" ส่วน IsEnabled บอกว่า
    /// "ครูเปิดใช้งาน enforcement เครื่องนี้ไว้ไหม" ดู DashboardWindow.xaml's STATUS/ENFORCEMENT column
    /// </summary>
    public bool IsOnline { get; set; }
}