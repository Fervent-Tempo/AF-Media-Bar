// 检查歌词页可见文案使用本地化资源，并在 STA 上验证三种语言的动态资源能解析为文本。
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Xml.Linq;
using AFMediaBar.Classes.Services.Localization;
using AFMediaBar.Resources;
using AFMediaBar.Classes.Settings;
using AFMediaBar.Views.Pages;
using System.Windows.Controls.Primitives;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

[TestClass]
public sealed class LyricsPageRuntimeLiteralTests
{
    [TestMethod]
    public void EmptyEditorCanRestoreDefaultSlashWithoutSavingSettings()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var saved = SettingsManager.Current.LyricsArtistSeparators;
                var editor = new ArtistSeparatorsEditor("", new AppSettings().LyricsArtistSeparators);
                Assert.AreEqual("", editor.GetSeparators());
                var button = (ButtonBase)editor.FindName("RestoreDefaultsButton");
                button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Assert.AreEqual("/", editor.GetSeparators());
                Assert.AreEqual(saved, SettingsManager.Current.LyricsArtistSeparators);
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(10)), "Editor check timed out.");
        if (failure is not null) throw failure;
    }

    [TestMethod]
    public void VisiblePageTextResolvesLocalizedResourcesAtRuntime()
    {
        var attributes = new[] { "LyricsPage.xaml", "ArtistSeparatorsEditor.xaml" }
            .Select(file => XDocument.Load(Path.Combine(AppContext.BaseDirectory, file)))
            .SelectMany(document => document.Descendants().Attributes())
            .Where(attribute => attribute.Name.LocalName is "Title" or "Description" or "Content" or "Text" or "Header" or "ToolTip")
            .ToArray();
        foreach (var attribute in attributes)
            Assert.IsTrue(attribute.Value.StartsWith('{'), $"Visible literal: {attribute}");
        var keys = attributes.Select(attribute => Regex.Match(attribute.Value, @"^\{DynamicResource Loc\.(.+)\}$"))
            .Where(match => match.Success).Select(match => match.Groups[1].Value)
            .Concat(["Lyrics.ArtistSeparators.Dialog.Confirm", "Common.Cancel"]).Distinct().ToArray();
        Assert.IsTrue(keys.Contains("Lyrics.Row.ArtistSeparators.Description"));
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                foreach (var language in Enum.GetValues<LocalizationLanguage>())
                foreach (var key in keys)
                {
                    var text = Translations.Get(key, language);
                    Assert.AreNotEqual(key, text, $"Missing translation: {language}/{key}");
                    var control = new TextBlock();
                    control.Resources["Loc." + key] = text;
                    control.SetResourceReference(TextBlock.TextProperty, "Loc." + key);
                    Assert.AreEqual(text, control.Text);
                }
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(10)), "Resource check timed out.");
        if (failure is not null) throw failure;
    }
}
