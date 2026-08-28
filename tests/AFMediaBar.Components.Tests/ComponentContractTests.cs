using AFMediaBar.Components.Abstractions;
using AFMediaBar.Components.BuiltIn;
using AFMediaBar.Components.BuiltIn.Layout;

namespace AFMediaBar.Components.Tests;

[TestClass]
public sealed class ComponentContractTests
{
    [TestMethod]
    public void BuiltInTypeIdsAreUniqueAndIncludeContainerAndFunctionalKinds()
    {
        var registry = new BuiltInComponentRegistry();
        Assert.AreEqual(registry.Items.Count, registry.Items.Select(x => x.Metadata.TypeId).Distinct(StringComparer.Ordinal).Count());
        Assert.IsTrue(registry.Items.Any(x => x.Kind == ComponentKind.Container));
        Assert.IsTrue(registry.Items.Any(x => x.Kind == ComponentKind.Functional));
    }

    [TestMethod]
    public void EveryDefinitionProvidesDefaultSettingsAndPositiveMeasurement()
    {
        var registry = new BuiltInComponentRegistry();
        var context = new ComponentMeasureContext(48, 24, 8, false);
        foreach (var definition in registry.Items)
        {
            var settings = definition.CreateDefaultSettings();
            Assert.AreEqual(definition.Metadata.TypeId, settings.TypeId);
            var result = definition.Measure(settings, context);
            Assert.IsTrue(result.PreferredWidth > 0 && result.PreferredHeight > 0);
            Assert.IsTrue(result.MinimumWidth > 0 && result.MinimumHeight > 0);
            Assert.IsEmpty(definition.Validate(settings));
        }
    }

    [TestMethod]
    public void EveryDefinitionOwnsTypedDefaultsAndSmallGridWarning()
    {
        var registry = new BuiltInComponentRegistry();
        Assert.HasCount(12, registry.Items);

        foreach (var definition in registry.Items)
        {
            var settings = definition.CreateDefaultSettings();
            Assert.AreEqual(definition.Metadata.TypeId, settings.TypeId);
            var result = definition.Measure(settings, new ComponentMeasureContext(1, 1, 8, false));
            Assert.IsTrue(result.HasWarning, definition.Metadata.TypeId);
        }
    }

    [TestMethod]
    public void FunctionalComponentsNeverReportInteractionWithoutTheCapability()
    {
        var registry = new BuiltInComponentRegistry();
        foreach (var definition in registry.Items.Where(x => x.Kind == ComponentKind.Functional))
        {
            var expected = (definition.Metadata.Capabilities & ComponentCapabilities.Interactive) != 0;
            var actual = definition.IsInteractive(definition.CreateDefaultSettings());
            Assert.IsFalse(actual && !expected, definition.Metadata.TypeId);
        }
    }

    [TestMethod]
    public void SeparatorDefinitionOwnsSettingsMeasurementAndValidation()
    {
        var definition = new SeparatorDefinition();
        var settings = (SeparatorSettings)definition.CreateDefaultSettings();
        var measured = definition.Measure(settings, new ComponentMeasureContext(48, 24, 8, false));

        Assert.AreEqual(ComponentTypeIds.Separator, settings.TypeId);
        Assert.AreEqual(3, measured.PreferredWidth);
        Assert.AreEqual(3, measured.PreferredHeight);
        Assert.IsEmpty(definition.Validate(settings));
        Assert.IsFalse(definition.IsInteractive(settings));
        Assert.IsNotEmpty(definition.Validate(new SeparatorSettings(0, 0)));
    }
}
