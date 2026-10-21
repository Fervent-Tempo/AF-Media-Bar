// 验证跨项移动、原始快照校验与既有设置保存约定；每次测试恢复全局设置。
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Models.Settings;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Services.Localization;
using AFMediaBar.Classes.Services.Settings;
using AFMediaBar.Classes.Settings;
using AFMediaBar.Components;
using AFMediaBar.ViewModels.Pages;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

[TestClass]
[DoNotParallelize]
public sealed class SettingsReorderTests
{
    [TestMethod]
    public void RequestRejectsChangedOrderMissingItemsAndNoOp()
    {
        var entries = new[] { new QuickLaunchEntry("a", "A", QuickLaunchTargetKind.Executable, "a.exe"),
            new QuickLaunchEntry("b", "B", QuickLaunchTargetKind.Executable, "b.exe"),
            new QuickLaunchEntry("c", "C", QuickLaunchTargetKind.Executable, "c.exe") };
        var request = new SettingsReorderRequest(entries[0], 2, entries);
        Assert.IsTrue(request.TryResolve(entries.Select(entry => entry with { DisplayName = "renamed" }).ToArray(), entry => entry.Id, out var source, out var target));
        Assert.AreEqual(0, source); Assert.AreEqual(2, target);
        Assert.IsFalse(request.TryResolve(new[] { entries[1], entries[0], entries[2] }, entry => entry.Id, out _, out _));
        Assert.IsFalse(request.TryResolve(entries.Take(2).ToArray(), entry => entry.Id, out _, out _));
        Assert.IsFalse(new SettingsReorderRequest(entries[0], 0, entries).TryResolve(entries, entry => entry.Id, out _, out _));
        Assert.IsFalse(new SettingsReorderRequest(entries[1], -1, entries).TryResolve(entries, entry => entry.Id, out _, out _));
        Assert.IsFalse(new SettingsReorderRequest(entries[2], 0, entries).TryResolve(entries, entry => entry.Id, out _, out _, minimumIndex: 1));
    }

    [TestMethod]
    public void LyricsMoveSavesOnceRejectsStaleAndRetainsDefaultSemantics() => Run((configuration, localization, context) =>
    {
        using var model = new LyricsViewModel(localization, configuration);
        var entries = model.SecondaryLineEntries.ToArray();
        var changes = 0;
        void Changed(object? sender, SettingsChangedEventArgs args) => changes++;
        SettingsManager.SettingsChanged += Changed;
        try
        {
            var request = new SettingsReorderRequest(entries[0], 2, entries);
            model.ReorderSecondaryLineCommand.Execute(request);
            CollectionAssert.AreEqual(new[] { entries[1].Mode, entries[2].Mode, entries[0].Mode }, model.SecondaryLineEntries.Select(entry => entry.Mode).ToArray());
            Assert.AreEqual(1, changes);
            model.ReorderSecondaryLineCommand.Execute(request);
            Assert.AreEqual(1, changes, "A stale drag must not write again.");
            var current = model.SecondaryLineEntries.ToArray();
            model.ReorderSecondaryLineCommand.Execute(new SettingsReorderRequest(current[2], 0, current));
            Assert.IsNull(SettingsManager.Current.LyricsSecondaryLine.Order, "The default order remains unconfigured.");
            current = model.SecondaryLineEntries.ToArray();
            model.ReorderSecondaryLineCommand.Execute(new SettingsReorderRequest(current[0], 0, current));
            Assert.AreEqual(2, changes);
            context.Deactivate();
            model.ReorderSecondaryLineCommand.Execute(new SettingsReorderRequest(current[0], 2, current));
            Assert.AreEqual(2, changes);
        }
        finally { SettingsManager.SettingsChanged -= Changed; }
    });

    [TestMethod]
    public void ManualSourceMoveKeepsUnknownIdsAndDisabledSelection() => Run((configuration, localization, context) =>
    {
        SettingsManager.SetLyricsSourceSettings(LyricsSourceSettings.Default with
        { Rule = LyricsSourceRule.Manual, EnabledSourceIds = ["Netease"], PrioritySourceIds = ["future-source"] });
        using var model = new LyricsViewModel(localization, configuration);
        var entries = model.SourceEntries.ToArray();
        model.ReorderSourceCommand.Execute(new SettingsReorderRequest(entries[^1], 0, entries));
        CollectionAssert.AreEqual(new[] { entries[^1].SourceId }.Concat(entries.Take(entries.Length - 1).Select(entry => entry.SourceId)).Append("future-source").ToArray(),
            SettingsManager.Current.LyricsSource.PrioritySourceIds!.ToArray());
        CollectionAssert.AreEqual(new[] { "Netease" }, SettingsManager.Current.LyricsSource.EnabledSourceIds!.ToArray());
        SettingsManager.SetLyricsSourceSettings(SettingsManager.Current.LyricsSource with { Rule = LyricsSourceRule.Default });
        var before = SettingsManager.Current.LyricsSource;
        entries = model.SourceEntries.ToArray();
        model.ReorderSourceCommand.Execute(new SettingsReorderRequest(entries[0], entries.Length - 1, entries));
        Assert.AreEqual(before, SettingsManager.Current.LyricsSource);
    });

    [TestMethod]
    public void ComponentMoveCannotCrossPinnedPrefixAndKeepsHiddenInput() => Run((configuration, localization, context) =>
    {
        SettingsManager.Current.TaskbarExperience = TaskbarExperienceSettings.Default with
        { RestComponentOrder = [TaskbarRestComponent.InputDevice, TaskbarRestComponent.Spectrum, TaskbarRestComponent.Performance, TaskbarRestComponent.OutputDevice, TaskbarRestComponent.Volume] };
        using var model = new TaskbarAppearanceViewModel(configuration, new TaskbarLengthConstraintsService(), localization);
        var entries = model.RestOrderEntries.ToArray();
        var moving = entries.Single(entry => entry.Component == TaskbarRestComponent.Volume);
        var pinnedCount = TaskbarRestLayoutPolicy.FixedOrder.Count;
        var before = SettingsManager.Current.TaskbarExperience;
        model.ReorderRestComponentCommand.Execute(new SettingsReorderRequest(moving, 0, entries));
        Assert.AreEqual(before, SettingsManager.Current.TaskbarExperience);
        model.ReorderRestComponentCommand.Execute(new SettingsReorderRequest(entries[0], entries.Length - 1, entries));
        Assert.AreEqual(before, SettingsManager.Current.TaskbarExperience);
        model.ReorderRestComponentCommand.Execute(new SettingsReorderRequest(moving, pinnedCount, entries));
        var expected = entries.Where(entry => entry != moving).ToList();
        expected.Insert(pinnedCount, moving);
        CollectionAssert.AreEqual(expected.Select(entry => entry.Component).ToArray(), model.RestOrderEntries.Select(entry => entry.Component).ToArray());
        CollectionAssert.AreEqual(expected.Skip(pinnedCount).Select(entry => entry.Component).ToArray(), SettingsManager.Current.TaskbarExperience.RestComponentOrder!.ToArray());
        CollectionAssert.AreEqual(entries.Take(pinnedCount).Select(entry => entry.Component).ToArray(), model.RestOrderEntries.Take(pinnedCount).Select(entry => entry.Component).ToArray());
        Assert.IsFalse(SettingsManager.Current.TaskbarExperience.InputDeviceVisible);
        CollectionAssert.Contains(SettingsManager.Current.TaskbarExperience.RestComponentOrder!.ToArray(), TaskbarRestComponent.InputDevice);
        entries = model.RestOrderEntries.ToArray();
        context.Deactivate();
        before = SettingsManager.Current.TaskbarExperience;
        model.ReorderRestComponentCommand.Execute(new SettingsReorderRequest(entries[^1], 2, entries));
        Assert.AreEqual(before, SettingsManager.Current.TaskbarExperience);
    });

    [TestMethod]
    public void MetricMoveInsertsRatherThanSwapsAndRejectsChangedSelection() => Run((configuration, localization, context) =>
    {
        SettingsManager.Current.PerformanceComponent = new PerformanceComponentSettings
        { Metrics = [MetricKind.SystemCpu, MetricKind.SystemGpu, MetricKind.SystemMemory, MetricKind.SystemNetwork] };
        using var model = new ComponentsSettingsViewModel(configuration, localization);
        var entries = model.MetricOrderEntries.ToArray();
        var request = new SettingsReorderRequest(entries[0], 3, entries);
        model.ReorderMetricCommand.Execute(request);
        CollectionAssert.AreEqual(entries.Skip(1).Select(entry => entry.Metric).Append(entries[0].Metric).ToArray(), SettingsManager.Current.PerformanceComponent.GetOrderedMetrics().ToArray());
        var before = SettingsManager.Current.PerformanceComponent;
        model.ReorderMetricCommand.Execute(request);
        Assert.AreEqual(before, SettingsManager.Current.PerformanceComponent);
        entries = model.MetricOrderEntries.ToArray();
        request = new SettingsReorderRequest(entries[0], 3, entries);
        model.ShowSystemNetwork = false;
        before = SettingsManager.Current.PerformanceComponent;
        model.ReorderMetricCommand.Execute(request);
        Assert.AreEqual(before, SettingsManager.Current.PerformanceComponent);
    });

    [TestMethod]
    public void QuickLaunchMovePreservesSelectionAndRejectsStaleRequests() => Run((configuration, localization, context) =>
    {
        var entries = Enumerable.Range(0, 4).Select(index => new QuickLaunchEntry(index.ToString(), "Player " + index,
            QuickLaunchTargetKind.Executable, "player" + index + ".exe")).ToArray();
        SettingsManager.Current.QuickLaunch = new(entries) { SelectedEntryId = "0" };
        using var selection = new QuickLaunchService();
        using var model = new QuickLaunchSettingsViewModel(selection, configuration, localization);
        var request = new SettingsReorderRequest(model.QuickLaunchEntries[0], 3, model.QuickLaunchEntries);
        model.ReorderQuickLaunchCommand.Execute(request);
        CollectionAssert.AreEqual(new[] { "1", "2", "3", "0" }, SettingsManager.Current.QuickLaunch.Entries!.Select(entry => entry.Id).ToArray());
        Assert.AreEqual("0", selection.Selected!.Id);
        var before = SettingsManager.Current.QuickLaunch;
        model.ReorderQuickLaunchCommand.Execute(request);
        Assert.AreEqual(before, SettingsManager.Current.QuickLaunch);
        context.Deactivate();
        var reordered = model.QuickLaunchEntries.ToArray();
        model.ReorderQuickLaunchCommand.Execute(new SettingsReorderRequest(reordered[0], 3, reordered));
        Assert.AreEqual(before, SettingsManager.Current.QuickLaunch);
    });

    private static void Run(Action<LegacySettingsConfiguration, LocalizationService, SettingsPageContext> action)
    {
        StaTest.Run(_ =>
        {
            var previous = SettingsManager.Current;
            using var context = new SettingsPageContext();
            context.Initialize(SettingsContext.Initial);
            using var localization = new LocalizationService();
            try { SettingsManager.Current = new AppSettings(); action(new LegacySettingsConfiguration(context), localization, context); }
            finally { SettingsManager.Current = previous; }
            return Task.CompletedTask;
        });
    }
}
