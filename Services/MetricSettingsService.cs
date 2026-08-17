using Microsoft.Win32;
using AFMediaBar.Models;

namespace AFMediaBar.Services;

/// <summary>
/// 从注册表读取、迁移并保存性能指标与可选控件设置。
/// Reads, migrates, and saves performance metric and optional control settings in the registry.
/// </summary>
internal static class MetricSettingsService
{
    private const string SettingsKeyPath = @"Software\AFMediaBar";

    internal static MetricSettings Load()
    {
        try
        {
            using var currentKey = Registry.CurrentUser.OpenSubKey(SettingsKeyPath, writable: false);
            if (currentKey is null)
            {
                return MetricSettings.Default;
            }

            var settings = new MetricSettings(
                ReadBoolean(
                    currentKey,
                    "MetricsEnabled",
                    MetricSettings.Default.Enabled),
                ReadBoolean(
                    currentKey,
                    "ShowSystemMemory",
                    MetricSettings.Default.ShowSystemMemory),
                ReadBoolean(
                    currentKey,
                    "ShowSystemCpu",
                    MetricSettings.Default.ShowSystemCpu),
                ReadBoolean(
                    currentKey,
                    "ShowSystemGpu",
                    MetricSettings.Default.ShowSystemGpu),
                ReadBoolean(
                    currentKey,
                    "ShowProcessMemory",
                    MetricSettings.Default.ShowProcessMemory),
                ReadBoolean(
                    currentKey,
                    "LowConfigMode",
                    ReadBoolean(
                        currentKey,
                        "LowGpuMode",
                        MetricSettings.Default.LowGpuMode)),
                ReadBoolean(
                    currentKey,
                    "AudioMonitorEnabled",
                    MetricSettings.Default.AudioMonitorEnabled),
                ReadBoolean(
                    currentKey,
                    "OutputDeviceSwitcherEnabled",
                    MetricSettings.Default.OutputDeviceSwitcherEnabled),
                ReadBoolean(
                    currentKey,
                    "VolumeControlEnabled",
                    MetricSettings.Default.VolumeControlEnabled),
                ReadBoolean(
                    currentKey,
                    "OpenTaskManagerOnMetricsClick",
                    MetricSettings.Default.OpenTaskManagerOnMetricsClick));

            if (currentKey is null || HasMissingValues(currentKey))
            {
                try
                {
                    Save(settings);
                }
                catch (Exception exception)
                {
                    DiagnosticsLogService.Write("metric-settings-migration", exception);
                    // 迁移写入失败时仍使用已读取的旧设置，避免阻断启动。
                    // Keep the loaded legacy settings when migration cannot write, so startup continues.
                }
            }

            return settings;
        }
        catch (Exception exception)
        {
            DiagnosticsLogService.Write("metric-settings-read", exception);
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
        key.SetValue(
            "OutputDeviceSwitcherEnabled",
            settings.OutputDeviceSwitcherEnabled ? 1 : 0,
            RegistryValueKind.DWord);
        key.SetValue(
            "VolumeControlEnabled",
            settings.VolumeControlEnabled ? 1 : 0,
            RegistryValueKind.DWord);
        key.SetValue(
            "OpenTaskManagerOnMetricsClick",
            settings.OpenTaskManagerOnMetricsClick ? 1 : 0,
            RegistryValueKind.DWord);
    }

    private static bool ReadBoolean(
        RegistryKey? currentKey,
        string name,
        bool defaultValue)
    {
        var value = currentKey?.GetValue(name);
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
            key.GetValue("AudioMonitorEnabled") is not int ||
            key.GetValue("OutputDeviceSwitcherEnabled") is not int ||
            key.GetValue("VolumeControlEnabled") is not int ||
            key.GetValue("OpenTaskManagerOnMetricsClick") is not int;
    }
}
