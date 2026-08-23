using Microsoft.Win32;
using AFMediaBar.Models;

namespace AFMediaBar.Services;

/// <summary>
/// 读取仍有效的低配置选项，并仅在首次布局迁移时消费旧组件注册表值。
/// Loads the remaining low-spec option and consumes legacy component registry values only during first-run layout migration.
/// </summary>
public static class MetricSettingsService
{
    private const string SettingsKeyPath = @"Software\AFMediaBar";
    private static readonly string[] LegacyValueNames =
    [
        "MetricsEnabled",
        "ShowSystemMemory",
        "ShowSystemCpu",
        "ShowSystemGpu",
        "ShowProcessMemory",
        "AudioMonitorEnabled",
        "OutputDeviceSwitcherEnabled",
        "VolumeControlEnabled",
        "OpenTaskManagerOnMetricsClick",
        "LowGpuMode"
    ];

    public static MetricSettings Load()
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

            try
            {
                // 旧组件配置只为首次布局迁移保留在内存中；读取后立即清理，避免新版本继续依赖注册表。
                // Legacy component settings stay in memory only for the first layout migration, then are removed so the new version no longer depends on the registry.
                Save(settings);
            }
            catch (Exception exception)
            {
                DiagnosticsLogService.Write("metric-settings-migration", exception);
                // 迁移写入失败时仍使用已读取的旧设置，避免阻断启动。
                // Keep the loaded legacy settings when migration cannot write, so startup continues.
            }

            return settings;
        }
        catch (Exception exception)
        {
            DiagnosticsLogService.Write("metric-settings-read", exception);
            return MetricSettings.Default;
        }
    }

    public static void Save(MetricSettings settings)
    {
        using var key = Registry.CurrentUser.CreateSubKey(SettingsKeyPath, writable: true);
        key.SetValue("LowConfigMode", settings.LowGpuMode ? 1 : 0, RegistryValueKind.DWord);
        foreach (var legacyName in LegacyValueNames)
        {
            key.DeleteValue(legacyName, throwOnMissingValue: false);
        }
    }

    private static bool ReadBoolean(
        RegistryKey? currentKey,
        string name,
        bool defaultValue)
    {
        var value = currentKey?.GetValue(name);
        return value switch
        {
            int integer => integer != 0,
            long integer => integer != 0,
            _ => defaultValue
        };
    }
}
