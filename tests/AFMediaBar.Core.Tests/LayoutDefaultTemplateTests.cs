using AFMediaBar.Layout.Defaults;
using AFMediaBar.Layout.Model;
using AFMediaBar.Layout.Models;
using AFMediaBar.Services;

namespace AFMediaBar.Core.Tests;

[TestClass]
public sealed class LayoutDefaultTemplateTests
{
    [TestMethod]
    public void EmbeddedTemplatesProduceValidSchemaFiveDocument()
    {
        var document = LayoutDefaultTemplates.LoadDocument();

        Assert.AreEqual(LayoutSchemaContract.Version, document.SchemaVersion);
        Assert.IsTrue(LayoutGridConstraintService.IsProfileValid(document.Horizontal));
        Assert.IsTrue(LayoutGridConstraintService.IsProfileValid(document.Vertical));
        Assert.IsNotEmpty(document.Horizontal.Containers);
        Assert.IsNotEmpty(document.Vertical.Containers);
    }
}
