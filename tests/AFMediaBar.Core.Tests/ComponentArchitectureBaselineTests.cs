using AFMediaBar.Layout.Defaults;
using AFMediaBar.Layout.Model;
using AFMediaBar.Layout.Models;
using AFMediaBar.Layout.Widgets;
using AFMediaBar.Services;

namespace AFMediaBar.Core.Tests;

/// <summary>
/// Phase-0 contracts for the componentized layout baseline. These tests lock
/// current structure before introducing new MVVM/component assemblies.
/// </summary>
[TestClass]
public sealed class ComponentArchitectureBaselineTests
{
    [TestMethod]
    public void BuiltInComponentIdsAreUniqueAndHavePositiveDefaults()
    {
        var catalog = new BuiltInWidgetCatalog();
        var ids = catalog.Items.Select(item => item.TypeId).ToArray();

        Assert.HasCount(ids.Length, ids.Distinct(StringComparer.Ordinal));

        foreach (var descriptor in catalog.Items)
        {
            var settings = LayoutComponentCatalog.CreateDefaultSettings(descriptor.TypeId);
            var profile = LayoutDefaultTemplates.LoadDocument().Horizontal;
            var widget = new LayoutWidgetElement(
                $"baseline-{descriptor.TypeId}",
                true,
                LayoutGeometry.Auto,
                descriptor.TypeId,
                settings,
                GridBounds: descriptor.DefaultBounds);

            var measured = WidgetMeasurementService.MeasureRequiredCells(profile, widget);

            Assert.IsNotNull(settings, descriptor.TypeId);
            Assert.IsGreaterThan(0, measured.Width, descriptor.TypeId);
            Assert.IsGreaterThan(0, measured.Height, descriptor.TypeId);
            Assert.IsGreaterThanOrEqualTo(descriptor.MinimumBounds.Width, 1);
            Assert.IsGreaterThanOrEqualTo(descriptor.MinimumBounds.Height, 1);
        }
    }

    [TestMethod]
    public void DefaultProfilesHaveContainerRootsAndContainedWidgets()
    {
        var document = LayoutDefaultTemplates.LoadDocument();

        foreach (var profile in new[] { document.Horizontal, document.Vertical })
        {
            Assert.IsNotEmpty(profile.Containers);
            Assert.IsTrue(LayoutGridConstraintService.IsProfileValid(profile));

            foreach (var container in profile.Containers)
            {
                var bounds = container.GridBounds ??
                    throw new AssertFailedException(container.InstanceId);
                AssertSlotContained(bounds, container.PrimarySlot);
                AssertSlotContained(bounds, container.SecondarySlot);
            }

            foreach (var collapse in profile.CollapseContainers)
            {
                AssertSlotContained(collapse.GridBounds, collapse.ExpandedSlot);
            }
        }
    }

    [TestMethod]
    public void FunctionalComponentsCannotBeAddedAtProfileRoot()
    {
        var profile = LayoutDefaultTemplates.LoadDocument().Horizontal;
        var result = LayoutGridConstraintService.TryAddWidget(
            profile,
            "root",
            LayoutSlotKind.Primary,
            LayoutGridConstraintService.CreateWidget(BuiltInWidgetTypeIds.Command));

        Assert.IsFalse(result.Success);
        Assert.AreEqual(LayoutGridFailure.ContainerNotFound, result.Failure);
    }

    [TestMethod]
    public void SeparatorAdapterPreservesSchemaSettingsAndMeasurement()
    {
        var profile = LayoutDefaultTemplates.LoadDocument().Horizontal;
        var defaults = LayoutComponentCatalog.CreateDefaultSettings(BuiltInWidgetTypeIds.Separator);
        Assert.AreEqual(new SeparatorWidgetSettings(1, 22), defaults);
        Assert.AreEqual(defaults, ComponentCatalog.CreateDefaultSettings(BuiltInWidgetTypeIds.Separator));

        var widget = new LayoutWidgetElement(
            "separator-adapter",
            true,
            LayoutGeometry.Auto,
            BuiltInWidgetTypeIds.Separator,
            new SeparatorWidgetSettings(8, 32));

        Assert.AreEqual((3, 4), WidgetMeasurementService.MeasureRequiredCells(profile, widget));
    }

    [TestMethod]
    public void MigratedDefinitionsPreserveAllSchemaFiveDefaultSettingTypes()
    {
        Assert.IsInstanceOfType<ArtworkWidgetSettings>(LayoutComponentCatalog.CreateDefaultSettings(BuiltInWidgetTypeIds.Artwork));
        Assert.IsInstanceOfType<MediaTextWidgetSettings>(LayoutComponentCatalog.CreateDefaultSettings(BuiltInWidgetTypeIds.MediaText));
        Assert.IsInstanceOfType<MediaTextWidgetSettings>(LayoutComponentCatalog.CreateDefaultSettings(BuiltInWidgetTypeIds.MediaSource));
        Assert.IsInstanceOfType<CommandWidgetSettings>(LayoutComponentCatalog.CreateDefaultSettings(BuiltInWidgetTypeIds.Command));
        Assert.IsInstanceOfType<MetricsWidgetSettings>(LayoutComponentCatalog.CreateDefaultSettings(BuiltInWidgetTypeIds.Metrics));
        Assert.IsInstanceOfType<SpectrumWidgetSettings>(LayoutComponentCatalog.CreateDefaultSettings(BuiltInWidgetTypeIds.Spectrum));
        Assert.IsInstanceOfType<SeparatorWidgetSettings>(LayoutComponentCatalog.CreateDefaultSettings(BuiltInWidgetTypeIds.Separator));
    }

    private static void AssertSlotContained(LayoutGridRect ownerBounds, LayoutSlot slot)
    {
        foreach (var widget in slot.Children.OfType<LayoutWidgetElement>())
        {
            var bounds = widget.GridBounds ??
                throw new AssertFailedException(widget.InstanceId);
            Assert.IsTrue(ownerBounds.Contains(new LayoutGridRect(
                ownerBounds.X + bounds.X,
                ownerBounds.Y + bounds.Y,
                bounds.Width,
                bounds.Height)));
        }

        foreach (var nested in slot.Children.OfType<LayoutContainerElement>())
        {
            Assert.Fail($"Nested container is not part of the current supported slot model: {nested.InstanceId}");
        }
    }
}
