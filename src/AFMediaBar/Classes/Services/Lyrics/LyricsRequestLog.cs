// 收集单轮取词的并行来源结果；完成时冻结摘要，迟到结果不进入下一轮日志。
using System.Diagnostics;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Settings;

namespace AFMediaBar.Classes.Services.Lyrics;

internal sealed class LyricsRequestLog(LyricsRequest request, LyricsSourceSettings settings, AppLogService? log)
{
    private static long _nextId;
    private readonly long _id = Interlocked.Increment(ref _nextId);
    private readonly long _started = Stopwatch.GetTimestamp();
    private readonly object _gate = new();
    private readonly List<string> _attempts = [];
    private bool _completed;

    internal int Begin(string source)
    {
        lock (_gate)
        {
            _attempts.Add($"{source}:InFlight");
            return _attempts.Count - 1;
        }
    }

    internal void Record(int index, string source, string status, int? score = null)
    {
        lock (_gate)
        {
            if (_completed) return;
            _attempts[index] = $"{source}:{status}" + (score is { } value ? $"(score={value})" : "");
        }
    }

    internal LyricsResult? Finish(LyricsResult? result)
    {
        string attempts;
        lock (_gate)
        {
            if (_completed) return result;
            _completed = true;
            attempts = string.Join(",", _attempts);
        }
        log?.Info("Lyrics", $"取词结束: request={_id} title=\"{request.Title}\" artist=\"{request.Artist}\" " +
            $"playbackSource={request.PlaybackSourceId ?? "none"} rule={settings.Rule}/{settings.AttemptMode}/{settings.ResultMode} " +
            $"attempts=[{attempts}] source={result?.Source ?? "none"} score={result?.MatchScore.ToString() ?? "none"} " +
            $"status={result?.Status.ToString() ?? "FailedOrUnmatched"} elapsed={Stopwatch.GetElapsedTime(_started).TotalMilliseconds:0}ms");
        return result;
    }
}
