// 本地歌词文件来源：按目录顺序找同名或同元数据的歌词文件，命中即用；不联网、不读音频标签。
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Settings;
using F23.StringSimilarity;
using KrcDecrypter = Lyricify.Lyrics.Decrypter.Krc.Decrypter;
using QrcDecrypter = Lyricify.Lyrics.Decrypter.Qrc.Decrypter;

namespace AFMediaBar.Classes.Services.Lyrics;

/// <summary>
/// 从用户添加的目录里找歌词文件。
///
/// 本地文件的元数据和在线曲库不一样：歌词文件经常只有正文，没有歌手或时长。所以评分只在双方都有值的字段上比较，
/// 缺项不计 0 分——否则一份标题正确的文件会因为没写 [ar:] 丢掉四成权重、被门槛挡掉，而标签全空的无关文件反而靠
/// “两边都为空算满分”凑够分数。
/// </summary>
public sealed partial class LocalFileLyricsProvider : ILyricsProvider
{
    /// <summary>认得出来的歌词文件；翻译和音译各有自己的后缀，进去后单独识别。</summary>
    private static readonly string[] Extensions = [".lrc", ".qrc", ".krc", ".tlyric", ".romalrc"];

    /// <summary>单文件上限，超过它的多半不是歌词。</summary>
    private const int MaxFileBytes = 2 * 1024 * 1024;

    /// <summary>与在线来源保持一致的门槛：标题对不上就不是同一首。</summary>
    private const int MinimumScore = 60;

    /// <summary>字段权重：标题为主，歌手次之，时长只作辅助。</summary>
    private const double TitleWeight = 0.6;
    private const double ArtistWeight = 0.3;
    private const double DurationWeight = 0.1;

    /// <summary>算得上「有独立佐证」的下限：标题几乎一致，且歌手或时长里至少有一项也对得上。</summary>
    private const double ConfidenceTitleFloor = 0.85;
    private const double ConfidenceArtistFloor = 0.6;
    private const double ConfidenceDurationFloor = 0.8;

    /// <summary>元数据只扫文件头部，整份歌词扫一遍没有意义。</summary>
    private const int MetadataScanBytes = 4096;

    private static readonly JaroWinkler Similarity = new();

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    static LocalFileLyricsProvider()
    {
        // 中文歌词大多是 GBK 落盘，严格 UTF-8 解码失败时要能回落到 GB18030。
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public string SourceName => LyricsSourceCatalog.LocalFile;

    public Task<LyricsResult?> GetLyricsAsync(LyricsRequest request, CancellationToken cancellationToken)
    {
        var settings = SettingsManager.Current.LyricsSource.Normalize();
        if (!settings.LocalEnabled || settings.LocalFolders is not { Count: > 0 } folders || string.IsNullOrWhiteSpace(request.Title))
            return Task.FromResult<LyricsResult?>(null);
        return Task.Run(() => Search(request, folders, cancellationToken), cancellationToken);
    }

    private static LyricsResult? Search(LyricsRequest request, IReadOnlyList<string> folders, CancellationToken cancellationToken)
    {
        Candidate? best = null;
        foreach (var folder in folders)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var path in ListLyricFiles(folder))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var (baseName, role) = Classify(path);
                // 翻译和罗马音只是主歌词的附件，不单独参与评分。
                if (role != LyricRole.Main) continue;
                if (!LooksRelated(baseName, request.Title)) continue;
                var candidate = Evaluate(request, path, baseName);
                // 没有歌词的候选直接跳过：本地文件没内容就等于没找到，不能被它顶掉带词的那一份。
                if (candidate is null || candidate.Document.Lines.Count == 0) continue;
                if (best is null || Better(candidate, best)) best = candidate;
            }
        }

        // 命中但没歌词不顶掉在线结果：这时当没找到，继续原有链路。
        if (best is not { Score: >= MinimumScore }) return null;
        return new LyricsResult(LyricsSourceCatalog.LocalFile, best.Document)
        {
            MatchScore = best.Score,
            LowConfidence = best.LowConfidence
        };
    }

    /// <summary>歌词文件在曲目里的角色。</summary>
    private enum LyricRole
    {
        Main,
        Translation,
        Romanization
    }

    /// <summary>
    /// 拆出基础名与角色。QQ 音乐下载的是「歌名_qm.qrc」，翻译和罗马音是同目录的 _qmts / _qmRoma；
    /// 网易云把翻译放在 .tlyric、音译放在 .romalrc。基础名去掉这些后缀，才能和请求标题对上、也才能互相配对。
    /// </summary>
    private static (string BaseName, LyricRole Role) Classify(string path)
    {
        var extension = Path.GetExtension(path);
        var name = Path.GetFileNameWithoutExtension(path);
        if (extension.Equals(".tlyric", StringComparison.OrdinalIgnoreCase)) return (name, LyricRole.Translation);
        if (extension.Equals(".romalrc", StringComparison.OrdinalIgnoreCase)) return (name, LyricRole.Romanization);
        if (name.EndsWith("_qmRoma", StringComparison.OrdinalIgnoreCase)) return (name[..^7], LyricRole.Romanization);
        if (name.EndsWith("_qmts_tw", StringComparison.OrdinalIgnoreCase)) return (name[..^8], LyricRole.Translation);
        if (name.EndsWith("_qmts", StringComparison.OrdinalIgnoreCase)) return (name[..^5], LyricRole.Translation);
        if (name.EndsWith("_qm", StringComparison.OrdinalIgnoreCase)) return (name[..^3], LyricRole.Main);
        return (name, LyricRole.Main);
    }

    private static IReadOnlyList<string> ListLyricFiles(string folder)
    {
        try
        {
            return Directory.GetFiles(folder, "*", SearchOption.AllDirectories)
                .Where(path => Extensions.Any(extension => path.EndsWith(extension, StringComparison.OrdinalIgnoreCase)))
                .ToArray();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return [];
        }
    }

    /// <summary>文件名规范化后互相包含才算候选，省掉对每个文件的读取和解析。</summary>
    private static bool LooksRelated(string fileName, string title)
    {
        var name = Normalize(fileName);
        var wanted = Normalize(title);
        if (name.Length < 2 || wanted.Length < 2) return false;
        if (name.Contains(wanted, StringComparison.Ordinal) || wanted.Contains(name, StringComparison.Ordinal)) return true;

        // 文件名常写成「歌手 - 歌名」，请求标题却带 (Live)、(Single Version) 这类后缀：
        // 两边都去掉括号内容再比一次，否则这类正常写法会被筛选直接漏掉。
        var nameCore = Normalize(Brackets().Replace(fileName, string.Empty));
        var wantedCore = Normalize(Brackets().Replace(title, string.Empty));
        return nameCore.Length >= 2 && wantedCore.Length >= 2 &&
               (nameCore.Contains(wantedCore, StringComparison.Ordinal) ||
                wantedCore.Contains(nameCore, StringComparison.Ordinal));
    }

    private static Candidate? Evaluate(LyricsRequest request, string path, string baseName)
    {
        byte[] bytes;
        try
        {
            var info = new FileInfo(path);
            if (info.Length is <= 0 or > MaxFileBytes) return null;
            bytes = File.ReadAllBytes(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        var text = LoadText(path, bytes);
        if (string.IsNullOrWhiteSpace(text)) return null;
        var metadata = ReadMetadata(text);
        var (fileTitle, fileArtist, fileDuration) = SplitFileName(request, baseName);
        var duration = metadata.Duration ?? fileDuration;
        // 元数据和文件名是两条独立线索，各评一次取更强的一条：哪边写全了就用哪边，不要求两份写法一致。
        var fromMetadata = Assess(request, metadata.Title, metadata.Artist, duration);
        var fromFileName = Assess(request, fileTitle, fileArtist, duration);
        var assessment = Stronger(fromMetadata, fromFileName);
        if (assessment.Score < MinimumScore) return null;

        var (translation, romanization) = ReadCompanions(path, baseName);
        var document = LyricsTextParser.Parse(text, translation, romanization, request,
            request.DurationSeconds ?? duration, request.FilterInfoLines);
        return new Candidate(path, assessment.Score, IsLowConfidence(fromMetadata, fromFileName), document);
    }

    /// <summary>按扩展名取文本：QRC/KRC 先解密，其余按编码探测。</summary>
    private static string? LoadText(string path, byte[] bytes) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".qrc" => DecryptQrc(bytes),
            ".krc" => DecryptKrc(bytes),
            _ => Decode(bytes)
        };

    /// <summary>
    /// QQ 音乐的 .qrc 有两种形态：网络接口给的是十六进制文本，客户端缓存的是带 magic 的二进制。
    /// 这里只解得开前者（复用依赖里的实现）；后者是客户端私有的 QMC 加密，解不开就当没有歌词。
    /// </summary>
    private static string? DecryptQrc(byte[] bytes)
    {
        try
        {
            var hex = Decode(bytes).Trim();
            if (hex.Length < 16 || hex.Length % 2 != 0 || !hex.All(Uri.IsHexDigit)) return null;
            var text = QrcDecrypter.DecryptLyrics(hex);
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>酷狗的 .krc 是二进制；依赖里的实现要求 Base64 入参，内部自己跳头、异或、解压。</summary>
    private static string? DecryptKrc(byte[] bytes)
    {
        try
        {
            var text = KrcDecrypter.DecryptLyrics(Convert.ToBase64String(bytes));
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>同目录里找这首歌的翻译与罗马音，交给解析器合并。找不到就各留空。</summary>
    private static (string? Translation, string? Romanization) ReadCompanions(string mainPath, string baseName)
    {
        var directory = Path.GetDirectoryName(mainPath);
        if (string.IsNullOrEmpty(directory)) return (null, null);
        string? translation = null, romanization = null;
        foreach (var (suffix, role) in CompanionSuffixes)
        {
            var path = Path.Combine(directory, baseName + suffix);
            if (!File.Exists(path)) continue;
            try
            {
                var text = LoadText(path, File.ReadAllBytes(path));
                if (string.IsNullOrWhiteSpace(text)) continue;
                if (role == LyricRole.Translation) translation ??= text;
                else romanization ??= text;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // 单个附件读不了不影响主歌词。
            }
        }
        return (translation, romanization);
    }

    /// <summary>发帖和歌词文件常见两种写法：QQ 的 _qmts/_qmRoma，网易云的 .tlyric/.romalrc。</summary>
    private static readonly (string Suffix, LyricRole Role)[] CompanionSuffixes =
    [
        ("_qmts.lrc", LyricRole.Translation),
        ("_qmRoma.lrc", LyricRole.Romanization),
        (".tlyric", LyricRole.Translation),
        (".romalrc", LyricRole.Romanization)
    ];

    /// <summary>文件名可能是「歌手 - 歌名」，也可能是 QQ 下载的「歌手 - 歌名 - 时长秒 - 专辑」，按更像请求标题的一段判定。</summary>
    private static (string Title, string? Artist, double? Duration) SplitFileName(LyricsRequest request, string fileName)
    {
        var parts = fileName.Split(" - ", StringSplitOptions.TrimEntries);
        if (parts.Length >= 3 && double.TryParse(parts[^2], out var seconds) && seconds is > 0 and < 36000)
            return (parts[1], parts[0], seconds);
        if (parts.Length >= 2)
        {
            var normalized = Normalize(request.Title);
            return Similarity.Similarity(normalized, Normalize(parts[0])) >=
                   Similarity.Similarity(normalized, Normalize(parts[1]))
                ? (parts[0], parts[1], null)
                : (parts[1], parts[0], null);
        }
        return (fileName, null, null);
    }

    /// <summary>一条线索（元数据或文件名）的匹配结论：分数，以及各字段各自的证据强度。</summary>
    private readonly record struct Assessment(int Score, double TitleScore, double? ArtistScore, double? DurationScore);

    /// <summary>两条线索里取分数更高的一条；同分时保持元数据优先。/ Takes the higher score; ties keep the metadata route.</summary>
    private static Assessment Stronger(Assessment left, Assessment right) => right.Score > left.Score ? right : left;

    private static Assessment Assess(LyricsRequest request, string? title, string? artist, double? duration)
    {
        var titleScore = Compare(request.Title, title);
        var artistScore = HasValue(request.Artist) && HasValue(artist) ? CompareArtist(request.Artist, artist) : (double?)null;
        // 只有文件自己也写了时长才谈得上比较；没写就是「这项不存在」，不能当成时长完全不符去扣分。
        var durationScore = request.DurationSeconds is > 0 && duration is > 0
            ? CompareDuration(request.DurationSeconds.Value, duration)
            : (double?)null;
        var weight = TitleWeight + (artistScore.HasValue ? ArtistWeight : 0) + (durationScore.HasValue ? DurationWeight : 0);
        var sum = TitleWeight * titleScore + (artistScore ?? 0) * ArtistWeight + (durationScore ?? 0) * DurationWeight;
        return new Assessment((int)Math.Round(100 * sum / weight), titleScore, artistScore, durationScore);
    }

    /// <summary>
    /// 只有标题一条线索时判为低置信度：同名翻唱、Live、同名片看起来都一样，得先让在线查一轮。
    /// 标题可能写在 [ti:]，也可能只在文件名里；歌手同理，所以两条线索的证据合起来看。
    /// </summary>
    private static bool IsLowConfidence(Assessment metadata, Assessment filename)
    {
        if (Math.Max(metadata.TitleScore, filename.TitleScore) < ConfidenceTitleFloor) return true;
        return Max(metadata.ArtistScore, filename.ArtistScore) is { } artist
            ? artist < ConfidenceArtistFloor
            : Max(metadata.DurationScore, filename.DurationScore) is not >= ConfidenceDurationFloor;
    }

    /// <summary>取两个可空证据里较大的一个；两边都缺就是缺（LINQ 的 Max 在空序列上会抛，不能直接用）。</summary>
    private static double? Max(double? left, double? right) =>
        left is { } first ? right is { } second ? Math.Max(first, second) : first : right;

    /// <summary>
    /// 歌手比较：除了整串相似度，再看一次字符集合重叠。「μ's (缪斯)」和「缪斯 (μ's)」写的是同一组人，
    /// 只因为顺序颠倒就让整串相似度归零，会把正确的文件挡在门外。
    /// </summary>
    private static double CompareArtist(string? expected, string? actual)
    {
        var left = Normalize(expected ?? string.Empty);
        var right = Normalize(actual ?? string.Empty);
        if (left.Length == 0 || right.Length == 0) return 0;
        var direct = Similarity.Similarity(left, right);
        var overlap = (double)left.Intersect(right).Count() / Math.Max(left.Length, right.Length);
        return Math.Max(direct, overlap);
    }

    /// <summary>同分时选内容更全的一份，再按路径稳定决胜，避免结果随枚举顺序变化。</summary>
    private static bool Better(Candidate candidate, Candidate current)
    {
        // 有歌手或时长佐证的一方优先：只靠标题拿满分的同名翻唱，不该压过标题略差但歌手对得上的那份。
        if (candidate.LowConfidence != current.LowConfidence) return !candidate.LowConfidence;
        if (candidate.Score != current.Score) return candidate.Score > current.Score;
        if (candidate.Document.Lines.Count != current.Document.Lines.Count) return candidate.Document.Lines.Count > current.Document.Lines.Count;
        return string.CompareOrdinal(candidate.Path, current.Path) < 0;
    }

    private static (string? Title, string? Artist, double? Duration) ReadMetadata(string text)
    {
        string? title = null, artist = null;
        double? duration = null;
        var head = text.Length > MetadataScanBytes ? text[..MetadataScanBytes] : text;
        foreach (Match match in MetadataLine().Matches(head))
        {
            var value = match.Groups["value"].Value.Trim();
            if (value.Length == 0) continue;
            switch (match.Groups["key"].Value.ToLowerInvariant())
            {
                case "ti": title ??= value; break;
                case "ar": artist ??= value; break;
                case "length": duration ??= ParseLength(value); break;
            }
        }
        return (title, artist, duration);
    }

    private static double? ParseLength(string value)
    {
        var parts = value.Split(':');
        if (parts.Length != 2 || !double.TryParse(parts[0], out var minutes) || !double.TryParse(parts[1], out var seconds)) return null;
        return minutes * 60 + seconds;
    }

    private static string Decode(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        try { return StrictUtf8.GetString(bytes); }
        catch (DecoderFallbackException) { return Encoding.GetEncoding(54936).GetString(bytes); }
    }

    private static double Compare(string? left, string? right)
    {
        left = Normalize(left ?? string.Empty);
        right = Normalize(right ?? string.Empty);
        if (left.Length == 0 || right.Length == 0) return 0;
        return Similarity.Similarity(left, right);
    }

    private static double CompareDuration(double expected, double? actual)
    {
        if (actual is not > 0) return 0;
        var difference = Math.Abs(expected - actual.Value);
        if (difference <= 1) return 1;
        if (difference >= 10) return 0;
        return 1 - (difference - 1) / 9;
    }

    private static bool HasValue(string? text) => !string.IsNullOrWhiteSpace(text);

    private static string Normalize(string text) => NonWordCharacters().Replace(text.ToLowerInvariant(), string.Empty);

    [GeneratedRegex(@"[\p{P}\p{S}\s]")]
    private static partial Regex NonWordCharacters();

    [GeneratedRegex(@"[\(（\[【].*?[\)）\]】]")]
    private static partial Regex Brackets();

    [GeneratedRegex(@"^\[(?<key>ti|ar|al|length):(?<value>[^\]]*)\]", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex MetadataLine();

    private sealed record Candidate(string Path, int Score, bool LowConfidence, LyricDocument Document);
}
