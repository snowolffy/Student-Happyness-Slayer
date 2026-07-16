using Microsoft.Win32;

namespace MyLabGuard.Client.Services;

public static class ClientIdentity
{
    private const string RegistryPath = @"SOFTWARE\MyLabGuard";
    private const string GuidValueName = "ClientGuid";

    public static string GetOrCreateClientGuid()
    {
        using var key = Registry.LocalMachine.CreateSubKey(RegistryPath, writable: true);

        var existing = key?.GetValue(GuidValueName) as string;
        if (!string.IsNullOrWhiteSpace(existing) && Guid.TryParse(existing, out _))
        {
            return existing;
        }

        var newGuid = Guid.NewGuid().ToString();
        key?.SetValue(GuidValueName, newGuid, RegistryValueKind.String);
        return newGuid;
    }

    public static string GetMachineName() => Environment.MachineName;
}