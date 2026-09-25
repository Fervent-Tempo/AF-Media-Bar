using System.IO;
using Lyricify.Lyrics.Decrypter.Qrc;

namespace AFMediaBar.Classes.Services.Lyrics;

/// <summary>
/// QQ 音乐本地歌词缓存读取：从缓存目录里的加密 QRC 文件直接取词，命中时不发网络请求。
/// The QQ Music local lyric cache: reads the encrypted QRC files straight from the cache directory, so a hit never touches
/// the network.
///
/// 缓存目录按注册表 <c>HKCU\Software\Tencent\QQMusic\LogConfig\CACHEPATH</c> 定位，歌词在其下的 <c>QQMusicLyricNew</c>；
/// 文件名形如 <c>歌手 - 曲名 - 序号 - 专辑_qm.qrc</c>（译文为 <c>_qmts.qrc</c>），与 Lyrix 的 qqmusic 读取器一致。
/// The cache directory comes from the registry value above with the lyrics under its <c>QQMusicLyricNew</c> subdirectory;
/// file names look like <c>artist - title - index - album_qm.qrc</c> (translations as <c>_qmts.qrc</c>), matching the Lyrix
/// qqmusic reader.
///
/// 匹配只比对曲名与专辑，歌手不参与（多歌手以 <c>_</c> 连接，无法可靠拆分），与参考实现相同。
/// Matching compares only title and album; artists are left out (multiple artists join with <c>_</c> and cannot be split
/// reliably), same as the reference implementation.
/// </summary>
internal static class QQMusicLocalCache
{
    private const string LogConfigKeyPath = @"Software\Tencent\QQMusic\LogConfig";
    private const string CachePathValueName = "CACHEPATH";
    private const string LyricNewDirName = "QQMusicLyricNew";
    private const string MainSuffix = "_qm.qrc";
    private const string TranslationSuffix = "_qmts.qrc";
    private const string PartSeparator = " - ";

    // QMC 魔数与掩码表来自 Lyrix 的 qrc 解密实现，缓存文件用它做逐字节异或。
    // The QMC magic and mask table come from Lyrix's qrc decryption; cache files are XORed byte-wise with them.
    private static readonly byte[] QmcMagic =
        [0x98, 0x25, 0xB0, 0xAC, 0xE3, 0x02, 0x83, 0x68, 0xE8, 0xFC, 0x6C];

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

    /// <summary>
    /// 按曲名与专辑在缓存里找 QRC 并解密；未命中或解密失败都返回 false。
    /// Finds and decrypts the cached QRC by title and album; a miss or a failed decryption both return false.
    /// </summary>
    public static bool TryRead(
        string title,
        string album,
        CancellationToken cancellationToken,
        out string? main,
        out string? translation)
    {
        main = null;
        translation = null;
        cancellationToken.ThrowIfCancellationRequested();

        title = title.Trim();
        album = album.Trim();
        if (title.Length == 0 || album.Length == 0)
        {
            AppLogService.Current?.Warn(
                "Lyrics",
                $"QQ 缓存缺元数据 / cache missing metadata: title=\"{title}\" album=\"{album}\"");
            return false;
        }

        // QQ 写缓存文件名时把文件名非法字符换成 "_"（例如 SAKURA*TRICK → SAKURA_TRICK），匹配前先同形清洗元数据。
        // QQ sanitizes filename-illegal characters into "_" when writing cache names (SAKURA*TRICK → SAKURA_TRICK), so the
        // metadata gets the same shape before matching.
        title = SanitizeForMatch(title);
        album = SanitizeForMatch(album);

        var directory = ResolveLyricDirectory();
        if (directory is null)
        {
            AppLogService.Current?.Info(
                "Lyrics",
                "QQ 缓存不可用 / QQ lyric cache not found (registry CACHEPATH or QQMusicLyricNew missing)");
            return false;
        }

        string? mainPath;
        try
        {
            mainPath = FindMainFile(directory, title, album, cancellationToken);
        }
        catch (IOException exception)
        {
            AppLogService.Current?.Warn("Lyrics", $"QQ 缓存扫描失败，回退在线取词 / cache scan failed: {exception.GetType().Name}");
            return false;
        }
        catch (UnauthorizedAccessException exception)
        {
            AppLogService.Current?.Warn("Lyrics", $"QQ 缓存扫描无权限，回退在线取词 / cache scan denied: {exception.GetType().Name}");
            return false;
        }
        if (mainPath is null)
        {
            AppLogService.Current?.Info(
                "Lyrics",
                $"QQ 缓存未命中 / cache miss: no file matches title=\"{title}\" album=\"{album}\"");
            return false;
        }

        // 解密失败按未命中处理，让网络路径继续兜底。
        // A failed decryption counts as a miss and leaves the network path to take over.
        cancellationToken.ThrowIfCancellationRequested();
        main = DecryptFile(mainPath);
        if (main is null)
        {
            AppLogService.Current?.Warn(
                "Lyrics",
                $"QQ 缓存解密失败 / cache decrypt failed: {Path.GetFileName(mainPath)}");
            return false;
        }

        var translationPath = mainPath[..^MainSuffix.Length] + TranslationSuffix;
        cancellationToken.ThrowIfCancellationRequested();
        translation = File.Exists(translationPath) ? DecryptFile(translationPath) : null;
        AppLogService.Current?.Info(
            "Lyrics",
            $"QQ 缓存命中 / cache hit: {Path.GetFileName(mainPath)} translation={translation is not null}");
        return true;
    }

    /// <summary>
    /// 定位歌词缓存目录：注册表 CACHEPATH 下的 <c>QQMusicLyricNew</c>；缓存根不存在或目录缺失都视为未安装缓存。
    /// Locates the lyric cache directory: <c>QQMusicLyricNew</c> under the registry CACHEPATH; a missing cache root or
    /// subdirectory counts as no cache installed.
    /// </summary>
    private static string? ResolveLyricDirectory()
    {
        string? cachePath;
        try
        {
            cachePath = Microsoft.Win32.Registry.CurrentUser
                .OpenSubKey(LogConfigKeyPath)?
                .GetValue(CachePathValueName) as string;
        }
        catch
        {
            // 注册表读取失败（键不存在或权限问题）按无缓存处理。
            // A failed registry read (missing key or access denied) counts as no cache.
            return null;
        }

        if (string.IsNullOrWhiteSpace(cachePath))
        {
            return null;
        }

        var directory = Path.Combine(cachePath.Trim(), LyricNewDirName);
        return Directory.Exists(directory) ? directory : null;
    }

    /// <summary>
    /// 匹配用的元数据清洗：把 Windows 文件名非法字符（以及 QQ 额外清洗过的 <c>*</c>）替换成 <c>_</c>。
    /// Match-side sanitization: filename-illegal characters on Windows — plus <c>*</c>, which QQ also sanitizes — become
    /// <c>_</c>.
    /// </summary>
    private static string SanitizeForMatch(string value)
    {
        const string illegal = "*\\/:?\"<>|";
        return string.Create(value.Length, value, static (span, source) =>
        {
            for (var i = 0; i < source.Length; i++)
            {
                span[i] = illegal.IndexOf(source[i]) >= 0 ? '_' : source[i];
            }
        });
    }

    /// <summary>
    /// 遍历缓存目录找文件名可解析且曲名、专辑都匹配的正文文件；文件名解析失败的条目直接跳过。
    /// Scans the cache directory for a main file whose name parses and whose title and album both match; entries with
    /// unparseable names are skipped.
    /// </summary>
    private static string? FindMainFile(
        string directory,
        string title,
        string album,
        CancellationToken cancellationToken)
    {
        string? found = null;
        var scanned = 0;
        var samples = new List<string>(3);
        foreach (var path in Directory.EnumerateFiles(directory, "*_qm.qrc", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fileName = Path.GetFileName(path);
            var info = ParseFileName(fileName);
            if (info is null)
            {
                continue;
            }

            scanned++;
            if (samples.Count < 3)
            {
                samples.Add($"\"{info.Value.Title}\" / \"{info.Value.Album}\"");
            }

            if (info.Value.Title == title && info.Value.Album == album)
            {
                found = path;
                break;
            }
        }

        if (found is null)
        {
            AppLogService.Current?.Info(
                "Lyrics",
                $"QQ 缓存扫描 / cache scanned files={scanned}, samples: {string.Join("; ", samples)}");
        }

        return found;
    }

    /// <summary>
    /// 解析缓存文件名：去掉 <c>_qm.qrc</c> 后按 " - " 从右拆四段，得到专辑、序号、曲名、歌手；序号必须是纯数字。
    /// Parses a cache file name: strips <c>_qm.qrc</c>, splits the rest from the right into album, index, title and artist;
    /// the index must be all digits.
    /// </summary>
    private static (string Artist, string Title, string Index, string Album)? ParseFileName(string fileName)
    {
        if (!fileName.EndsWith(MainSuffix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var baseName = fileName[..^MainSuffix.Length].Trim();
        var parts = baseName.Split(PartSeparator);
        if (parts.Length < 4)
        {
            return null;
        }

        // 从右取四段：专辑、序号、曲名、歌手；其余段（曲名或专辑里自带的 " - "）留在最左段一起并入歌手，随匹配被忽略。
        // The rightmost four segments are album, index, title and artist; extra separators stay with the leftmost segment and
        // are absorbed into the artist, which the match ignores anyway.
        var album = parts[^1].Trim();
        var index = parts[^2].Trim();
        var title = parts[^3].Trim();
        var artist = string.Join(PartSeparator, parts[..^3]).Trim();

        if (artist.Length == 0 || title.Length == 0 || album.Length == 0 || index.Length == 0)
        {
            return null;
        }

        if (!index.All(char.IsAsciiDigit))
        {
            return null;
        }

        return (artist, title, index, album);
    }

    /// <summary>
    /// 读取并解密单个 QRC 文件：缓存里是二进制密文，开头带 11 字节 QMC 魔数时先按掩码表逐字节异或并跳过魔数，再交给
    /// 3DES 解密；结果看起来不像歌词（既不以 <c>&lt;</c> 也不以 <c>[</c> 开头）时视为解密失败。
    /// Reads and decrypts one QRC file: the cache holds binary ciphertext; a leading 11-byte QMC magic means the bytes are
    /// first XORed with the mask table and the magic skipped, then handed to the 3DES decrypter; content that looks nothing
    /// like lyrics (neither starting with <c>&lt;</c> nor <c>[</c>) counts as a failed decryption.
    /// </summary>
    private static string? DecryptFile(string path)
    {
        try
        {
            var data = File.ReadAllBytes(path);
            var start = 0;
            if (StartsWith(data, QmcMagic))
            {
                for (var i = 0; i < data.Length; i++)
                {
                    // 与参考实现一致：偏移超过 0x7FFF 时先取模再掩码。
                    // Matches the reference implementation: offsets past 0x7FFF wrap by modulo before masking.
                    data[i] ^= Qmc1Key[(i > 0x7FFF ? i % 0x7FFF : i) & 0x7F];
                }

                start = QmcMagic.Length;
            }

            var decrypted = Decrypter.DecryptLyrics(Convert.ToHexString(data, start, data.Length - start));
            var trimmed = decrypted?.TrimStart();
            if (string.IsNullOrWhiteSpace(trimmed) ||
                (trimmed[0] != '<' && trimmed[0] != '['))
            {
                return null;
            }

            return decrypted;
        }
        catch
        {
            return null;
        }
    }

    private static bool StartsWith(byte[] data, byte[] prefix)
    {
        if (data.Length < prefix.Length)
        {
            return false;
        }

        for (var i = 0; i < prefix.Length; i++)
        {
            if (data[i] != prefix[i])
            {
                return false;
            }
        }

        return true;
    }
}
