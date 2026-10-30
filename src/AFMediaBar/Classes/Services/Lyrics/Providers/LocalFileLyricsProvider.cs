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
    private static readonly string[] Extensions = [".lrc", ".qrc", ".krc", ".tlyric", ".romalrc", ".srt", ".ass", ".ssa"];

    /// <summary>单文件上限，超过它的多半不是歌词。</summary>
    private const int MaxFileBytes = 2 * 1024 * 1024;

    /// <summary>与在线来源保持一致的门槛：标题对不上就不是同一首。</summary>
    private const int MinimumScore = 60;

    /// <summary>字段权重：标题为主，歌手次之，时长只作辅助。</summary>
    private const double TitleWeight = 0.6;
    private const double ArtistWeight = 0.3;
    private const double DurationWeight = 0.1;

    /// <summary>两边都写了歌手、相似度却低于这条线时，视为「明确不是同一位歌手」，整份候选不采用。</summary>
    private const double ArtistMismatchFloor = 0.45;

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
        // 请求侧的派生值对每个候选都一样，算一次即可；候选侧的每份仍各自解析。
        var key = new RequestKey(VersionKeys(request.Title), SingleCharTitle(request.Title));
        Candidate? best = null;
        foreach (var folder in folders)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var path in ListLyricFiles(folder, cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var (baseName, role) = Classify(path);
                // 翻译和罗马音只是主歌词的附件，不单独参与评分。
                if (role != LyricRole.Main) continue;
                if (!LooksRelated(baseName, request.Title)) continue;
                var candidate = Evaluate(request, path, baseName, key);
                // 没有歌词的候选直接跳过：本地文件没内容就等于没找到，不能被它顶掉带词的那一份。
                if (candidate is null || candidate.Document.Lines.Count == 0) continue;
                if (best is null || Better(candidate, best)) best = candidate;
            }
        }

        // 一条都没命中就当没找到，继续原有链路。
        if (best is null) return null;
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

    /// <summary>
    /// 逐个目录往下走，单独隔离不可读项：整棵树一次枚举时，任何一个无权限或损坏的子目录都会让整个根目录返回空。
    /// </summary>
    private static IReadOnlyList<string> ListLyricFiles(string folder, CancellationToken cancellationToken)
    {
        var files = new List<string>();
        var pending = new Stack<string>();
        pending.Push(folder);
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = pending.Pop();
            try
            {
                foreach (var path in Directory.EnumerateFiles(current))
                    if (IsLyricFile(path)) files.Add(path);
                foreach (var child in Directory.EnumerateDirectories(current)) pending.Push(child);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
            {
                // 这个目录读不了就跳过它，父目录与其它子目录照常。
            }
        }
        return files;
    }

    private static bool IsLyricFile(string path) =>
        Extensions.Any(extension => path.EndsWith(extension, StringComparison.OrdinalIgnoreCase));

    /// <summary>文件名规范化后互相包含才算候选，省掉对每个文件的读取和解析。</summary>
    private static bool LooksRelated(string fileName, string title)
    {
        var name = Normalize(fileName);
        var wanted = Normalize(title);
        if (name.Length == 0 || wanted.Length == 0) return false;
        // 单字歌名（「光」）也要能进评分：短名只在整串或词尾一致时放行，避免把库里每个文件都读一遍。
        if (name.Length < 2 || wanted.Length < 2)
            return name == wanted || name.EndsWith(wanted, StringComparison.Ordinal) ||
                   wanted.EndsWith(name, StringComparison.Ordinal);
        if (name.Contains(wanted, StringComparison.Ordinal) || wanted.Contains(name, StringComparison.Ordinal)) return true;

        // 文件名常写成「歌手 - 歌名」，请求标题却带 (Live)、(Single Version) 这类后缀：
        // 两边都去掉括号内容再比一次，否则这类正常写法会被筛选直接漏掉。
        var nameCore = Normalize(Brackets().Replace(fileName, string.Empty));
        var wantedCore = Normalize(Brackets().Replace(title, string.Empty));
        return nameCore.Length >= 2 && wantedCore.Length >= 2 &&
               (nameCore.Contains(wantedCore, StringComparison.Ordinal) ||
                wantedCore.Contains(nameCore, StringComparison.Ordinal));
    }

    private static Candidate? Evaluate(LyricsRequest request, string path, string baseName, RequestKey key)
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
        // 伴奏、卡拉 OK 这类无词版本直接排除；同一首歌的不同 Remix/Live 只降置信度，交给在线先查。
        var metadataConflict = VersionConflictOf(key.Versions, metadata.Title);
        if (metadataConflict == VersionConflict.Hard) return null;
        var fileConflict = VersionConflictOf(key.Versions, fileTitle);
        if (fileConflict == VersionConflict.Hard) return null;
        // 单字标题靠相似度分不清「光」与「月光」，只接受完全同名。
        if (key.SingleCharTitle is { } exact &&
            Normalize(metadata.Title ?? string.Empty) != exact && Normalize(fileTitle) != exact) return null;
        var duration = metadata.Duration ?? fileDuration;
        // 元数据和文件名是两条独立线索，各评一次取更强的一条：哪边写全了就用哪边，不要求两份写法一致。
        var fromMetadata = Assess(request, metadata.Title, metadata.Artist, duration);
        var fromFileName = Assess(request, fileTitle, fileArtist, duration);
        // 证据跨两条线索各取最大值：标题写在元数据、歌手写在文件名，合起来才算数。
        var titleEvidence = Math.Max(fromMetadata.TitleScore, fromFileName.TitleScore);
        var artistEvidence = Max(fromMetadata.ArtistScore, fromFileName.ArtistScore);
        var durationEvidence = Max(fromMetadata.DurationScore, fromFileName.DurationScore);
        // 两边都写了歌手、却明显不是同一位时整份跳过：本地同名翻唱不能顶掉正在播的那首。
        if (artistEvidence is { } matchedArtist && matchedArtist < ArtistMismatchFloor) return null;
        var assessment = Stronger(fromMetadata, fromFileName);
        if (assessment.Score < MinimumScore) return null;
        // 文件名与内部标签给出相反的歌手证据、或版本描述对不上时，都不能当成确定匹配。
        var lowConfidence = IsLowConfidence(titleEvidence, artistEvidence, durationEvidence) ||
                            HasArtistContradiction(fromMetadata.ArtistScore, fromFileName.ArtistScore) ||
                            metadataConflict == VersionConflict.Soft || fileConflict == VersionConflict.Soft;

        var (translation, romanization) = ReadCompanions(path, baseName);
        var document = LyricsTextParser.Parse(text, translation, romanization, request,
            request.DurationSeconds ?? duration, request.FilterInfoLines);
        return new Candidate(path, assessment.Score, lowConfidence, document);
    }

    /// <summary>按扩展名取文本：QRC/KRC 先解密，字幕先归一化成 LRC，其余按编码探测。</summary>
    private static string? LoadText(string path, byte[] bytes) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".qrc" => DecryptQrc(bytes),
            ".krc" => DecryptKrc(bytes),
            ".srt" or ".ass" or ".ssa" => SubtitleTextConverter.TryConvert(Decode(bytes)),
            _ => Decode(bytes)
        };

    /// <summary>客户端缓存的 .qrc 前面有 11 字节文件头，QMC1 之后要去掉。/ The cached .qrc carries an 11-byte header before the QMC1 payload.</summary>
    private const int QmcHeaderBytes = 11;

    /// <summary>QMC1 的 128 字节掩码；配合按位与 0x7F 循环使用。/ The 128-byte QMC1 mask, cycled with `index &amp; 0x7F`.</summary>
    private static readonly byte[] Qmc1Key =
    [
        0xc3, 0x4a, 0xd6, 0xca, 0x90, 0x67, 0xf7, 0x52, 0xd8, 0xa1, 0x66, 0x62, 0x9f, 0x5b, 0x09, 0x00,
        0xc3, 0x5e, 0x95, 0x23, 0x9f, 0x13, 0x11, 0x7e, 0xd8, 0x92, 0x3f, 0xbc, 0x90, 0xbb, 0x74, 0x0e,
        0xc3, 0x47, 0x74, 0x3d, 0x90, 0xaa, 0x3f, 0x51, 0xd8, 0xf4, 0x11, 0x84, 0x9f, 0xde, 0x95, 0x1d,
        0xc3, 0xc6, 0x09, 0xd5, 0x9f, 0xfa, 0x66, 0xf9, 0xd8, 0xf0, 0xf7, 0xa0, 0x90, 0xa1, 0xd6, 0xf3,
        0xc3, 0xf3, 0xd6, 0xa1, 0x90, 0xa0, 0xf7, 0xf0, 0xd8, 0xf9, 0x66, 0xfa, 0x9f, 0xd5, 0x09, 0xc6,
        0xc3, 0x1d, 0x95, 0xde, 0x9f, 0x84, 0x11, 0xf4, 0xd8, 0x51, 0x3f, 0xaa, 0x90, 0x3d, 0x74, 0x47,
        0xc3, 0x0e, 0x74, 0xbb, 0x90, 0xbc, 0x3f, 0x92, 0xd8, 0x7e, 0x11, 0x13, 0x9f, 0x23, 0x95, 0x5e,
        0xc3, 0x00, 0x09, 0x5b, 0x9f, 0x62, 0x66, 0xa1, 0xd8, 0x52, 0xf7, 0x67, 0x90, 0xca, 0xd6, 0x4a
    ];

    /// <summary>解密器可能给出空串（格式对但内容为空），统一按「没有歌词」处理。</summary>
    private static string? Usable(string? text) => string.IsNullOrWhiteSpace(text) ? null : text;

    /// <summary>
    /// QQ 音乐的 .qrc 有两种形态，都交给依赖里的解密器收尾：
    /// ① 云端：十六进制文本，直接解密；
    /// ② 客户端缓存：整文件先做 QMC1（与 128 字节掩码异或），去掉 11 字节头，再把剩余字节重新编码成十六进制。
    /// 解不开就当没有歌词，不影响其它文件。
    /// </summary>
    private static string? DecryptQrc(byte[] bytes)
    {
        try
        {
            // 先按字节认形态：二进制缓存文件就不会白跑一遍编码探测（严格 UTF-8 必然失败，还会再解一次 GB18030）。
            if (IsHexText(bytes))
                return Usable(QrcDecrypter.DecryptLyrics(Encoding.ASCII.GetString(bytes).Trim()));

            if (bytes.Length <= QmcHeaderBytes) return null;
            var payload = Qmc1Unmask(bytes);
            return Usable(QrcDecrypter.DecryptLyrics(
                Convert.ToHexString(payload, QmcHeaderBytes, payload.Length - QmcHeaderBytes)));
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// 云端形态是纯十六进制文本：整份只含 0-9A-F 与首尾空白才算。二进制缓存文件在第一个字节就被挡下，
    /// 也不会把它错当文本去吃一次解密。
    /// </summary>
    private static bool IsHexText(byte[] bytes)
    {
        if (bytes.Length < 32) return false;
        var content = 0;
        foreach (var value in bytes)
        {
            if (value is (byte)'\r' or (byte)'\n' or (byte)' ' or (byte)'\t') continue;
            if (!Uri.IsHexDigit((char)value)) return false;
            content++;
        }
        return content >= 32;
    }

    private static byte[] Qmc1Unmask(byte[] bytes)
    {
        var data = (byte[])bytes.Clone();
        for (var i = 0; i < data.Length; i++)
        {
            // 与参考实现一致：越过 32KB 后索引按 i % 0x7FFF 折回，超过它的文件才需要这条。
            var index = i > 0x7FFF ? i % 0x7FFF & 0x7F : i & 0x7F;
            data[i] ^= Qmc1Key[index];
        }
        return data;
    }

    /// <summary>酷狗的 .krc 是二进制；依赖里的实现要求 Base64 入参，内部自己跳头、异或、解压。</summary>
    private static string? DecryptKrc(byte[] bytes)
    {
        try
        {
            return Usable(KrcDecrypter.DecryptLyrics(Convert.ToBase64String(bytes)));
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

    /// <summary>
    /// 附件命名：QQ 用 `_qmts`（简中译文）、`_qmts_tw`（繁中译文）、`_qmRoma`（罗马音），客户端缓存是 .qrc、转存后是 .lrc；
    /// 网易云用独立扩展名 .tlyric / .romalrc。顺序即优先级，简中译文排在繁中之前。
    /// </summary>
    private static readonly (string Suffix, LyricRole Role)[] CompanionSuffixes =
    [
        ("_qmts.lrc", LyricRole.Translation),
        ("_qmts.qrc", LyricRole.Translation),
        ("_qmts_tw.lrc", LyricRole.Translation),
        ("_qmts_tw.qrc", LyricRole.Translation),
        ("_qmRoma.lrc", LyricRole.Romanization),
        ("_qmRoma.qrc", LyricRole.Romanization),
        (".tlyric", LyricRole.Translation),
        (".romalrc", LyricRole.Romanization)
    ];

    /// <summary>文件名可能是「歌手 - 歌名」，也可能是 QQ 下载的「歌手 - 歌名 - 时长秒 - 专辑」，按更像请求标题的一段判定。</summary>
    private static (string Title, string? Artist, double? Duration) SplitFileName(LyricsRequest request, string fileName)
    {
        var parts = fileName.Split(" - ", StringSplitOptions.TrimEntries);
        if (parts.Length >= 3 && double.TryParse(parts[^2], out var seconds) && seconds is > 0 and < 36000)
            return (parts[1], ArtistOf(parts[0]), seconds);
        if (parts.Length >= 2)
        {
            var normalized = Normalize(request.Title);
            return Similarity.Similarity(normalized, Normalize(parts[0])) >=
                   Similarity.Similarity(normalized, Normalize(parts[1]))
                ? (parts[0], ArtistOf(parts[1]), null)
                : (parts[1], ArtistOf(parts[0]), null);
        }
        return (fileName, null, null);
    }

    /// <summary>
    /// 「厂牌 _ 歌手」是下载器的常见写法，厂牌不该算进歌手，否则歌手比较全是噪声。
    /// 只取最后一段；没有该分隔符时原样返回。
    /// </summary>
    private static string ArtistOf(string value)
    {
        var segments = value.Split(" _ ", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return segments.Length == 0 ? value : segments[^1];
    }

    /// <summary>一次搜索内不变的请求侧派生值：版本描述集合，以及单字标题的精确匹配键（非单字为 null）。</summary>
    private readonly record struct RequestKey(HashSet<string> Versions, string? SingleCharTitle);

    /// <summary>单字标题无法靠相似度区分「光」与「月光」，返回归一化后的标题供精确比较。</summary>
    private static string? SingleCharTitle(string title)
    {
        var normalized = Normalize(title);
        return normalized.Length == 1 ? normalized : null;
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
    /// 两条线索给出相反的歌手证据：一条对得上、另一条明确不是同一位。
    /// 这时无法判断是文件名错了还是标签过时，不能当成确定匹配。
    /// </summary>
    private static bool HasArtistContradiction(double? metadataArtist, double? fileNameArtist) =>
        metadataArtist is { } first && fileNameArtist is { } second &&
        Math.Max(first, second) >= ConfidenceArtistFloor && Math.Min(first, second) < ArtistMismatchFloor;

    /// <summary>
    /// 只有标题一条线索时判为低置信度：同名翻唱、Live、同名片看起来都一样，得先让在线查一轮。
    /// 标题、歌手、时长的证据都已按两条线索合并，这里只做判定。
    /// </summary>
    private static bool IsLowConfidence(double titleScore, double? artistScore, double? durationScore) =>
        titleScore < ConfidenceTitleFloor ||
        (artistScore is { } artist ? artist < ConfidenceArtistFloor : durationScore is not >= ConfidenceDurationFloor);

    /// <summary>取两个可空证据里较大的一个；两边都缺就是缺（LINQ 的 Max 在空序列上会抛，不能直接用）。</summary>
    private static double? Max(double? left, double? right) =>
        left is { } first ? right is { } second ? Math.Max(first, second) : first : right;

    /// <summary>
    /// 歌手比较：整串相似度与字符集合重叠取高者，再按长度比例打折。
    /// 「μ's (缪斯)」和「缪斯 (μ's)」写的是同一组人，只因为顺序颠倒就让整串相似度归零会把正确文件挡在门外；
    /// 但 Jaro-Winkler 对超短串很宽容——「Lia」对「Girls Dead Monster (ガルデモ)」能到 0.61，
    /// 不打折就会被当成歌手佐证，把完全不同的歌手判成高置信。
    /// </summary>
    private static double CompareArtist(string? expected, string? actual)
    {
        var left = Normalize(expected ?? string.Empty);
        var right = Normalize(actual ?? string.Empty);
        if (left.Length == 0 || right.Length == 0) return 0;
        var direct = Similarity.Similarity(left, right);
        var longer = Math.Max(left.Length, right.Length);
        var overlap = (double)left.Intersect(right).Count() / longer;
        var lengthFactor = 0.55 + 0.45 * Math.Min(left.Length, right.Length) / longer;
        return Math.Max(direct, overlap) * lengthFactor;
    }

    /// <summary>
    /// 版本描述冲突的程度。Hard 表示候选根本是另一种录音（伴奏、卡拉 OK），词不能互借；
    /// Soft 表示是同一首歌的不同版本（DJ甲/DJ乙 Remix、Live 2010/Live 2024），可以先查在线再兜底。
    /// </summary>
    private enum VersionConflict
    {
        None,
        Soft,
        Hard
    }

    /// <summary>无词的录音形态：命中即整份排除，不拿它的词配人声版。</summary>
    private static readonly string[] HardVersionMarkers =
    [
        "instrumental", "inst", "off vocal", "offvocal", "karaoke", "カラオケ", "伴奏"
    ];

    /// <summary>同一个歌手的不同版本：描述不一致时降置信度，不直接扔。</summary>
    private static readonly string[] SoftVersionMarkers =
    [
        "live", "remix", "acoustic", "demo", "cover", "version", "ver", "tv size", "tvsize",
        "short", "edit", "remaster"
    ];

    private static readonly Regex HardVersionPattern = MarkerPattern(HardVersionMarkers);
    private static readonly Regex SoftVersionPattern = MarkerPattern(SoftVersionMarkers);

    /// <summary>只匹配独立出现的标记：「Edition」里的 edit 不算，因为后面还跟着字母。</summary>
    private static Regex MarkerPattern(string[] markers) => new(
        $@"(?<![\p{{L}}\p{{N}}])(?:{string.Join("|", markers.Select(Regex.Escape))})(?![\p{{L}}\p{{N}}])",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>括号内的版本描述，归一化后连同类属（无词形态 / 同曲异版）。</summary>
    private readonly record struct VersionDescriptor(string Text, bool IsHard);

    /// <summary>取标题里带版本标记的括号描述：`DJ甲 Remix` 与 `DJ乙 Remix` 是两条不同描述。</summary>
    private static List<VersionDescriptor> VersionDescriptors(string? title)
    {
        var descriptors = new List<VersionDescriptor>();
        if (string.IsNullOrWhiteSpace(title)) return descriptors;
        foreach (Match group in Brackets().Matches(title))
        {
            var inner = group.Value[1..^1];
            var isHard = HardVersionPattern.IsMatch(inner);
            if (!isHard && !SoftVersionPattern.IsMatch(inner)) continue;
            var normalized = Normalize(inner);
            if (normalized.Length > 0) descriptors.Add(new VersionDescriptor(normalized, isHard));
        }
        return descriptors;
    }

    /// <summary>请求侧的版本描述集合，供候选比较时做「完全一致或互相包含」判定。</summary>
    private static HashSet<string> VersionKeys(string? title) =>
        VersionDescriptors(title).Select(descriptor => descriptor.Text).ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// 比较候选与请求的版本描述：候选没写版本按原版处理；写了就必须与请求的某一条相容，
    /// 否则按类型给出软/硬冲突。
    /// </summary>
    private static VersionConflict VersionConflictOf(HashSet<string> wanted, string? candidateTitle)
    {
        var conflict = VersionConflict.None;
        foreach (var descriptor in VersionDescriptors(candidateTitle))
        {
            var compatible = wanted.Any(item => item == descriptor.Text ||
                item.Contains(descriptor.Text, StringComparison.Ordinal) ||
                descriptor.Text.Contains(item, StringComparison.Ordinal));
            if (compatible) continue;
            if (descriptor.IsHard) return VersionConflict.Hard;
            conflict = VersionConflict.Soft;
        }
        return conflict;
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
