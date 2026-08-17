using System.IO;
using System.Text;

namespace AFMediaBar.Services;

/// <summary>
/// 以有界、尽力而为的方式记录诊断信息，日志失败不能反过来影响程序。
/// Writes bounded, best-effort diagnostics without allowing logging failures to affect the app.
/// </summary>
internal static class DiagnosticsLogService
{
    private const long MaxLogBytes = 1_048_576;
    private const int MaxFieldLength = 2_048;
    private static readonly object SyncRoot = new();
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    internal static string EnsureLogFile()
    {
        var path = GetLogFilePath() ??
            throw new InvalidOperationException("The local application data directory is unavailable.");
        lock (SyncRoot)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            RotateIfNeeded(path);
            if (!File.Exists(path))
            {
                File.WriteAllText(path, string.Empty, Utf8NoBom);
            }
        }

        return path;
    }

    internal static void Write(string category, Exception? exception = null, string? details = null)
    {
        try
        {
            var path = GetLogFilePath();
            if (path is null)
            {
                return;
            }

            lock (SyncRoot)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                RotateIfNeeded(path);
                var message = exception is null
                    ? string.Empty
                    : exception.ToString();
                var line = string.Join(
                    "\t",
                    DateTimeOffset.UtcNow.ToString("O"),
                    Sanitize(category),
                    Sanitize(details),
                    Sanitize(message));
                File.AppendAllText(path, line + Environment.NewLine, Utf8NoBom);
            }
        }
        catch
        {
            // 诊断日志不能成为新的崩溃源。 / Diagnostics must never become a crash source.
        }
    }

    private static string? GetLogFilePath()
    {
        var localAppData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        return string.IsNullOrWhiteSpace(localAppData)
            ? null
            : Path.Combine(localAppData, "AFMediaBar", "logs", "afmediabar.log");
    }

    private static void RotateIfNeeded(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        var length = new FileInfo(path).Length;
        if (length < MaxLogBytes)
        {
            return;
        }

        var archivePath = path + ".1";
        File.Move(path, archivePath, overwrite: true);
    }

    private static string Sanitize(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var sanitized = value
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Replace('\t', ' ');
        return sanitized.Length <= MaxFieldLength
            ? sanitized
            : sanitized[..MaxFieldLength];
    }
}
