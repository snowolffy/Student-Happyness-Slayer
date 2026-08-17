namespace OnionProcOparetor.Agent.Models;

/// <summary>
/// Payload ของ command "Shutdown" / "Restart" ที่ Console ส่งมา - { "delaySeconds": int }
///
/// เดิม payload เป็น null เสมอ (Agent ใช้ค่า const 10 วิ ตายตัว) ตอนนี้ครูตั้งเวลาได้เองจาก
/// Console แล้ว แต่ยังต้องรองรับ payload null/เก่าอยู่ - ถ้าไม่มี payload มาด้วยให้ใช้
/// DefaultDelaySeconds เหมือนพฤติกรรมเดิมเป๊ะๆ (backward compatible กับ Console เวอร์ชันเก่า
/// หรือ command เก่าที่ค้างอยู่ใน DB ตั้งแต่ก่อน deploy)
/// </summary>
public class PowerActionPayload
{
    /// <summary>ค่าเดิมก่อนมี field นี้ - ใช้เมื่อ payload เป็น null/ว่าง/แปลงไม่ได้</summary>
    public const int DefaultDelaySeconds = 10;

    /// <summary>ต้องตรงกับ Console (ActionPanelWindow.MinDelaySeconds) - defense-in-depth ทั้ง 2 ฝั่ง</summary>
    public const int MinDelaySeconds = 1;

    /// <summary>ต้องตรงกับ Console (ActionPanelWindow.MaxDelaySeconds) - defense-in-depth ทั้ง 2 ฝั่ง</summary>
    public const int MaxDelaySeconds = 120;

    public int DelaySeconds { get; set; } = DefaultDelaySeconds;

    /// <summary>
    /// บีบค่าให้อยู่ในช่วง 1-120 เสมอ - Console validate มาแล้วรอบหนึ่ง แต่ Agent ต้องไม่เชื่อ
    /// ค่าที่รับมาจากเครือข่าย (defense-in-depth) เพราะ POST /api/commands ไม่ได้ validate
    /// payload shape เลย ใครยิง HTTP ตรงเข้ามาก็ใส่ค่าอะไรก็ได้
    /// </summary>
    public static int Clamp(int delaySeconds) =>
        Math.Clamp(delaySeconds, MinDelaySeconds, MaxDelaySeconds);
}
