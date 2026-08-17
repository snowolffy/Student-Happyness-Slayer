using System.ComponentModel;
using System.Windows;
using OnionProcOparetor.AgentTray.Models;

namespace OnionProcOparetor.AgentTray;

/// <summary>
/// หน้าต่างเล็กมุมจอล่างขวา นับถอยหลังก่อนเครื่อง shutdown/restart ตามคำสั่งจาก Console
///
/// แยกจาก BroadcastPopupWindow เดิม (popup กลางจอมีปุ่มปิด auto-close 20 วิ - ใช้กับข้อความ
/// ทั่วไปจากครู) และแยกจาก LockScreenWindow (full-screen บล็อก input ทั้งหมด) โดยตั้งใจ
/// เพราะเป็นคนละเจตนากันคนละแบบ:
/// - Broadcast popup = "อ่านแล้วปิดได้"
/// - LockScreen     = "ห้ามใช้เครื่อง"
/// - หน้านี้        = "ใช้เครื่องต่อได้ แต่รีบเซฟ เพราะอีก N วิเครื่องดับแน่นอน"
///
/// ปิดเองอัตโนมัติเมื่อนับถึง 0 (จังหวะเดียวกับที่ฝั่ง Agent เรียก ExitWindowsEx พอดี)
/// user ปิดเองไม่ได้ - กันปิดทิ้งแล้วลืมว่าเครื่องกำลังจะดับ (pattern เดียวกับ LockScreenWindow
/// ที่ override OnClosing กันปิด แต่ที่นี่ปลดล็อกให้ตอนนับครบแทนที่จะรอ command จาก Console)
/// </summary>
public partial class ShutdownNotifyWindow : Window
{
    /// <summary>ระยะห่างจากขอบจอ (px) - เว้นไว้ไม่ให้ชนขอบ/ทับ taskbar พอดีเป๊ะ</summary>
    private const double ScreenMargin = 16;

    private bool _allowClose;

    public ShutdownNotifyWindow(ShutdownNotifyDto notify)
    {
        InitializeComponent();

        Countdown.Completed += (_, _) => ForceClose();

        // ข้อความตามสเปก: "เครื่องนี้จะ [Shutdown/Restart] ในอีก {N} วินาที โดยผู้ดูแลระบบ"
        Countdown.Start(notify.DelaySeconds, $"เครื่องนี้จะ{notify.ActionLabel}ในอีก {{0}} วินาที โดยผู้ดูแลระบบ");

        HintText.Text = notify.IsRestart
            ? "กรุณาบันทึกงานของท่านทันที เครื่องจะรีสตาร์ทอัตโนมัติเมื่อครบเวลา"
            : "กรุณาบันทึกงานของท่านทันที เครื่องจะปิดอัตโนมัติเมื่อครบเวลา";

        // ต้องรอ Loaded ก่อนถึงจะรู้ ActualWidth/ActualHeight จริง (SizeToContent="Height"
        // ทำให้ความสูงยังไม่นิ่งจนกว่า layout pass แรกจะเสร็จ) - วางก่อนหน้านั้นจะได้ตำแหน่งเพี้ยน
        Loaded += (_, _) => MoveToBottomRight();
    }

    /// <summary>
    /// วางมุมล่างขวาของ "พื้นที่ทำงาน" (WorkArea) ไม่ใช่ขอบจอดิบ - WorkArea กันพื้นที่ taskbar
    /// ไว้ให้แล้ว ไม่ว่า user จะย้าย taskbar ไปอยู่ด้านไหนของจอก็ตาม
    ///
    /// SystemParameters.WorkArea เป็นของจอหลักจอเดียว - ตรงกับสมมติฐานเดิมของโปรเจกต์
    /// (ดู LockScreenWindow: "เครื่องมีจอเดียว ไม่ต้องรองรับ multi-monitor ในเวอร์ชันนี้")
    /// </summary>
    private void MoveToBottomRight()
    {
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right - ActualWidth - ScreenMargin;
        Top = workArea.Bottom - ActualHeight - ScreenMargin;
    }

    /// <summary>ปิดจริง - เรียกตอนนับครบ หรือตอน App ปิดตัว (OnExit)</summary>
    public void ForceClose()
    {
        _allowClose = true;
        Countdown.Stop();
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_allowClose)
        {
            // กันปิดทุกทางที่ไม่ได้มาจาก ForceClose() - user ต้องเห็นเวลาที่เหลือจนวินาทีสุดท้าย
            e.Cancel = true;
            return;
        }

        base.OnClosing(e);
    }
}
