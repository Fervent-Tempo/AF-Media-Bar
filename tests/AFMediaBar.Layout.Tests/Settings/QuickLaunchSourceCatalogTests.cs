// 验证发现来源与可排序启动项共用一份启动设置，释放后不保留目录订阅。
using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Models.Settings;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Services.Localization;
using AFMediaBar.Classes.Services.Settings;
using AFMediaBar.Classes.Settings;
using AFMediaBar.ViewModels.Pages;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>来源添加、提示开关和页面释放的集成回归，不启动播放器。</summary>
[TestClass]
[DoNotParallelize]
public sealed class QuickLaunchSourceCatalogTests
{
    [TestMethod]
    public void DiscoveredSourceStateTracksEntriesAndStopsAfterDisposal() => StaTest.Run(_ =>
    {
        var previous = SettingsManager.Current;
        using var context = new SettingsPageContext();
        context.Initialize(SettingsContext.Initial);
        using var localization = new LocalizationService();
        try
        {
            SettingsManager.Current = new AppSettings();
            using var selection = new QuickLaunchService();
            var catalog = new Catalog();
            var model = new QuickLaunchSettingsViewModel(selection, new LegacySettingsConfiguration(context), localization, catalog);
            try
            {
                Assert.AreEqual(1, catalog.Listeners);
                Assert.AreEqual(2, model.AvailableSources.Count);
                Assert.IsFalse(model.AvailableSources[1].CanAdd);
                model.AddSourceCommand.Execute(model.AvailableSources[0]);
                Assert.AreEqual(1, model.QuickLaunchEntries.Count);
                Assert.IsTrue(model.AvailableSources[0].IsAdded);
                model.AddSourceCommand.Execute(model.AvailableSources[0]);
                Assert.AreEqual(1, model.QuickLaunchEntries.Count);
                model.RemoveQuickLaunchCommand.Execute(model.QuickLaunchEntries[0]);
                Assert.IsTrue(model.AvailableSources[0].CanAdd);
                model.ShowTooltips = false;
                Assert.IsFalse(selection.Settings.ShowTooltips);
                context.Deactivate();
                model.AddSourceCommand.Execute(model.AvailableSources[0]);
                Assert.AreEqual(0, model.QuickLaunchEntries.Count);
            }
            finally { model.Dispose(); }
            Assert.AreEqual(0, catalog.Listeners);
        }
        finally { SettingsManager.Current = previous; }
        return Task.CompletedTask;
    });

    private sealed class Catalog : IMediaSourceCatalog
    {
        public int Listeners { get; private set; }
        public IReadOnlyList<MediaSourceDescriptor> CurrentDiscoveredSources =>
        [new("player", "Player", QuickLaunchTargetKind.Executable, "player.exe"), new("unknown", "Unknown", null, null)];
        public event Action<IReadOnlyList<MediaSourceDescriptor>>? DiscoveredSourcesChanged { add => Listeners++; remove => Listeners--; }
    }
}
