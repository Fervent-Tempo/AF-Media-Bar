using System.Globalization;
using System.Resources;
using AFMediaBar.Classes.Services.Localization;
using AFMediaBar.Resources;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>验证越南语选择与原始资源完整性，防止语言回退掩盖缺项。</summary>
[TestClass]
public sealed class VietnameseLocalizationTests
{
    [TestMethod]
    public void PolicyResolvesVietnameseExplicitSetting()
    {
        var resolved = InterfaceLanguagePolicy.Resolve(InterfaceLanguage.Vietnamese, CultureInfo.InvariantCulture);
        Assert.AreEqual(LocalizationLanguage.Vietnamese, resolved);
    }

    [TestMethod]
    public void PolicyResolvesVietnameseSystemCulture()
    {
        var viVN = new CultureInfo("vi-VN");
        var resolved = InterfaceLanguagePolicy.Resolve(InterfaceLanguage.System, viVN);
        Assert.AreEqual(LocalizationLanguage.Vietnamese, resolved);

        var vi = new CultureInfo("vi");
        Assert.AreEqual(LocalizationLanguage.Vietnamese, InterfaceLanguagePolicy.ResolveSystem(vi));
    }

    [TestMethod]
    public void PolicyReturnsViCultureName()
    {
        Assert.AreEqual("vi", InterfaceLanguagePolicy.ToCultureName(LocalizationLanguage.Vietnamese));
    }

    [TestMethod]
    public void AllKeysHaveNonEmptyVietnameseTranslation()
    {
        Assert.IsTrue(Translations.Keys.Count > 700);

        var manager = new ResourceManager("AFMediaBar.Resources.StringsVi", typeof(Translations).Assembly);
        using var resources = manager.GetResourceSet(CultureInfo.InvariantCulture, createIfNotExists: true, tryParents: false);
        Assert.IsNotNull(resources);
        var missing = Translations.Keys.Where(key => string.IsNullOrWhiteSpace(resources.GetString(key))).ToArray();
        Assert.AreEqual(0, missing.Length, $"Missing Vietnamese resources: {string.Join(", ", missing)}");

        foreach (var key in Translations.Keys)
        {
            var text = resources.GetString(key);
            Assert.IsFalse(string.IsNullOrWhiteSpace(text), $"Vietnamese translation for '{key}' is empty");
            Assert.AreNotEqual(key, text, $"Missing Vietnamese translation for key '{key}'");
        }
    }

    [TestMethod]
    public void LanguageNameIsProperlyLocalized()
    {
        Assert.AreEqual("Tiếng Việt", Translations.Get("About.Language.Vietnamese", LocalizationLanguage.Vietnamese));
        Assert.AreEqual("Tiếng Việt", Translations.Get("About.Language.Vietnamese", LocalizationLanguage.English));
        Assert.AreEqual("Tiếng Việt", Translations.Get("About.Language.Vietnamese", LocalizationLanguage.SimplifiedChinese));
        Assert.AreEqual("Tiếng Việt", Translations.Get("About.Language.Vietnamese", LocalizationLanguage.TraditionalChinese));
    }
}
