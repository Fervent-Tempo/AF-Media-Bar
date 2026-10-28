// 首页的只读展示契约；不包含安装指令或播放器原始数据。
namespace AFMediaBar.Classes.Models.Home;

public sealed record HomeCopy(string Title, string Summary);
public sealed record HomeFeature(string Id, string Kind, string? Version, string? Image,
    string? Url, IReadOnlyDictionary<string, HomeCopy> Localizations);
public sealed record HomeSupportEntry(string Key, IReadOnlyDictionary<string, HomeCopy> Localizations);
public sealed record HomeCatalogue(int SchemaVersion, IReadOnlyList<HomeFeature> Items)
{
    public IReadOnlyList<HomeSupportEntry>? Support { get; init; }
}
public sealed record ProjectThread(int Number, string Title, bool IsDraft, DateTimeOffset UpdatedAt, string Url);
public sealed record ProjectActivitySnapshot(int? Stars, int? OpenIssues, int? RecentIssues, int? OpenPullRequests,
    IReadOnlyList<ProjectThread> Issues, IReadOnlyList<ProjectThread> PullRequests, DateTimeOffset? FetchedAt, bool IsCached = true);
public sealed record HomeCatalogueResult(HomeCatalogue Catalogue, DateTimeOffset? FetchedAt, bool IsCached);
