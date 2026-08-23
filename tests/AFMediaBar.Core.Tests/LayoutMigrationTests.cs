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

    private static LayoutDocument CreateDefaultDocument() =>
        LayoutMigrationService.CreateFromLegacy(WindowSettings.Default, MetricSettings.Default);
}
