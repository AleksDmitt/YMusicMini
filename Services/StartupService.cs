using System.Diagnostics;
using Microsoft.Win32;

namespace YMusicMini.Services;

/// <summary>Optional per-user autostart. Never requires admin permissions.</summary>
public static class StartupService
{
    private const string RegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "YMusicMini";

    public static bool Configure(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RegistryPath);
            if (key is null) return false;
            if (!enable)
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
                return true;
            }

            var path = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(path) ||
                !path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return false;
            // While debugging this path points to bin/Debug. Enable again after publishing.
            key.SetValue(ValueName, $"\"{path}\"", RegistryValueKind.String);
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Autostart: {ex}");
            return false;
        }
    }
}
