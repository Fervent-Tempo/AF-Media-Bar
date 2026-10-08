// Read-only release highlights contracts; no download or installation information belongs here.
namespace AFMediaBar.Classes.Models.Updates;

/// <summary>One language's reviewed release title and highlights.</summary>
public sealed record ReleaseHighlightsText(string Title, IReadOnlyList<string> Highlights);

/// <summary>A formal release with its language variants and official notes link.</summary>
public sealed record ReleaseHighlights(string Version, DateOnly? ReleaseDate, string? ReleaseNotesUrl,
    IReadOnlyDictionary<string, ReleaseHighlightsText> Localizations);

/// <summary>A catalogue load result; failures retain the last usable releases.</summary>
public sealed record ReleaseHighlightsResult(IReadOnlyList<ReleaseHighlights> Releases, bool IsCached,
    DateTimeOffset? FetchedAt, string? FailureKey);
