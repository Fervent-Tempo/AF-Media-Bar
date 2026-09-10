using System.Runtime.InteropServices;
using System.Windows.Media;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services.Lyrics;
using AFMediaBar.Classes.Utils;
using Windows.Media.Control;
using WindowsMediaController;
using static WindowsMediaController.MediaManager;

namespace AFMediaBar.Classes.Services;

/// <summary>
/// 从选中的 SMTC 会话构建统一媒体快照，并异步补充歌词。
/// Builds the unified media snapshot from a selected SMTC session and enriches it with lyrics asynchronously.
/// </summary>
public sealed class MediaSnapshotBuilder
{
    private const string UnknownSourceName = "未知来源";
    private readonly LyricsService _lyricsService;
    private readonly Dictionary<string, LyricsResult?> _lyricsCache = new(StringComparer.Ordinal);
    private readonly HashSet<string> _pendingLyrics = new(StringComparer.Ordinal);

    public event Action? EnrichmentCompleted;

    public MediaSnapshotBuilder(LyricsService lyricsService)
    {
        _lyricsService = lyricsService;
    }

    public MediaSnapshot? Build(MediaSession? session, bool isStarted)
    {
        if (session is null || !isStarted)
        {
            return MediaSnapshot.Disconnected;
        }

        var controlSession = session.ControlSession;
        var songInfo = TryGetMediaProperties(controlSession);
        if (songInfo is null)
        {
            return null;
        }

        var playbackInfo = controlSession.GetPlaybackInfo();
        var timelineProperties = controlSession.GetTimelineProperties();
        var artwork = ArtworkLoader.GetThumbnail(songInfo.Thumbnail);
        BitmapHelper.GetDominantColors(1);
        var sourceId = controlSession.SourceAppUserModelId ?? string.Empty;
        var title = songInfo.Title ?? string.Empty;
        var artist = songInfo.Artist ?? string.Empty;
        var lyricsKey = $"{session.Id}\u001f{title}\u001f{artist}";
        var lyrics = GetLyrics(lyricsKey, sourceId, title, artist, songInfo, timelineProperties);

        return new MediaSnapshot(
            true,
            playbackInfo.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
            playbackInfo.Controls?.IsPlayPauseToggleEnabled ?? false,
            playbackInfo.Controls?.IsPreviousEnabled ?? false,
            playbackInfo.Controls?.IsNextEnabled ?? false,
            title,
            artist,
            sourceId,
            MediaSourceNameFormatter.GetDisplayName(sourceId, UnknownSourceName),
            artwork,
            lyrics,
            timelineProperties.Position.TotalSeconds);
    }

    private LyricsResult? GetLyrics(
        string key,
        string sourceId,
        string title,
        string artist,
        GlobalSystemMediaTransportControlsSessionMediaProperties songInfo,
        GlobalSystemMediaTransportControlsSessionTimelineProperties timelineProperties)
    {
        if (_lyricsCache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        if (_pendingLyrics.Contains(key) ||
            sourceId.Contains("cloudmusic", StringComparison.OrdinalIgnoreCase) ||
            sourceId.Contains("netease", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        _pendingLyrics.Add(key);
        _ = LoadLyricsAsync(key, title, artist, songInfo, timelineProperties);
        return null;
    }

    private async Task LoadLyricsAsync(
        string key,
        string title,
        string artist,
        GlobalSystemMediaTransportControlsSessionMediaProperties songInfo,
        GlobalSystemMediaTransportControlsSessionTimelineProperties timelineProperties)
    {
        try
        {
            var duration = (timelineProperties.EndTime - timelineProperties.StartTime).TotalSeconds;
            var request = new LyricsRequest(
                title,
                artist,
                songInfo.AlbumTitle ?? string.Empty,
                duration > 0 ? duration : null,
                NetEaseSongId: null);
            _lyricsCache[key] = await _lyricsService.GetLyricsAsync(request, CancellationToken.None);
        }
        catch
        {
            _lyricsCache[key] = null;
        }
        finally
        {
            _pendingLyrics.Remove(key);
            EnrichmentCompleted?.Invoke();
        }
    }

    private static GlobalSystemMediaTransportControlsSessionMediaProperties? TryGetMediaProperties(
        GlobalSystemMediaTransportControlsSession controlSession)
    {
        try
        {
            return controlSession.TryGetMediaPropertiesAsync().GetAwaiter().GetResult();
        }
        catch (COMException)
        {
            return null;
        }
    }
}
