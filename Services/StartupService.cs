using Microsoft.Win32;
using System.Diagnostics;

namespace ClipDesk.Services;

public sealed class StartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private static string ValueName => AppEnvironment.Identity;

    public bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return key?.GetValue(ValueName) is string value && !string.IsNullOrWhiteSpace(value);
    }

    public void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
            ?? Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
        if (enabled)
        {
            var executable = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName
                ?? throw new InvalidOperationException("Não foi possível localizar o ClipDesk.");
            key.SetValue(ValueName, $"\"{executable}\" --background{(AppEnvironment.IsTestClient ? $" --test-client={AppEnvironment.TestClientNumber}" : "")}", RegistryValueKind.String);
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
