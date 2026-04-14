using Microsoft.Win32;

namespace ClipHive;

/// <summary>
/// Manages the Windows startup registry entry for ClipHive.
/// Reads/writes HKCU\Software\Microsoft\Windows\CurrentVersion\Run.
/// </summary>
public static class StartupHelper
{
    private const string RegistryKeyPath =
        @"Software\Microsoft\Windows\CurrentVersion\Run";

    private const string AppName = "ClipHive";

    /// <summary>
    /// Returns true if ClipHive is configured to start with Windows.
    /// </summary>
    public static bool IsStartupEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, writable: false);
        return key?.GetValue(AppName) is not null;
    }

    /// <summary>
    /// Adds or removes the ClipHive startup registry entry based on <paramref name="enable"/>.
    /// Uses the current process executable path when enabling.
    /// </summary>
    public static void SetStartup(bool enable)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, writable: true)
            ?? throw new InvalidOperationException(
                $"Cannot open registry key: {RegistryKeyPath}");

        if (enable)
        {
            string? exePath = Environment.ProcessPath
                ?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;

            if (string.IsNullOrEmpty(exePath))
                throw new InvalidOperationException("Unable to determine the executable path.");

            key.SetValue(AppName, $"\"{exePath}\"");
        }
        else
        {
            key.DeleteValue(AppName, throwOnMissingValue: false);
        }
    }
}
