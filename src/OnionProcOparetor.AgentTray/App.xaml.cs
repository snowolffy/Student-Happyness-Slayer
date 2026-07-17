using System.Windows;

namespace OnionProcOparetor.AgentTray;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        TrayText = "Onion ProcOparetor - กำลังทำงาน";
    }

    public string TrayText { get; private set; } = "Onion ProcOparetor - กำลังทำงาน";
}
