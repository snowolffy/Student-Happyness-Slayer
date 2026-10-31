namespace OnionProcOparetor.Agent.Models;

/// <summary>
/// payload ของ command "LockWorkstation" (WP3-3, 2026-10-31) - ส่งมาจาก Console
/// (LockScreenMessageWindow, ดู WP3-5 ActionPanelWindow.xaml.cs LockButton_Click) Message เป็น null/
/// ว่าง = ครูเว้นว่างไว้ตอนกดล็อก ให้ CommandProcessor.HandleShowLockScreenAsync ใช้ข้อความเริ่มต้นจาก
/// SignInSettings.LockScreenDefaultMessage (cache ไว้ใน ClientState) แทน
/// </summary>
public class LockWorkstationPayload
{
    public string? Message { get; set; }
}
