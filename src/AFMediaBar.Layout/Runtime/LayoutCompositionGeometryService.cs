using AFMediaBar.Layout.Models;

namespace AFMediaBar.Layout.Runtime;

/// <summary>
/// Pure composition geometry for expanded and collapsed layout containers.
/// </summary>
public static class LayoutCompositionGeometryService
{
    public static LayoutSize GridRectToDip(LayoutGridRect rect, int cellSizeDip)
    {
        var cell = Math.Max(cellSizeDip, 1);
        return new LayoutSize(rect.Width * cell, rect.Height * cell);
    }

    public static LayoutSize CalculateDesiredSize(LayoutProfile profile)
    {
        var grid = LayoutGridSettings.Normalize(profile.Grid);
        var union = LayoutGridGeometryService.CalculateBodyGridBounds(profile);
        return union is null
            ? new LayoutSize(grid.CellSizeDip, grid.CellSizeDip)
            : GridRectToDip(union, grid.CellSizeDip);
    }

    public static LayoutSize CalculateCompositionSize(
        LayoutProfile profile,
        IReadOnlySet<string>? expandedCollapseIds = null)
    {
        var grid = LayoutGridSettings.Normalize(profile.Grid);
        var union = CalculateCompositionGridBounds(profile, expandedCollapseIds);
        return union is null
            ? new LayoutSize(grid.CellSizeDip, grid.CellSizeDip)
            : GridRectToDip(union, grid.CellSizeDip);
    }

    public static LayoutGridRect? CalculateCompositionGridBounds(
        LayoutProfile profile,
        IReadOnlySet<string>? expandedCollapseIds = null)
    {
        var union = LayoutGridGeometryService.CalculateBodyGridBounds(profile);
        foreach (var collapse in profile.CollapseContainers)
        {
            if (!collapse.Enabled)
            {
                continue;
            }

            var bounds = collapse.GridBounds;
            var expanded = expandedCollapseIds is null || expandedCollapseIds.Contains(collapse.InstanceId);
            var footprint = expanded
                ? bounds
                : CalculateCollapseTriggerBounds(collapse, profile);
            union = union is { } current ? Union(current, footprint) : footprint;
        }

        return union;
    }

    public static LayoutGridRect CalculateCollapseTriggerBounds(
        LayoutCollapseContainer collapse,
        LayoutProfile profile)
    {
        var bounds = collapse.GridBounds;
        var grid = LayoutGridSettings.Normalize(profile.Grid);
        var info = AFMediaBar.Services.LayoutGridConstraintService.ResolveAttachment(collapse, profile);
        if (!info.Valid || info.SharedEdge.IsEmpty)
        {
            return ClampToGrid(bounds, grid);
        }

        var cellSize = Math.Max(grid.CellSizeDip, 1);
        var trigger = Math.Max(
            1,
            (int)Math.Ceiling(Math.Clamp(collapse.TriggerThicknessDip, 2, 24) / (double)cellSize));
        var shared = info.SharedEdge;
        var side = AFMediaBar.Services.LayoutGridConstraintService.ConnectionSide(collapse.Attachment);
        var rect = side switch
        {
            LayoutEdge.Top => new LayoutGridRect(shared.X, bounds.Y, shared.Width, Math.Min(trigger, bounds.Height)),
            LayoutEdge.Bottom => new LayoutGridRect(shared.X, bounds.Bottom - Math.Min(trigger, bounds.Height), shared.Width, Math.Min(trigger, bounds.Height)),
            LayoutEdge.Left => new LayoutGridRect(bounds.X, shared.Y, Math.Min(trigger, bounds.Width), shared.Height),
            _ => new LayoutGridRect(bounds.Right - Math.Min(trigger, bounds.Width), shared.Y, Math.Min(trigger, bounds.Width), shared.Height)
        };
        return ClampToGrid(rect, grid);
    }

    private static LayoutGridRect ClampToGrid(LayoutGridRect rect, LayoutGridSettings grid)
    {
        var left = Math.Max(0, rect.X);
        var top = Math.Max(0, rect.Y);
        var right = Math.Min(grid.Columns, rect.Right);
        var bottom = Math.Min(grid.Rows, rect.Bottom);
        return new LayoutGridRect(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
    }

    private static LayoutGridRect Union(LayoutGridRect a, LayoutGridRect b) =>
        new(
            Math.Min(a.X, b.X),
            Math.Min(a.Y, b.Y),
            Math.Max(a.Right, b.Right) - Math.Min(a.X, b.X),
            Math.Max(a.Bottom, b.Bottom) - Math.Min(a.Y, b.Y));
}

public readonly record struct LayoutSize(double WidthDip, double HeightDip);
