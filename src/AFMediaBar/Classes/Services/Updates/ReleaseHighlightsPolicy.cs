// Parses and projects release content without I/O or UI dependencies.
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using AFMediaBar.Classes.Models.Updates;

namespace AFMediaBar.Classes.Services.Updates;

/// <summary>Validates the independent highlights catalogue and merges the current update snapshot.</summary>
public static class ReleaseHighlightsPolicy
{
    /// <summary>Limits remote and cached catalogues before parsing.</summary>
    public const int MaximumBytes = 2 * 1024 * 1024;

    /// <summary>Parses schema 1, rejecting incompatible or ambiguous catalogues.</summary>
    public static IReadOnlyList<ReleaseHighlights> Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("schemaVersion", out var schema) || schema.ValueKind != JsonValueKind.Number || !schema.TryGetInt32(out var number) || number != 1 ||
            !root.TryGetProperty("releases", out var releases) || releases.ValueKind != JsonValueKind.Array)
            throw new FormatException("Unsupported release highlights catalogue.");
        var result = new List<ReleaseHighlights>();
        var versions = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in releases.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object) throw new FormatException("Invalid release.");
            var version = String(entry, "version");
            if (version is null || !Regex.IsMatch(version, @"^\d+\.\d+\.\d+(?:\.\d+)?$") ||
                !UpdateVersionPolicy.TryParse(version, out var parsed))
                throw new FormatException("Invalid release version.");
            version = UpdateVersionPolicy.Format(parsed);
            if (!versions.Add(version)) throw new FormatException("Duplicate release version.");
            var dateText = String(entry, "releaseDate");
            DateOnly? date = null;
            if (dateText is not null)
            {
                if (!DateOnly.TryParseExact(dateText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var value))
                    throw new FormatException("Invalid release date.");
                date = value;
            }
            var notes = String(entry, "releaseNotesUrl");
            if (notes is not null && !IsSafeNotesUrl(notes)) throw new FormatException("Invalid release notes URL.");
            var languages = new Dictionary<string, ReleaseHighlightsText>(StringComparer.OrdinalIgnoreCase);
            if (!entry.TryGetProperty("localizations", out var texts) || texts.ValueKind != JsonValueKind.Object)
                throw new FormatException("Missing release texts.");
            foreach (var language in texts.EnumerateObject())
            {
                if (language.Value.ValueKind != JsonValueKind.Object || languages.ContainsKey(language.Name))
                    throw new FormatException("Invalid or duplicate release language.");
                var title = String(language.Value, "title");
                if (title is null || !language.Value.TryGetProperty("highlights", out var lines) || lines.ValueKind != JsonValueKind.Array)
                    throw new FormatException("Invalid release text.");
                var highlights = lines.EnumerateArray().Select(item => item.ValueKind == JsonValueKind.String ? item.GetString()?.Trim() : null)
                    .Where(item => !string.IsNullOrEmpty(item)).Select(item => item!).ToArray();
                if (highlights.Length == 0) throw new FormatException("Empty release highlights.");
                languages.Add(language.Name, new ReleaseHighlightsText(title, highlights));
            }
            if (languages.Count == 0) throw new FormatException("Missing release languages.");
            result.Add(new ReleaseHighlights(version, date, notes, languages));
        }
        if (result.Count == 0) throw new FormatException("Empty release catalogue.");
        return Sort(result);
    }

    /// <summary>Merges a manifest without duplicating equivalent versions or changing installation state.</summary>
    public static IReadOnlyList<ReleaseHighlights> Merge(IReadOnlyList<ReleaseHighlights> catalogue, UpdateManifest? manifest)
    {
        if (manifest is null || !UpdateVersionPolicy.TryParse(manifest.Version, out var parsed)) return Sort(catalogue);
        var version = UpdateVersionPolicy.Format(parsed);
        var existing = catalogue.FirstOrDefault(item => item.Version == version);
        if (existing is not null)
        {
            var texts = new Dictionary<string, ReleaseHighlightsText>(existing.Localizations, StringComparer.OrdinalIgnoreCase);
            texts.TryAdd("original", new ReleaseHighlightsText(manifest.Title ?? $"AF Media Bar {version}", manifest.Changelog));
            return Sort(catalogue.Select(item => ReferenceEquals(item, existing) ? item with { Localizations = texts } : item));
        }
        var text = new ReleaseHighlightsText(manifest.Title ?? $"AF Media Bar {version}", manifest.Changelog);
        var extra = new ReleaseHighlights(version, manifest.ReleaseDate,
            IsSafeNotesUrl(manifest.ReleaseNotesUrl) ? manifest.ReleaseNotesUrl : null,
            new Dictionary<string, ReleaseHighlightsText> { ["original"] = text });
        return Sort(catalogue.Append(extra));
    }

    /// <summary>Selects a translation, explicitly reporting when original text must be shown.</summary>
    public static (ReleaseHighlightsText Text, bool IsOriginal) ResolveText(ReleaseHighlights release, string language)
    {
        if (release.Localizations.TryGetValue(language, out var text)) return (text, false);
        foreach (var fallback in new[] { "original", "zh-Hans", "en", "zh-Hant" })
            if (release.Localizations.TryGetValue(fallback, out text)) return (text, true);
        return (release.Localizations.Values.First(), true);
    }

    /// <summary>Allows official HTTPS release pages only; content cannot launch arbitrary protocols.</summary>
    public static bool IsSafeNotesUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps &&
        uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) && uri.IsDefaultPort &&
        string.IsNullOrEmpty(uri.UserInfo) && uri.AbsolutePath.StartsWith("/Fervent-Tempo/AF-Media-Bar/releases/", StringComparison.OrdinalIgnoreCase);

    private static string? String(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()?.Trim() is { Length: > 0 } text ? text : null : null;

    private static IReadOnlyList<ReleaseHighlights> Sort(IEnumerable<ReleaseHighlights> releases) =>
        releases.OrderByDescending(item => { UpdateVersionPolicy.TryParse(item.Version, out var version); return version; }).ToArray();
}
