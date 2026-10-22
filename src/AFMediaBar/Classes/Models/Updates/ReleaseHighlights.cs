// Read-only release highlights contracts; no download or installation information belongs here.
namespace AFMediaBar.Classes.Models.Updates;

/// <summary>One language's reviewed release title and highlights.</summary>
public sealed record ReleaseHighlightsText(string Title, IReadOnlyList<string> Highlights)
{
    public ReleaseHighlightsContent? Content { get; init; }
}

/// <summary>版本简介与按主题组织的纯文本更新条目。</summary>
public sealed record ReleaseHighlightsContent(string Summary, IReadOnlyList<ReleaseHighlightsSection> Sections);

/// <summary>始终可见的更新主题及其条目。</summary>
public sealed record ReleaseHighlightsSection(string Title, IReadOnlyList<ReleaseHighlightsEntry> Items);

/// <summary>可展开的更新条目；标题与详情均不执行标记。</summary>
public sealed record ReleaseHighlightsEntry(string Title, string Description);

/// <summary>A formal release with its language variants and official notes link.</summary>
public sealed record ReleaseHighlights(string Version, DateOnly? ReleaseDate, string? ReleaseNotesUrl,
    IReadOnlyDictionary<string, ReleaseHighlightsText> Localizations);

/// <summary>A catalogue load result; failures retain the last usable releases.</summary>
public sealed record ReleaseHighlightsResult(IReadOnlyList<ReleaseHighlights> Releases, bool IsCached,
    DateTimeOffset? FetchedAt, string? FailureKey);
