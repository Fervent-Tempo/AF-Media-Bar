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
    private LyricDocument? _document;
    private int _lastIndex = -1;

    /// <summary>
    /// 处理媒体快照并输出当前歌词行。
    /// Processes a media snapshot and writes the active lyric line.
    /// </summary>
    public void OnSnapshotChanged(object? sender, MediaSnapshot snapshot)
    {
        if (snapshot.Lyrics is not { } lyrics || lyrics.Document.Lines.Count == 0)
            return;

        if (!ReferenceEquals(_document, lyrics.Document))
        {
            _document = lyrics.Document;
            _lastIndex = -1;
        }

        var index = LyricLineIndexPolicy.FindIndex(lyrics.Document.Lines, snapshot.Position);
        if (index < 0 || index == _lastIndex)
            return;

        _lastIndex = index;
        var line = lyrics.Document.Lines[index];
        // Debug.WriteLine(
        //     $"[Lyrics][{lyrics.Source}][{lyrics.Document.SourceFormat}/{lyrics.Document.SyncType}] " +
        //     $"{TimeSpan.FromSeconds(line.Start):mm\\:ss} {line.Text} (words={line.Words.Count})");
    }
}
#endif
