using AFMediaBar.Models;

namespace AFMediaBar.Services;

/// <summary>
/// 描述内置组件的能力、可用窗口上下文和设置入口；目录不创建 WPF 控件，也不访问系统 API。
/// Describes built-in widget capabilities, supported contexts, and settings entry points without creating WPF controls or touching system APIs.
/// </summary>
internal sealed record ComponentDefinition(
    string TypeId,
    string NameResourceKey,
    string DescriptionResourceKey,
    WidgetCapabilities Capabilities,
    bool SupportsTaskbar,
    bool SupportsFloating,
    bool SupportsHorizontal,
    bool SupportsVertical,
    bool SupportsCollapsedSlot);

/// <summary>
/// 内置组件注册表；稳定 TypeId 让布局文件可以跨版本迁移，未知组件由加载器禁用并回退。
/// Built-in widget registry; stable TypeIds keep layout files migratable, while unknown widgets are disabled or replaced during loading.
/// </summary>
internal static class ComponentCatalog
{
    private static readonly IReadOnlyList<ComponentDefinition> Definitions =
    [
        new(
            BuiltInWidgetTypeIds.Artwork,
            "Settings.LayoutWidget.ArtworkTitle",
            "Settings.LayoutWidget.ArtworkDescription",
            WidgetCapabilities.Display | WidgetCapabilities.Invoke,
            true,
            true,
            true,
            true,
            true),
        new(
            BuiltInWidgetTypeIds.MediaText,
            "Settings.LayoutWidget.MediaTextTitle",
            "Settings.LayoutWidget.MediaTextDescription",
            WidgetCapabilities.Display,
            true,
            true,
            true,
            true,
            true),
        new(
            BuiltInWidgetTypeIds.MediaSource,
            "Settings.LayoutWidget.MediaSourceTitle",
            "Settings.LayoutWidget.MediaSourceDescription",
            WidgetCapabilities.Display | WidgetCapabilities.Invoke,
            true,
            true,
            true,
            true,
            true),
        new(
            BuiltInWidgetTypeIds.Command,
            "Settings.LayoutWidget.CommandTitle",
            "Settings.LayoutWidget.CommandDescription",
            WidgetCapabilities.Invoke,
            true,
            true,
            true,
            true,
            false),
        new(
            BuiltInWidgetTypeIds.Metrics,
            "Settings.LayoutWidget.MetricsTitle",
            "Settings.LayoutWidget.MetricsDescription",
            WidgetCapabilities.Display | WidgetCapabilities.Invoke,
            true,
            true,
            true,
            true,
            true),
        new(
            BuiltInWidgetTypeIds.Spectrum,
            "Settings.LayoutWidget.SpectrumTitle",
            "Settings.LayoutWidget.SpectrumDescription",
            WidgetCapabilities.Display,
            true,
            true,
            true,
            true,
            true),
        new(
            BuiltInWidgetTypeIds.Separator,
            "Settings.LayoutWidget.SeparatorTitle",
            "Settings.LayoutWidget.SeparatorDescription",
            WidgetCapabilities.Display,
            true,
            true,
            true,
            true,
            true)
    ];

    internal static IReadOnlyList<ComponentDefinition> All => Definitions;

    internal static bool TryGet(string typeId, out ComponentDefinition definition)
    {
        definition = Definitions.FirstOrDefault(item =>
            string.Equals(item.TypeId, typeId, StringComparison.Ordinal))!;
        return definition is not null;
    }

    internal static WidgetSettings CreateDefaultSettings(string typeId) => typeId switch
    {
        BuiltInWidgetTypeIds.Artwork => new ArtworkWidgetSettings(6, false),
        BuiltInWidgetTypeIds.MediaText => new MediaTextWidgetSettings(
            MediaTextKind.Title,
            true,
            14,
            1),
        BuiltInWidgetTypeIds.MediaSource => new MediaTextWidgetSettings(
            MediaTextKind.Source,
            false,
            11,
            1),
        BuiltInWidgetTypeIds.Command => new CommandWidgetSettings(
            MediaCommandKind.PlayPause,
            36),
        BuiltInWidgetTypeIds.Metrics => new MetricsWidgetSettings(
            MetricKind.SystemMemory,
            false,
            2500,
            [MetricKind.SystemMemory]),
        BuiltInWidgetTypeIds.Spectrum => new SpectrumWidgetSettings(
            9,
            20,
            100),
        BuiltInWidgetTypeIds.Separator => new SeparatorWidgetSettings(1, 22),
        _ => new MediaTextWidgetSettings(MediaTextKind.Title, false, 14, 1)
    };
}
