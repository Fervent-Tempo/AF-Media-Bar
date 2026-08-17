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
    private const string AFShellSettingsKeyPath = @"Software\AFShell\MediaBar";
    private const string TaskbarPlayerSettingsKeyPath = @"Software\TaskbarPlayer";

    internal static MetricSettings Load()
    {
        try
        {
            using var currentKey = Registry.CurrentUser.OpenSubKey(SettingsKeyPath, writable: false);
            using var afShellKey = Registry.CurrentUser.OpenSubKey(
                AFShellSettingsKeyPath,
                writable: false);
            using var taskbarPlayerKey = Registry.CurrentUser.OpenSubKey(
                TaskbarPlayerSettingsKeyPath,
                writable: false);
            if (currentKey is null && afShellKey is null && taskbarPlayerKey is null)
            {
                return MetricSettings.Default;
            }

            var settings = new MetricSettings(
                ReadBoolean(
                    currentKey,
                    afShellKey,
                    taskbarPlayerKey,
                    "MetricsEnabled",
                    MetricSettings.Default.Enabled),
                ReadBoolean(
                    currentKey,
                    afShellKey,
                    taskbarPlayerKey,
                    "ShowSystemMemory",
                    MetricSettings.Default.ShowSystemMemory),
                ReadBoolean(
                    currentKey,
                    afShellKey,
                    taskbarPlayerKey,
                    "ShowSystemCpu",
                    MetricSettings.Default.ShowSystemCpu),
                ReadBoolean(
                    currentKey,
                    afShellKey,
                    taskbarPlayerKey,
                    "ShowSystemGpu",
                    MetricSettings.Default.ShowSystemGpu),
                ReadBoolean(
                    currentKey,
                    afShellKey,
                    taskbarPlayerKey,
                    "ShowProcessMemory",
                    MetricSettings.Default.ShowProcessMemory),
                ReadBoolean(
                    currentKey,
                    afShellKey,
                    taskbarPlayerKey,
                    "LowConfigMode",
                    ReadBoolean(
                        currentKey,
                        afShellKey,
                        taskbarPlayerKey,
                        "LowGpuMode",
                        MetricSettings.Default.LowGpuMode)),
                ReadBoolean(
                    currentKey,
                    afShellKey,
                    taskbarPlayerKey,
                    "AudioMonitorEnabled",
                    MetricSettings.Default.AudioMonitorEnabled),
                ReadBoolean(
                    currentKey,
                    afShellKey,
                    taskbarPlayerKey,
                    "OutputDeviceSwitcherEnabled",
                    MetricSettings.Default.OutputDeviceSwitcherEnabled),
                ReadBoolean(
                    currentKey,
                    afShellKey,
                    taskbarPlayerKey,
                    "VolumeControlEnabled",
                    MetricSettings.Default.VolumeControlEnabled),
                ReadBoolean(
                    currentKey,
                    afShellKey,
                    taskbarPlayerKey,
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
        RegistryKey? afShellKey,
        RegistryKey? taskbarPlayerKey,
        string name,
        bool defaultValue)
    {
        var value = currentKey?.GetValue(name) ??
            afShellKey?.GetValue(name) ??
            taskbarPlayerKey?.GetValue(name);
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
