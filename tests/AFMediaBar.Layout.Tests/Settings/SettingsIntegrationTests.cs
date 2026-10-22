// 验证背景层与应用外观互不覆盖，以及合并后新增歌词选项仍受上下文保护；测试结束恢复全局设置。
using System.Text.Json;
using AFMediaBar.Classes.Models.Settings;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Services.Localization;
using AFMediaBar.Classes.Services.Settings;
using AFMediaBar.Classes.Settings;
using AFMediaBar.ViewModels.Pages;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>三个 PR 的设置职责、重置与兼容状态回归。</summary>
[TestClass]
[DoNotParallelize]
public sealed class SettingsIntegrationTests
{
    [TestMethod]
    public void ChineseConversionOnlyRefreshesRelatedBindingsAndHonorsInactiveContext()
    {
        StaTest.Run(_ =>
        {
            var previous = SettingsManager.Current;
            using var context = new SettingsPageContext();
            using var localization = new LocalizationService();
            try
            {
                SettingsManager.Current = new AppSettings();
                using var viewModel = new LyricsViewModel(localization, new LegacySettingsConfiguration(context));
                var notifications = new List<string?>();
                viewModel.PropertyChanged += (_, e) => notifications.Add(e.PropertyName);
                viewModel.ChineseConversion = LyricsChineseConversionMode.TraditionalToSimplified;
                CollectionAssert.AreEquivalent(new[] { nameof(viewModel.ChineseConversion) }, notifications);
                notifications.Clear();
                viewModel.ChineseConversion = viewModel.ChineseConversion;
                Assert.AreEqual(0, notifications.Count);
                context.Deactivate();
                viewModel.ChineseConversion = LyricsChineseConversionMode.SimplifiedToTraditional;
                Assert.AreEqual(LyricsChineseConversionMode.TraditionalToSimplified, SettingsManager.Current.LyricsChineseConversion);
                Assert.AreEqual(0, notifications.Count);
            }
            finally { SettingsManager.Current = previous; }
            return Task.CompletedTask;
        });
    }

    [TestMethod]
    public void LongLineScrollIsOnlyConfigurableWhileTheBarFollowsContent()
    {
        StaTest.Run(_ =>
        {
            var previous = SettingsManager.Current;
            using var context = new SettingsPageContext();
            using var localization = new LocalizationService();
            try
            {
                SettingsManager.Current = new AppSettings();
                using var viewModel = new LyricsViewModel(localization, new LegacySettingsConfiguration(context));
                Assert.IsTrue(viewModel.CanConfigureLongLineScroll, "默认「随内容变化」，长歌词动态滚动应当可操作。");
                SettingsManager.Current.TaskbarExperience = SettingsManager.Current.TaskbarExperience with { LengthMode = TaskbarLengthMode.Fixed };
                Assert.IsFalse(viewModel.CanConfigureLongLineScroll, "固定长度下封顶没有落点，开关必须置灰。");
                SettingsManager.Current.TaskbarExperience = SettingsManager.Current.TaskbarExperience with { LengthMode = TaskbarLengthMode.FollowContent };
                Assert.IsTrue(viewModel.CanConfigureLongLineScroll, "切回「随内容变化」后开关应当放行。");
            }
            finally { SettingsManager.Current = previous; }
            return Task.CompletedTask;
        });
    }

    [TestMethod]
    public void EditingApplicationAppearancePreservesTaskbarMaterialAndViceVersa()
    {
        StaTest.Run(_ =>
        {
            var previous = SettingsManager.Current;
            using var context = new SettingsPageContext();
            using var localization = new LocalizationService();
            var configuration = new LegacySettingsConfiguration(context);
            try
            {
                SettingsManager.Current = new AppSettings();
                using var application = new AppearanceViewModel(localization, configuration);
                using var taskbar = new TaskbarAppearanceViewModel(configuration, new TaskbarLengthConstraintsService(), localization);
                taskbar.UseFrostedTaskbarBackground = true;
                taskbar.TaskbarFrostedStyle = TaskbarFrostedStyle.Warm;
                taskbar.TaskbarBackgroundOpacityPercent = 75;
                taskbar.ShowTaskbarBackgroundWhenIdle = false;
                taskbar.ShowTaskbarBackgroundTint = false;
                taskbar.ShowTaskbarBackgroundBorder = false;
                taskbar.ShowTaskbarBackgroundTexture = false;
                taskbar.SetIdleNoteColorCommand.Execute("#42cba6");
                application.FontWeight = 700;
                Assert.AreEqual("#42CBA6", SettingsManager.Current.Appearance.IdleNoteColor);
                taskbar.IdleNoteColorHex = "#12";
                Assert.AreEqual("#42CBA6", SettingsManager.Current.Appearance.IdleNoteColor);
                taskbar.ComponentSpacingDip = 18;
                Assert.AreEqual("#12", taskbar.IdleNoteColorHex, "无关布局更新不应清除尚未输入完整的颜色。");
                Assert.IsTrue(taskbar.UseFrostedTaskbarBackground);
                Assert.AreEqual(TaskbarFrostedStyle.Warm, taskbar.TaskbarFrostedStyle);
                Assert.AreEqual(75, taskbar.TaskbarBackgroundOpacityPercent);
                Assert.IsFalse(taskbar.ShowTaskbarBackgroundWhenIdle);
                Assert.IsFalse(taskbar.ShowTaskbarBackgroundTint);
                Assert.IsFalse(taskbar.ShowTaskbarBackgroundBorder);
                Assert.IsFalse(taskbar.ShowTaskbarBackgroundTexture);
                taskbar.TaskbarFrostedStyle = TaskbarFrostedStyle.Cool;
                Assert.AreEqual(700, application.FontWeight);
                var before = SettingsManager.Current.Appearance;
                context.Deactivate();
                taskbar.UseFrostedTaskbarBackground = false;
                taskbar.ShowTaskbarBackgroundWhenIdle = true;
                taskbar.ShowTaskbarBackgroundTint = true;
                taskbar.ShowTaskbarBackgroundBorder = true;
                taskbar.ShowTaskbarBackgroundTexture = true;
                taskbar.ResetIdleNoteColorCommand.Execute(null);
                application.FontWeight = 400;
                Assert.AreEqual(before, SettingsManager.Current.Appearance);
                context.Initialize(SettingsContext.Initial with { Mode = SettingsMode.DynamicIsland });
                context.Activate();
                taskbar.UseFrostedTaskbarBackground = false;
                taskbar.SetIdleNoteColorCommand.Execute("#112233");
                Assert.AreEqual(before, SettingsManager.Current.Appearance);
            }
            finally { SettingsManager.Current = previous; }
            return Task.CompletedTask;
        });
    }

    [TestMethod]
    public void AppearanceResetsRestoreTheirOwnSnapshotFieldsOnly()
    {
        var previous = SettingsManager.Current;
        var previousDefaults = SettingsManager.UserDefaults;
        try
        {
            var saved = new AppSettings
            {
                Appearance = AppearanceSettings.Default with
                {
                    FontWeight = 600,
                    PlayerForegroundMode = PlayerForegroundMode.LightText,
                    TaskbarBackgroundMaterial = TaskbarBackgroundMaterial.Frosted,
                    TaskbarFrostedStyle = TaskbarFrostedStyle.Warm,
                    TaskbarBackgroundOpacityPercent = 75,
                    ShowTaskbarBackgroundWhenIdle = false,
                    ShowTaskbarBackgroundTint = false,
                    ShowTaskbarBackgroundBorder = false,
                    ShowTaskbarBackgroundTexture = false,
                    IdleNoteColor = "#42CBA6"
                }
            };
            SettingsManager.SetUserDefaults(saved);
            var current = saved.Appearance with
            {
                FontWeight = 800,
                PlayerForegroundMode = PlayerForegroundMode.DarkText,
                TaskbarFrostedStyle = TaskbarFrostedStyle.Cool,
                TaskbarBackgroundOpacityPercent = 90,
                ShowTaskbarBackgroundWhenIdle = true,
                ShowTaskbarBackgroundTint = true,
                ShowTaskbarBackgroundBorder = true,
                ShowTaskbarBackgroundTexture = true,
                IdleNoteColor = "#FF8A8A"
            };
            SettingsManager.Current = new AppSettings { Appearance = current };
            SettingsManager.ResetApplicationAppearance();
            Assert.AreEqual(600, SettingsManager.Current.Appearance.FontWeight);
            Assert.AreEqual(PlayerForegroundMode.DarkText, SettingsManager.Current.Appearance.PlayerForegroundMode);
            Assert.AreEqual(TaskbarFrostedStyle.Cool, SettingsManager.Current.Appearance.TaskbarFrostedStyle);
            Assert.AreEqual(90, SettingsManager.Current.Appearance.TaskbarBackgroundOpacityPercent);
            Assert.IsTrue(SettingsManager.Current.Appearance.ShowTaskbarBackgroundWhenIdle);
            Assert.IsTrue(SettingsManager.Current.Appearance.ShowTaskbarBackgroundTint);
            Assert.IsTrue(SettingsManager.Current.Appearance.ShowTaskbarBackgroundBorder);
            Assert.IsTrue(SettingsManager.Current.Appearance.ShowTaskbarBackgroundTexture);
            Assert.AreEqual(current.IdleNoteColor, SettingsManager.Current.Appearance.IdleNoteColor);
            SettingsManager.Current.Appearance = current;
            SettingsManager.ResetTaskbarAppearance();
            Assert.AreEqual(800, SettingsManager.Current.Appearance.FontWeight);
            Assert.AreEqual(saved.Appearance.PlayerForegroundMode, SettingsManager.Current.Appearance.PlayerForegroundMode);
            Assert.AreEqual(saved.Appearance.TaskbarBackgroundMaterial, SettingsManager.Current.Appearance.TaskbarBackgroundMaterial);
            Assert.AreEqual(saved.Appearance.TaskbarFrostedStyle, SettingsManager.Current.Appearance.TaskbarFrostedStyle);
            Assert.AreEqual(saved.Appearance.TaskbarBackgroundOpacityPercent, SettingsManager.Current.Appearance.TaskbarBackgroundOpacityPercent);
            Assert.IsFalse(SettingsManager.Current.Appearance.ShowTaskbarBackgroundWhenIdle);
            Assert.IsFalse(SettingsManager.Current.Appearance.ShowTaskbarBackgroundTint);
            Assert.IsFalse(SettingsManager.Current.Appearance.ShowTaskbarBackgroundBorder);
            Assert.IsFalse(SettingsManager.Current.Appearance.ShowTaskbarBackgroundTexture);
            Assert.AreEqual(saved.Appearance.IdleNoteColor, SettingsManager.Current.Appearance.IdleNoteColor);
            Assert.AreEqual(saved.Appearance, SettingsManager.UserDefaults!.Appearance);
        }
        finally { SettingsManager.SetUserDefaults(previousDefaults); SettingsManager.Current = previous; }
    }

    [TestMethod]
    public void IdleNoteColorNormalizesAndRoundTripsWithoutRequiringOldSettingsToContainIt()
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        foreach (var json in new[] { "{\"appearance\":{}}", "{\"appearance\":{\"idleNoteColor\":null}}", "{\"appearance\":{\"idleNoteColor\":\"invalid\"}}" })
            Assert.IsNull(JsonSerializer.Deserialize<AppSettings>(json, options)!.Normalize().Appearance.IdleNoteColor);
        var settings = JsonSerializer.Deserialize<AppSettings>("{\"appearance\":{\"idleNoteColor\":\" #8842cba6 \"}}", options)!.Normalize();
        Assert.AreEqual("#42CBA6", settings.Appearance.IdleNoteColor);
        var loaded = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings, options), options)!.Normalize().Clone();
        Assert.AreEqual("#42CBA6", loaded.Appearance.IdleNoteColor);
    }

    [TestMethod]
    public void BackgroundLayerChoicesPreserveOldDefaultsAndRoundTripExplicitOff()
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        foreach (var json in new[] { "{}", "{\"appearance\":{}}" })
        {
            var old = JsonSerializer.Deserialize<AppSettings>(json, options)!.Normalize().Appearance;
            Assert.IsTrue(old.ShowTaskbarBackgroundWhenIdle);
            Assert.IsTrue(old.ShowTaskbarBackgroundTint);
            Assert.IsTrue(old.ShowTaskbarBackgroundBorder);
            Assert.IsTrue(old.ShowTaskbarBackgroundTexture);
        }
        var settings = new AppSettings
        {
            Appearance = AppearanceSettings.Default with
            {
                ShowTaskbarBackgroundWhenIdle = false,
                ShowTaskbarBackgroundTint = false,
                ShowTaskbarBackgroundBorder = false,
                ShowTaskbarBackgroundTexture = false,
            }
        };
        var loaded = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings, options), options)!.Normalize().Clone();
        Assert.IsFalse(loaded.Appearance.ShowTaskbarBackgroundWhenIdle);
        Assert.IsFalse(loaded.Appearance.ShowTaskbarBackgroundTint);
        Assert.IsFalse(loaded.Appearance.ShowTaskbarBackgroundBorder);
        Assert.IsFalse(loaded.Appearance.ShowTaskbarBackgroundTexture);
        foreach (var key in new[] { "showTaskbarBackgroundWhenIdle", "showTaskbarBackgroundTint", "showTaskbarBackgroundBorder", "showTaskbarBackgroundTexture" })
            Assert.ThrowsException<JsonException>(() => JsonSerializer.Deserialize<AppSettings>(
                "{\"appearance\":{\"" + key + "\":null}}", options));
    }

    [TestMethod]
    public void ArtworkProgressReadsOldSettingsAndPreservesExplicitChoice()
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var oldSettings = JsonSerializer.Deserialize<AppSettings>("{\"taskbarExperience\":{\"hoverControls\":{\"progressVisible\":false}}}", options)!.Normalize();
        Assert.IsTrue(oldSettings.TaskbarExperience.ArtworkProgressVisible, "旧悬停进度开关不得控制新增封面进度。");
        oldSettings.TaskbarExperience = oldSettings.TaskbarExperience with { ArtworkProgressVisible = false };
        var roundTrip = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(oldSettings, options), options)!.Normalize().Clone();
        Assert.IsFalse(roundTrip.TaskbarExperience.ArtworkProgressVisible);
        Assert.ThrowsException<JsonException>(() => JsonSerializer.Deserialize<AppSettings>("{\"taskbarExperience\":{\"artworkProgressVisible\":null}}", options));
    }

    [TestMethod]
    public void AcknowledgedCompatibilitySurvivesSerializationAndReset()
    {
        var previous = SettingsManager.Current;
        var previousDefaults = SettingsManager.UserDefaults;
        try
        {
            SettingsManager.SetUserDefaults(null);
            var loaded = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(new AppSettings
            {
                TranslucentTbCompatibilityPromptShown = true
            }))!.Normalize().Clone();
            Assert.IsTrue(loaded.TranslucentTbCompatibilityPromptShown);
            SettingsManager.Current = loaded;
            SettingsManager.ResetAll();
            Assert.IsTrue(SettingsManager.Current.TranslucentTbCompatibilityPromptShown);
        }
        finally { SettingsManager.SetUserDefaults(previousDefaults); SettingsManager.Current = previous; }
    }
}
