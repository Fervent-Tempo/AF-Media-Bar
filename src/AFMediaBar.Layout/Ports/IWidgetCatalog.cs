using AFMediaBar.Layout.Models;

namespace AFMediaBar.Layout.Ports;

public interface IWidgetCatalog
{
    IReadOnlyList<WidgetDescriptor> Items { get; }

    bool TryGet(string typeId, out WidgetDescriptor descriptor);
}

public sealed record WidgetDescriptor(
    string TypeId,
    LayoutComponentCategory Category,
    WidgetCapabilities Capabilities,
    LayoutGridRect DefaultBounds,
    LayoutGridRect MinimumBounds,
    bool SupportsCollapsedSlot);

public enum LayoutComponentCategory
{
    Media = 0,
    Controls = 1,
    Audio = 2,
    System = 3,
    Layout = 4
}
