namespace OnionProcOparetor.AgentTray.Models;

/// <summary>
/// ต้องตรงกับ OnionProcOparetor.Server.Models.StudentSessionEndReason เป๊ะๆ (ก๊อปมือแบบเดียวกับ
/// AgentTrayPipeMessage - ไม่มี shared library ระหว่างโปรเจกต์) ห้ามเปลี่ยนลำดับ/ค่าตัวเลข
/// </summary>
public enum StudentSessionEndReason
{
    ManualLogout,
    Idle,
    Shutdown,
    TeacherForced,
    NotMe,
    KickedByOtherLogin
}
