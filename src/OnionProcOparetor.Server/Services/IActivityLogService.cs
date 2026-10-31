using OnionProcOparetor.Server.Models;

namespace OnionProcOparetor.Server.Services;

/// <summary>
/// เขียนแถวลง Activity Log (ดู ActivityLogDbContext) - แยก method ตามหมวดให้ call site อ่านง่าย
/// ว่ากำลังบันทึกอะไร ไม่ต้องมาคิด Category/Severity เองทุกจุด
/// </summary>
public interface IActivityLogService
{
    /// <summary>บันทึกการกระทำของครู/แอดมิน - เรียกหลัง SaveChangesAsync() ของ action นั้นสำเร็จเสมอ</summary>
    Task LogAdminActionAsync(
        AdminUser admin,
        string eventType,
        string description,
        string? targetClientGuid = null,
        string? targetMachineName = null,
        string? detailJson = null);

    /// <summary>บันทึก agent connect/disconnect/online-offline transition</summary>
    Task LogAgentConnectivityAsync(
        string eventType,
        string description,
        string clientGuid,
        string? machineName);

    /// <summary>
    /// บันทึกการกระทำของนักเรียนเอง (WP3-1, 2026-10-31) เช่น StudentLoggedIn, StudentLoggedOut,
    /// StudentPasswordChanged, StudentAccountLocked - student เป็น null ได้เฉพาะกรณี login ล้มเหลวที่
    /// ไม่เจอบัญชีจริง (ไม่มี Id ให้ denormalize) ส่วน studentCodeOverride ใช้แทนตอนนั้น
    /// </summary>
    Task LogStudentActionAsync(
        Student? student,
        string eventType,
        string description,
        string? targetClientGuid = null,
        string? targetMachineName = null,
        string? studentCodeOverride = null);
}
