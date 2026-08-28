using AFMediaBar.Layout.Defaults;
using AFMediaBar.Layout.Model;
using AFMediaBar.Layout.Models;
using AFMediaBar.Layout.Widgets;
using AFMediaBar.Layout.Runtime;
using AFMediaBar.Services;
using AFMediaBar.Components.Abstractions;
using AFMediaBar.Components.BuiltIn;
using AFMediaBar.Components.BuiltIn.Audio;
using AFMediaBar.Components.BuiltIn.Layout;
using AFMediaBar.Components.BuiltIn.Media;
using AFMediaBar.Components.BuiltIn.Playback;
using AFMediaBar.Components.BuiltIn.System;
using ComponentMetricKind = AFMediaBar.Components.BuiltIn.System.MetricKind;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AFMediaBar.Core.Tests;

/// <summary>
/// Phase-0 contracts for the componentized layout baseline. These tests lock
/// current structure before introducing new MVVM/component assemblies.
/// </summary>
[TestClass]
public sealed class ComponentArchitectureBaselineTests
{
    [TestMethod]
    public void LayoutTypeIdsAreCompatibilityAliasesOfComponentTypeIds()
    {
        var registry = new BuiltInComponentRegistry();
        var aliases = new[]
        {
            BuiltInWidgetTypeIds.Artwork,
            BuiltInWidgetTypeIds.MediaText,
            BuiltInWidgetTypeIds.MediaSource,
            BuiltInWidgetTypeIds.Command,
            BuiltInWidgetTypeIds.Metrics,
            BuiltInWidgetTypeIds.Spectrum,
            BuiltInWidgetTypeIds.Separator
        };

        foreach (var alias in aliases)
        {
            Assert.IsTrue(registry.TryGet(alias, out var definition), alias);
            Assert.AreEqual(alias, definition.Metadata.TypeId, alias);
        }
    }

    [TestMethod]
    public void SchemaSettingsMapToComponentSettingsForEverySupportedWidget()
    {
        var profile = LayoutDefaultTemplates.LoadDocument().Horizontal;
        var catalog = new BuiltInWidgetCatalog();

        foreach (var descriptor in catalog.Items)
        {
            var widget = new LayoutWidgetElement(
                $"mapping-{descriptor.TypeId}",
                true,
                LayoutGeometry.Auto,
                descriptor.TypeId,
                LayoutComponentCatalog.CreateDefaultSettings(descriptor.TypeId));

            Assert.IsTrue(ComponentDefinitionAdapter.TryMapSettings(widget, out var mapped), descriptor.TypeId);
            Assert.AreEqual(descriptor.TypeId, mapped.TypeId, descriptor.TypeId);
        }
    }

    [TestMethod]
    public void CommandSchemaSettingsMapToDedicatedAudioComponentTypes()
    {
        var output = new LayoutWidgetElement(
            "mapping-output",
            true,
            LayoutGeometry.Auto,
            BuiltInWidgetTypeIds.Command,
            new CommandWidgetSettings(MediaCommandKind.SelectOutputDevice, 24));
        var volume = output with
        {
            InstanceId = "mapping-volume",
            Settings = new CommandWidgetSettings(MediaCommandKind.AdjustVolume, 24)
        };

        Assert.IsTrue(ComponentDefinitionAdapter.TryMapSettings(output, out var outputSettings));
        Assert.IsTrue(ComponentDefinitionAdapter.TryMapSettings(volume, out var volumeSettings));
        Assert.AreEqual(ComponentTypeIds.OutputDevice, outputSettings.TypeId);
        Assert.AreEqual(ComponentTypeIds.Volume, volumeSettings.TypeId);
    }

    [TestMethod]
    public void Schema5TypeResolverKeepsLegacyCommandForPlaybackCommands()
    {
        var mapper = new Schema5ComponentSettingsMapper();
        var playback = new LayoutWidgetElement(
            "mapping-playback",
            true,
            LayoutGeometry.Auto,
            ComponentTypeIds.PlaybackCommand,
            new CommandWidgetSettings(MediaCommandKind.PlayPause, 24));

        Assert.IsTrue(mapper.TryMapSettings(playback, out var settings));
        Assert.AreEqual(ComponentTypeIds.PlaybackCommand, settings.TypeId);
    }

    [TestMethod]
    public void SchemaMapperIsAnInjectableConversionBoundary()
    {
        var mapper = new Schema5ComponentSettingsMapper(new BuiltInComponentRegistry());
        var widget = new LayoutWidgetElement(
            "mapper-boundary",
            true,
            LayoutGeometry.Auto,
            ComponentTypeIds.MediaText,
            new MediaTextWidgetSettings(MediaTextKind.Title, true, 14, 1));

        Assert.IsTrue(mapper.TryMapSettings(widget, out var settings));
        Assert.AreEqual(ComponentTypeIds.MediaText, settings.TypeId);
        Assert.IsTrue(mapper.TryCreateDefaultSettings(ComponentTypeIds.MediaText, out var defaults));
        Assert.IsInstanceOfType<MediaTextWidgetSettings>(defaults);
    }

    [TestMethod]
    public void SchemaMapperRejectsUnknownTypes()
    {
        var mapper = new Schema5ComponentSettingsMapper();
        var widget = new LayoutWidgetElement(
            "mapper-unknown",
            true,
            LayoutGeometry.Auto,
            "unknown.component",
            new MediaTextWidgetSettings(MediaTextKind.Title, false, 14, 1));

        Assert.IsFalse(mapper.TryMapSettings(widget, out _));
        Assert.IsFalse(mapper.TryCreateDefaultSettings("unknown.component", out _));
    }

    [TestMethod]
    public void SchemaMapperRejectsKnownTypesWithIncompatibleSchemaSettings()
    {
        var mapper = new Schema5ComponentSettingsMapper();
        var incompatible = new LayoutWidgetElement(
            "mapper-incompatible",
            true,
            LayoutGeometry.Auto,
            ComponentTypeIds.Spectrum,
            new MediaTextWidgetSettings(MediaTextKind.Title, false, 14, 1));

        Assert.IsFalse(mapper.TryMapSettings(incompatible, out _));

        var invalidOutput = new LayoutWidgetElement(
            "mapper-invalid-output",
            true,
            LayoutGeometry.Auto,
            ComponentTypeIds.OutputDevice,
            new CommandWidgetSettings(MediaCommandKind.PlayPause, 24));

        Assert.IsFalse(mapper.TryMapSettings(invalidOutput, out _));
    }

    [TestMethod]
    public void SchemaMapperRoundTripsComponentSettingsThroughSchemaFiveBoundary()
    {
        var mapper = new Schema5ComponentSettingsMapper();
        var settings = new IComponentSettings[]
        {
            new ArtworkSettings(8, true, false),
            new MediaTextSettings(MediaTextContentKind.TitleAndArtist, true, 16, 2),
            new MediaSourceSettings(12, 1),
            new PlaybackCommandSettings(PlaybackCommandKind.Next, 28),
            new OutputDeviceSettings(30),
            new VolumeSettings(32),
            new SpectrumSettings(7, 24, 80),
            new MetricsSettings(ComponentMetricKind.SystemCpu, true, 750, [ComponentMetricKind.SystemCpu, ComponentMetricKind.SystemGpu]),
            new SeparatorSettings(2, 24)
        };

        foreach (var original in settings)
        {
            Assert.IsTrue(mapper.TryMapToSchema5(original, out var typeId, out var schemaSettings), original.TypeId);
            var widget = new LayoutWidgetElement(
                $"roundtrip-{original.TypeId}",
                true,
                LayoutGeometry.Auto,
                typeId,
                schemaSettings);

            Assert.IsTrue(mapper.TryMapSettings(widget, out var restored), original.TypeId);
            Assert.AreEqual(original.TypeId, restored.TypeId, original.TypeId);
            if (original is MetricsSettings expectedMetrics && restored is MetricsSettings actualMetrics)
            {
                Assert.AreEqual(expectedMetrics.Metric, actualMetrics.Metric, original.TypeId);
                Assert.AreEqual(expectedMetrics.OpenTaskManagerOnClick, actualMetrics.OpenTaskManagerOnClick, original.TypeId);
                Assert.AreEqual(expectedMetrics.RefreshIntervalMilliseconds, actualMetrics.RefreshIntervalMilliseconds, original.TypeId);
                CollectionAssert.AreEqual(expectedMetrics.EffectiveCycleMetrics.ToArray(), actualMetrics.EffectiveCycleMetrics.ToArray(), original.TypeId);
            }
            else
            {
                Assert.AreEqual(original, restored, original.TypeId);
            }
        }
    }

    [TestMethod]
    public void SchemaMapperHasCodecForEveryRegisteredFunctionalComponent()
    {
        var registry = new BuiltInComponentRegistry();
        var mapper = new Schema5ComponentSettingsMapper(registry);

        foreach (var definition in registry.Items.Where(item => item.Kind == ComponentKind.Functional))
        {
            Assert.IsTrue(
                mapper.TryCreateDefaultSettings(definition.Metadata.TypeId, out var schemaSettings),
                definition.Metadata.TypeId);
            var componentSettings = definition.CreateDefaultSettings();
            Assert.IsTrue(
                mapper.TryMapToSchema5(componentSettings, out var typeId, out _),
                definition.Metadata.TypeId);
            Assert.AreEqual(definition.Metadata.TypeId, typeId, definition.Metadata.TypeId);
            Assert.IsNotNull(schemaSettings, definition.Metadata.TypeId);
        }
    }

    [TestMethod]
    public void SchemaMapperRejectsARegistryWithMissingFunctionalCodec()
    {
        var registry = new TestComponentRegistry(
            new BuiltInComponentRegistry().Items
                .Append(new UnmappedDefinition())
                .ToArray());

        var exception = Assert.Throws<InvalidOperationException>(() => new Schema5ComponentSettingsMapper(registry));
        StringAssert.Contains(exception.Message, "test.unmapped");
    }

    private sealed class TestComponentRegistry(IReadOnlyList<IComponentDefinition> items) : IComponentRegistry
    {
        public IReadOnlyList<IComponentDefinition> Items { get; } = items;

        public bool TryGet(string typeId, out IComponentDefinition definition)
        {
            definition = Items.FirstOrDefault(item => item.Metadata.TypeId == typeId)!;
            return definition is not null;
        }
    }

    private sealed class UnmappedDefinition : IComponentDefinition
    {
        public ComponentMetadata Metadata { get; } = new(
            "test.unmapped", "test", "test", ComponentCategory.Layout,
            ComponentCapabilities.Display, true, true, true, true, false);
        public ComponentKind Kind => ComponentKind.Functional;
        public IComponentSettings CreateDefaultSettings() => new UnmappedSettings();
        public ComponentMeasureResult Measure(IComponentSettings settings, ComponentMeasureContext context) =>
            new(1, 1, 1, 1, true);
        public IReadOnlyList<ComponentValidationIssue> Validate(IComponentSettings settings) => [];
        public bool IsInteractive(IComponentSettings settings) => false;
    }

    private sealed record UnmappedSettings : IComponentSettings
    {
        public string TypeId => "test.unmapped";
    }
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
    public void BuiltInWidgetCatalogAcceptsTheSharedComponentRegistry()
    {
        var registry = new BuiltInComponentRegistry();
        var catalog = new BuiltInWidgetCatalog(registry);

        Assert.AreEqual(
            registry.Items.Count(definition => definition.Kind == ComponentKind.Functional &&
                definition.Metadata.TypeId is
                    ComponentTypeIds.Artwork or
                    ComponentTypeIds.MediaText or
                    ComponentTypeIds.MediaSource or
                    ComponentTypeIds.PlaybackCommand or
                    ComponentTypeIds.Metrics or
                    ComponentTypeIds.Spectrum or
                    ComponentTypeIds.Separator),
            catalog.Items.Count);
        Assert.IsTrue(catalog.TryGet(ComponentTypeIds.MediaText, out _));
    }

    [TestMethod]
    public void LayoutCatalogRejectsUnknownTypeIdsInsteadOfCreatingFallbackSettings()
    {
        Assert.Throws<ArgumentException>(() => LayoutComponentCatalog.CreateDefaultSettings("unknown.component"));
    }

    [TestMethod]
    public void LayoutGridGeometryServiceMatchesRuntimeFacadeForBodyBounds()
    {
        var profile = LayoutDefaultTemplates.LoadDocument().Horizontal;

        Assert.AreEqual(
            LayoutRuntimeService.CalculateBodyGridBounds(profile),
            LayoutGridGeometryService.CalculateBodyGridBounds(profile));
    }

    [TestMethod]
    public void LayoutProfileSelectorMatchesRuntimeFacade()
    {
        var document = LayoutDefaultTemplates.LoadDocument();

        Assert.AreEqual(
            LayoutRuntimeService.ResolveProfileKey(true),
            LayoutProfileSelector.ResolveProfileKey(true));
        Assert.AreEqual(
            new LayoutRuntimeService().ResolveProfile(document, false),
            LayoutProfileSelector.ResolveProfile(document, false));
    }

    [TestMethod]
    public void LayoutComponentFeatureQueryProjectsEnabledRuntimeCapabilities()
    {
        var profile = LayoutDefaultTemplates.LoadDocument().Horizontal;
        var features = LayoutComponentFeatureQueryService.Resolve(profile);

        Assert.Contains(AFMediaBar.Layout.Models.MetricKind.SystemMemory, features.RequestedMetrics);
        Assert.DoesNotContain(AFMediaBar.Layout.Models.MetricKind.SystemCpu, features.RequestedMetrics);
        Assert.IsFalse(features.SpectrumEnabled);
        Assert.IsTrue(features.OutputDeviceEnabled);
        Assert.IsTrue(features.VolumeEnabled);
        Assert.IsFalse(features.OpenTaskManagerOnClick);
        Assert.AreEqual(2500, features.MinimumMetricRefreshIntervalMilliseconds);
    }

    [TestMethod]
    public void LayoutComponentFeatureQueryReturnsNoMetricIntervalWhenMetricsAreAbsent()
    {
        var profile = LayoutDefaultTemplates.LoadDocument().Horizontal with
        {
            Containers = LayoutDefaultTemplates.LoadDocument().Horizontal.Containers
                .Select(container => container with
                {
                    PrimarySlot = container.PrimarySlot with
                    {
                        Children = container.PrimarySlot.Children
                            .Where(child => child is not LayoutWidgetElement widget ||
                                widget.TypeId != ComponentTypeIds.Metrics)
                            .ToArray()
                    },
                    SecondarySlot = container.SecondarySlot with
                    {
                        Children = container.SecondarySlot.Children
                            .Where(child => child is not LayoutWidgetElement widget ||
                                widget.TypeId != ComponentTypeIds.Metrics)
                            .ToArray()
                    }
                }).ToArray()
        };

        var features = LayoutComponentFeatureQueryService.Resolve(profile);

        Assert.IsEmpty(features.RequestedMetrics);
        Assert.IsNull(features.MinimumMetricRefreshIntervalMilliseconds);
        Assert.IsFalse(features.SpectrumEnabled);
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

    [TestMethod]
    public void SchemaFiveDocumentRoundTripsPolymorphicDiscriminators()
    {
        var document = LayoutDefaultTemplates.LoadDocument();
        var options = CreateSchemaOptions();
        var json = JsonSerializer.Serialize(document, options);
        var restored = JsonSerializer.Deserialize<LayoutDocument>(json, options);

        Assert.IsNotNull(restored);
        Assert.AreEqual(LayoutDocument.CurrentSchemaVersion, restored.SchemaVersion);
        Assert.AreEqual(
            JsonSerializer.Serialize(document, options),
            JsonSerializer.Serialize(restored, options));
        StringAssert.Contains(json, "\"containerKind\": \"static\"");
        StringAssert.Contains(json, "\"type\": \"command\"");
    }

    [TestMethod]
    public void SchemaFiveRejectsUnknownWidgetSettingsDiscriminator()
    {
        var json = """
            {"schemaVersion":5,"horizontal":{"key":"horizontal","layoutMode":"horizontal","surface":{"lengthScalePercent":100,"thicknessScalePercent":100,"gapDip":4,"cornerRadiusDip":6,"sizeToContent":true,"edgeCollapseEnabled":false},"grid":{"columns":48,"rows":24,"cellSizeDip":8},"containers":[],"collapseContainers":[]},"vertical":{"key":"vertical","layoutMode":"vertical","surface":{"lengthScalePercent":100,"thicknessScalePercent":100,"gapDip":4,"cornerRadiusDip":6,"sizeToContent":true,"edgeCollapseEnabled":false},"grid":{"columns":48,"rows":24,"cellSizeDip":8},"containers":[{"instanceId":"c","enabled":true,"geometry":{"margin":{"left":0,"top":0,"right":0,"bottom":0}},"containerKind":"static","orientation":"automatic","contentAlignment":"center","secondaryContentAlignment":"center","trigger":"always","proximityDip":0,"animation":{"enabled":true,"durationMilliseconds":220,"delayMilliseconds":0,"easing":"easeOut"},"primarySlot":{"slotId":"p","children":[{"kind":"widget","instanceId":"w","enabled":true,"geometry":{"margin":{"left":0,"top":0,"right":0,"bottom":0}},"typeId":"builtin.command","settings":{"type":"future-widget","value":1}}]},"secondarySlot":{"slotId":"s","children":[]},"gridBounds":{"x":0,"y":0,"width":4,"height":4}}],"collapseContainers":[]}}
            """;

        Assert.Throws< JsonException>(() => JsonSerializer.Deserialize<LayoutDocument>(json, CreateSchemaOptions()));
    }

    [TestMethod]
    public void SchemaFiveWidgetSettingsDiscriminatorsRoundTripEverySupportedType()
    {
        var settings = new WidgetSettings[]
        {
            new ArtworkWidgetSettings(6, true, false),
            new MediaTextWidgetSettings(MediaTextKind.Title, true, 14, 1),
            new CommandWidgetSettings(MediaCommandKind.PlayPause, 24),
            new MetricsWidgetSettings(AFMediaBar.Layout.Models.MetricKind.SystemCpu, false, 500, [AFMediaBar.Layout.Models.MetricKind.SystemCpu, AFMediaBar.Layout.Models.MetricKind.SystemMemory]),
            new SpectrumWidgetSettings(9, 30, 75),
            new SeparatorWidgetSettings(1, 22)
        };
        var options = CreateSchemaOptions();

        foreach (var original in settings)
        {
            var json = JsonSerializer.Serialize<WidgetSettings>(original, options);
            var restored = JsonSerializer.Deserialize<WidgetSettings>(json, options);

            Assert.IsNotNull(restored, original.GetType().Name);
            Assert.AreEqual(original.GetType(), restored.GetType(), original.GetType().Name);
            Assert.AreEqual(json, JsonSerializer.Serialize<WidgetSettings>(restored, options), original.GetType().Name);
        }
    }

    [TestMethod]
    public void SchemaFiveLayoutElementDiscriminatorsRoundTripWidgetAndContainer()
    {
        var profile = LayoutDefaultTemplates.LoadDocument().Horizontal;
        var widget = profile.Containers
            .SelectMany(container => container.PrimarySlot.Children)
            .OfType<LayoutWidgetElement>()
            .First();
        var elements = new LayoutElement[] { widget, profile.Containers.First() };
        var options = CreateSchemaOptions();

        foreach (var original in elements)
        {
            var json = JsonSerializer.Serialize<LayoutElement>(original, options);
            var restored = JsonSerializer.Deserialize<LayoutElement>(json, options);

            Assert.IsNotNull(restored, original.GetType().Name);
            Assert.AreEqual(original.GetType(), restored.GetType(), original.GetType().Name);
            Assert.AreEqual(json, JsonSerializer.Serialize<LayoutElement>(restored, options), original.GetType().Name);
        }
    }

    private static JsonSerializerOptions CreateSchemaOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
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
