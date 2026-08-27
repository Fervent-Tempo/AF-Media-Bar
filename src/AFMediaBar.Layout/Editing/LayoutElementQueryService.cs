using AFMediaBar.Layout.Models;

namespace AFMediaBar.Layout.Editing;

/// <summary>
/// Read-only element lookup for editor consumers.
/// </summary>
public static class LayoutElementQueryService
{
    public static object? Find(LayoutProfile profile, string instanceId)
    {
        foreach (var container in profile.Containers)
        {
            if (container.InstanceId == instanceId)
            {
                return container;
            }

            if (Find(container, instanceId) is { } nested)
            {
                return nested;
            }
        }

        object? collapse = profile.CollapseContainers.FirstOrDefault(item => item.InstanceId == instanceId);
        return collapse ?? profile.CollapseContainers
            .SelectMany(item => item.ExpandedSlot.Children)
            .FirstOrDefault(item => item.InstanceId == instanceId);
    }

    public static LayoutContainerElement? FindContainer(LayoutProfile profile, string instanceId) =>
        profile.Containers.SelectMany(EnumerateContainers).FirstOrDefault(item => item.InstanceId == instanceId);

    public static LayoutCollapseContainer? FindCollapse(LayoutProfile profile, string instanceId) =>
        profile.CollapseContainers.FirstOrDefault(item => item.InstanceId == instanceId);

    public static LayoutElement? Find(LayoutContainerElement container, string instanceId)
    {
        if (container.InstanceId == instanceId)
        {
            return container;
        }

        foreach (var child in container.PrimarySlot.Children.Concat(container.SecondarySlot.Children))
        {
            if (child.InstanceId == instanceId)
            {
                return child;
            }

            if (child is LayoutContainerElement nested && Find(nested, instanceId) is { } match)
            {
                return match;
            }
        }

        return null;
    }

    private static IEnumerable<LayoutContainerElement> EnumerateContainers(LayoutContainerElement container)
    {
        yield return container;
        foreach (var nested in container.PrimarySlot.Children.Concat(container.SecondarySlot.Children).OfType<LayoutContainerElement>())
        {
            foreach (var item in EnumerateContainers(nested))
            {
                yield return item;
            }
        }
    }
}
