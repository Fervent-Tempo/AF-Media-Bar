using Microsoft.Win32;
using TaskbarPlayer.Models;

namespace TaskbarPlayer.Services;

internal static class MetricSettingsService
{
    private const string SettingsKeyPath = @"Software\TaskbarPlayer";

    internal static MetricSettings Load()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(SettingsKeyPath, writable: false);
            if (key is null)
            {
                return MetricSettings.Default;
            }

            return new MetricSettings(
                ReadBoolean(key, "ShowSystemMemory", MetricSettings.Default.ShowSystemMemory),
                ReadBoolean(key, "ShowSystemCpu", MetricSettings.Default.ShowSystemCpu),
                ReadBoolean(key, "ShowProcessMemory", MetricSettings.Default.ShowProcessMemory));
        }
        catch
        {
            return MetricSettings.Default;
        }
    }

    internal static void Save(MetricSettings settings)
    {
        using var key = Registry.CurrentUser.CreateSubKey(SettingsKeyPath, writable: true);
        key.SetValue("ShowSystemMemory", settings.ShowSystemMemory ? 1 : 0, RegistryValueKind.DWord);
        key.SetValue("ShowSystemCpu", settings.ShowSystemCpu ? 1 : 0, RegistryValueKind.DWord);
        key.SetValue("ShowProcessMemory", settings.ShowProcessMemory ? 1 : 0, RegistryValueKind.DWord);
    }

    private static bool ReadBoolean(RegistryKey key, string name, bool defaultValue)
    {
        return key.GetValue(name) is int value ? value != 0 : defaultValue;
    }
}
