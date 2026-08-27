using AFMediaBar.Models;
using AFMediaBar.Services;
using AFMediaBar.Layout.Defaults;
using AFMediaBar.Layout.Widgets;

namespace AFMediaBar.Core.Tests;

[TestClass]
public sealed class LayoutArchitectureContractTests
{
    [TestMethod]
    public void DefaultDocumentUsesSchemaFive()
    {
        var document = LayoutDefaultTemplates.LoadDocument();

        Assert.AreEqual(LayoutDocument.CurrentSchemaVersion, document.SchemaVersion);
    }

    [TestMethod]
    public void BuiltInCatalogExposesStableMinimumAndCommandDefaults()
    {
        var catalog = new BuiltInWidgetCatalog();

        Assert.IsTrue(catalog.TryGet(BuiltInWidgetTypeIds.Command, out var command));
        Assert.AreEqual(LayoutGridRect.Unit(0, 0), command.MinimumBounds);
        Assert.AreEqual(new LayoutGridRect(0, 0, 3, 3), command.DefaultBounds);
    }
}
