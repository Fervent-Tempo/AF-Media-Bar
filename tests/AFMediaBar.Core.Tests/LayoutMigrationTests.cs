using System.Text.Json;
using System.Text.Json.Serialization;
using AFMediaBar.Models;
using AFMediaBar.Services;

namespace AFMediaBar.Core.Tests;

[TestClass]
public sealed class LayoutMigrationTests
{
    [TestMethod]
    public void CreateFromLegacy_ProducesCurrentHorizontalAndVerticalProfiles()
    {
        var document = CreateDefaultDocument();

        Assert.AreEqual(LayoutDocument.CurrentSchemaVersion, document.SchemaVersion);
        Assert.AreEqual(LayoutProfileKey.Horizontal, document.Horizontal.Key);
        Assert.AreEqual(PlayerLayoutMode.Horizontal, document.Horizontal.LayoutMode);
        Assert.AreEqual(LayoutProfileKey.Vertical, document.Vertical.Key);
        Assert.AreEqual(PlayerLayoutMode.Vertical, document.Vertical.LayoutMode);
        Assert.IsNotEmpty(document.Horizontal.InlineContainers);
        Assert.IsNotEmpty(document.Vertical.InlineContainers);
    }

    [TestMethod]
    public void Normalize_ClampsSpectrumAndSurfaceValues()
    {
        var document = CreateDefaultDocument();
        var container = LayoutEditorService.CreateInlineContainer(LayoutContainerKind.Static);
        var spectrum = new LayoutWidgetElement(
            "spectrum-test",
            true,
            LayoutGeometry.Auto,
            BuiltInWidgetTypeIds.Spectrum,
            new SpectrumWidgetSettings(99, 100, 900));
        Assert.IsTrue(LayoutEditorService.TryAddWidget(
            document.Horizontal with { InlineContainers = [container] },
            container.InstanceId,
            LayoutSlotKind.Primary,
            spectrum,
            out var profile,
            out var failure), failure.ToString());

        var normalized = LayoutMigrationService.Normalize(document with
        {
            Horizontal = profile with
            {
                Surface = profile.Surface with
                {
                    LengthScalePercent = 1,
                    ThicknessScalePercent = 999
                }
            }
        });
        var normalizedSpectrum = LayoutRuntimeService.FindWidgets(
                normalized.Horizontal,
                BuiltInWidgetTypeIds.Spectrum)
            .Single();
        var settings = (SpectrumWidgetSettings)normalizedSpectrum.Settings;

        Assert.AreEqual(70, normalized.Horizontal.Surface.LengthScalePercent);
        Assert.AreEqual(125, normalized.Horizontal.Surface.ThicknessScalePercent);
        Assert.AreEqual(SpectrumWidgetSettings.MaximumBandCount, settings.BandCount);
        Assert.AreEqual(30, settings.RefreshRateHz);
        Assert.AreEqual(400, settings.SensitivityPercent);
    }

    [TestMethod]
    public void LayoutDocument_RoundTripsPolymorphicWidgetSettings()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        var source = CreateDefaultDocument();

        var json = JsonSerializer.Serialize(source, options);
        var restored = JsonSerializer.Deserialize<LayoutDocument>(json, options);

        Assert.IsNotNull(restored);
        Assert.AreEqual(LayoutDocument.CurrentSchemaVersion, restored.SchemaVersion);
        Assert.IsTrue(LayoutRuntimeService.ContainsWidget(
            restored.Horizontal,
            BuiltInWidgetTypeIds.MediaText));
    }

    [TestMethod]
    public void Normalize_UnknownSkinFallsBackWithoutDisablingWidget()
    {
        var document = CreateDefaultDocument();
        var container = LayoutEditorService.CreateInlineContainer(LayoutContainerKind.Static);
        var widget = new LayoutWidgetElement(
            "play-pause-skin",
            true,
            LayoutGeometry.Auto,
            BuiltInWidgetTypeIds.Command,
            new CommandWidgetSettings(MediaCommandKind.PlayPause, 36),
            "missing.skin",
            99,
            new Dictionary<string, string> { ["accent"] = "invalid" });
        Assert.IsTrue(LayoutEditorService.TryAddWidget(
            document.Horizontal with { InlineContainers = [container] },
            container.InstanceId,
            LayoutSlotKind.Primary,
            widget,
            out var profile,
            out var failure), failure.ToString());

        var normalized = LayoutMigrationService.Normalize(document with { Horizontal = profile });
        var restored = LayoutRuntimeService.FindWidgets(
            normalized.Horizontal,
            BuiltInWidgetTypeIds.Command).Single();

        Assert.IsTrue(restored.Enabled);
        Assert.IsNull(restored.SkinId);
        Assert.IsNull(restored.SkinVersion);
        Assert.IsNull(restored.SkinSettings);
    }

    [TestMethod]
    public void Normalize_IncompatibleSkinVersionFallsBack()
    {
        var assignment = ComponentSkinCatalog.Normalize(
            BuiltInWidgetTypeIds.Command,
            ComponentSkinCatalog.ExampleSkinId,
            2,
            null);

        Assert.IsNull(assignment);
    }

    [TestMethod]
    public void SkinCatalog_RequirementsAreProvidedByDefaultGlobalTheme()
    {
        var tokens = GlobalTheme.Default.SemanticTokens.ToHashSet(StringComparer.Ordinal);

        foreach (var definition in ComponentSkinCatalog.All)
        {
            Assert.IsTrue(
                definition.RequiredSemanticTokens.All(tokens.Contains),
                definition.SkinId);
        }
    }

    [TestMethod]
    public void LayoutDocument_RoundTripsSupportedSkinAssignment()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        var source = CreateDefaultDocument();
        var command = new LayoutWidgetElement(
            "play-pause-skin",
            true,
            LayoutGeometry.Auto,
            BuiltInWidgetTypeIds.Command,
            new CommandWidgetSettings(MediaCommandKind.PlayPause, 36),
            ComponentSkinCatalog.ExampleSkinId,
            1,
            new Dictionary<string, string> { ["emphasis"] = "true" });
        var container = LayoutEditorService.CreateInlineContainer(LayoutContainerKind.Static);
        Assert.IsTrue(LayoutEditorService.TryAddWidget(
            source.Horizontal with { InlineContainers = [container] },
            container.InstanceId,
            LayoutSlotKind.Primary,
            command,
            out var profile,
            out var failure), failure.ToString());

        var json = JsonSerializer.Serialize(source with { Horizontal = profile }, options);
        var restored = JsonSerializer.Deserialize<LayoutDocument>(json, options);
        var restoredCommand = LayoutRuntimeService.FindWidgets(
                restored!.Horizontal,
                BuiltInWidgetTypeIds.Command)
            .Single();

        Assert.AreEqual(ComponentSkinCatalog.ExampleSkinId, restoredCommand.SkinId);
        Assert.AreEqual(1, restoredCommand.SkinVersion);
        Assert.AreEqual("true", restoredCommand.SkinSettings!["emphasis"]);
    }

    private static LayoutDocument CreateDefaultDocument() =>
        LayoutMigrationService.CreateFromLegacy(WindowSettings.Default, MetricSettings.Default);
}
