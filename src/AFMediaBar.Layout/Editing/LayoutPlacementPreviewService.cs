using AFMediaBar.Layout.Models;
using AFMediaBar.Layout.Widgets;
using AFMediaBar.Services;

namespace AFMediaBar.Layout.Editing;

public sealed record LayoutPlacementPreview(LayoutGridRect Bounds, bool IsValid);

/// <summary>
/// Pure candidate calculation for fine-grid placement previews.
/// </summary>
public static class LayoutPlacementPreviewService
{
    public static LayoutPlacementPreview Calculate(
        LayoutProfile profile,
        LayoutPlacementTool tool,
        int startX,
        int startY,
        int currentX,
        int currentY,
        WidgetSettings? widgetSettings,
        Func<LayoutContainerElement, LayoutSlotKind>? visibleSlotResolver = null)
    {
        var dragging = startX != currentX || startY != currentY;
        var bounds = dragging
            ? LayoutGridRect.FromDrag(startX, startY, currentX, currentY)
            : CreateDefaultBounds(profile, tool, currentX, currentY, widgetSettings, visibleSlotResolver);

        var valid = tool.IsContainer
            ? LayoutPlacementService.CanPlaceContainer(profile, bounds)
            : ResolveOwner(profile, startX, startY, visibleSlotResolver) is { } owner &&
              bounds.X >= owner.Bounds.X && bounds.Y >= owner.Bounds.Y &&
              bounds.Right <= owner.Bounds.Right && bounds.Bottom <= owner.Bounds.Bottom &&
              LayoutGridConstraintService.CanPlaceWidget(
                  profile,
                  owner.ContainerId,
                  owner.SlotKind,
                  new LayoutGridRect(
                      bounds.X - owner.Bounds.X,
                      bounds.Y - owner.Bounds.Y,
                      bounds.Width,
                      bounds.Height));

        return new LayoutPlacementPreview(bounds, valid);
    }

    private static LayoutGridRect CreateDefaultBounds(
        LayoutProfile profile,
        LayoutPlacementTool tool,
        int cellX,
        int cellY,
        WidgetSettings? widgetSettings,
        Func<LayoutContainerElement, LayoutSlotKind>? visibleSlotResolver)
    {
        if (tool.IsContainer)
        {
            var size = tool.ContainerKind == LayoutContainerKind.HoverSwitch
                ? (Width: 6, Height: 3)
                : (Width: 4, Height: 3);
            return new LayoutGridRect(cellX, cellY, size.Width, size.Height);
        }

        if (ResolveOwner(profile, cellX, cellY, visibleSlotResolver) is not { } owner)
        {
            return LayoutGridRect.Unit(cellX, cellY);
        }

        var settings = widgetSettings ?? LayoutComponentCatalog.CreateDefaultSettings(tool.WidgetTypeId!);
        var widget = new LayoutWidgetElement(
            "placement-preview",
            true,
            LayoutGeometry.Auto,
            tool.WidgetTypeId!,
            settings);
        var desired = WidgetMeasurementService.MeasureRequiredCells(profile, widget);
        return new LayoutGridRect(
            cellX,
            cellY,
            Math.Min(desired.Width, owner.Bounds.Right - cellX),
            Math.Min(desired.Height, owner.Bounds.Bottom - cellY));
    }

    private static (string ContainerId, LayoutGridRect Bounds, LayoutSlotKind SlotKind)? ResolveOwner(
        LayoutProfile profile,
        int cellX,
        int cellY,
        Func<LayoutContainerElement, LayoutSlotKind>? visibleSlotResolver)
    {
        foreach (var container in profile.Containers.Where(item => item.Enabled && item.GridBounds is not null))
        {
            var bounds = container.GridBounds!;
            if (cellX >= bounds.X && cellX < bounds.Right && cellY >= bounds.Y && cellY < bounds.Bottom)
            {
                return (container.InstanceId, bounds, visibleSlotResolver?.Invoke(container) ?? LayoutSlotKind.Primary);
            }
        }

        foreach (var collapse in profile.CollapseContainers.Where(item => item.Enabled))
        {
            var bounds = collapse.GridBounds;
            if (cellX >= bounds.X && cellX < bounds.Right && cellY >= bounds.Y && cellY < bounds.Bottom)
            {
                return (collapse.InstanceId, bounds, LayoutSlotKind.Expanded);
            }
        }

        return null;
    }
}
