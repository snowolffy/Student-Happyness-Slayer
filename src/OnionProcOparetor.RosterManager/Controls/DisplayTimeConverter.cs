using System.Globalization;
using System.Windows.Data;

namespace OnionProcOparetor.RosterManager.Controls;

/// <summary>
/// Converter เวลาแบบง่าย - Roster Manager ไม่มีตัวเลือก "เวลาไทย/UTC" แบบ Console (WP6) นี่แค่ทำให้
/// resource key "DisplayTimeConverter" ที่ Theme/Controls.xaml ต้องการมีอยู่จริง (คัดลอก Controls.xaml
/// มาทั้งไฟล์จาก Console โดยไม่แก้ - ดู comment หัวไฟล์นั้น) แปลงเป็นเวลาท้องถิ่นเครื่องนี้ธรรมดา
/// </summary>
public sealed class DisplayTimeConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not DateTime time)
        {
            return "-";
        }

        var local = time.Kind == DateTimeKind.Utc || time.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(time, DateTimeKind.Utc).ToLocalTime()
            : time;

        return parameter is string template
            ? string.Format(CultureInfo.InvariantCulture, template, local.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture))
            : local.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
