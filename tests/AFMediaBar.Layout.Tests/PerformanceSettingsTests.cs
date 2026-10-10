// 验证旧配置、排序保存和用户快照；所有文件写入独立临时目录，结束时恢复静态设置。
using System.Text.Json;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Models.Settings;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Services.Localization;
using AFMediaBar.Classes.Services.Settings;
using AFMediaBar.Classes.Settings;
using AFMediaBar.ViewModels.Pages;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>性能显示设置兼容性和页面命令的回归验证。</summary>
[TestClass]
[DoNotParallelize]
public sealed class PerformanceSettingsTests
{
    [TestMethod]
    public void PersistenceUsesCycleAndNullOrderForMissingOrInvalidNewFields()
    {
        var directory = Path.Combine(Path.GetTempPath(), "afmb-metrics-contract-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            using var store = new SettingsPersistenceService(directory);
            foreach (var fields in new[] { "", ",\"displayMode\":null,\"metricOrder\":null", ",\"displayMode\":999" })
            {
                File.WriteAllText(Path.Combine(directory, "user-defaults.json"),
                    "{\"schemaVersion\":2,\"settings\":{\"performanceComponent\":{\"metrics\":[\"SystemNetwork\",\"SystemCpu\"],\"refreshIntervalMilliseconds\":500,\"openTaskManagerOnClick\":true" + fields + "}}}");
                var snapshot = store.LoadUserDefaults();
                Assert.IsNotNull(snapshot);
                Assert.AreEqual(PerformanceDisplayMode.Cycle, snapshot.PerformanceComponent.DisplayMode);
                Assert.IsNull(snapshot.PerformanceComponent.MetricOrder);
            }
        }
        finally { Directory.Delete(directory, true); }
    }

    [TestMethod]
    public void OldSettingsPreserveCycleAndMetricSelection()
    {
        var settings = JsonSerializer.Deserialize<PerformanceComponentSettings>(
            """{"Metrics":[3,1],"RefreshIntervalMilliseconds":2500,"OpenTaskManagerOnClick":true}""").Normalize();
        Assert.AreEqual(PerformanceDisplayMode.Cycle, settings.DisplayMode);
        CollectionAssert.AreEqual(new[] { MetricKind.SystemCpu, MetricKind.ProcessMemory }, settings.Metrics!.ToArray());
        CollectionAssert.AreEqual(settings.Metrics!.ToArray(), settings.GetOrderedMetrics().ToArray());
        Assert.IsFalse(settings.Metrics!.Contains(MetricKind.SystemNetwork));
    }

    [TestMethod]
    public void InvalidOrderIsFilteredAndSelectedMissingMetricsAreAppended()
    {
        var settings = new PerformanceComponentSettings([MetricKind.SystemNetwork, MetricKind.SystemCpu, MetricKind.SystemCpu], 499, true)
        {
            DisplayMode = (PerformanceDisplayMode)999,
            MetricOrder = [MetricKind.SystemNetwork, MetricKind.SystemNetwork, MetricKind.SystemMemory, (MetricKind)999]
        }.Normalize();
        Assert.AreEqual(PerformanceDisplayMode.Cycle, settings.DisplayMode);
        CollectionAssert.AreEqual(new[] { MetricKind.SystemNetwork, MetricKind.SystemCpu }, settings.GetOrderedMetrics().ToArray());
        Assert.AreEqual(500, settings.RefreshIntervalMilliseconds);
        Assert.AreEqual(MetricKind.SystemMemory, new PerformanceComponentSettings([], 500, true).Normalize().Metrics!.Single());
    }

    [TestMethod]
    public void PageCommandsPreserveOrderAcrossModesAndPreventEmptySelection()
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
                using var viewModel = new ComponentsSettingsViewModel(new LegacySettingsConfiguration(page), localization);
                viewModel.ShowSystemNetwork = true;
                viewModel.MoveMetricUpCommand.Execute(viewModel.MetricOrderEntries.Last());
                viewModel.PerformanceMode = PerformanceDisplayMode.Parallel;
                CollectionAssert.AreEqual(new[] { MetricKind.SystemNetwork, MetricKind.SystemMemory },
                    SettingsManager.Current.PerformanceComponent.GetOrderedMetrics().ToArray());
                viewModel.PerformanceMode = PerformanceDisplayMode.Cycle;
                Assert.AreEqual(MetricKind.SystemNetwork, viewModel.MetricOrderEntries.First().Metric);
                viewModel.ShowSystemMemory = false;
                viewModel.ShowSystemNetwork = false;
                Assert.IsTrue(viewModel.ShowSystemNetwork);
                Assert.IsFalse(viewModel.CanUncheckSystemNetwork);
                viewModel.ShowSystemCpu = true;
                Assert.AreEqual(MetricKind.SystemCpu, viewModel.MetricOrderEntries.Last().Metric);
                page.Deactivate();
                viewModel.MoveMetricDownCommand.Execute(viewModel.MetricOrderEntries.First());
                Assert.AreEqual(MetricKind.SystemNetwork, viewModel.MetricOrderEntries.First().Metric);
            }
            finally { SettingsManager.Current = previous; }
            return Task.CompletedTask;
        });
    }

    [TestMethod]
    public void RealPersistenceAndSnapshotResetKeepModeOrderAndAdjacentSettings()
    {
        StaTest.Run(_ =>
        {
            var previous = SettingsManager.Current.Clone();
            var defaults = SettingsManager.UserDefaults?.Clone();
            var directory = Path.Combine(Path.GetTempPath(), "afmb-metrics-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                var settings = new PerformanceComponentSettings([MetricKind.SystemMemory, MetricKind.SystemNetwork], 500, false)
                {
                    DisplayMode = PerformanceDisplayMode.Parallel,
                    MetricOrder = [MetricKind.SystemNetwork, MetricKind.SystemMemory]
                }.Normalize();
                using (var store = new SettingsPersistenceService(directory))
                {
                    store.Initialize();
                    SettingsManager.Current.PerformanceComponent = settings;
                    SettingsManager.Current.LyricsEnabled = false;
                    store.Flush();
                    Assert.IsNull(store.SaveCurrentAsUserDefaults());
                }
                using (var reloaded = new SettingsPersistenceService(directory))
                {
                    reloaded.Initialize();
                    Assert.AreEqual(PerformanceDisplayMode.Parallel, SettingsManager.Current.PerformanceComponent.DisplayMode);
                    CollectionAssert.AreEqual(settings.GetOrderedMetrics().ToArray(), SettingsManager.Current.PerformanceComponent.GetOrderedMetrics().ToArray());
                    var snapshot = reloaded.LoadUserDefaults()!;
                    SettingsManager.SetUserDefaults(snapshot);
                    SettingsManager.Current.PerformanceComponent = PerformanceComponentSettings.Default;
                    SettingsManager.ResetComponents();
                    Assert.IsFalse(SettingsManager.Current.LyricsEnabled);
                    Assert.AreEqual(PerformanceDisplayMode.Parallel, SettingsManager.Current.PerformanceComponent.DisplayMode);
                    CollectionAssert.AreEqual(settings.GetOrderedMetrics().ToArray(), SettingsManager.Current.PerformanceComponent.GetOrderedMetrics().ToArray());
                    Assert.AreNotSame(snapshot.PerformanceComponent.MetricOrder, SettingsManager.Current.PerformanceComponent.MetricOrder);
                }
            }
            finally
            {
                SettingsManager.SetUserDefaults(defaults);
                SettingsManager.Current = previous;
                Directory.Delete(directory, true);
            }
            return Task.CompletedTask;
        });
    }
}
