// 验证设置重组后用户默认快照、页面重置和旧入口的边界；每次测试恢复全局设置。
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Models.Settings;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Services.Settings;
using AFMediaBar.Classes.Settings;
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
        current.TaskbarExperience = current.TaskbarExperience with { MediaFontSizePercent = 120 };
        current.Position = TaskbarBarPosition.End;
        current.TaskbarTargetMonitorDeviceIds = ["secondary"];
        current.LyricsEnabled = false;
        current.LyricsCharacterSpacingPercent = 10;
        current.LyricsSource = new LyricsSourceSettings([]);
        var appearance = current.Appearance;

        SettingsManager.ResetContentLayout();

        current = SettingsManager.Current;
        Assert.AreEqual(saved.TaskbarExperience.Normalize() with { MediaFontSizePercent = 120, RestComponentOrder = null },
            current.TaskbarExperience with { RestComponentOrder = null });
        CollectionAssert.AreEqual(saved.TaskbarExperience.RestComponentOrder!.ToArray(), current.TaskbarExperience.RestComponentOrder!.ToArray());
        Assert.AreEqual(20, current.SpectrumComponent.BandCount);
        CollectionAssert.AreEqual(new[] { MetricKind.SystemCpu }, current.PerformanceComponent.Metrics!.ToArray());
        Assert.AreEqual(LyricsTextAlignment.Left, current.LyricsTextAlignment);
        Assert.IsTrue(current.LyricsFixedWidthEnabled);
        Assert.AreEqual(330, current.LyricsFixedWidthDip);
        Assert.AreEqual(appearance, current.Appearance);
        Assert.AreEqual(TaskbarBarPosition.End, current.Position);
        CollectionAssert.AreEqual(new[] { "secondary" }, current.TaskbarTargetMonitorDeviceIds!.ToArray());
        Assert.IsFalse(current.LyricsEnabled);
        Assert.AreEqual(10, current.LyricsCharacterSpacingPercent);
        Assert.AreEqual(0, current.LyricsSource.EnabledSourceIds!.Count);
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

    [TestMethod]
    public void SevenMainPagesAndLegacyDestinationsResolveToActualGroups()
    {
        var pages = SettingsPageCatalog.ForMode(SettingsMode.Taskbar);
        Assert.AreEqual(7, pages.Count(page => !page.IsFooter));
        Assert.AreEqual(2, pages.Count(page => page.IsFooter));
        Assert.AreEqual((SettingsPageKey.Appearance, "Common.Group.Fonts"),
            SettingsPageCatalog.ResolveDestination(SettingsPageKey.ApplicationAppearance, "Common.Group.Fonts"));
        Assert.AreEqual((SettingsPageKey.Components, "Common.RestLayer"),
            SettingsPageCatalog.ResolveDestination(SettingsPageKey.DisplayModes, "Common.RestLayer"));
        Assert.AreEqual((SettingsPageKey.Components, "Appearance.Group.RestLayout"),
            SettingsPageCatalog.ResolveDestination(SettingsPageKey.Lyrics, "Common.Group.LyricsAlignment"));
        Assert.AreEqual((SettingsPageKey.Components, "Common.Group.MediaBarWidth"),
            SettingsPageCatalog.ResolveDestination(SettingsPageKey.Appearance, "Common.Group.MediaBarWidth"));
    }
}
