using System.IO;
using System.Xml.Linq;

namespace AFMediaBar.Core.Tests;

[TestClass]
public sealed class LanguageResourceDictionaryTests
{
    private static readonly XNamespace XamlNamespace =
        "http://schemas.microsoft.com/winfx/2006/xaml";

    [TestMethod]
    public void LanguageDictionariesHaveUniqueKeysAndMatchingKeySets()
    {
        var languageDirectory = FindLanguageDirectory();
        var dictionaries = Directory.GetFiles(languageDirectory, "*.xaml")
            .OrderBy(Path.GetFileName, StringComparer.Ordinal)
            .Select(path => new
            {
                Path = path,
                Keys = XDocument.Load(path)
                    .Descendants()
                    .Select(element => element.Attribute(XamlNamespace + "Key")?.Value)
                    .Where(key => !string.IsNullOrWhiteSpace(key))
                    .Cast<string>()
                    .ToArray()
            })
            .ToArray();

        Assert.IsNotEmpty(dictionaries);
        foreach (var dictionary in dictionaries)
        {
            var duplicateKeys = dictionary.Keys
                .GroupBy(key => key, StringComparer.Ordinal)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .ToArray();

            Assert.IsEmpty(
                duplicateKeys,
                $"{Path.GetFileName(dictionary.Path)} contains duplicate keys: {string.Join(", ", duplicateKeys)}");
        }

        var expectedKeys = dictionaries[0].Keys.ToHashSet(StringComparer.Ordinal);
        foreach (var dictionary in dictionaries.Skip(1))
        {
            var actualKeys = dictionary.Keys.ToHashSet(StringComparer.Ordinal);
            Assert.IsTrue(
                expectedKeys.SetEquals(actualKeys),
                $"{Path.GetFileName(dictionary.Path)} does not match {Path.GetFileName(dictionaries[0].Path)}.");
        }
    }

    private static string FindLanguageDirectory()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            var candidate = Path.Combine(
                directory.FullName,
                "src",
                "AFMediaBar",
                "Resources",
                "Languages");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new DirectoryNotFoundException(
            "Could not locate src/AFMediaBar/Resources/Languages from the test output directory.");
    }
}
