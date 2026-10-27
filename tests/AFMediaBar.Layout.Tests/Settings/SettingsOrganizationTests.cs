// 验证设置重组后用户默认快照、页面重置和旧入口的边界；每次测试恢复全局设置。
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Models.Settings;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Services.Settings;
using AFMediaBar.Classes.Settings;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>防止重置一页误改其他页或旧快捷入口失效。</summary>
[TestClass]
[DoNotParallelize]
public sealed class SettingsOrganizationTests
{
    private AppSettings _previous = null!;
    private AppSettings? _previousDefaults;

    [TestInitialize]
    public void Initialize()
    {
        _previous = SettingsManager.Current;
        _previousDefaults = SettingsManager.UserDefaults;
        SettingsManager.SetUserDefaults(null);
        SettingsManager.Current = new AppSettings();
    }

    [TestCleanup]
    public void Cleanup()
    {
        SettingsManager.SetUserDefaults(_previousDefaults);
        SettingsManager.Current = _previous;
    }

    [TestMethod]
    public void ContentResetUsesSnapshotAndPreservesAppearancePlacementAndLyricProcessing()
    {
        var saved = new AppSettings
        {
            TaskbarExperience = TaskbarExperienceSettings.Default with
            {
                SpectrumVisible = false,
                PerformanceVisible = false,
                ArtworkProgressVisible = false,
                MediaTextAlignment = TaskbarMediaTextAlignment.Right,
                RestComponentOrder = [TaskbarRestComponent.Volume, TaskbarRestComponent.Spectrum],
                FixedLengthDip = 420
            },
            SpectrumComponent = SpectrumComponentSettings.Default with { BandCount = 20 },
            PerformanceComponent = PerformanceComponentSettings.Default with { Metrics = [MetricKind.SystemCpu] },
            LyricsTextAlignment = LyricsTextAlignment.Left,
            LyricsFixedWidthEnabled = true,
            LyricsFixedWidthDip = 330
        };
        SettingsManager.SetUserDefaults(saved);
        var current = SettingsManager.Current;
        current.Appearance = current.Appearance with { FontWeight = 700, TaskbarFrostedStyle = TaskbarFrostedStyle.Warm };
        current.TaskbarExperience = current.TaskbarExperience with { MediaFontSizePercent = 120, ArtworkProgressVisible = true };
        current.Position = TaskbarBarPosition.End;
        current.TaskbarTargetMonitorDeviceIds = ["secondary"];
        current.LyricsEnabled = false;
        current.LyricsCharacterSpacingPercent = 10;
        current.LyricsSource = new LyricsSourceSettings([]);
        current.Interaction = current.Interaction with { ArtworkHoverMode = ArtworkHoverMode.Zoom };
        var appearance = current.Appearance;
        var spectrum = current.SpectrumComponent;
        var performance = current.PerformanceComponent;
        current.BluetoothBatteryWidget = current.BluetoothBatteryWidget with { LastKnownName = "Keep me" };
        var battery = current.BluetoothBatteryWidget;

        SettingsManager.ResetContentLayout();

        current = SettingsManager.Current;
        Assert.AreEqual(saved.TaskbarExperience.Normalize() with { MediaFontSizePercent = 120, RestComponentOrder = null, SpectrumVisible = true, PerformanceVisible = true },
            current.TaskbarExperience with { RestComponentOrder = null });
        CollectionAssert.AreEqual(saved.TaskbarExperience.RestComponentOrder!.ToArray(), current.TaskbarExperience.RestComponentOrder!.ToArray());
        Assert.AreEqual(spectrum, current.SpectrumComponent);
        Assert.AreEqual(JsonSerializer.Serialize(performance), JsonSerializer.Serialize(current.PerformanceComponent));
        Assert.AreEqual(battery, current.BluetoothBatteryWidget);
        Assert.AreEqual(LyricsTextAlignment.Left, current.LyricsTextAlignment);
        Assert.IsTrue(current.LyricsFixedWidthEnabled);
        Assert.AreEqual(330, current.LyricsFixedWidthDip);
        Assert.AreEqual(appearance, current.Appearance);
        Assert.AreEqual(TaskbarBarPosition.End, current.Position);
        CollectionAssert.AreEqual(new[] { "secondary" }, current.TaskbarTargetMonitorDeviceIds!.ToArray());
        Assert.IsFalse(current.LyricsEnabled);
        Assert.AreEqual(10, current.LyricsCharacterSpacingPercent);
        Assert.AreEqual(0, current.LyricsSource.EnabledSourceIds!.Count);
        Assert.AreEqual(ArtworkHoverMode.Zoom, current.Interaction.ArtworkHoverMode);
    }

    [TestMethod]
    public void WidgetResetRestoresSnapshotWithoutChangingLayoutOrDeviceRouting()
    {
        var saved = new AppSettings
        {
            SpectrumComponent = SpectrumComponentSettings.Default with { CaptureDeviceId = "saved-endpoint", CaptureDeviceName = "Saved" },
            PerformanceComponent = PerformanceComponentSettings.Default with { RefreshIntervalMilliseconds = 4000 },
            BluetoothBatteryWidget = new() { Enabled = true, DeviceId = "headphones", LastKnownName = "Headphones" },
            TaskbarExperience = TaskbarExperienceSettings.Default with { SpectrumVisible = false, PerformanceVisible = false }
        };
        SettingsManager.SetUserDefaults(saved);
        SettingsManager.Current.TaskbarExperience = SettingsManager.Current.TaskbarExperience with
        { ComponentSpacingDip = 18, MediaFontSizePercent = 120 };
        SettingsManager.Current.AudioDevices = new() { SwitchOutputCommunications = true };
        var layout = SettingsManager.Current.TaskbarExperience;
        var devices = SettingsManager.Current.AudioDevices;

        SettingsManager.ResetComponents();

        Assert.AreEqual(saved.SpectrumComponent, SettingsManager.Current.SpectrumComponent);
        Assert.AreEqual(JsonSerializer.Serialize(saved.PerformanceComponent.Normalize()), JsonSerializer.Serialize(SettingsManager.Current.PerformanceComponent));
        Assert.AreEqual(saved.BluetoothBatteryWidget.Normalize(), SettingsManager.Current.BluetoothBatteryWidget);
        Assert.AreEqual(layout with { SpectrumVisible = false, PerformanceVisible = false }, SettingsManager.Current.TaskbarExperience);
        Assert.AreEqual(JsonSerializer.Serialize(devices), JsonSerializer.Serialize(SettingsManager.Current.AudioDevices));
        SettingsManager.ResetDeviceManagement();
        Assert.AreEqual(saved.BluetoothBatteryWidget.Normalize(), SettingsManager.Current.BluetoothBatteryWidget);
    }

    [TestMethod]
    public void LyricAndAppearanceResetsPreserveMovedLayoutFields()
    {
        var current = SettingsManager.Current;
        current.TaskbarExperience = current.TaskbarExperience with
        {
            ContentLayout = TaskbarContentLayout.CompactInline,
            MediaTextAlignment = TaskbarMediaTextAlignment.Right,
            ComponentSpacingDip = 18,
            SpectrumVisible = false,
            MediaFontSizePercent = 120
        };
        current.LyricsTextAlignment = LyricsTextAlignment.Right;
        current.LyricsFixedWidthEnabled = true;
        current.LyricsFixedWidthDip = 350;
        var layout = current.TaskbarExperience;
        SettingsManager.ResetLyrics();
        Assert.AreEqual(LyricsTextAlignment.Right, SettingsManager.Current.LyricsTextAlignment);
        Assert.IsTrue(SettingsManager.Current.LyricsFixedWidthEnabled);
        Assert.AreEqual(350, SettingsManager.Current.LyricsFixedWidthDip);
        SettingsManager.ResetApplicationAppearance();
        SettingsManager.ResetTaskbarAppearance();
        Assert.AreEqual(layout with { MediaFontSizePercent = TaskbarExperienceSettings.Default.MediaFontSizePercent }, SettingsManager.Current.TaskbarExperience);
    }

    [TestMethod]
    public void PlacementResetPreservesContentAndIndependentArrangement()
    {
        var current = SettingsManager.Current;
        current.TaskbarExperience = current.TaskbarExperience with { Arrangement = TaskbarContentArrangement.Right, HoverLayerEnabled = false };
        var content = current.TaskbarExperience;
        current.Position = TaskbarBarPosition.End;
        current.TaskbarBarManualPadding = 70;
        current.TaskbarBarCrossAxisOffsetDip = 12;
        SettingsManager.ResetScreenAndPlacement();
        Assert.AreEqual(new AppSettings().Position, SettingsManager.Current.Position);
        Assert.AreEqual(0, SettingsManager.Current.TaskbarBarManualPadding);
        Assert.AreEqual(0, SettingsManager.Current.TaskbarBarCrossAxisOffsetDip);
        Assert.AreEqual(content, SettingsManager.Current.TaskbarExperience);
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void DisplayModeResetRestoresArtworkSettingsAndPreservesContentLayout(bool useUserDefaults)
    {
        var defaults = new AppSettings();
        if (useUserDefaults)
        {
            defaults.TaskbarExperience = defaults.TaskbarExperience with
            {
                ArtworkVisible = false
            };
            SettingsManager.SetUserDefaults(defaults);
        }

        SettingsManager.Current.TaskbarExperience = (SettingsManager.Current.TaskbarExperience with
        {
            ArtworkVisible = !defaults.TaskbarExperience.ArtworkVisible,
            ContentLayout = TaskbarContentLayout.CompactInline,
            MediaTextAlignment = TaskbarMediaTextAlignment.Right,
            MediaFontSizePercent = 120,
            ComponentSpacingDip = 18,
            RestComponentOrder = [TaskbarRestComponent.Volume, TaskbarRestComponent.MediaText]
        }).Normalize();
        var before = SettingsManager.Current.TaskbarExperience;
        SettingsManager.Current.Interaction = SettingsManager.Current.Interaction with { ArtworkHoverMode = ArtworkHoverMode.Zoom };

        SettingsManager.ResetDisplayModes();

        var actual = SettingsManager.Current.TaskbarExperience;
        Assert.AreEqual(defaults.TaskbarExperience.ArtworkVisible, actual.ArtworkVisible);
        Assert.AreEqual(ArtworkHoverMode.Zoom, SettingsManager.Current.Interaction.ArtworkHoverMode);
        Assert.AreEqual(before.ContentLayout, actual.ContentLayout);
        Assert.AreEqual(before.MediaTextAlignment, actual.MediaTextAlignment);
        Assert.AreEqual(before.MediaFontSizePercent, actual.MediaFontSizePercent);
        Assert.AreEqual(before.ComponentSpacingDip, actual.ComponentSpacingDip);
        CollectionAssert.AreEqual(before.RestComponentOrder!.ToArray(), actual.RestComponentOrder!.ToArray());
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void InteractionResetRestoresArtworkHoverModeAndPreservesArtworkVisibility(bool useUserDefaults)
    {
        var defaults = new AppSettings();
        if (useUserDefaults)
        {
            defaults.Interaction = defaults.Interaction with { ArtworkHoverMode = ArtworkHoverMode.Preview };
            SettingsManager.SetUserDefaults(defaults);
        }
        SettingsManager.Current.Interaction = SettingsManager.Current.Interaction with { ArtworkHoverMode = ArtworkHoverMode.Zoom };
        SettingsManager.Current.TaskbarExperience = SettingsManager.Current.TaskbarExperience with { ArtworkVisible = false };

        SettingsManager.ResetInteraction();

        Assert.AreEqual(defaults.Interaction.ArtworkHoverMode, SettingsManager.Current.Interaction.ArtworkHoverMode);
        Assert.IsFalse(SettingsManager.Current.TaskbarExperience.ArtworkVisible);
    }

    [TestMethod]
    public void ArtworkHoverModeKeepsNoZoomAndDefaultsToSlightZoom()
    {
        // 旧设置缺字段时仍是轻微放大（1.1×），新增选项不能改掉这个默认语义。
        Assert.AreEqual(ArtworkHoverMode.Off, new AppSettings().Interaction.ArtworkHoverMode);
        // 「不放大」要能通过保存前的归一化，不能被枚举校验打回默认。
        var settings = new AppSettings().Interaction with { ArtworkHoverMode = ArtworkHoverMode.None };
        Assert.AreEqual(ArtworkHoverMode.None, settings.Normalize().ArtworkHoverMode);
    }
}
