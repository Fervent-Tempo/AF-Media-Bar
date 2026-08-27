using AFMediaBar.Layout.Widgets;
using AFMediaBar.Layout.Defaults;
using AFMediaBar.Models;
using AFMediaBar.Services;

namespace AFMediaBar.Core.Tests;

[TestClass]
public sealed class WidgetMeasurementTests
{
    [TestMethod]
    public void DefaultCommandUsesThreeByThreeCells()
    {
        var profile = LayoutDefaultTemplates.LoadDocument().Horizontal;
        var widget = LayoutGridConstraintService.CreateWidget(BuiltInWidgetTypeIds.Command);

        var size = WidgetMeasurementService.MeasureRequiredCells(profile, widget);

        Assert.AreEqual(3, size.Width);
        Assert.AreEqual(3, size.Height);
    }

    [TestMethod]
    public void LargerTitleMeasurementGrowsWithFontAndCombinedText()
    {
        var profile = LayoutDefaultTemplates.LoadDocument().Horizontal;
        var widget = LayoutGridConstraintService.CreateWidget(BuiltInWidgetTypeIds.MediaText) with
        {
            Settings = new MediaTextWidgetSettings(MediaTextKind.TitleAndArtist, true, 24, 2)
        };

        var size = WidgetMeasurementService.MeasureRequiredCells(profile, widget);

        Assert.IsGreaterThanOrEqualTo(size.Width, 19);
        Assert.IsGreaterThanOrEqualTo(size.Height, 8);
    }
}
