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
}
