using System.Diagnostics;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services.Lyrics;

#if DEBUG
namespace AFMediaBar.Classes.Services;

/// <summary>
/// Debug 构建中按歌词行变化输出实时歌词诊断。
/// Emits real-time lyric diagnostics on line changes in Debug builds.
/// </summary>
public sealed class DebugLyricsDiagnostics
{
    private string? _lrc;
    private IReadOnlyList<LrcLine> _lines = [];
    private int _lastIndex = -1;

    /// <summary>
    /// 处理媒体快照并输出当前歌词行。
    /// Processes a media snapshot and writes the active lyric line.
    /// </summary>
    public void OnSnapshotChanged(object? sender, MediaSnapshot snapshot)
    {
        if (snapshot.Lyrics is not { } lyrics || string.IsNullOrWhiteSpace(lyrics.Lrc))
            return;

        if (!string.Equals(_lrc, lyrics.Lrc, StringComparison.Ordinal))
        {
            _lrc = lyrics.Lrc;
            _lines = LrcParser.Parse(lyrics.Lrc);
            _lastIndex = -1;
        }

        var index = LrcParser.FindIndex(_lines, TimeSpan.FromSeconds(snapshot.Position));
        if (index < 0 || index == _lastIndex)
            return;

        _lastIndex = index;
        Debug.WriteLine($"[Lyrics][{lyrics.Source}] {_lines[index].Time:mm\\:ss} {_lines[index].Text}");
    }
}
#endif
