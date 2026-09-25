using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using Lyricify.Lyrics.Decrypter.Krc;
using AFMediaBar.Classes.Models;

namespace AFMediaBar.Classes.Services.Lyrics;

/// <summary>
/// 酷狗本地 KRC 缓存：从用户配置的歌词目录按曲目元数据找文件，解密后交给共用解析器。
/// The Kugou local KRC cache: finds a file by track metadata in Kugou's configured lyric directory and decrypts it for the shared parser.
/// </summary>
internal static partial class KugouLocalCache
{
    private const string RegistryKey = @"Software\KuGou";
    private const string AppDataPathValue = "AppDataPath";
    private const string IniName = "KuGou.ini";
    private const int MaximumKrcBytes = 2 * 1024 * 1024;
    private static readonly TimeSpan MaximumDurationDifference = TimeSpan.FromSeconds(8);

    [GeneratedRegex(@"^(?<label>.+)-[0-9a-f]{32}-\d+-[0-9a-f]{8}\.krc$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex KrcFileNamePattern();

    [GeneratedRegex(@"^\[total:(?<milliseconds>\d+)\]$", RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TotalTagPattern();

    public static LocalLyricsCacheLookup.CachedText? TryReadText(
        LyricsRequest request,
        CancellationToken cancellationToken)
    {
        var directory = ResolveLyricDirectory(cancellationToken);
        return directory is null ? null : TryReadFromDirectory(directory, request, cancellationToken);
    }

    private static string? ResolveLyricDirectory(CancellationToken cancellationToken)
    {
        string? appDataPath = null;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryKey);
            appDataPath = key?.GetValue(AppDataPathValue) as string;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            AppLogService.Current?.Warn("Lyrics", $"酷狗配置读取失败 / Kugou registry read failed: {exception.GetType().Name}");
        }

        var configured = ReadLyricPathFromIni(appDataPath, cancellationToken);
        if (configured is not null)
        {
            return configured;
        }

        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var fallback = Path.Combine(roaming, "Kugou8");
        return string.Equals(appDataPath?.TrimEnd(Path.DirectorySeparatorChar), fallback, StringComparison.OrdinalIgnoreCase)
            ? null
            : ReadLyricPathFromIni(fallback, cancellationToken);
    }

    internal static string? ReadLyricPathFromIni(string? appDataPath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(appDataPath))
        {
            return null;
        }

        var iniPath = Path.Combine(appDataPath, IniName);
        if (!File.Exists(iniPath))
        {
            return null;
        }

        using var reader = new StreamReader(iniPath, detectEncodingFromByteOrderMarks: true);
        var configuredPath = ParseLyricPath(reader, cancellationToken);
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            return null;
        }

        var expanded = Environment.ExpandEnvironmentVariables(configuredPath.Trim().Trim('"'));
        return Path.IsPathFullyQualified(expanded) && Directory.Exists(expanded) ? expanded : null;
    }

    internal static string? ParseLyricPath(TextReader reader, CancellationToken cancellationToken)
    {
        var inLyricSection = false;
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var trimmed = line.Trim();
            if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
            {
                inLyricSection = trimmed.Equals("[LyricConfigSection]", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (!inLyricSection || trimmed.StartsWith(';') || trimmed.StartsWith('#'))
            {
                continue;
            }

            var separator = trimmed.IndexOf('=');
            if (separator > 0 && trimmed[..separator].Trim().Equals("LyricPath", StringComparison.OrdinalIgnoreCase))
            {
                return trimmed[(separator + 1)..].Trim();
            }
        }

        return null;
    }

    internal static LocalLyricsCacheLookup.CachedText? TryReadFromDirectory(
        string directory,
        LyricsRequest request,
        CancellationToken cancellationToken)
    {
        var expectedLabel = $"{request.Artist.Trim()} - {request.Title.Trim()}".Normalize(NormalizationForm.FormC);
        var matched = new List<(string Text, double Difference)>();
        foreach (var path in Directory.EnumerateFiles(directory, "*.krc", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!FileNameMatches(Path.GetFileName(path), expectedLabel))
            {
                continue;
            }

            try
            {
                var file = new FileInfo(path);
                if (file.Length is < 5 or > MaximumKrcBytes)
                {
                    continue;
                }

                var data = File.ReadAllBytes(path);
                if (data.Length < 5 || data[0] != 'k' || data[1] != 'r' || data[2] != 'c' || data[3] != '1')
                {
                    continue;
                }

                cancellationToken.ThrowIfCancellationRequested();
                var text = Decrypter.DecryptLyrics(Convert.ToBase64String(data));
                if (string.IsNullOrWhiteSpace(text) || !text.TrimStart().StartsWith('['))
                {
                    continue;
                }

                var difference = DurationDifference(text, request.DurationSeconds);
                if (double.IsFinite(difference) && difference > MaximumDurationDifference.TotalSeconds)
                {
                    continue;
                }

                matched.Add((text, difference));
            }
            catch (Exception exception)
            {
                cancellationToken.ThrowIfCancellationRequested();
                AppLogService.Current?.Warn("Lyrics", $"酷狗缓存文件跳过 / Kugou cache file skipped: {exception.GetType().Name}");
            }
        }

        if (matched.Count == 0 || (matched.Count > 1 && matched.All(static item => !double.IsFinite(item.Difference))))
        {
            return null;
        }

        return new LocalLyricsCacheLookup.CachedText(matched.OrderBy(static item => item.Difference).First().Text);
    }

    internal static bool FileNameMatches(string fileName, string expectedLabel)
    {
        var match = KrcFileNamePattern().Match(fileName);
        return match.Success && string.Equals(
            match.Groups["label"].Value.Normalize(NormalizationForm.FormC),
            expectedLabel,
            StringComparison.OrdinalIgnoreCase);
    }

    private static double DurationDifference(string text, double? requestedSeconds)
    {
        if (requestedSeconds is not { } duration || !double.IsFinite(duration) || duration <= 0)
        {
            return double.PositiveInfinity;
        }

        var match = TotalTagPattern().Match(text.Replace("\r", string.Empty, StringComparison.Ordinal));
        return match.Success && long.TryParse(match.Groups["milliseconds"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var total)
            ? Math.Abs(total / 1000.0 - duration)
            : double.PositiveInfinity;
    }
}
