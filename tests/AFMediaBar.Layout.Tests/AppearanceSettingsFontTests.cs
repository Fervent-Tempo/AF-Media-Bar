using AFMediaBar.Classes.Settings;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

[TestClass]
public sealed class AppearanceSettingsFontTests
{
    [TestMethod]
    public void DefaultAppearanceSettings_HasSystemDefaultLatinAndCjkFont()
    {
        Assert.AreEqual(LatinFontPreset.SystemDefault, AppearanceSettings.Default.LatinFont);
        Assert.AreEqual(CjkFontPreset.SystemDefault, AppearanceSettings.Default.CjkFont);
    }

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
    public void ResolveFontFamilySource_WhenLatinFontIsExplicit_PutsLatinFontFirst()
    {
        var settings = AppearanceSettings.Default with
        {
            LatinFont = LatinFontPreset.SegoeUi,
            CjkFont = CjkFontPreset.MicrosoftYaHei
        };

        var resolved = settings.ResolveFontFamilySource("Microsoft YaHei UI");
        Assert.IsTrue(resolved.StartsWith("Segoe UI Variable Text, Segoe UI", StringComparison.OrdinalIgnoreCase));
        Assert.IsTrue(resolved.Contains("Microsoft YaHei UI", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void Normalize_InvalidLatinFont_FallsBackToDefault()
    {
        var settings = (AppearanceSettings.Default with { LatinFont = (LatinFontPreset)999 }).Normalize();
        Assert.AreEqual(LatinFontPreset.SystemDefault, settings.LatinFont);
    }
}
