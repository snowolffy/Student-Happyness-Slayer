using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using OnionProcOparetor.Console.Services;

namespace OnionProcOparetor.Console.Controls;

/// <summary>
/// Converter สำหรับผูกคอลัมน์เวลาเข้ากับ <see cref="TimeDisplay"/> (WP6)
///
/// ใช้: <c>Binding="{Binding LastSeenAt, Converter={StaticResource DisplayTimeConverter}}"</c>
/// การเรียงตามคอลัมน์ยังถูกต้อง เพราะ DataGrid เรียงด้วย SortMemberPath (= path ของ DateTime เดิม)
/// ไม่ได้เรียงด้วยข้อความที่ converter คืน
///
/// ConverterParameter (ถ้ามี) เป็นแม่แบบแบบ string.Format เช่น "เห็นล่าสุด: {0}" - จะได้เวลาพร้อม
/// ป้ายเขตเวลา ใช้กับ ToolTip ซึ่งไม่มีหัวคอลัมน์กำกับ (และ StringFormat ใช้กับ ToolTip ไม่ได้
/// เพราะ ToolTip เป็นชนิด object ไม่ใช่ string)
/// </summary>
public sealed class DisplayTimeConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not DateTime time)
        {
            return "-";
        }

        return parameter is string template
            ? string.Format(CultureInfo.InvariantCulture, template, TimeDisplay.TextWithZone(time))
            : TimeDisplay.Text(time);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// หัวคอลัมน์เวลาที่เปลี่ยนตามโหมดเวลาเอง (WP6) - ตั้ง <c>controls:TimeColumn.Header="เห็นล่าสุด"</c>
/// บน DataGridColumn แทน <c>Header</c> แล้วหัวจะเป็น "เห็นล่าสุด" (เวลาไทย) หรือ
/// "เห็นล่าสุด (UTC)" ตามที่ตั้งค่าไว้ และเปลี่ยนทันทีเมื่อสลับในหน้าตั้งค่า
///
/// ต้องเป็น attached property + ทะเบียนคอลัมน์ เพราะ DataGridColumn ไม่ได้อยู่ใน visual/logical tree
/// - DynamicResource หรือ Binding ไปหา element อื่นจากหัวคอลัมน์ทำไม่ได้ ทะเบียนเก็บแบบ
/// WeakReference ไม่ให้คอลัมน์ของหน้าต่างที่ปิดไปแล้วถูกตรึงไว้ในหน่วยความจำ
///
/// ส่วน "ค่าในเซลล์" ไม่ได้ refresh จากที่นี่ (คอลัมน์ไม่รู้ว่าตัวเองอยู่ DataGrid ไหน) - เจ้าของตาราง
/// ฟัง <see cref="TimeDisplay.ModeChanged"/> แล้วสั่ง Items.Refresh() เอง (ดู DashboardWindow)
/// </summary>
public static class TimeColumn
{
    private static readonly List<WeakReference<DataGridColumn>> Columns = new();

    static TimeColumn()
    {
        TimeDisplay.ModeChanged += () =>
        {
            Columns.RemoveAll(w => !w.TryGetTarget(out _));
            foreach (var weak in Columns)
            {
                if (weak.TryGetTarget(out var column))
                {
                    column.Header = TimeDisplay.Header(GetHeader(column));
                }
            }
        };
    }

    public static readonly DependencyProperty HeaderProperty = DependencyProperty.RegisterAttached(
        "Header", typeof(string), typeof(TimeColumn), new PropertyMetadata(string.Empty, OnHeaderChanged));

    public static string GetHeader(DependencyObject d) => (string)d.GetValue(HeaderProperty);
    public static void SetHeader(DependencyObject d, string value) => d.SetValue(HeaderProperty, value);

    private static void OnHeaderChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not DataGridColumn column)
        {
            return;
        }

        column.Header = TimeDisplay.Header((string)e.NewValue);

        if (!Columns.Any(w => w.TryGetTarget(out var c) && ReferenceEquals(c, column)))
        {
            Columns.Add(new WeakReference<DataGridColumn>(column));
        }
    }
}
