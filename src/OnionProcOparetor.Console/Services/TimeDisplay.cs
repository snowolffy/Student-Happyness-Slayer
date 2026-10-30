using System.Globalization;

namespace OnionProcOparetor.Console.Services;

/// <summary>
/// แปลงเวลาจาก Server (UTC) เป็นข้อความที่ครูอ่าน (WP6) - ค่าเริ่มต้นคือเวลาไทย (UTC+7)
/// เปลี่ยนกลับเป็น UTC ได้จากหน้าตั้งค่า (<c>UiSettings.ShowTimesInUtc</c>)
///
/// ทำไมต้องรวมไว้ที่เดียว: เดิมทุกตารางผูก DateTime ตรง ๆ แล้วเขียนหัวคอลัมน์ว่า "(UTC)" ครูต้องบวก
/// 7 ชั่วโมงในหัวเองทุกครั้งที่ไล่ log - ถ้าแต่ละตารางแปลงเอง วันหนึ่งจะมีตารางที่ลืมแปลง แล้วสองตาราง
/// ในหน้าเดียวกันจะบอกเวลาของเหตุการณ์เดียวกันต่างกัน 7 ชั่วโมง
///
/// ใช้ InvariantCulture เสมอ: ถ้าเครื่องครูตั้ง culture เป็น th-TH ตัว format จะได้ปฏิทินพุทธ
/// (ปี 2569) ปนกับตารางอื่นที่เป็น ค.ศ. - รูปแบบ yyyy-MM-dd HH:mm:ss คงเดิมตามที่ตารางใช้มาตลอด
/// </summary>
public static class TimeDisplay
{
    public const string Format = "yyyy-MM-dd HH:mm:ss";

    /// <summary>
    /// เขตเวลาไทย - ประเทศไทยไม่มี daylight saving จึงใช้ offset คงที่ +7 ได้ตรงเสมอ แต่ยังลองหา
    /// time zone ของ Windows ก่อน เผื่อวันหนึ่งกฎเปลี่ยน (ไม่เจอ = ใช้ +7 คงที่)
    /// </summary>
    private static readonly TimeZoneInfo ThaiZone = FindThaiZone();

    private static TimeZoneInfo FindThaiZone()
    {
        foreach (var id in new[] { "SE Asia Standard Time", "Asia/Bangkok" })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (Exception)
            {
                // ลองชื่อถัดไป
            }
        }

        return TimeZoneInfo.CreateCustomTimeZone("ICT", TimeSpan.FromHours(7), "เวลาไทย", "เวลาไทย");
    }

    /// <summary>true = แสดงเวลา UTC ตามแบบเดิม · false (ค่าเริ่มต้น) = แสดงเวลาไทย</summary>
    public static bool UseUtc => SettingsService.Current.Ui.ShowTimesInUtc;

    /// <summary>
    /// ยิงเมื่อผู้ใช้สลับโหมดเวลาในหน้าตั้งค่า - หัวคอลัมน์ (<c>Controls.TimeColumn</c>) และตารางที่
    /// เปิดอยู่ต้องวาดใหม่ทันที ไม่ใช่รอรอบ refresh ถัดไป
    /// </summary>
    public static event Action? ModeChanged;

    private static bool _lastUseUtc = UseUtc;

    static TimeDisplay()
    {
        SettingsService.Changed += settings =>
        {
            if (settings.Ui.ShowTimesInUtc == _lastUseUtc)
            {
                return;
            }

            _lastUseUtc = settings.Ui.ShowTimesInUtc;
            ModeChanged?.Invoke();
        };
    }

    /// <summary>
    /// ค่าจาก Server ถือว่าเป็น UTC เสมอ - SQLite/EF ส่ง DateTime ออกมาแบบ Kind=Unspecified (JSON
    /// ไม่มี "Z") จึงต้องระบุเองว่าเป็น UTC ส่วนค่าที่มี offset มา System.Text.Json จะแปลงเป็น Local
    /// ให้แล้ว ก็แปลงกลับเป็น UTC ก่อน
    /// </summary>
    public static DateTime ToDisplay(DateTime value)
    {
        var utc = value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };

        return UseUtc ? utc : TimeZoneInfo.ConvertTimeFromUtc(utc, ThaiZone);
    }

    /// <summary>ข้อความเวลาสำหรับตาราง/แผงข้อมูล - ไม่มีป้ายเขตเวลา (ป้ายอยู่ที่หัวคอลัมน์แทน)</summary>
    public static string Text(DateTime value) => ToDisplay(value).ToString(Format, CultureInfo.InvariantCulture);

    public static string Text(DateTime? value) => value is { } v ? Text(v) : "-";

    /// <summary>ข้อความเวลาพร้อมป้ายเขตเวลา (ใช้ใน ToolTip ที่ไม่มีหัวคอลัมน์กำกับ)</summary>
    public static string TextWithZone(DateTime value) => Text(value) + (UseUtc ? " UTC" : " (เวลาไทย)");

    /// <summary>
    /// หัวคอลัมน์ - ตอนแสดงเวลาไทย **ต้องไม่มี "(UTC)"** (ป้ายผิดแย่กว่าไม่มีป้าย) และไม่ต้องเขียน
    /// "(เวลาไทย)" ด้วยเพราะเป็นค่าที่ครูคาดอยู่แล้ว ป้ายจึงโผล่เฉพาะตอนเลือกดูเป็น UTC
    /// </summary>
    public static string Header(string baseHeader) => UseUtc ? $"{baseHeader} (UTC)" : baseHeader;
}
