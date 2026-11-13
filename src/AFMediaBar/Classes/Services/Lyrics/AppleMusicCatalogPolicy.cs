// Apple Music 译名还原的纯策略：来源识别、曲库请求地址、候选校验、原名选择与两轮取舍；不做 I/O。
using System.Globalization;
using System.Text;
using AFMediaBar.Classes.Models;
using F23.StringSimilarity;

namespace AFMediaBar.Classes.Services.Lyrics;

/// <summary>Apple 曲库里某一地区的一条歌曲记录。</summary>
internal sealed record AppleMusicTrack(
    string Region, long TrackId, string Name, string Artist, string Album, double? DurationSeconds, string Genre);

/// <summary>还原出的原名及曲库时长；只用于取词请求，不改变界面显示与缓存键。</summary>
internal sealed record AppleMusicOriginal(string Title, string Artist, string Album, string Region, double? DurationSeconds = null);

/// <summary>两轮取词各自的结果；分数只与本轮的可靠线比较，两轮之间不比分。</summary>
internal readonly record struct AppleMusicRounds(
    LyricsResult? FirstOnline, LyricsResult? FirstLocal, LyricsResult? SecondOnline, LyricsResult? SecondLocal);

/// <summary>Apple Music 译名还原的判定与选择规则。</summary>
internal static class AppleMusicCatalogPolicy
{
    internal const string Japan = "JP";
    internal const string UnitedStates = "US";
    internal const string China = "CN";
    internal const string Korea = "KR";
    internal const string Taiwan = "TW";
    internal const string HongKong = "HK";

    /// <summary>CN、KR 区的 search 不返回结果；JP 区能用译名检索，JP 区没有候选时再用 TW 区找华语歌。</summary>
    internal static readonly string[] SearchRegions = [Japan, Taiwan];

    /// <summary>US 区名称与 SMTC 译名一致，CN 区给出华语歌的简体原名；两者并发 lookup 做校验。</summary>
    internal static readonly string[] LookupRegions = [UnitedStates, China];

    internal const int ResultLimit = 25;
    internal const double DurationToleranceSeconds = 2;
    private const double ArtistSimilarityFloor = 0.9;

    /// <summary>Apple Music for Windows 的 AUMID 前缀；以实机日志为准。</summary>
    private const string SourceMarker = "AppleInc.AppleMusic";

    /// <summary>Apple Music 在 SMTC 歌手字段里用它连接歌手与专辑（实机：「爱缪 — Her Blue Sky - EP」）。</summary>
    private const string ArtistAlbumSeparator = " — ";

    private const string Endpoint = "https://itunes.apple.com/";

    /// <summary>都没有偏好地区给出答案时，按此顺序找任一含中日韩文字且与译名不同的名称。</summary>
    private static readonly string[] _fallbackRegions = [Japan, Korea, China, Taiwan, HongKong, UnitedStates];

    private static readonly JaroWinkler _similarity = new();

    public static bool IsAppleMusic(string? playbackSourceId) =>
        !string.IsNullOrWhiteSpace(playbackSourceId) &&
        playbackSourceId.Contains(SourceMarker, StringComparison.OrdinalIgnoreCase);

    /// <summary>可靠：有可用歌词、达到优先源的直接采纳线，且不是低置信命中。</summary>
    public static bool IsReliable(LyricsResult? result) =>
        LyricsService.IsAvailable(result) && result!.MatchScore >= LyricsRetrievalPolicy.PreferredMinimumScore && !result.LowConfidence;

    /// <summary>
    /// 曲库查询用的请求：SMTC 歌手字段是「歌手 — 专辑」时按首个分隔符拆开，搜索与校验只用歌手，专辑用于区分同名版本；
    /// 没有分隔符时原样返回。只影响原名解析，首轮取词仍用播放器原始元数据。
    /// </summary>
    public static LyricsRequest ForCatalog(LyricsRequest request)
    {
        var index = request.Artist.IndexOf(ArtistAlbumSeparator, StringComparison.Ordinal);
        if (index <= 0) return request;
        var album = request.Artist[(index + ArtistAlbumSeparator.Length)..].Trim();
        return request with { Artist = request.Artist[..index].Trim(), Album = album.Length > 0 ? album : request.Album };
    }

    public static Uri SearchUri(string term, string region) =>
        new($"{Endpoint}search?term={Uri.EscapeDataString(term)}&country={region.ToLowerInvariant()}" +
            $"&media=music&entity=song&limit={ResultLimit}");

    public static Uri LookupUri(IEnumerable<long> trackIds, string region) =>
        new($"{Endpoint}lookup?id={string.Join(",", trackIds)}&country={region.ToLowerInvariant()}" +
            $"&media=music&entity=song&limit={ResultLimit}");

    /// <summary>SMTC 有时长时只保留相差不超过 2 秒的候选；没有时长时不筛，改由校验要求歌手一致（时长都对不上时解析器同样处理）。</summary>
    public static IReadOnlyList<AppleMusicTrack> FilterByDuration(IEnumerable<AppleMusicTrack> tracks, double? durationSeconds) =>
        durationSeconds is > 0
            ? tracks.Where(track => track.DurationSeconds is { } seconds &&
                                    Math.Abs(seconds - durationSeconds.Value) <= DurationToleranceSeconds).ToArray()
            : tracks.ToArray();

    /// <summary>候选在各地区的名称：搜索得到的锚点，加上 lookup 中同一 trackId 的记录。</summary>
    public static Dictionary<string, AppleMusicTrack> NamesOf(
        AppleMusicTrack anchor, IReadOnlyDictionary<string, IReadOnlyList<AppleMusicTrack>> lookups)
    {
        var names = new Dictionary<string, AppleMusicTrack>(StringComparer.Ordinal) { [anchor.Region] = anchor };
        foreach (var (region, tracks) in lookups)
        {
            if (names.ContainsKey(region)) continue;
            if (tracks.FirstOrDefault(track => track.TrackId == anchor.TrackId) is { } match) names[region] = match;
        }
        return names;
    }

    /// <summary>
    /// 任一地区的曲名与 SMTC 一致、且同一地区的歌手一致或高度相似才算同一首；SMTC 没有时长时歌手必须一致。
    /// 多个候选通过时先取专辑一致的（同一首歌常同时收在 EP 与精选集里），再取时长最接近的，同差保持搜索顺序。
    /// </summary>
    public static AppleMusicTrack? SelectCandidate(LyricsRequest request, IReadOnlyList<AppleMusicTrack> anchors,
        IReadOnlyDictionary<string, IReadOnlyList<AppleMusicTrack>> lookups)
    {
        var strictArtist = request.DurationSeconds is not > 0;
        var album = Normalize(request.Album);
        return anchors
            .Select(anchor => (Anchor: anchor, Names: NamesOf(anchor, lookups)))
            .Where(candidate => candidate.Names.Values.Any(track => Matches(request, track, strictArtist)))
            .OrderBy(candidate => album.Length > 0 && candidate.Names.Values.Any(track => Normalize(track.Album) == album) ? 0 : 1)
            .ThenBy(candidate => request.DurationSeconds is > 0 && candidate.Anchor.DurationSeconds is { } seconds
                ? Math.Abs(seconds - request.DurationSeconds.Value) : 0)
            .Select(candidate => candidate.Anchor)
            .FirstOrDefault();
    }

    /// <summary>
    /// 选原名还缺哪些地区：含假名只看 JP；含谚文或 K-Pop 查 KR；只有汉字时 CN 不足以判断才查 TW、HK，Cantopop 直接查 HK、TW。
    /// </summary>
    public static IReadOnlyList<string> ExtraRegions(LyricsRequest request, AppleMusicTrack anchor,
        IReadOnlyDictionary<string, AppleMusicTrack> names)
    {
        var scripts = ScriptsOf(anchor);
        string[] needed;
        if (scripts.HasFlag(Scripts.Kana)) needed = [];
        else if (scripts.HasFlag(Scripts.Hangul) || HasGenre(names, "K-Pop")) needed = [Korea];
        else if (!scripts.HasFlag(Scripts.Han)) needed = [];
        else if (HasGenre(names, "Cantopop")) needed = [HongKong, Taiwan];
        else needed = names.TryGetValue(China, out var china) && HasCjk(china) ? [] : [Taiwan, HongKong];
        return needed.Where(region => !names.ContainsKey(region)).ToArray();
    }

    /// <summary>
    /// 按文字选原名：含假名取 JP，含谚文取 KR，只有汉字依次取 CN、TW、HK（Cantopop 依次取 HK、TW、CN）。
    /// 偏好地区里第一个含中日韩文字的名称作数：与译名相同说明 SMTC 显示的已是原名。
    /// 偏好地区都没有中日韩文字时，回退到任一含中日韩文字且与译名不同的地区；否则视为无原名。
    /// </summary>
    public static AppleMusicOriginal? SelectOriginal(LyricsRequest request, AppleMusicTrack anchor,
        IReadOnlyDictionary<string, AppleMusicTrack> names)
    {
        foreach (var region in PreferredRegions(anchor, names))
        {
            if (!names.TryGetValue(region, out var track) || !HasCjk(track)) continue;
            return Differs(request, track) ? Original(track) : null;
        }
        foreach (var region in _fallbackRegions)
        {
            if (names.TryGetValue(region, out var track) && HasCjk(track) && Differs(request, track)) return Original(track);
        }
        return null;
    }

    /// <summary>
    /// 两轮的固定优先级，不比较两轮分数：第二轮有佐证的本地命中 → 首轮可靠 → 第二轮在线可靠 → 首轮有歌词 →
    /// 第二轮在线有歌词 → 低置信本地（首轮先）→ 确认无歌词（首轮先）→ 无结果。
    /// 有歌词的两项各自在本轮内部沿用本地让位规则：只靠标题命中的本地结果明显高于同轮在线时保留本地。
    /// </summary>
    public static LyricsResult? Pick(AppleMusicRounds rounds)
    {
        var (firstOnline, firstLocal, secondOnline, secondLocal) = rounds;
        if (IsCorroborated(secondLocal)) return secondLocal;
        if (IsReliable(firstOnline)) return firstOnline;
        if (IsReliable(secondOnline)) return secondOnline;
        if (LyricsService.IsAvailable(firstOnline)) return WithinRound(firstLocal, firstOnline!);
        if (LyricsService.IsAvailable(secondOnline)) return WithinRound(secondLocal, secondOnline!);
        if (LyricsService.IsAvailable(firstLocal)) return firstLocal;
        if (LyricsService.IsAvailable(secondLocal)) return secondLocal;
        if (firstOnline is { Status: LyricsResultStatus.NoLyrics }) return firstOnline;
        return secondOnline is { Status: LyricsResultStatus.NoLyrics } ? secondOnline : null;
    }

    /// <summary>有歌手或时长佐证的本地命中。</summary>
    public static bool IsCorroborated(LyricsResult? local) => LyricsService.IsAvailable(local) && !local!.LowConfidence;

    private static LyricsResult WithinRound(LyricsResult? local, LyricsResult online) =>
        local is not null && LyricsService.IsAvailable(local) && LyricsService.ShouldPreferLocal(local, online) ? local : online;

    private static IEnumerable<string> PreferredRegions(AppleMusicTrack anchor, IReadOnlyDictionary<string, AppleMusicTrack> names)
    {
        var scripts = ScriptsOf(anchor);
        if (scripts.HasFlag(Scripts.Kana)) return [Japan];
        if (scripts.HasFlag(Scripts.Hangul) || HasGenre(names, "K-Pop")) return [Korea];
        if (!scripts.HasFlag(Scripts.Han)) return [];
        return HasGenre(names, "Cantopop") ? [HongKong, Taiwan, China] : [China, Taiwan, HongKong];
    }

    private static AppleMusicOriginal Original(AppleMusicTrack track) =>
        new(track.Name, track.Artist, track.Album, track.Region, track.DurationSeconds);

    /// <summary>
    /// 第二轮的时长：播放器没有时长、或与曲库相差超过 2 秒时（切歌瞬间常沿用上一首）改用曲库时长，否则保留播放器时长。
    /// </summary>
    public static double? SecondRoundDuration(double? playerSeconds, double? catalogSeconds) =>
        catalogSeconds is > 0 && (playerSeconds is not > 0 || Math.Abs(playerSeconds.Value - catalogSeconds.Value) > DurationToleranceSeconds)
            ? catalogSeconds
            : playerSeconds;

    private static bool Matches(LyricsRequest request, AppleMusicTrack track, bool strictArtist)
    {
        if (Normalize(request.Title) != Normalize(track.Name)) return false;
        var expected = Normalize(request.Artist);
        var actual = Normalize(track.Artist);
        if (expected == actual) return true;
        return !strictArtist && expected.Length > 0 && actual.Length > 0 &&
               _similarity.Similarity(expected, actual) >= ArtistSimilarityFloor;
    }

    private static bool Differs(LyricsRequest request, AppleMusicTrack track) =>
        Normalize(request.Title) != Normalize(track.Name) || Normalize(request.Artist) != Normalize(track.Artist);

    private static bool HasGenre(IReadOnlyDictionary<string, AppleMusicTrack> names, string genre) =>
        names.Values.Any(track => track.Genre.Equals(genre, StringComparison.OrdinalIgnoreCase));

    private static bool HasCjk(AppleMusicTrack track) => ScriptsOf(track) != Scripts.None;

    /// <summary>忽略大小写、全半角与空白差异。</summary>
    private static string Normalize(string? text)
    {
        var builder = new StringBuilder();
        foreach (var character in (text ?? string.Empty).Normalize(NormalizationForm.FormKC))
        {
            if (!char.IsWhiteSpace(character)) builder.Append(char.ToLower(character, CultureInfo.InvariantCulture));
        }
        return builder.ToString();
    }

    [Flags]
    private enum Scripts
    {
        None = 0,
        Kana = 1,
        Hangul = 2,
        Han = 4
    }

    private static Scripts ScriptsOf(AppleMusicTrack track) => ScriptsOf(track.Name) | ScriptsOf(track.Artist);

    private static Scripts ScriptsOf(string text)
    {
        var scripts = Scripts.None;
        foreach (var rune in text.EnumerateRunes())
        {
            var value = rune.Value;
            // 中点（U+30FB）在中文名里也常见，不作为假名证据。
            if (value is >= 0x3041 and <= 0x30FF and not 0x30FB || value is >= 0x31F0 and <= 0x31FF or >= 0xFF66 and <= 0xFF9F)
                scripts |= Scripts.Kana;
            else if (value is >= 0xAC00 and <= 0xD7AF or >= 0x1100 and <= 0x11FF or >= 0x3130 and <= 0x318F)
                scripts |= Scripts.Hangul;
            else if (value is >= 0x4E00 and <= 0x9FFF or >= 0x3400 and <= 0x4DBF or >= 0xF900 and <= 0xFAFF or >= 0x20000 and <= 0x3134F)
                scripts |= Scripts.Han;
        }
        return scripts;
    }
}
