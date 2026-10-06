// 验证三份界面语言 resx 均嵌入主程序集，且运行时查找不会因迁移而退回键名。
// This test guards resource packaging and lookup; Translations owns the runtime language table.
using System.Collections;
using System.Globalization;
using System.Resources;
using AFMediaBar.Classes.Services.Localization;
using AFMediaBar.Resources;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

[TestClass]
public sealed class LocalizationResourcesTests
{
    [TestMethod]
    public void AllLanguageResourcesHaveTheSameKeysAndResolveThroughTranslations()
    {
        var languages = new[]
        {
            (Name: "StringsZhHans", Language: LocalizationLanguage.SimplifiedChinese),
            (Name: "StringsZhHant", Language: LocalizationLanguage.TraditionalChinese),
            (Name: "StringsEn", Language: LocalizationLanguage.English),
        };

        HashSet<string>? baseline = null;
        foreach (var (name, language) in languages)
        {
            var manager = new ResourceManager($"AFMediaBar.Resources.{name}", typeof(Translations).Assembly);
            var resources = manager.GetResourceSet(CultureInfo.InvariantCulture, createIfNotExists: true, tryParents: false);
            Assert.IsNotNull(resources, $"Missing embedded resource: {name}");

            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (DictionaryEntry entry in resources)
            {
                var key = (string)entry.Key;
                var value = entry.Value as string;
                Assert.IsFalse(string.IsNullOrEmpty(value), $"Empty translation: {name}/{key}");
                Assert.IsTrue(keys.Add(key), $"Duplicate translation: {name}/{key}");
                Assert.AreEqual(value, Translations.Get(key, language), $"Lookup mismatch: {name}/{key}");
            }

            Assert.AreEqual(703, keys.Count, $"Unexpected translation count: {name}");
            if (baseline is null)
                baseline = keys;
            else
                Assert.IsTrue(baseline.SetEquals(keys), $"Translation keys differ: {name}");
        }

        Assert.IsNotNull(baseline);
        Assert.AreEqual(baseline.Count, Translations.Count);
        Assert.IsTrue(baseline.SetEquals(Translations.Keys));
    }
}
