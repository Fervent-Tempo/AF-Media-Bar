using AFMediaBar.Layout.Defaults;
using AFMediaBar.Layout.Editing;
using AFMediaBar.Layout.Models;

namespace AFMediaBar.Core.Tests;

[TestClass]
public sealed class LayoutPlacementServiceTests
{
    [TestMethod]
    public void ExpandGridForRect_GrowsGridAndShiftsExistingElements()
    {
        var profile = LayoutDefaultTemplates.LoadDocument().Horizontal;
        var originalGrid = LayoutGridSettings.Normalize(profile.Grid);
        var originalBounds = profile.Containers[0].GridBounds!;
        var candidate = new LayoutGridRect(-2, -1, 4, 3);

        var expanded = LayoutPlacementService.ExpandGridForRect(profile, candidate);
        var expandedGrid = LayoutGridSettings.Normalize(expanded.Profile.Grid);

        Assert.AreEqual(originalGrid.Columns + 2, expandedGrid.Columns);
        Assert.AreEqual(originalGrid.Rows + 1, expandedGrid.Rows);
        Assert.AreEqual(0, expanded.Rect.X);
        Assert.AreEqual(0, expanded.Rect.Y);
        Assert.AreEqual(originalBounds.X + 2, expanded.Profile.Containers[0].GridBounds!.X);
        Assert.AreEqual(originalBounds.Y + 1, expanded.Profile.Containers[0].GridBounds!.Y);
    }
}
