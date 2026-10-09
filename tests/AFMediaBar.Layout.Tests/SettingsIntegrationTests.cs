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
                viewModel.ChineseConversionEnabled = true;
                CollectionAssert.AreEquivalent(new[] { nameof(viewModel.ChineseConversionEnabled),
                    nameof(viewModel.ChineseConversion), nameof(viewModel.CanConfigureChineseConversion) }, notifications);
                notifications.Clear();
                viewModel.ChineseConversionEnabled = true;
                viewModel.ChineseConversion = viewModel.ChineseConversion;
                Assert.AreEqual(0, notifications.Count);
                context.Deactivate();
                viewModel.ChineseConversionEnabled = false;
                Assert.AreEqual(LyricsChineseConversionMode.SimplifiedToTraditional, SettingsManager.Current.LyricsChineseConversion);
                Assert.AreEqual(0, notifications.Count);
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
                application.FontWeight = 700;
                Assert.IsTrue(taskbar.UseFrostedTaskbarBackground);
                Assert.AreEqual(TaskbarFrostedStyle.Warm, taskbar.TaskbarFrostedStyle);
                Assert.AreEqual(75, taskbar.TaskbarBackgroundOpacityPercent);
                taskbar.TaskbarFrostedStyle = TaskbarFrostedStyle.Cool;
                Assert.AreEqual(700, application.FontWeight);
                var before = SettingsManager.Current.Appearance;
                context.Deactivate();
                taskbar.UseFrostedTaskbarBackground = false;
                application.FontWeight = 400;
                Assert.AreEqual(before, SettingsManager.Current.Appearance);
                context.Initialize(SettingsContext.Initial with { Mode = SettingsMode.DynamicIsland });
                context.Activate();
                taskbar.UseFrostedTaskbarBackground = false;
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
                    TaskbarBackgroundOpacityPercent = 75
                }
            };
            SettingsManager.SetUserDefaults(saved);
            var current = saved.Appearance with
            {
                FontWeight = 800,
                PlayerForegroundMode = PlayerForegroundMode.DarkText,
                TaskbarFrostedStyle = TaskbarFrostedStyle.Cool,
                TaskbarBackgroundOpacityPercent = 90
            };
            SettingsManager.Current = new AppSettings { Appearance = current };
            SettingsManager.ResetApplicationAppearance();
            Assert.AreEqual(600, SettingsManager.Current.Appearance.FontWeight);
            Assert.AreEqual(PlayerForegroundMode.DarkText, SettingsManager.Current.Appearance.PlayerForegroundMode);
            Assert.AreEqual(TaskbarFrostedStyle.Cool, SettingsManager.Current.Appearance.TaskbarFrostedStyle);
            Assert.AreEqual(90, SettingsManager.Current.Appearance.TaskbarBackgroundOpacityPercent);
            SettingsManager.Current.Appearance = current;
            SettingsManager.ResetTaskbarAppearance();
            Assert.AreEqual(800, SettingsManager.Current.Appearance.FontWeight);
            Assert.AreEqual(saved.Appearance.PlayerForegroundMode, SettingsManager.Current.Appearance.PlayerForegroundMode);
            Assert.AreEqual(saved.Appearance.TaskbarBackgroundMaterial, SettingsManager.Current.Appearance.TaskbarBackgroundMaterial);
            Assert.AreEqual(saved.Appearance.TaskbarFrostedStyle, SettingsManager.Current.Appearance.TaskbarFrostedStyle);
            Assert.AreEqual(saved.Appearance.TaskbarBackgroundOpacityPercent, SettingsManager.Current.Appearance.TaskbarBackgroundOpacityPercent);
            Assert.AreEqual(saved.Appearance, SettingsManager.UserDefaults!.Appearance);
        }
        finally { SettingsManager.SetUserDefaults(previousDefaults); SettingsManager.Current = previous; }
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
