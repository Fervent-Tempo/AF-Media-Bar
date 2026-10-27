// 动态列表按稳定组件身份生成搜索目标，不枚举用户设备或启动程序。
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Models.Settings;
using AFMediaBar.Classes.Settings;
using AFMediaBar.Resources;

namespace AFMediaBar.Classes.Services.Settings;

/// <summary>三状态列表与指标样式的具体设置目标。</summary>
public static class SettingsDynamicSearchCatalog
{
    public static IEnumerable<SettingsSearchEntry> Entries(SettingsContext context)
    {
        if (SettingsPageCatalog.CanEditProfile(context.Mode))
            foreach (var state in Enum.GetValues<BarLayoutState>())
                foreach (var entry in ComponentLayoutPolicy.Resolve(SettingsManager.Current, state))
                {
                    var page = state == BarLayoutState.Hover ? SettingsPageKey.Components : SettingsPageKey.RestState;
                    var group = state == BarLayoutState.Idle ? "Settings.RestState.Idle" : state == BarLayoutState.Hover ? "Common.HoverLayer" : "Common.RestLayer";
                    yield return Create(page, group, ComponentName(entry.Component, state), $"component:{state}:{entry.Component}",
                        state == BarLayoutState.Idle ? "ComponentLayout.Idle" : state == BarLayoutState.Hover ? "ComponentLayout.Hover" : "ComponentLayout.Resident");
                }
        foreach (var metric in Enum.GetValues<MetricKind>())
        {
            var name = ComponentName(ComponentLayoutPolicy.Component(metric), BarLayoutState.Resident);
            yield return Create(SettingsPageKey.Performance, "Widgets.Performance", name, $"metric:bar:{metric}", "Integration.Performance.Bar");
            yield return Create(SettingsPageKey.Performance, "Performance.Panel", name, $"metric:panel:{metric}", "Integration.Performance.Panel");
        }
    }

    private static SettingsSearchEntry Create(SettingsPageKey page, string group, string name, string id, string descriptionKey) =>
        new(page, group, SettingsSearchIndex.GetPageTitle(page), name, Translations.Get(descriptionKey), [name, id]) { OptionId = id };

    private static string ComponentName(TaskbarRestComponent component, BarLayoutState state) => component switch
    {
        TaskbarRestComponent.SystemCpu => "CPU",
        TaskbarRestComponent.SystemGpu => "GPU",
        _ => Translations.Get(component switch
        {
            TaskbarRestComponent.Artwork => state == BarLayoutState.Idle ? "DisplayModes.Rest.Component.QuickLaunchNote" : "DisplayModes.Rest.Component.Artwork",
            TaskbarRestComponent.MediaText => "DisplayModes.Rest.Component.MediaText",
            TaskbarRestComponent.Spectrum => "DisplayModes.Rest.Component.Spectrum",
            TaskbarRestComponent.Performance => "DisplayModes.Rest.Component.Performance",
            TaskbarRestComponent.OutputDevice => "DisplayModes.Rest.Component.OutputDevice",
            TaskbarRestComponent.InputDevice => "Common.InputDevice",
            TaskbarRestComponent.Volume => "DisplayModes.Rest.Component.Volume",
            TaskbarRestComponent.BluetoothBattery => "Battery.Title",
            TaskbarRestComponent.SystemMemory => "Media.Metric.SystemMemory.Title",
            TaskbarRestComponent.ProcessMemory => "Media.Metric.ProcessMemory.Title",
            TaskbarRestComponent.SystemNetwork => "Media.Metric.SystemNetwork.Title",
            TaskbarRestComponent.PreviousTrack => "Common.PreviousTrack",
            TaskbarRestComponent.PlayPause => "Common.PlayPause",
            TaskbarRestComponent.NextTrack => "Common.NextTrack",
            _ => "Common.Page.RestState"
        })
    };
}
