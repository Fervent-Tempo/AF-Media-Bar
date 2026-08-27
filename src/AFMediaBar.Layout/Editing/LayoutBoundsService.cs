using AFMediaBar.Layout.Models;

namespace AFMediaBar.Layout.Editing;

/// <summary>
/// Pure layout bounds calculations shared by editor placement and runtime adapters.
/// </summary>
public static class LayoutBoundsService
{
    public static LayoutGridRect? CalculateBodyGridBounds(LayoutProfile profile)
    {
        var grid = LayoutGridSettings.Normalize(profile.Grid);
        LayoutGridRect? union = null;
        foreach (var container in profile.Containers)
        {
            if (!container.Enabled || container.GridBounds is not { } bounds)
            {
                continue;
            }

            var clamped = ClampToGrid(bounds, grid);
            union = union is { } current ? Union(current, clamped) : clamped;
        }

        return union;
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
