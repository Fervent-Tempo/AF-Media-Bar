using System.Windows.Media;
using AFMediaBar.Resources;

namespace AFMediaBar.Classes.Settings;

/// <summary>An installed font family shown in the appearance selectors.</summary>
public sealed record FontFamilyChoice(string Name, string DisplayName);

/// <summary>Reads the font families available to WPF on this computer.</summary>
public static class InstalledFontCatalog
{
    public static IReadOnlyList<FontFamilyChoice> GetChoices(string followSystemResourceKey)
    {
        var installed = Fonts.SystemFontFamilies
            .Select(family => family.Source)
            .Where(name => !string.IsNullOrWhiteSpace(name) && !name.Contains(','))
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
            .Select(name => new FontFamilyChoice(name, name));

        return new[] { new FontFamilyChoice(string.Empty, Translations.Get(followSystemResourceKey)) }
            .Concat(installed)
            .ToArray();
    }

    public static string MatchSelection(string selectedName, IReadOnlyList<FontFamilyChoice> choices)
    {
        // The old Segoe preset contains two fallback names. Choose whichever is installed here.
        foreach (var name in selectedName.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var match = choices.FirstOrDefault(choice =>
                string.Equals(choice.Name, name, StringComparison.OrdinalIgnoreCase));
            if (match is not null) return match.Name;
        }

        return string.Empty;
    }
}
