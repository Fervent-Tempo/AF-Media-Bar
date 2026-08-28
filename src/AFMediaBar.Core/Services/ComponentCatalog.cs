using AFMediaBar.Models;
using AFMediaBar.Components.Abstractions;
using AFMediaBar.Components.BuiltIn;

namespace AFMediaBar.Services;

/// <summary>
/// 描述内置组件的能力、可用窗口上下文和设置入口；目录不创建 WPF 控件，也不访问系统 API。
/// Describes built-in widget capabilities, supported contexts, and settings entry points without creating WPF controls or touching system APIs.
/// </summary>
public sealed record ComponentDefinition(
    string TypeId,
    string NameResourceKey,
    string DescriptionResourceKey,
    ComponentCategory Category,
    WidgetCapabilities Capabilities,
    bool SupportsTaskbar,
    bool SupportsFloating,
    bool SupportsHorizontal,
    bool SupportsVertical,
    bool SupportsCollapsedSlot);

public enum ComponentCategory
{
    Media = 0,
    Controls = 1,
    Audio = 2,
    System = 3,
    Layout = 4
}

/// <summary>
/// 内置组件注册表；稳定 TypeId 让布局文件可以跨版本迁移，未知组件由加载器禁用并回退。
/// Built-in widget registry; stable TypeIds keep layout files migratable, while unknown widgets are disabled or replaced during loading.
/// </summary>
public static class ComponentCatalog
{
    private static readonly IReadOnlyList<ComponentDefinition> Definitions = CreateDefinitions();

    public static IReadOnlyList<ComponentDefinition> All => Definitions;

    public static bool TryGet(string typeId, out ComponentDefinition definition)
    {
        definition = Definitions.FirstOrDefault(item =>
            string.Equals(item.TypeId, typeId, StringComparison.Ordinal))!;
        return definition is not null;
    }

    public static bool IsInteractive(LayoutWidgetElement widget) =>
        Layout.Widgets.LayoutComponentCatalog.IsInteractive(widget);

    public static WidgetSettings CreateDefaultSettings(string typeId) =>
        Layout.Widgets.LayoutComponentCatalog.CreateDefaultSettings(typeId);

    private static IReadOnlyList<ComponentDefinition> CreateDefinitions()
    {
        var registry = new BuiltInComponentRegistry();
        return registry.Items
            .Where(definition => definition.Kind == ComponentKind.Functional)
            .Select(definition => new ComponentDefinition(
                definition.Metadata.TypeId,
                definition.Metadata.NameResourceKey,
                definition.Metadata.DescriptionResourceKey,
                ToLegacyCategory(definition.Metadata.Category),
                (WidgetCapabilities)(int)definition.Metadata.Capabilities,
                definition.Metadata.SupportsTaskbar,
                definition.Metadata.SupportsFloating,
                definition.Metadata.SupportsHorizontal,
                definition.Metadata.SupportsVertical,
                definition.Metadata.SupportsCollapsedSlot))
            .ToArray();
    }

    private static ComponentCategory ToLegacyCategory(AFMediaBar.Components.Abstractions.ComponentCategory category) => category switch
    {
        AFMediaBar.Components.Abstractions.ComponentCategory.Media => ComponentCategory.Media,
        AFMediaBar.Components.Abstractions.ComponentCategory.Playback => ComponentCategory.Controls,
        AFMediaBar.Components.Abstractions.ComponentCategory.Audio => ComponentCategory.Audio,
        AFMediaBar.Components.Abstractions.ComponentCategory.System => ComponentCategory.System,
        _ => ComponentCategory.Layout
    };
}
