using AFMediaBar.Classes.Settings;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

[TestClass]
public sealed class AppearanceSettingsFontTests
{
    [TestMethod]
    public void ResolveFontFamilySource_WhenLatinFontIsSystemDefault_PutsSystemFontFirst()
    {
        var settings = AppearanceSettings.Default with
        {
            LatinFont = LatinFontPreset.SystemDefault,
            CjkFont = CjkFontPreset.SystemDefault
        };

        var resolved = settings.ResolveFontFamilySource("Microsoft YaHei UI");
        var parts = resolved.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        Assert.AreEqual("Microsoft YaHei UI", parts[0]);
        Assert.IsTrue(parts.Contains("Microsoft JhengHei UI"));
    }

    [TestMethod]
    public void Normalize_InvalidLatinFont_FallsBackToDefault()
    {
        var settings = (AppearanceSettings.Default with { LatinFont = (LatinFontPreset)999 }).Normalize();
        Assert.AreEqual(LatinFontPreset.SystemDefault, settings.LatinFont);
    }

    [TestMethod]
    public void SelectedInstalledFontNamesOverrideLegacyPresets()
    {
        var settings = AppearanceSettings.Default with
        {
            LatinFont = LatinFontPreset.Arial,
            CjkFont = CjkFontPreset.SimSun,
            LatinFontFamily = "  Test Latin  ",
            CjkFontFamily = "Test CJK"
        };

        var resolved = settings.Normalize().ResolveFontFamilySource("System Font");
        var parts = resolved.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        Assert.AreEqual("Test Latin", parts[0]);
        Assert.AreEqual("Test CJK", parts[1]);
    }

    [TestMethod]
    public void EmptySelectedFontFollowsSystemWhileMissingFieldKeepsLegacyPreset()
    {
        var settings = AppearanceSettings.Default with
        {
            LatinFont = LatinFontPreset.Arial,
            LatinFontFamily = string.Empty
        };

        Assert.AreEqual("System Font", settings.ResolveFontFamilySource("System Font").Split(',')[0]);
        Assert.AreEqual("Arial", (settings with { LatinFontFamily = null }).ResolveFontFamilySource("System Font").Split(',')[0]);
    }

    [TestMethod]
    public void Normalize_RejectsFallbackListInSelectedFontName()
    {
        var settings = (AppearanceSettings.Default with { LatinFontFamily = "Arial, SimSun" }).Normalize();
        Assert.AreEqual(string.Empty, settings.LatinFontFamily);
    }

    [TestMethod]
    public void MatchSelection_UsesInstalledNameFromLegacyFallbackChain()
    {
        var choices = new[]
        {
            new FontFamilyChoice(string.Empty, "Follow system"),
            new FontFamilyChoice("Segoe UI", "Segoe UI")
        };

        Assert.AreEqual("Segoe UI", InstalledFontCatalog.MatchSelection("Segoe UI Variable Text, Segoe UI", choices));
        Assert.AreEqual(string.Empty, InstalledFontCatalog.MatchSelection("Missing Font", choices));
    }

    [TestMethod]
    public void CompositeFont_GivesHanCharactersTheCjkChoiceBeforeLatinFallback()
    {
        // Regression guard: otherwise a Latin font containing Han glyphs makes the CJK setting appear ineffective.
        var settings = AppearanceSettings.Default with
        {
            LatinFontFamily = "Arial Unicode MS",
            CjkFontFamily = "Microsoft YaHei UI"
        };

        var family = InstalledFontCatalog.CreateCompositeFont(settings, "Segoe UI");

        Assert.AreEqual(2, family.FamilyMaps.Count);
        Assert.IsTrue(family.FamilyMaps[0].Unicode.Contains("4E00-9FFF", StringComparison.OrdinalIgnoreCase), family.FamilyMaps[0].Unicode);
        Assert.IsTrue(family.FamilyMaps[0].Target.StartsWith("Microsoft YaHei UI", StringComparison.Ordinal));
        Assert.IsTrue(family.FamilyMaps[1].Target.StartsWith("Arial Unicode MS", StringComparison.Ordinal));
    }

    [TestMethod]
    public void InstalledFontChoices_CanBeBuiltForBothSelectors()
    {
        // A font-enumeration failure would prevent the appearance ViewModel and settings page from opening.
        var latin = InstalledFontCatalog.GetChoices("Appearance.LatinFont.FollowSystem", cjk: false);
        var cjk = InstalledFontCatalog.GetChoices("Appearance.CjkFont.FollowSystem", cjk: true);

        Assert.IsTrue(latin.Count > 0);
        Assert.IsTrue(cjk.Count > 0);
        Assert.IsTrue(latin.All(choice => choice.PreviewFontFamily is not null));
        Assert.IsTrue(cjk.All(choice => choice.PreviewFontFamily is not null));
    }
}
