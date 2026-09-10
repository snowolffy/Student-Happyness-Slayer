using System.IO;
using System.Reflection;
using System.Windows;

using OnionProcOparetor.Console.Services;

namespace OnionProcOparetor.Console;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();

        var assembly = Assembly.GetExecutingAssembly();
        VersionText.Text = $"Version {assembly.GetName().Version}";

        // ไม่มี CI pipeline ที่ stamp build date/commit ให้ตอนนี้ (ดู Directory.Build.props - Version
        // เดียวที่มีคือ AssemblyVersion/FileVersion) ใช้ last-write-time ของ .exe ที่รันอยู่จริงแทน เพราะ
        // เป็นข้อมูลจริงที่ได้มาฟรีโดยไม่ต้องเพิ่ม build step ใหม่
        var buildDate = File.Exists(assembly.Location) ? File.GetLastWriteTime(assembly.Location) : (DateTime?)null;
        BuildText.Text = buildDate is null ? "Build: unknown" : $"Build {buildDate:yyyy-MM-dd}";
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
