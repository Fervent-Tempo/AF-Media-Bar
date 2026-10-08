// 构造歌词搜索关键词与播放器译名别名；保留请求原文用于评分和展示，不持有外部资源。
using System.Text.RegularExpressions;
using AFMediaBar.Classes.Models;

namespace AFMediaBar.Classes.Services.Lyrics;

/// <summary>带括号译名的标题和歌手的额外搜索词，不修改媒体快照。/ Search variants for translated metadata.</summary>
internal static partial class LyricsSearchQueryPolicy
{
    public static string WithoutTranslation(string value)
    {
        var match = TranslationSuffix().Match(value);
        // 汉字后缀通常是译名，但已知版本语义不能当作译名去掉。
        return match.Success && !VersionSuffix().IsMatch(match.Value) ? value[..match.Index].Trim() : value.Trim();
    }

    public static IReadOnlyList<string> Build(LyricsRequest request)
    {
        var title = WithoutTranslation(request.Title);
        var artist = string.Join(" ", LyricsArtistPolicy.Split(request).Select(WithoutTranslation));
        return new[] { $"{title} {artist}".Trim(), $"{request.Title} {request.Artist}".Trim() }
            .Concat(string.IsNullOrWhiteSpace(request.ArtistSeparators) ? [] :
                LyricsArtistPolicy.Split(request).Select(value => $"{title} {WithoutTranslation(value)}".Trim()))
            .Append(title)
            .Where(query => query.Length > 0).Distinct(StringComparer.Ordinal).ToArray();
    }

    internal static IReadOnlyList<string> BuildArtists(LyricsRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ArtistSeparators))
            return [WithoutTranslation(request.Artist)];
        var artists = LyricsArtistPolicy.Split(request).Select(WithoutTranslation).ToArray();
        return artists.Prepend(string.Join(" ", artists)).Distinct(StringComparer.Ordinal).ToArray();
    }

    [GeneratedRegex(@"\s*[（(][^（）()]*[\p{IsCJKUnifiedIdeographs}][^（）()]*[）)]\s*$")]
    private static partial Regex TranslationSuffix();

    [GeneratedRegex("现场|現場|伴奏|翻唱|混音|重制|重製|倍速|live|remix|cover|acoustic|instrumental", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex VersionSuffix();
}
