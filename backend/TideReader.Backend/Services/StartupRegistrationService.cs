using Microsoft.Win32;

namespace TideReader.Backend.Services;

public interface IRegistryRunKey
{
    void DeleteValue(string name, bool throwOnMissingValue);
    void SetValue(string name, string value);
}

public interface IRegistryRunKeyFactory
{
    IRegistryRunKey? OpenCurrentUserRunKey();
}

internal sealed class RegistryRunKeyAdapter(RegistryKey key) : IRegistryRunKey
{
    public void DeleteValue(string name, bool throwOnMissingValue) => key.DeleteValue(name, throwOnMissingValue);
    public void SetValue(string name, string value) => key.SetValue(name, value);
}

public sealed class RegistryRunKeyFactory : IRegistryRunKeyFactory
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public IRegistryRunKey? OpenCurrentUserRunKey()
    {
        var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, true);
        return key is null ? null : new RegistryRunKeyAdapter(key);
    }
}

public sealed class StartupRegistrationService(IRegistryRunKeyFactory runKeyFactory) : IStartupRegistration
{
    private const string ValueName = "TideReader";

    public void Sync(bool enabled)
    {
        var key = runKeyFactory.OpenCurrentUserRunKey();
        if (key is null)
        {
            return;
        }

        if (!enabled)
        {
            key.DeleteValue(ValueName, false);
            return;
        }

        var exePath = Environment.ProcessPath ?? "";
        if (string.IsNullOrWhiteSpace(exePath))
        {
            return;
        }

        key.SetValue(ValueName, $"\"{exePath}\"");
    }
}
