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
    public void SearchDestinationsFollowDeclaredGroups(SettingsPageKey page, string fileName)
    {
        var root = FindRepository();
        var document = XDocument.Load(Path.Combine(root, "src", "AFMediaBar", "Views", "Pages", fileName + ".xaml"));
        var groupKeys = document.Descendants()
            .Where(element => element.Name.LocalName == "SettingsGroup")
            .Select(element => ((string)element.Attribute("Header")!)
                .Replace("{DynamicResource Loc.", "").TrimEnd('}'))
            .ToArray();
        var entries = SettingsSearchIndex.Entries.Where(entry => entry.Page == page).OrderBy(entry => entry.GroupIndex).ToArray();
        Assert.AreEqual(groupKeys.Length, entries.Length, fileName);
        for (var index = 0; index < groupKeys.Length; index++)
        {
            Assert.AreEqual(index, entries[index].GroupIndex, fileName);
            Assert.AreEqual(Translations.Get(groupKeys[index]), entries[index].Title, fileName + " group " + index);
        }
    }

    [DataTestMethod]
    [DataRow("字体", "Common.Group.TextAndFonts")]
    [DataRow("媒体文字大小", "Common.Group.TextAndFonts")]
    [DataRow("font", "Common.Group.TextAndFonts")]
    public void FormerFontAndMediaTextTermsResolveToMergedGroup(string query, string groupKey)
    {
        var hits = SettingsSearchPolicy.Search(query, SettingsSearchIndex.Entries);
        Assert.IsTrue(hits.Any(hit => hit.Page == SettingsPageKey.Appearance &&
            hit.GroupIndex == 1 && hit.Title == Translations.Get(groupKey)));
    }

    private static string FindRepository()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src", "AFMediaBar")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Settings page sources were not found.");
    }
}
