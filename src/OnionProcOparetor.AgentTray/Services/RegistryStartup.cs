using Microsoft.Win32;

namespace OnionProcOparetor.AgentTray.Services;

public static class RegistryStartup
{
    private const string RunKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "OnionProcOparetorAgentTray";

    public static void EnsureAutoStartEnabled()
    {
        try
        {
            var exePath = Environment.ProcessPath;

            if (string.IsNullOrWhiteSpace(exePath))
            {
                return;
            }

            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                ?? Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);

            if (key is null)
            {
                return;
            }

            var existingValue = key.GetValue(ValueName) as string;

            if (!string.Equals(existingValue, exePath, StringComparison.OrdinalIgnoreCase))
            {
                key.SetValue(ValueName, exePath, RegistryValueKind.String);
            }
        }
        catch
        {
        }
    }

    public static void DisableAutoStart()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            key?.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        catch
        {
        }
    }
}
