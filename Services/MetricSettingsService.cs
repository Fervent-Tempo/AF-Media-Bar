using Microsoft.Win32;
using TaskbarPlayer.Models;

namespace TaskbarPlayer.Services;

internal static class MetricSettingsService
{
    private const string SettingsKeyPath = @"Software\AFShell\MediaBar";
    private const string LegacySettingsKeyPath = @"Software\TaskbarPlayer";

    internal static MetricSettings Load()
    {
        try
        {
            using var currentKey = Registry.CurrentUser.OpenSubKey(SettingsKeyPath, writable: false);
            using var legacyKey = Registry.CurrentUser.OpenSubKey(
                LegacySettingsKeyPath,
                writable: false);
            if (currentKey is null && legacyKey is null)
            {
                return MetricSettings.Default;
            }

            var settings = new MetricSettings(
                ReadBoolean(
                    currentKey,
                    legacyKey,
                    "MetricsEnabled",
                    MetricSettings.Default.Enabled),
                ReadBoolean(
                    currentKey,
                    legacyKey,
                    "ShowSystemMemory",
                    MetricSettings.Default.ShowSystemMemory),
                ReadBoolean(
                    currentKey,
                    legacyKey,
                    "ShowSystemCpu",
                    MetricSettings.Default.ShowSystemCpu),
                ReadBoolean(
                    currentKey,
                    legacyKey,
                    "ShowSystemGpu",
                    MetricSettings.Default.ShowSystemGpu),
                ReadBoolean(
                    currentKey,
                    legacyKey,
                    "ShowProcessMemory",
                    MetricSettings.Default.ShowProcessMemory),
                ReadBoolean(
                    currentKey,
                    legacyKey,
                    "LowConfigMode",
                    ReadBoolean(
                        currentKey,
                        legacyKey,
                        "LowGpuMode",
                        MetricSettings.Default.LowGpuMode)),
                ReadBoolean(
                    currentKey,
                    legacyKey,
                    "AudioMonitorEnabled",
                    MetricSettings.Default.AudioMonitorEnabled));

            if (currentKey is null || HasMissingValues(currentKey))
            {
                try
                {
                    Save(settings);
                }
                catch
                {
                    // Loading legacy settings should still succeed if migration cannot write.
                }
            }

            return settings;
        }
        catch
        {
            return MetricSettings.Default;
        }
    }

    internal static void Save(MetricSettings settings)
    {
        using var key = Registry.CurrentUser.CreateSubKey(SettingsKeyPath, writable: true);
        key.SetValue("MetricsEnabled", settings.Enabled ? 1 : 0, RegistryValueKind.DWord);
        key.SetValue("ShowSystemMemory", settings.ShowSystemMemory ? 1 : 0, RegistryValueKind.DWord);
        key.SetValue("ShowSystemCpu", settings.ShowSystemCpu ? 1 : 0, RegistryValueKind.DWord);
        key.SetValue("ShowSystemGpu", settings.ShowSystemGpu ? 1 : 0, RegistryValueKind.DWord);
        key.SetValue("ShowProcessMemory", settings.ShowProcessMemory ? 1 : 0, RegistryValueKind.DWord);
        key.SetValue("LowConfigMode", settings.LowGpuMode ? 1 : 0, RegistryValueKind.DWord);
        key.SetValue("AudioMonitorEnabled", settings.AudioMonitorEnabled ? 1 : 0, RegistryValueKind.DWord);
    }

    private static bool ReadBoolean(
        RegistryKey? currentKey,
        RegistryKey? legacyKey,
        string name,
        bool defaultValue)
    {
        var value = currentKey?.GetValue(name) ?? legacyKey?.GetValue(name);
        return value is int integer ? integer != 0 : defaultValue;
    }

    private static bool HasMissingValues(RegistryKey key)
    {
        return key.GetValue("MetricsEnabled") is not int ||
            key.GetValue("ShowSystemMemory") is not int ||
            key.GetValue("ShowSystemCpu") is not int ||
            key.GetValue("ShowSystemGpu") is not int ||
            key.GetValue("ShowProcessMemory") is not int ||
            key.GetValue("LowConfigMode") is not int ||
            key.GetValue("AudioMonitorEnabled") is not int;
    }
}
