// Guards search destinations against page regrouping: a stale index sends users to the wrong setting.
using System.Xml.Linq;
using AFMediaBar.Classes.Services;
using AFMediaBar.Resources;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

[TestClass]
public sealed class SettingsSearchLayoutTests
{
    [DataTestMethod]
    [DataRow(SettingsPageKey.Appearance, "AppearancePage")]
    [DataRow(SettingsPageKey.Interaction, "InteractionPage")]
    [DataRow(SettingsPageKey.MediaAndNotifications, "ExtraFeaturesPage")]
    [DataRow(SettingsPageKey.Lyrics, "LyricsPage")]
    [DataRow(SettingsPageKey.Components, "ComponentsSettingsPage")]
    [DataRow(SettingsPageKey.Application, "ApplicationPage")]
    [DataRow(SettingsPageKey.ScreenAndPlacement, "ScreenAndPlacementPage")]
    [DataRow(SettingsPageKey.About, "AboutPage")]
    [DataRow(SettingsPageKey.ReleaseHighlights, "ReleaseHighlightsPage")]
    public void SearchDestinationsFollowDeclaredGroups(SettingsPageKey page, string fileName)
    {
        var root = FindRepository();
        var document = XDocument.Load(Path.Combine(root, "src", "AFMediaBar", "Views", "Pages", fileName + ".xaml"));
        var groups = document.Descendants().Where(element => element.Name.LocalName == "SettingsGroup").ToArray();
        var entries = SettingsSearchIndex.Entries.Where(entry => entry.Page == page).ToDictionary(entry => entry.GroupId);
        Assert.AreEqual(groups.Length, entries.Count, fileName);
        foreach (var group in groups.Reverse())
        {
            var id = (string?)group.Attribute("GroupId");
            Assert.IsFalse(string.IsNullOrWhiteSpace(id));
            Assert.IsTrue(entries.TryGetValue(id!, out var entry), "Missing stable search destination: " + id);
            var header = ((string)group.Attribute("Header")!).Replace("{DynamicResource Loc.", "").TrimEnd('}');
            Assert.AreEqual(Translations.Get(header), entry.Title);
        }
    }

    [DataTestMethod]
    [DataRow("字体", SettingsPageKey.Appearance, "Common.Group.Fonts")]
    [DataRow("媒体文字大小", SettingsPageKey.Appearance, "Common.Group.MediaBarText")]
    [DataRow("font", SettingsPageKey.Appearance, "Common.Group.Fonts")]
    [DataRow("组件设置", SettingsPageKey.Components, "Common.RestLayer")]
    [DataRow("应用外观", SettingsPageKey.Appearance, "Common.Group.ThemeAndBackdrop")]
    [DataRow("歌词对齐", SettingsPageKey.Components, "Appearance.Group.RestLayout")]
    public void FontAndMediaTextTermsResolveToTheirOwners(string query, SettingsPageKey page, string group)
    {
        var hits = SettingsSearchPolicy.Search(query, SettingsSearchIndex.Entries);
        Assert.IsTrue(hits.Any(hit => hit.Page == page && hit.GroupId == group));
    }

    private static string FindRepository()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src", "AFMediaBar")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Settings page sources were not found.");
    }

    [DataTestMethod]
    [DataRow("显示器")]
    [DataRow("位置")]
    [DataRow("厚度方向偏移")]
    [DataRow("monitor")]
    public void PlacementKeywordsOpenTheIndependentPage(string query)
    {
        var hits = SettingsSearchPolicy.Search(query, SettingsSearchIndex.Entries);
        Assert.IsTrue(hits.Any(hit => hit.Page == SettingsPageKey.ScreenAndPlacement && hit.GroupId == "Common.Group.ScreenAndPlacement"));
        Assert.IsFalse(SettingsSearchIndex.Entries.Any(entry => entry.Page == SettingsPageKey.DisplayModes &&
            entry.Title == Translations.Get("Common.Group.ScreenAndPlacement")));
    }

    [TestMethod]
    public void ContextSearchRetainsVerticalTextLayoutAndHidesUnsupportedModeSections()
    {
        var vertical = new AFMediaBar.Classes.Models.Settings.SettingsContext(AFMediaBar.Classes.Models.Settings.SettingsMode.Taskbar, "vertical", AFMediaBar.Classes.Models.Layout.LayoutOrientation.Vertical, false, true);
        var entries = SettingsSearchIndex.ForContext(vertical);
        Assert.IsTrue(entries.Any(entry => entry.GroupId == "Appearance.Group.RestLayout"));
        Assert.IsTrue(entries.Any(entry => entry.Page == SettingsPageKey.Appearance && entry.GroupId == "Common.Group.Fonts"));
        Assert.AreEqual(0, SettingsSearchPolicy.Search("组件顺序", entries).Count);
        Assert.AreEqual(0, SettingsSearchPolicy.Search("展开方向", entries).Count);
        Assert.IsTrue(SettingsSearchPolicy.Search("歌词对齐", entries).Any(hit => hit.GroupId == "Appearance.Group.RestLayout"));
        var future = vertical with { Mode = AFMediaBar.Classes.Models.Settings.SettingsMode.DynamicIsland };
        Assert.IsTrue(SettingsSearchIndex.ForContext(future).All(entry => entry.Page != SettingsPageKey.Components &&
            (entry.Page != SettingsPageKey.Appearance || entry.GroupId is "Common.Group.Fonts" or "Common.Group.ThemeAndBackdrop")));
        Assert.IsFalse(SettingsSearchIndex.Entries.Any(entry => entry.GroupId == "Common.Group.IslandAppearance"));
    }
}
