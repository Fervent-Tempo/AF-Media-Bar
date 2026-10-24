// 防止兼容性指引与下载目录脱节，以及默认首页意外联网或释放后继续发布结果。
using System.Net.Http;
using AFMediaBar.Classes.Models.Settings;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Services.Localization;
using AFMediaBar.Classes.Services.Plugins;
using AFMediaBar.Classes.Services.Settings;
using AFMediaBar.Classes.Services.Updates;
using AFMediaBar.Classes.Settings;
using AFMediaBar.ViewModels.Pages;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

[TestClass]
[DoNotParallelize]
public sealed class PlayerSupportTests
{
    [TestMethod]
    public void PluginAdviceHasOneDownloadEntryAndHiddenReleasePageRemainsResolvable()
    {
        var recommended = PlayerSupportCatalog.Entries.Where(player => player.PluginId is not null)
            .Select(player => player.PluginId!).Order().ToArray();
        CollectionAssert.AreEqual(PluginCatalog.Entries.Select(plugin => plugin.Id).Order().ToArray(), recommended);
        Assert.IsFalse(PluginCatalog.Entries.SelectMany(plugin => plugin.Assets).Any(asset => asset.Architecture == "ARM64"));
        Assert.IsTrue(PluginCatalog.Entries.All(plugin => plugin.Assets.Count > 0));
        var pages = SettingsPageCatalog.ForMode(SettingsMode.Taskbar);
        Assert.AreEqual(SettingsPageKey.QuickStart, pages.First(page => page.ShowInNavigation).Key);
        Assert.IsFalse(SettingsPageCatalog.Find(SettingsPageKey.ReleaseHighlights, SettingsMode.Taskbar)!.ShowInNavigation);
        Assert.IsTrue(SettingsSearchIndex.Entries.Any(entry => entry.Page == SettingsPageKey.ReleaseHighlights));
    }

    [TestMethod]
    public void QuickStartReadsLocalReleasesAndDisposedPageRejectsLanguageCallbacks()
    {
        StaTest.Run(async dispatcher =>
        {
            var directory = Path.Combine(Path.GetTempPath(), "AFMB-QuickStart-" + Guid.NewGuid().ToString("N"));
            using var handler = new NoNetwork();
            using var client = new HttpClient(handler);
            using var reader = new ReleaseHighlightsService(client: client, cachePath: Path.Combine(directory, "highlights.json"));
            using var localization = new LocalizationService();
            localization.Apply(InterfaceLanguage.SimplifiedChinese);
            using var model = new QuickStartViewModel(reader, localization, dispatcher);
            try
            {
                await model.ActivateAsync();
                Assert.AreEqual(0, handler.RequestCount, "Opening the default page must not refresh online highlights.");
                Assert.IsTrue(model.LatestVersion.Length > 0);
                Assert.IsTrue(model.Highlights.Count > 0);
                model.SearchText = "MoeKoeMusic";
                Assert.AreEqual("moekoe", model.Players.Single().Profile.Id);
                Assert.IsFalse(model.Players.Single().HasPlugin);
                var notifications = 0;
                model.PropertyChanged += (_, _) => notifications++;
                localization.Apply(InterfaceLanguage.English);
                model.Dispose();
                var before = notifications;
                await dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                Assert.AreEqual(before, notifications);
                await model.ActivateAsync();
                Assert.AreEqual(before, notifications);
            }
            finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        });
    }

    private sealed class NoNetwork : HttpMessageHandler
    {
        public int RequestCount { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            throw new AssertFailedException("Quick start must only read local release information.");
        }
    }
}
