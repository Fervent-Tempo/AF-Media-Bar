using AFMediaBar.Layout.Models;

namespace AFMediaBar.Layout.Runtime;

/// <summary>
/// UI- and platform-independent queries over a schema-5 layout profile.
/// Keeps component discovery separate from Core metric/runtime coordination.
/// </summary>
public static class LayoutProfileQueryService
{
    public static bool ContainsWidget(LayoutProfile profile, string typeId) =>
        EnumerateWidgets(profile).Any(widget =>
            widget.Enabled && string.Equals(widget.TypeId, typeId, StringComparison.Ordinal));

    public static IReadOnlyList<LayoutWidgetElement> FindWidgets(
        LayoutProfile profile,
        string typeId) =>
        EnumerateWidgets(profile)
            .Where(widget => widget.Enabled &&
                string.Equals(widget.TypeId, typeId, StringComparison.Ordinal))
            .ToArray();

    private static IEnumerable<LayoutWidgetElement> EnumerateWidgets(LayoutProfile profile)
    {
        foreach (var container in profile.Containers.Where(container => container.Enabled))
        {
            foreach (var widget in EnumerateContainerWidgets(container))
            {
                yield return widget;
            }
        }

        foreach (var collapse in profile.CollapseContainers.Where(collapse => collapse.Enabled))
        {
            foreach (var widget in collapse.ExpandedSlot.Children.OfType<LayoutWidgetElement>())
            {
                yield return widget;
            }
        }
    }

    private static IEnumerable<LayoutWidgetElement> EnumerateContainerWidgets(
        LayoutContainerElement container)
    {
        foreach (var widget in container.PrimarySlot.Children.OfType<LayoutWidgetElement>())
        {
            yield return widget;
        }

        foreach (var widget in container.SecondarySlot.Children.OfType<LayoutWidgetElement>())
        {
            yield return widget;
        }
    }
}
