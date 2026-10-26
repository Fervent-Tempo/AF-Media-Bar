// 防止新增图表设置破坏旧配置、两种表面隔离和页面重置边界。
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Models.Settings;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Services.Localization;
using AFMediaBar.Classes.Services.Settings;
using AFMediaBar.Classes.Settings;
using AFMediaBar.ViewModels.Pages;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>使用真实配置存储和页面上下文检查缩放及样式兼容。</summary>
[TestClass]
[DoNotParallelize]
public sealed class MetricVisualSettingsTests
{
    [TestMethod]
    public void OldEnvelopeKeepsMetricsAndSuppliesPanelScaleAndSeparateDefaults()
    {
        var directory = Path.Combine(Path.GetTempPath(), "afmb-visual-contract-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "user-defaults.json"), """
                {"schemaVersion":2,"settings":{"performanceComponent":{"metrics":["SystemCpu","SystemNetwork"],"displayMode":"Parallel","metricOrder":["SystemNetwork","SystemCpu"],"refreshIntervalMilliseconds":1500,"openTaskManagerOnClick":false},"taskbarExperience":{"fullPanel":{"mediaInfoVisible":true,"mediaControlsVisible":true,"audioControlsVisible":true,"performanceVisible":true}}}}
                """);
            using var store = new SettingsPersistenceService(directory);
            var settings = store.LoadUserDefaults()!;
            Assert.IsNotNull(settings);
            Assert.AreEqual(100, settings.TaskbarExperience.FullPanel.ScalePercent);
            Assert.AreEqual(MetricVisualStyle.Area, settings.TaskbarExperience.FullPanel.PerformanceVisuals.SystemCpu);
            Assert.AreEqual(MetricVisualStyle.Numeric, settings.PerformanceComponent.Visuals.SystemCpu);
            Assert.AreEqual(1500, settings.PerformanceComponent.RefreshIntervalMilliseconds);
            Assert.AreEqual(PerformanceDisplayMode.Parallel, settings.PerformanceComponent.DisplayMode);
            CollectionAssert.AreEqual(new[] { MetricKind.SystemNetwork, MetricKind.SystemCpu }, settings.PerformanceComponent.GetOrderedMetrics().ToArray());
        }
        finally { Directory.Delete(directory, true); }
    }

    [TestMethod]
    public void PersistenceAndUserDefaultsRespectStyleAndScaleResetOwnership()
    {
        StaTest.Run(_ =>
        {
            var previous = SettingsManager.Current.Clone();
            var oldDefaults = SettingsManager.UserDefaults?.Clone();
            var directory = Path.Combine(Path.GetTempPath(), "afmb-visual-save-" + Guid.NewGuid().ToString("N"));
            try
            {
                using (var store = new SettingsPersistenceService(directory))
                {
                    store.Initialize();
                    SettingsManager.Current.TaskbarExperience = SettingsManager.Current.TaskbarExperience with
                    {
                        FullPanel = TaskbarFullPanelSettings.Full with
                        {
                            ScalePercent = 175,
                            PerformanceVisuals = MetricVisualSettings.PanelDefault with { SystemNetwork = MetricVisualStyle.Line }
                        }
                    };
                    SettingsManager.Current.PerformanceComponent = PerformanceComponentSettings.Default with
                    { Visuals = new MetricVisualSettings { ProcessMemory = MetricVisualStyle.Area } };
                    store.Flush();
                    Assert.IsNull(store.SaveCurrentAsUserDefaults());
                }
                using var loaded = new SettingsPersistenceService(directory);
                loaded.Initialize();
                Assert.AreEqual(175, SettingsManager.Current.TaskbarExperience.FullPanel.ScalePercent);
                Assert.AreEqual(MetricVisualStyle.Line, SettingsManager.Current.TaskbarExperience.FullPanel.PerformanceVisuals.SystemNetwork);
                Assert.AreEqual(MetricVisualStyle.Area, SettingsManager.Current.PerformanceComponent.Visuals.ProcessMemory);
                SettingsManager.SetUserDefaults(loaded.LoadUserDefaults());
                SettingsManager.Current.TaskbarExperience = SettingsManager.Current.TaskbarExperience with
                { FullPanel = TaskbarFullPanelSettings.Full with { ScalePercent = 80, PerformanceVisuals = new() { SystemCpu = MetricVisualStyle.Bar } } };
                SettingsManager.ResetContentLayout();
                Assert.AreEqual(175, SettingsManager.Current.TaskbarExperience.FullPanel.ScalePercent);
                Assert.AreEqual(MetricVisualStyle.Bar, SettingsManager.Current.TaskbarExperience.FullPanel.PerformanceVisuals.SystemCpu,
                    "内容布局重置不得覆盖小组件页的样式。");
                SettingsManager.Current.TaskbarExperience = SettingsManager.Current.TaskbarExperience with
                { FullPanel = SettingsManager.Current.TaskbarExperience.FullPanel with { ScalePercent = 125 } };
                SettingsManager.ResetComponents();
                Assert.AreEqual(125, SettingsManager.Current.TaskbarExperience.FullPanel.ScalePercent,
                    "小组件重置不得覆盖面板缩放。");
                Assert.AreEqual(MetricVisualStyle.Line, SettingsManager.Current.TaskbarExperience.FullPanel.PerformanceVisuals.SystemNetwork);
            }
            finally
            {
                SettingsManager.SetUserDefaults(oldDefaults);
                SettingsManager.Current = previous;
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
            return Task.CompletedTask;
        });
    }

    [TestMethod]
    public void StyleEditorsAreIndependentAndInactiveContextCannotWrite()
    {
        StaTest.Run(_ =>
        {
            var previous = SettingsManager.Current.Clone();
            using var page = new SettingsPageContext();
            page.Initialize(SettingsContext.Initial);
            using var localization = new LocalizationService();
            try
            {
                SettingsManager.Current = new AppSettings();
                using var model = new ComponentsSettingsViewModel(new LegacySettingsConfiguration(page), localization);
                model.BarProcessMemoryVisual = MetricVisualStyle.Ring;
                model.PanelSystemNetworkVisual = MetricVisualStyle.Bar;
                Assert.AreEqual(MetricVisualStyle.Numeric, model.BarProcessMemoryVisual);
                Assert.AreEqual(MetricVisualStyle.Numeric, model.PanelSystemNetworkVisual);
                model.BarSystemCpuVisual = MetricVisualStyle.Ring;
                model.PanelSystemNetworkVisual = MetricVisualStyle.Line;
                Assert.AreEqual(MetricVisualStyle.Area, model.PanelSystemCpuVisual);
                Assert.AreEqual(MetricVisualStyle.Numeric, model.BarSystemNetworkVisual);
                page.Deactivate();
                model.PanelSystemNetworkVisual = MetricVisualStyle.Bar;
                Assert.AreEqual(MetricVisualStyle.Line, model.PanelSystemNetworkVisual);
            }
            finally { SettingsManager.Current = previous; }
            return Task.CompletedTask;
        });
    }
}
