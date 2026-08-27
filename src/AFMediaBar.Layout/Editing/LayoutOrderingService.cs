using AFMediaBar.Layout.Models;
using AFMediaBar.Services;

namespace AFMediaBar.Layout.Editing;

/// <summary>
/// Immutable sibling ordering and widget relocation operations for the editor.
/// </summary>
public static class LayoutOrderingService
{
    public static bool TryReorderTopLevel(
        LayoutProfile profile,
        string sourceId,
        string targetId,
        out LayoutProfile updated)
    {
        if (TryReorderList(profile.Containers, sourceId, targetId, out var containers))
        {
            updated = profile with { Containers = containers };
            return true;
        }

        if (TryReorderList(profile.CollapseContainers, sourceId, targetId, out var collapses))
        {
            updated = profile with { CollapseContainers = collapses };
            return true;
        }

        updated = profile;
        return false;
    }

    public static bool TryMoveSibling(LayoutProfile profile, string instanceId, int offset, out LayoutProfile updated)
    {
        if (offset == 0)
        {
            updated = profile;
            return false;
        }

        if (TryMoveList(profile.Containers, instanceId, offset, out var containers))
        {
            updated = profile with { Containers = containers };
            return true;
        }

        if (TryMoveList(profile.CollapseContainers, instanceId, offset, out var collapses))
        {
            updated = profile with { CollapseContainers = collapses };
            return true;
        }

        foreach (var container in profile.Containers)
        {
            foreach (var (slotKind, slot) in new[]
            {
                (LayoutSlotKind.Primary, container.PrimarySlot),
                (LayoutSlotKind.Secondary, container.SecondarySlot)
            })
            {
                if (!TryMoveList(slot.Children, instanceId, offset, out var children))
                {
                    continue;
                }

                updated = profile with
                {
                    Containers = profile.Containers.Select(item => item.InstanceId == container.InstanceId
                        ? slotKind == LayoutSlotKind.Primary
                            ? item with { PrimarySlot = slot with { Children = children } }
                            : item with { SecondarySlot = slot with { Children = children } }
                        : item).ToArray()
                };
                return true;
            }
        }

        updated = profile;
        return false;
    }

    public static bool TryRelocateWidget(
        LayoutProfile profile,
        string instanceId,
        string targetContainerId,
        LayoutSlotKind targetSlot,
        out LayoutProfile updated)
    {
        if (LayoutGridConstraintService.FindAny(profile, instanceId) is not LayoutWidgetElement widget)
        {
            updated = profile;
            return false;
        }

        var removed = LayoutGridConstraintService.TryRemove(profile, instanceId);
        if (!removed.Success || removed.Updated is not { } withoutWidget)
        {
            updated = profile;
            return false;
        }

        if (targetSlot == LayoutSlotKind.Expanded &&
            LayoutGridConstraintService.FindCollapse(withoutWidget, targetContainerId) is { } collapse)
        {
            var collapseCandidate = withoutWidget with
            {
                CollapseContainers = withoutWidget.CollapseContainers.Select(item => item.InstanceId == collapse.InstanceId
                    ? item with
                    {
                        ExpandedSlot = collapse.ExpandedSlot with
                        {
                            Children = collapse.ExpandedSlot.Children.Append(widget).ToArray()
                        }
                    }
                    : item).ToArray()
            };
            if (LayoutGridConstraintService.ValidateProfile(collapseCandidate).Count == 0)
            {
                updated = collapseCandidate;
                return true;
            }

            updated = profile;
            return false;
        }

        var target = LayoutGridConstraintService.FindContainer(withoutWidget, targetContainerId);
        if (target is null || targetSlot == LayoutSlotKind.Expanded ||
            targetSlot == LayoutSlotKind.Secondary && target.ContainerKind != LayoutContainerKind.HoverSwitch)
        {
            updated = profile;
            return false;
        }

        var slot = targetSlot == LayoutSlotKind.Secondary ? target.SecondarySlot : target.PrimarySlot;
        var localBounds = widget.GridBounds ?? LayoutGridRect.Unit(0, 0);
        var candidateWidget = widget with { GridBounds = localBounds };
        var candidate = withoutWidget with
        {
            Containers = withoutWidget.Containers.Select(item => item.InstanceId == target.InstanceId
                ? targetSlot == LayoutSlotKind.Secondary
                    ? item with { SecondarySlot = slot with { Children = slot.Children.Append(candidateWidget).ToArray() } }
                    : item with { PrimarySlot = slot with { Children = slot.Children.Append(candidateWidget).ToArray() } }
                : item).ToArray()
        };
        var errors = LayoutGridConstraintService.ValidateProfile(candidate);
        if (errors.Count != 0)
        {
            updated = profile;
            return false;
        }

        updated = candidate;
        return true;
    }

    private static bool TryMoveList<T>(IReadOnlyList<T> source, string instanceId, int offset, out IReadOnlyList<T> updated)
    {
        var items = source.ToArray();
        var index = Array.FindIndex(items, item => GetId(item) == instanceId);
        var target = index + Math.Sign(offset);
        if (index < 0 || target < 0 || target >= items.Length)
        {
            updated = source;
            return false;
        }

        (items[index], items[target]) = (items[target], items[index]);
        updated = items;
        return true;
    }

    private static bool TryReorderList<T>(IReadOnlyList<T> source, string sourceId, string targetId, out IReadOnlyList<T> updated)
    {
        var items = source.ToList();
        var sourceIndex = items.FindIndex(item => GetId(item) == sourceId);
        var targetIndex = items.FindIndex(item => GetId(item) == targetId);
        if (sourceIndex < 0 || targetIndex < 0 || sourceIndex == targetIndex)
        {
            updated = source;
            return false;
        }

        var item = items[sourceIndex];
        items.RemoveAt(sourceIndex);
        if (sourceIndex < targetIndex)
        {
            targetIndex--;
        }
        items.Insert(targetIndex, item);
        updated = items.ToArray();
        return true;
    }

    private static string? GetId<T>(T item) => item switch
    {
        LayoutElement element => element.InstanceId,
        LayoutCollapseContainer collapse => collapse.InstanceId,
        _ => null
    };
}
