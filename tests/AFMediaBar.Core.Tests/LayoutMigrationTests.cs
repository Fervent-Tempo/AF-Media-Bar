using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using AFMediaBar.Models;
using AFMediaBar.Services;

namespace AFMediaBar.Core.Tests;

[TestClass]
public sealed class LayoutMigrationTests
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    [TestMethod]
    public void CreateFromLegacy_ProducesCurrentHorizontalAndVerticalProfiles()
    {
        var document = CreateDefaultDocument();

        Assert.AreEqual(LayoutDocument.CurrentSchemaVersion, document.SchemaVersion);
        Assert.AreEqual(LayoutProfileKey.Horizontal, document.Horizontal.Key);
        Assert.AreEqual(PlayerLayoutMode.Horizontal, document.Horizontal.LayoutMode);
        Assert.AreEqual(LayoutProfileKey.Vertical, document.Vertical.Key);
        Assert.AreEqual(PlayerLayoutMode.Vertical, document.Vertical.LayoutMode);
        Assert.IsNotEmpty(document.Horizontal.Containers);
        Assert.IsNotEmpty(document.Vertical.Containers);
        Assert.IsTrue(LayoutGridConstraintService.IsProfileValid(document.Horizontal));
        Assert.IsTrue(LayoutGridConstraintService.IsProfileValid(document.Vertical));
    }

    [TestMethod]
    public void Normalize_ClampsSpectrumAndSurfaceValues()
    {
        var document = CreateDefaultDocument();
        var container = CreatePlacedContainer(LayoutContainerKind.Static);
        var spectrum = new LayoutWidgetElement(
            "spectrum-test",
            true,
            LayoutGeometry.Auto,
            BuiltInWidgetTypeIds.Spectrum,
            new SpectrumWidgetSettings(99, 100, 900));
        Assert.IsTrue(LayoutEditorService.TryAddWidget(
            document.Horizontal with { Containers = [container] },
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
    public void Normalize_ClearsLegacyDipSizeOverrides()
    {
        var document = CreateDefaultDocument();
        var container = CreatePlacedContainer(LayoutContainerKind.Static);
        var widget = new LayoutWidgetElement(
            "command-override",
            true,
            LayoutGeometry.Auto with
            {
                WidthDip = 120,
                HeightDip = 48
            },
            BuiltInWidgetTypeIds.Command,
            new CommandWidgetSettings(MediaCommandKind.PlayPause, 36));
        Assert.IsTrue(LayoutEditorService.TryAddWidget(
            document.Horizontal with { Containers = [container] },
            container.InstanceId,
            LayoutSlotKind.Primary,
            widget,
            out var profile,
            out _));

        var normalized = LayoutMigrationService.Normalize(document with { Horizontal = profile });
        var restored = LayoutRuntimeService.FindWidgets(
                normalized.Horizontal,
                BuiltInWidgetTypeIds.Command)
            .Single();

        Assert.IsNull(restored.Geometry.WidthDip);
        Assert.IsNull(restored.Geometry.HeightDip);
        Assert.IsNotNull(restored.GridBounds);
    }

    [TestMethod]
    public void LayoutDocument_RoundTripsPolymorphicWidgetSettings()
    {
        var source = CreateDefaultDocument();

        var json = JsonSerializer.Serialize(source, JsonOptions);
        var restored = JsonSerializer.Deserialize<LayoutDocument>(json, JsonOptions);

        Assert.IsNotNull(restored);
        Assert.AreEqual(LayoutDocument.CurrentSchemaVersion, restored.SchemaVersion);
        Assert.IsTrue(LayoutRuntimeService.ContainsWidget(
            restored.Horizontal,
            BuiltInWidgetTypeIds.MediaText));
        Assert.IsTrue(LayoutGridConstraintService.IsProfileValid(restored.Horizontal));
    }

    [TestMethod]
    public void Normalize_UnknownSkinFallsBackWithoutDisablingWidget()
    {
        var document = CreateDefaultDocument();
        var container = CreatePlacedContainer(LayoutContainerKind.Static);
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
            document.Horizontal with { Containers = [container] },
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
        var container = CreatePlacedContainer(LayoutContainerKind.Static);
        Assert.IsTrue(LayoutEditorService.TryAddWidget(
            source.Horizontal with { Containers = [container] },
            container.InstanceId,
            LayoutSlotKind.Primary,
            command,
            out var profile,
            out var failure), failure.ToString());

        var json = JsonSerializer.Serialize(source with { Horizontal = profile }, JsonOptions);
        var restored = JsonSerializer.Deserialize<LayoutDocument>(json, JsonOptions);
        var restoredCommand = LayoutRuntimeService.FindWidgets(
                restored!.Horizontal,
                BuiltInWidgetTypeIds.Command)
            .Single();

        Assert.AreEqual(ComponentSkinCatalog.ExampleSkinId, restoredCommand.SkinId);
        Assert.AreEqual(1, restoredCommand.SkinVersion);
        Assert.AreEqual("true", restoredCommand.SkinSettings!["emphasis"]);
    }

    // ---------- schema 3 → 4 ----------

    [TestMethod]
    public void Schema3Fixture_MigratesToValidSchema4()
    {
        var migrated = LayoutMigrationService.MigrateSchema3To4(CreateSchema3Fixture());

        Assert.AreEqual(LayoutDocument.CurrentSchemaVersion, migrated.SchemaVersion);
        Assert.IsTrue(LayoutGridConstraintService.IsProfileValid(migrated.Horizontal));
        Assert.IsTrue(LayoutGridConstraintService.IsProfileValid(migrated.Vertical));
        Assert.IsNotEmpty(migrated.Horizontal.Containers);
        Assert.IsNotEmpty(migrated.Vertical.Containers);
        Assert.IsTrue(LayoutRuntimeService.ContainsWidget(
            migrated.Horizontal,
            BuiltInWidgetTypeIds.Artwork));
    }

    [TestMethod]
    public void Schema3Fixture_RoundTripsThroughRealJson()
    {
        var fixture = CreateSchema3Fixture();
        var json = JsonSerializer.Serialize(fixture, JsonOptions);
        var restored = JsonSerializer.Deserialize<Schema3LayoutDocument>(json, JsonOptions);
        Assert.IsNotNull(restored);

        var migrated = LayoutMigrationService.MigrateSchema3To4(restored);

        Assert.AreEqual(LayoutDocument.CurrentSchemaVersion, migrated.SchemaVersion);
        Assert.IsTrue(LayoutGridConstraintService.IsProfileValid(migrated.Horizontal));
        Assert.IsTrue(LayoutGridConstraintService.IsProfileValid(migrated.Vertical));
        Assert.IsTrue(LayoutRuntimeService.ContainsWidget(
            migrated.Horizontal,
            BuiltInWidgetTypeIds.Command));
    }

    [TestMethod]
    public void Schema3Fixture_MigrationIsDeterministic()
    {
        var fixture = CreateSchema3Fixture();

        var first = LayoutMigrationService.MigrateSchema3To4(fixture);
        var second = LayoutMigrationService.MigrateSchema3To4(fixture);
        var json1 = JsonSerializer.Serialize(first, JsonOptions);
        var json2 = JsonSerializer.Serialize(second, JsonOptions);

        Assert.AreEqual(json1, json2);
    }

    [TestMethod]
    public void Schema3Fixture_WithEdgeContainer_MigratesToAnchorCollapse()
    {
        var fixture = CreateSchema3Fixture();
        var edge = new Schema3EdgeContainer(
            "edge-1",
            true,
            LayoutEdge.Right,
            0,
            6,
            72,
            LayoutAnimationSettings.Default,
            new Schema3Slot("expanded", [
                new Schema3WidgetElement(
                    "volume",
                    true,
                    LayoutGeometry.Auto,
                    BuiltInWidgetTypeIds.Command,
                    new CommandWidgetSettings(MediaCommandKind.AdjustVolume, 36))
            ]));
        fixture = fixture with
        {
            Horizontal = fixture.Horizontal with { EdgeContainers = [edge] }
        };

        var migrated = LayoutMigrationService.MigrateSchema3To4(fixture);

        Assert.HasCount(1, migrated.Horizontal.CollapseContainers);
        var collapse = migrated.Horizontal.CollapseContainers[0];
        Assert.AreEqual("edge-1", collapse.InstanceId);
        Assert.IsTrue(LayoutGridConstraintService.IsProfileValid(migrated.Horizontal));
        Assert.IsNotNull(
            LayoutGridConstraintService.FindContainer(migrated.Horizontal, collapse.Attachment.AnchorContainerId));
        Assert.AreEqual(LayoutEdge.Right, collapse.Attachment.AttachmentSide);
    }

    // ---------- schema 1/2 → 4 ----------

    [TestMethod]
    public void LegacySchema2_MigratesThroughTo4()
    {
        var fixture = CreateSchema3Fixture();
        var legacy = new LegacyLayoutDocument(
            2,
            fixture.Horizontal,
            fixture.Vertical,
            fixture.Horizontal,
            fixture.Vertical);

        var schema3 = LayoutMigrationService.MigrateLegacyDocument(legacy, WindowHostMode.Taskbar);
        var migrated = LayoutMigrationService.MigrateSchema3To4(schema3);

        Assert.AreEqual(LayoutDocument.CurrentSchemaVersion, migrated.SchemaVersion);
        Assert.IsTrue(LayoutGridConstraintService.IsProfileValid(migrated.Horizontal));
        Assert.IsTrue(LayoutGridConstraintService.IsProfileValid(migrated.Vertical));
    }

    // ---------- 未来版本与失败回退 ----------

    [TestMethod]
    public void FutureSchema_NormalizeIsRejected()
    {
        var document = CreateDefaultDocument() with { SchemaVersion = 99 };
        Assert.ThrowsExactly<InvalidDataException>(() =>
            LayoutMigrationService.Normalize(document));
    }

    [TestMethod]
    public void InvalidMigration_ValidationThrows()
    {
        // 只有折叠容器、没有非折叠锚点容器时，迁移后的全量验证必须抛错，触发调用方保留原文件。
        var fixture = CreateSchema3Fixture();
        fixture = fixture with
        {
            Horizontal = new Schema3Profile(
                LayoutProfileKey.Horizontal,
                PlayerLayoutMode.Horizontal,
                fixture.Horizontal.Surface,
                [],
                [
                    new Schema3EdgeContainer(
                        "broken-edge",
                        true,
                        LayoutEdge.Right,
                        0,
                        6,
                        72,
                        LayoutAnimationSettings.Default,
                        new Schema3Slot("expanded", []))
                ])
        };

        Assert.ThrowsExactly<InvalidDataException>(() =>
            LayoutMigrationService.MigrateSchema3To4(fixture));
    }

    // ---------- 尺寸 ----------

    [TestMethod]
    public void MigratedProfile_DesiredSizeComesFromGridUnion()
    {
        var migrated = LayoutMigrationService.MigrateSchema3To4(CreateSchema3Fixture());
        var size = LayoutRuntimeService.CalculateDesiredSize(migrated.Horizontal);
        var grid = LayoutGridSettings.Normalize(migrated.Horizontal.Grid);

        Assert.IsGreaterThan(0, size.WidthDip);
        Assert.IsGreaterThan(0, size.HeightDip);
        // 横向档案的联合边界高度不应等于整个编辑画布高度。
        // IsLessThan(upperBound, value) 断言 value < upperBound。
        Assert.IsLessThan((double)grid.Rows * grid.CellSizeDip, size.HeightDip);
    }

    [TestMethod]
    public void MigratedProfile_UnionStartsAtOriginWithoutLeadingBlank()
    {
        var migrated = LayoutMigrationService.MigrateSchema3To4(CreateSchema3Fixture());
        var union = LayoutRuntimeService.CalculateBodyGridBounds(migrated.Horizontal);

        Assert.IsNotNull(union);
        Assert.AreEqual(0, union!.X);
        Assert.AreEqual(0, union.Y);
    }

    // ---------- 辅助 ----------

    private static LayoutDocument CreateDefaultDocument() =>
        LayoutMigrationService.CreateFromLegacy(WindowSettings.Default, MetricSettings.Default);

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }

    private static LayoutContainerElement CreatePlacedContainer(LayoutContainerKind kind) =>
        LayoutEditorService.CreateContainer(kind) with
        {
            GridBounds = new LayoutGridRect(0, 0, 24, 8)
        };

    private static Schema3LayoutDocument CreateSchema3Fixture()
    {
        var horizontal = new Schema3Profile(
            LayoutProfileKey.Horizontal,
            PlayerLayoutMode.Horizontal,
            LayoutSurfaceSettings.Default with { EdgeCollapseEnabled = false },
            [
                new Schema3ContainerElement(
                    "always-leading",
                    true,
                    LayoutGeometry.Auto,
                    LayoutContainerKind.Static,
                    LayoutFlowOrientation.Automatic,
                    LayoutContentAlignment.Center,
                    LayoutContentAlignment.Center,
                    LayoutTriggerMode.Always,
                    0,
                    new LayoutAnimationSettings(false, 0, 0, LayoutEasingKind.Linear),
                    new Schema3Slot("content", [
                        new Schema3WidgetElement(
                            "artwork",
                            true,
                            LayoutGeometry.Auto,
                            BuiltInWidgetTypeIds.Artwork,
                            new ArtworkWidgetSettings(4, false, true))
                    ]),
                    Schema3Slot.Empty("unused"),
                    Schema3Slot.Empty("legacy-collapsed")),
                new Schema3ContainerElement(
                    "media-interaction",
                    true,
                    LayoutGeometry.Auto,
                    LayoutContainerKind.HoverSwitch,
                    LayoutFlowOrientation.Horizontal,
                    LayoutContentAlignment.Center,
                    LayoutContentAlignment.Center,
                    LayoutTriggerMode.PointerNear,
                    0,
                    LayoutAnimationSettings.Default,
                    new Schema3Slot("idle", [
                        new Schema3WidgetElement(
                            "title",
                            true,
                            LayoutGeometry.Auto,
                            BuiltInWidgetTypeIds.MediaText,
                            new MediaTextWidgetSettings(MediaTextKind.Title, true, 14, 1))
                    ]),
                    new Schema3Slot("active", [
                        new Schema3WidgetElement(
                            "previous",
                            true,
                            LayoutGeometry.Auto,
                            BuiltInWidgetTypeIds.Command,
                            new CommandWidgetSettings(MediaCommandKind.Previous, 36)),
                        new Schema3WidgetElement(
                            "play-pause",
                            true,
                            LayoutGeometry.Auto,
                            BuiltInWidgetTypeIds.Command,
                            new CommandWidgetSettings(MediaCommandKind.PlayPause, 36)),
                        new Schema3WidgetElement(
                            "next",
                            true,
                            LayoutGeometry.Auto,
                            BuiltInWidgetTypeIds.Command,
                            new CommandWidgetSettings(MediaCommandKind.Next, 36))
                    ]),
                    Schema3Slot.Empty("collapsed"))
            ],
            []);
        var vertical = horizontal with
        {
            Key = LayoutProfileKey.Vertical,
            LayoutMode = PlayerLayoutMode.Vertical
        };
        return new Schema3LayoutDocument(3, horizontal, vertical);
    }
}