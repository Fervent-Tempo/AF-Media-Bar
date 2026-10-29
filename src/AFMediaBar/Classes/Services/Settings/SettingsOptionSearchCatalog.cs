using System.Xml.Linq;
using AFMediaBar.Classes.Models.Settings;
using AFMediaBar.Classes.Services.Localization;
using AFMediaBar.Resources;

namespace AFMediaBar.Classes.Services.Settings;

/// <summary>从页面的只读标记生成选项索引，避免复制一份设置项清单或创建编辑器。</summary>
public static class SettingsOptionSearchCatalog
{
    private sealed record Option(SettingsPageKey Page, string GroupId, string TitleKey, string? DescriptionKey);
    private static readonly Lazy<IReadOnlyList<Option>> Options = new(ReadOptions);

    /// <summary>以标题资源键作为选项身份，并保留其他语言的搜索词。</summary>
    public static IEnumerable<SettingsSearchEntry> ForLanguage(LocalizationLanguage language)
    {
        foreach (var option in Options.Value)
        {
            var destination = SettingsPageCatalog.ResolveDestination(option.Page, option.GroupId);
            if (SettingsPageCatalog.Find(destination.Page, SettingsMode.Taskbar) is not { } page) continue;
            var title = Text(option.TitleKey, language);
            var description = option.DescriptionKey is { } key ? Text(key, language) : string.Empty;
            var keywords = Enum.GetValues<LocalizationLanguage>()
                .Select(value => Text(option.TitleKey, value)).Distinct().ToArray();
            yield return new(destination.Page, destination.GroupId, Translations.Get(page.TitleKey, language), title, description, keywords)
            { OptionId = option.TitleKey };
        }
    }

    private static string Text(string key, LocalizationLanguage language) => key.StartsWith('@') ? key[1..] : Translations.Get(key, language);

    private static IReadOnlyList<Option> ReadOptions()
    {
        var assembly = typeof(SettingsOptionSearchCatalog).Assembly;
        var options = new List<Option>();
        foreach (var name in assembly.GetManifestResourceNames().Where(name => name.StartsWith("AFMediaBar.SettingsSearch.", StringComparison.Ordinal)))
        {
            using var stream = assembly.GetManifestResourceStream(name)!;
            var document = XDocument.Load(stream);
            var className = document.Root?.Attribute(XName.Get("Class", "http://schemas.microsoft.com/winfx/2006/xaml"))?.Value.Split('.').Last();
            var pageName = className?.Replace("Page", string.Empty, StringComparison.Ordinal) switch
            {
                "ComponentsSettings" => "Components",
                "WidgetsSettings" => "Widgets",
                "ExtraFeatures" => "MediaAndNotifications",
                var other => other
            };
            if (!Enum.TryParse<SettingsPageKey>(pageName, out var page) || SettingsPageCatalog.Find(page, SettingsMode.Taskbar) is null) continue;
            foreach (var row in document.Descendants().Where(element => element.Name.LocalName == "SettingsRow"))
            {
                var group = row.Ancestors().FirstOrDefault(element => element.Name.LocalName == "SettingsGroup")?.Attribute("GroupId")?.Value;
                var title = ResourceKey(row.Attribute("Title")?.Value);
                if (group is null || title is null) continue;
                options.Add(new(page, group, title, ResourceKey(row.Attribute("Description")?.Value)));
            }
        }
        return options.Distinct().ToArray();
    }

    private static string? ResourceKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        const string prefix = "{DynamicResource Loc.";
        if (value.StartsWith(prefix, StringComparison.Ordinal) && value.EndsWith('}')) return value[prefix.Length..^1];
        return value.StartsWith('{') ? null : "@" + value;
    }
}
