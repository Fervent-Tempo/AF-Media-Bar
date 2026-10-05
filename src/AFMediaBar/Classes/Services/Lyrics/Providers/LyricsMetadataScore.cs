// 曲目元数据评分的纯策略；不调用歌词库的评分或持有网络资源。
using System.Text.RegularExpressions;
using AFMediaBar.Classes.Models;
using F23.StringSimilarity;

namespace AFMediaBar.Classes.Services.Lyrics;

/// <summary>字符串相似度与线性时长衰减组成的统一匹配分。/ Unified metadata similarity score.</summary>
internal static partial class LyricsMetadataScore
{
    // 试听时长不能否决正确曲目：时长仅占 10%，释放的权重均分给标题和歌手。
    // 阈值和权重属于代码策略，不能重新暴露成匹配严格度设置。
    internal const int MinimumScore = 60;
    internal const double TitleWeight = 0.40;
    internal const double ArtistWeight = 0.40;
    internal const double AlbumWeight = 0.10;
    internal const double DurationWeight = 0.10;
    private static readonly JaroWinkler Similarity = new();

    /// <summary>评分组成；标题缺失时改用标题和歌手合并后的指纹相似度。</summary>
    internal readonly record struct Breakdown(double Title, double Artist, double Album, double Duration,
        double? Fingerprint)
    {
        public double UnroundedScore => 100 * (Fingerprint ??
            (TitleWeight * Title + ArtistWeight * Artist + AlbumWeight * Album + DurationWeight * Duration));
        public int TotalScore => (int)Math.Round(UnroundedScore);
    }

    public static int Calculate(LyricsRequest request, string? title, string? artist, string? album, double? duration) =>
        Calculate(request, [title ?? string.Empty], [artist ?? string.Empty], [album ?? string.Empty], duration);

    public static int Calculate(LyricsRequest request, string[] titles, string[] artists, string[] albums, double? duration) =>
        Explain(request, titles, artists, albums, duration).TotalScore;

    /// <summary>返回实际评分路径的各项相似度，供独立测试器解释分数，不执行搜索或取词。</summary>
    internal static Breakdown Explain(LyricsRequest request, string[] titles, string[] artists, string[] albums, double? duration)
    {
        var requestArtists = LyricsArtistPolicy.Split(request);
        var candidateArtists = artists.SelectMany(artist =>
            LyricsArtistPolicy.Split(artist, request.ArtistSeparators)).ToArray();
        if (string.IsNullOrWhiteSpace(request.Title) || !titles.Any(title => !string.IsNullOrWhiteSpace(title)))
        {
            var scores = from requestArtist in requestArtists
                         let query = Fingerprint($"{request.Title} {requestArtist}")
                         from title in titles
                         from artist in candidateArtists
                         let candidate = Fingerprint($"{title} {artist}")
                         select query.Length == 0 || candidate.Length == 0 ? 0 : Similarity.Similarity(query, candidate);
            return new(0, 0, 0, 0, scores.DefaultIfEmpty(0).Max());
        }

        var titleScore = titles.Select(title => Compare(request.Title, title)).DefaultIfEmpty(0).Max();
        // Preserve the existing best-artist-match policy, now comparing individual artists on both sides.
        var artistScore = (from requestArtist in requestArtists
                           from candidateArtist in candidateArtists
                           select Compare(requestArtist, candidateArtist)).DefaultIfEmpty(0).Max();
        var albumScore = albums.Select(album => Compare(request.Album, album)).DefaultIfEmpty(0).Max();
        return new(titleScore, artistScore, albumScore, CompareDuration(request.DurationSeconds, duration), null);
    }

    private static double Compare(string? left, string? right)
    {
        left = left?.Trim().ToLowerInvariant() ?? string.Empty;
        right = right?.Trim().ToLowerInvariant() ?? string.Empty;
        if (left.Length == 0 && right.Length == 0) return 1;
        if (left.Length == 0 || right.Length == 0) return 0;
        return Similarity.Similarity(left, right);
    }

    private static double CompareDuration(double? local, double? remote)
    {
        if (local is not > 0 || remote is not > 0 || !double.IsFinite(local.Value) || !double.IsFinite(remote.Value)) return 0;
        var difference = Math.Abs(local.Value - remote.Value);
        if (difference <= 1) return 1;
        if (difference >= 10) return 0;
        return 1 - (difference - 1) / 9;
    }

    private static string Fingerprint(string text) => string.Join(" ",
        NonWordCharacters().Replace(text.ToLowerInvariant(), " ")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries).OrderBy(token => token, StringComparer.Ordinal));

    [GeneratedRegex(@"[\p{P}\p{S}]")]
    private static partial Regex NonWordCharacters();
}
