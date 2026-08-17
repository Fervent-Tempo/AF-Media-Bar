using System.Globalization;
using System.IO;
using System.Text;

namespace AFMediaBar.Services;

/// <summary>
/// 以有界、尽力而为的方式记录诊断信息，日志失败不能反过来影响程序。
/// Writes bounded, best-effort diagnostics without allowing logging failures to affect the app.
/// 每条记录包含本地时间、事件名称、可选详情和保留换行的异常堆栈。
/// Each entry includes local time, event name, optional details, and a readable multiline stack trace.
/// </summary>
internal static class DiagnosticsLogService
{
    private const long MaxLogBytes = 1_048_576;
    private const int MaxFieldLength = 2_048;
    private const int MaxExceptionLength = 8_192;
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
                File.AppendAllText(path, FormatEntry(category, exception, details), Utf8NoBom);
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

    private static string FormatEntry(string category, Exception? exception, string? details)
    {
        var builder = new StringBuilder();
        var timestamp = DateTimeOffset.Now.ToString(
            "yyyy-MM-dd HH:mm:ss.fff zzz",
            CultureInfo.InvariantCulture);

        builder.Append('[')
            .Append(timestamp)
            .Append("] Event: ")
            .AppendLine(Sanitize(category));

        if (!string.IsNullOrWhiteSpace(details))
        {
            builder.Append("Details: ")
                .AppendLine(Sanitize(details));
        }

        if (exception is not null)
        {
            builder.AppendLine("Exception:");
            AppendIndented(builder, exception.ToString(), MaxExceptionLength);
        }

        builder.AppendLine();
        return builder.ToString();
    }

    private static void AppendIndented(StringBuilder builder, string value, int maxLength)
    {
        var text = value.Length <= maxLength
            ? value
            : value[..maxLength] + Environment.NewLine + "[exception text truncated]";
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        foreach (var line in lines)
        {
            builder.Append("  ").AppendLine(line);
        }
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
