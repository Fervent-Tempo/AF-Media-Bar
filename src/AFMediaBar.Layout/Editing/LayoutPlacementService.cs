using AFMediaBar.Layout.Models;
using AFMediaBar.Services;

namespace AFMediaBar.Layout.Editing;

/// <summary>
/// UI-independent placement calculations used by the fine-grid editor.
/// The WPF layer supplies pointer coordinates and renders the result; this
/// service owns grid expansion and container placement validation.
/// </summary>
public static class LayoutPlacementService
{
    public static LayoutGridEditResult TryAddContainer(LayoutProfile profile, LayoutContainerKind kind)
    {
        if (kind == LayoutContainerKind.AutoCollapse)
        {
            return LayoutGridEditResult.Fail(LayoutGridFailure.NotSupported);
        }

        var size = ResolveDefaultContainerCells(profile);
        return TryFindFreePlacement(profile, size.Width, size.Height, out var rect)
            ? TryCreateContainer(profile, LayoutPlacementTool.Container(kind), rect)
            : LayoutGridEditResult.Fail(LayoutGridFailure.OutOfGrid);
    }

    public static LayoutGridEditResult TryAddCollapse(
        LayoutProfile profile,
        LayoutEdge attachmentSide,
        LayoutEdge? unavailableSide)
    {
        if (unavailableSide == attachmentSide)
        {
            return LayoutGridEditResult.Fail(LayoutGridFailure.InvalidAttachmentSide);
        }

        var anchor = profile.Containers.FirstOrDefault(item => item.Enabled && item.GridBounds is not null);
        if (anchor?.GridBounds is not { } anchorBounds)
        {
            return LayoutGridEditResult.Fail(LayoutGridFailure.ContainerNotFound);
        }

        var grid = LayoutGridSettings.Normalize(profile.Grid);
        var size = ResolveDefaultCollapseCells(profile);
        var rect = ResolveCollapseRect(anchorBounds, attachmentSide, size);
        if (!IsInGrid(rect, grid))
        {
            var fallback = attachmentSide switch
            {
                LayoutEdge.Top => LayoutEdge.Bottom,
                LayoutEdge.Bottom => LayoutEdge.Top,
                LayoutEdge.Left => LayoutEdge.Right,
                _ => LayoutEdge.Left
            };
            if (unavailableSide == fallback)
            {
                return LayoutGridEditResult.Fail(LayoutGridFailure.InvalidAttachmentSide);
            }

            rect = ResolveCollapseRect(anchorBounds, fallback, size);
            if (!IsInGrid(rect, grid))
            {
                return LayoutGridEditResult.Fail(LayoutGridFailure.OutOfGrid);
            }

            attachmentSide = fallback;
        }

        var collapse = LayoutGridConstraintService.CreateCollapseContainer(anchor, attachmentSide, rect);
        var candidate = profile with
        {
            CollapseContainers = profile.CollapseContainers.Append(collapse).ToArray()
        };
        var errors = LayoutGridConstraintService.ValidateProfile(candidate);
        return errors.Count == 0
            ? LayoutGridEditResult.Ok(candidate)
            : LayoutGridEditResult.Fail(errors[0].Failure);
    }

    public static LayoutGridEditResult TryCreateContainer(
        LayoutProfile profile,
        LayoutPlacementTool tool,
        LayoutGridRect editorRect)
    {
        var normalized = ExpandGridForRect(profile, editorRect);
        return LayoutGridConstraintService.TryCreateFromDrag(
            normalized.Profile,
            tool,
            normalized.Rect.X,
            normalized.Rect.Y,
            normalized.Rect.Right - 1,
            normalized.Rect.Bottom - 1);
    }

    public static bool CanPlaceContainer(LayoutProfile profile, LayoutGridRect editorRect)
    {
        var normalized = ExpandGridForRect(profile, editorRect);
        return LayoutGridConstraintService.CanPlaceContainer(normalized.Profile, normalized.Rect);
    }

    public static (LayoutProfile Profile, LayoutGridRect Rect) ExpandGridForRect(
        LayoutProfile profile,
        LayoutGridRect rect)
    {
        var grid = LayoutGridSettings.Normalize(profile.Grid);
        var shiftX = Math.Max(0, -rect.X);
        var shiftY = Math.Max(0, -rect.Y);
        var growRight = Math.Max(0, rect.Right - grid.Columns);
        var growBottom = Math.Max(0, rect.Bottom - grid.Rows);
        if (shiftX == 0 && shiftY == 0 && growRight == 0 && growBottom == 0)
        {
            return (profile, rect);
        }

        LayoutGridRect Shift(LayoutGridRect value) => new(
            value.X + shiftX,
            value.Y + shiftY,
            value.Width,
            value.Height);

        var containers = profile.Containers
            .Select(container => container.GridBounds is { } bounds
                ? container with { GridBounds = Shift(bounds) }
                : container)
            .ToArray();
        var collapses = profile.CollapseContainers
            .Select(collapse => collapse with { GridBounds = Shift(collapse.GridBounds) })
            .ToArray();
        var expandedGrid = grid with
        {
            Columns = grid.Columns + shiftX + growRight,
            Rows = grid.Rows + shiftY + growBottom
        };
        return (
            profile with
            {
                Grid = LayoutGridSettings.Normalize(expandedGrid),
                Containers = containers,
                CollapseContainers = collapses
            },
            Shift(rect));
    }

    private static bool TryFindFreePlacement(LayoutProfile profile, int width, int height, out LayoutGridRect rect)
    {
        var grid = LayoutGridSettings.Normalize(profile.Grid);
        var union = LayoutBoundsService.CalculateBodyGridBounds(profile);
        if (union is not null)
        {
            var startX = profile.LayoutMode == PlayerLayoutMode.Vertical ? union.X : union.Right;
            if (FindPlacementFrom(profile, startX, Math.Max(0, union.Y), width, height, out rect))
            {
                return true;
            }
        }

        return FindPlacementFrom(profile, 0, 0, width, height, out rect);
    }

    private static bool FindPlacementFrom(LayoutProfile profile, int startX, int startY, int width, int height, out LayoutGridRect rect)
    {
        var grid = LayoutGridSettings.Normalize(profile.Grid);
        for (var y = startY; y + height <= grid.Rows; y++)
        {
            for (var x = startX; x + width <= grid.Columns; x++)
            {
                var candidate = new LayoutGridRect(x, y, width, height);
                if (LayoutGridConstraintService.CanPlaceContainer(profile, candidate))
                {
                    rect = candidate;
                    return true;
                }
            }
        }

        rect = LayoutGridRect.Unit(0, 0);
        return false;
    }

    private static (int Width, int Height) ResolveDefaultContainerCells(LayoutProfile profile)
    {
        var cell = Math.Max(LayoutGridSettings.Normalize(profile.Grid).CellSizeDip, 1);
        var widthDip = profile.LayoutMode == PlayerLayoutMode.Vertical ? 48 : 168;
        var heightDip = profile.LayoutMode == PlayerLayoutMode.Vertical ? 168 : 48;
        return (ToCells(widthDip, cell), ToCells(heightDip, cell));
    }

    private static (int Width, int Height) ResolveDefaultCollapseCells(LayoutProfile profile)
    {
        var cell = Math.Max(LayoutGridSettings.Normalize(profile.Grid).CellSizeDip, 1);
        return (ToCells(120, cell), ToCells(80, cell));
    }

    private static LayoutGridRect ResolveCollapseRect(LayoutGridRect anchor, LayoutEdge side, (int Width, int Height) size) => side switch
    {
        LayoutEdge.Top => new LayoutGridRect(anchor.X, anchor.Y - size.Height, size.Width, size.Height),
        LayoutEdge.Bottom => new LayoutGridRect(anchor.X, anchor.Bottom, size.Width, size.Height),
        LayoutEdge.Left => new LayoutGridRect(anchor.X - size.Width, anchor.Y, size.Width, size.Height),
        _ => new LayoutGridRect(anchor.Right, anchor.Y, size.Width, size.Height)
    };

    private static int ToCells(double dip, int cellSizeDip) =>
        Math.Max(1, (int)Math.Ceiling(Math.Max(0, dip) / cellSizeDip));

    private static bool IsInGrid(LayoutGridRect rect, LayoutGridSettings grid) =>
        rect.Width >= 1 && rect.Height >= 1 && rect.X >= 0 && rect.Y >= 0 &&
        rect.Right <= grid.Columns && rect.Bottom <= grid.Rows;
}
