// Converts SMTC sessions and owns their enrichment caches; lyrics retrieval is delegated to LyricsService.
using System.Runtime.InteropServices;
using System.Windows.Media;
using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services.Lyrics;
using AFMediaBar.Classes.Settings;
using AFMediaBar.Classes.Utils;
using AFMediaBar.Resources;
using Windows.Media.Control;
using WindowsMediaController;
using static WindowsMediaController.MediaManager;

namespace AFMediaBar.Classes.Services.Media.Smtc;

/// <summary>
/// 从选中的 SMTC 会话构建统一媒体快照，并异步补充歌词。
/// Builds the unified media snapshot from a selected SMTC session and enriches it with lyrics asynchronously.
/// </summary>
public sealed class MediaSnapshotBuilder : IMemoryPrunable, IDisposable
{
    /// <summary>
    /// 无法识别来源时的回退名称，按快照构建时的语言取值：它不能是常量，否则切换语言后来源名会停在启动时的语言上。
    /// Fallback name for an unrecognized source, read while the snapshot is built: it cannot be a constant, because a
    /// language switch would otherwise leave the source name in the language the application started in.
    /// </summary>
    private static string UnknownSourceName => Translations.Get("Service.MediaSource.Unknown");

    /// <summary>
    /// 歌词缓存的容量：来源变多以后必须封顶，否则长时间播放会一直堆积解析结果。
    /// 条数之外还受 <see cref="LyricsCacheBudgetPolicy.DefaultBudgetBytes"/> 约束：带逐字时间轴的条目比纯文本的重几十倍，
    /// 只按条数封顶挡不住真正占内存的那一类。
    /// Capacity of the lyric cache: with more sources it has to be capped, otherwise long playback keeps accumulating parsed results. On top of that entry
    /// count it is bounded by <see cref="LyricsCacheBudgetPolicy.DefaultBudgetBytes"/>: entries with a syllable timeline are tens of times heavier than
    /// plain text, and an entry-count cap alone does not hold back the kind that actually costs memory.
    /// </summary>
    private const int LyricsCacheCapacity = 64;

    private readonly LyricsService _lyricsService;
    private readonly LruCache<(string SessionId, LyricsRequest Request), LyricsResult?> _lyricsCache;
    private readonly HashSet<(string SessionId, LyricsRequest Request)> _pendingLyrics = [];
    private (string SessionId, LyricsRequest Request)? _lastLyricsKey;
    private readonly CancellationTokenSource _lifetime = new();
    private bool _disposed;

    /// <summary>
    /// 缓存代次：设置变化清空缓存时自增，让仍在飞行中的取词结果写不回来（否则它会把按旧设置取到的歌词塞进新缓存）。
    /// Cache generation: incremented when a settings change clears the cache, so a retrieval still in flight cannot write back
    /// (otherwise it would push lyrics fetched under the old settings into the new cache).
    /// </summary>
    private int _lyricsCacheGeneration;

    public event Action? EnrichmentCompleted;

    /// <summary>参与者名称，只用于诊断。/ Participant name, used for diagnostics only.</summary>
    public string PruneParticipantName => "lyrics-cache";

    /// <summary>
    /// 丢开歌词缓存。空闲档位就做这件事：此时既没有在播的媒体、用户也已经离开，缓存的歌词文本与解析结果只会占着内存，
    /// 而重新取回它们的代价只是一次网络请求，发生在用户真的回来播放之后。
    /// Drops the lyric cache. This is the whole job at the idle level: nothing is playing and the user is gone, so cached lyric text and parse
    /// results only occupy memory, while fetching them again costs one network request that happens after the user is genuinely back.
    ///
    /// 代次自增与设置变化时一致：仍在飞行中的取词结果会因此被丢弃，而不是把剪枝前的歌词写回新缓存。
    /// The generation is bumped exactly as a settings change does, so a retrieval still in flight is discarded instead of writing pre-prune lyrics
    /// back into the fresh cache.
    /// </summary>
    /// <param name="level">目标档位。/ The target level.</param>
    public void Prune(MemoryPruneLevel level)
    {
        if (_disposed || level < MemoryPruneLevel.Idle)
        {
            return;
        }

        _lyricsCache.Clear();
        _lyricsCacheGeneration++;

        // 封面缩略图与主色缓存也归本构建器管：它们是本类在构建快照时填进去的（见 Build），因此也由本类丢弃，
        // 而不是让某个"清理服务"隔着模块去动别人的静态缓存。
        // The thumbnail and dominant-color caches belong to this builder as well: this class fills them while building a snapshot (see Build), so it
        // drops them too, instead of some cleanup service reaching into another module's static cache.
        ArtworkLoader.ClearCache();
        BitmapHelper.ClearCache();
    }

    /// <summary>
    /// 创建快照构建器，并使用歌词服务执行与当前曲目版本绑定的异步补全。
    /// Creates the snapshot builder and uses the lyrics service for asynchronous enrichment tied to the current track version.
    /// </summary>
    public MediaSnapshotBuilder(LyricsService lyricsService)
        : this(lyricsService, TimeProvider.System)
    {
    }

    internal MediaSnapshotBuilder(LyricsService lyricsService, TimeProvider timeProvider)
    {
        _lyricsService = lyricsService;
        _lyricsCache = new(LyricsCacheCapacity,
            result => LyricsCacheBudgetPolicy.EstimateBytes(result?.Document),
            LyricsCacheBudgetPolicy.DefaultBudgetBytes, timeProvider);
        SettingsManager.SettingsChanged += OnSettingsChanged;
    }

    /// <summary>取消取词并解除设置订阅；释放后不再发布补全结果。</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        SettingsManager.SettingsChanged -= OnSettingsChanged;
        _lifetime.Cancel();
        _lifetime.Dispose();
        _lyricsCache.Clear();
        _lastLyricsKey = null;
    }

    /// <summary>
    /// 取词相关设置变化时清空缓存并立即重取当前曲目。
    /// Clears the cache and immediately refetches the current track after a retrieval-related settings change.
    /// </summary>
    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e)
    {
        if (_disposed || !LyricsCacheInvalidationPolicy.ShouldClearCache(e.PropertyName, e.ResetScope))
        {
            return;
        }

        _lyricsCache.Clear();
        _lyricsCacheGeneration++;
        EnrichmentCompleted?.Invoke();
    }

    /// <summary>
    /// 从一个稳定会话读取不可变媒体快照；异步封面或歌词结果通过补全事件另行发布。
    /// Builds an immutable media snapshot from a stable session; asynchronous artwork or lyrics results are published separately.
    /// </summary>
    public async Task<MediaSnapshot?> BuildAsync(MediaSession? session, bool isStarted, CancellationToken cancellationToken = default)
    {
        if (_disposed || session is null || !isStarted)
        {
            return MediaSnapshot.Disconnected;
        }

        var controlSession = session.ControlSession;

        // 第三方库可能在选中之后关闭会话并清除 ControlSession；本次构建跳过，关闭事件会安排下一次刷新。
        // The third-party library can close the session and clear ControlSession after selection; skip this build and let
        // the close event schedule the next refresh.
        if (controlSession is null)
        {
            return null;
        }

        SmtcMediaRead? read;
        try
        {
            // WinRT calls, stream hashing and WPF bitmap decoding can all wait on an external player or disk.
            // Frozen artwork can safely cross back to the UI thread; color brushes and lyric caches stay UI-owned.
            read = await Task.Run(async () =>
            {
                var songInfo = await controlSession.TryGetMediaPropertiesAsync();
                if (songInfo is null) return null;
                var playbackInfo = controlSession.GetPlaybackInfo();
                var timeline = controlSession.GetTimelineProperties();
                var artwork = ArtworkLoader.GetThumbnail(songInfo.Thumbnail);
                return new SmtcMediaRead(
                    songInfo.Title ?? string.Empty,
                    songInfo.Artist ?? string.Empty,
                    songInfo.AlbumTitle ?? string.Empty,
                    controlSession.SourceAppUserModelId ?? string.Empty,
                    playbackInfo.PlaybackType == Windows.Media.MediaPlaybackType.Video ||
                    songInfo.PlaybackType == Windows.Media.MediaPlaybackType.Video,
                    artwork,
                    artwork is null ? 0 : ArtworkLoader.CurrentThumbnailHash,
                    playbackInfo.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
                    playbackInfo.Controls?.IsPlayPauseToggleEnabled ?? false,
                    playbackInfo.Controls?.IsPreviousEnabled ?? false,
                    playbackInfo.Controls?.IsNextEnabled ?? false,
                    playbackInfo.Controls?.IsPlaybackPositionEnabled ?? false,
                    playbackInfo.Controls?.IsRepeatEnabled ?? false,
                    MapRepeatMode(playbackInfo.AutoRepeatMode),
                    playbackInfo.PlaybackRate is > 0 ? playbackInfo.PlaybackRate.Value : 1,
                    timeline.StartTime.TotalSeconds,
                    (timeline.EndTime - timeline.StartTime).TotalSeconds,
                    timeline.Position.TotalSeconds,
                    timeline.LastUpdatedTime);
            }, cancellationToken);
        }
        catch (Exception exception) when (exception is COMException or InvalidOperationException or ObjectDisposedException)
        {
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (read is null)
        {
            return null;
        }

        var duration = Math.Max(0, read.Duration);
        var position = Math.Clamp(read.Position - read.TimelineStart, 0, duration > 0 ? duration : double.MaxValue);
        BitmapHelper.GetDominantColors(1, read.ArtworkHash);
        var sourceId = read.SourceId;
        var title = read.Title;
        var artist = read.Artist;
        var request = new LyricsRequest(title, artist, read.Album, duration > 0 ? duration : null, null,
            PlaybackSourceId: sourceId);
        var lyrics = GetLyrics(session.Id, sourceId, request, read.IsVideo);

        return new MediaSnapshot(
            true,
            read.IsPlaying,
            read.CanPlayPause,
            read.CanSkipPrevious,
            read.CanSkipNext,
            title,
            artist,
            sourceId,
            MediaSourceNameFormatter.GetDisplayName(sourceId, UnknownSourceName),
            read.Artwork,
            lyrics,
            position,
            duration,
            duration > 0 && read.CanSeek,
            read.CanChangeRepeat,
            read.RepeatMode,
            read.PlaybackRate,
            read.TimelineUpdatedAt);
    }

    private sealed record SmtcMediaRead(
        string Title, string Artist, string Album, string SourceId, bool IsVideo, System.Windows.Media.Imaging.BitmapImage? Artwork,
        int ArtworkHash, bool IsPlaying, bool CanPlayPause, bool CanSkipPrevious, bool CanSkipNext,
        bool CanSeek, bool CanChangeRepeat, MediaRepeatMode RepeatMode, double PlaybackRate,
        double TimelineStart, double Duration, double Position, DateTimeOffset TimelineUpdatedAt);

    private static MediaRepeatMode MapRepeatMode(Windows.Media.MediaPlaybackAutoRepeatMode? mode) => mode switch
    {
        Windows.Media.MediaPlaybackAutoRepeatMode.None => MediaRepeatMode.Off,
        Windows.Media.MediaPlaybackAutoRepeatMode.List => MediaRepeatMode.All,
        Windows.Media.MediaPlaybackAutoRepeatMode.Track => MediaRepeatMode.One,
        _ => MediaRepeatMode.Unavailable
    };

    internal LyricsResult? GetLyrics(string sessionId, string sourceId, LyricsRequest request, bool isVideo = false)
    {
        if (_disposed) return null;
        if (!LyricsMediaEligibilityPolicy.ShouldFetch(sourceId, isVideo, SettingsManager.Current.AllowBrowserAndVideoLyrics))
            return null;
        request = request with { PlaybackSourceId = sourceId };
        // SMTC 的总时长可能随时间轴更新变化；它用于匹配，不用于缓存或在途请求的曲目身份。
        var key = (sessionId, request with { DurationSeconds = null });
        _lastLyricsKey = key;
        if (_lyricsCache.TryGetValue(key, out var cached))
        {
            return cached;
        }
        if (_pendingLyrics.Contains(key) ||
            IsProvidedBySourceProvider(sourceId) ||
            string.IsNullOrWhiteSpace(request.Title))
        {
            return null;
        }

        _pendingLyrics.Add(key);
        _ = LoadLyricsAsync(key, request);
        return null;
    }

    /// <summary>
    /// 该来源是否由来源提供器负责供词 —— 这类来源在这里不取词，因为提供器给的是逐字歌词与精确进度。
    /// Whether this source is served by a source provider: such a source is not fetched here, because the provider supplies word-level
    /// lyrics together with an exact position.
    /// </summary>
    private static bool IsProvidedBySourceProvider(string sourceId) =>
        sourceId.Contains("cloudmusic", StringComparison.OrdinalIgnoreCase) ||
        sourceId.Contains("netease", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 为"来源提供器读不出媒体"的会话发起一次在线取词兜底，结果同样进本类的歌词缓存，
    /// 因此下一次 <see cref="BuildAsync"/> 会把它挂到快照上。
    ///
    /// 这是常规路径的例外通道：<see cref="GetLyrics"/> 对来源提供器负责的来源一律不取词，提供器一旦失效就完全没有
    /// 兜底，界面只剩 SMTC 的标题与歌手。是否该走这条通道由 <see cref="MediaEnrichmentFallbackPolicy"/> 判定，
    /// 调用方 MUST NOT 无条件调用（提供器每 233 毫秒都会报一次"没有媒体"）。
    /// Starts one online retrieval for a session whose source provider cannot read any media. The result lands in this class's lyric
    /// cache, so the next <see cref="BuildAsync"/> attaches it to the snapshot.
    ///
    /// This is the exception channel of the ordinary path: <see cref="GetLyrics"/> never fetches for a source a provider is responsible
    /// for, so a failing provider leaves no fallback at all and only SMTC's title and artist remain. Whether this channel applies is
    /// decided by <see cref="MediaEnrichmentFallbackPolicy"/>, and the caller MUST NOT call this unconditionally (the provider reports
    /// "no media" every 233 milliseconds).
    /// </summary>
    /// <param name="sessionId">SMTC 会话标识，参与歌词缓存键。/ SMTC session identifier, part of the lyric cache key.</param>
    /// <param name="sourceId">来源应用的 SMTC 标识，保留调用方的来源上下文。</param>
    /// <param name="title">曲名。/ Title.</param>
    /// <param name="artist">歌手。/ Artist.</param>
    /// <param name="durationSeconds">曲目时长（秒）；不可用时传 null。/ Track duration in seconds, or null when unavailable.</param>
    public void RequestOnlineLyrics(string sessionId, string sourceId, string title, string artist, double? durationSeconds)
    {
        if (_disposed || string.IsNullOrWhiteSpace(sessionId) || string.IsNullOrWhiteSpace(title) ||
            !LyricsMediaEligibilityPolicy.ShouldFetch(sourceId, false, SettingsManager.Current.AllowBrowserAndVideoLyrics))
        {
            return;
        }

        var request = new LyricsRequest(title, artist, string.Empty, durationSeconds is > 0 ? durationSeconds : null, null,
            PlaybackSourceId: sourceId);
        // The service snapshot has no album field; reuse the matching request already read from SMTC.
        (string SessionId, LyricsRequest Request) key = _lastLyricsKey is { } last && last.SessionId == sessionId &&
                  last.Request.PlaybackSourceId == sourceId &&
                  last.Request.Title == title && last.Request.Artist == artist
            ? last : (sessionId, request with { DurationSeconds = null });
        if (_lyricsCache.TryGetValue(key, out _) || !_pendingLyrics.Add(key))
        {
            return;
        }

        _ = LoadLyricsAsync(key, request with { Album = key.Request.Album });
    }

    private async Task LoadLyricsAsync((string SessionId, LyricsRequest Request) key, LyricsRequest request)
    {
        var generation = _lyricsCacheGeneration;
        try
        {
            var result = await _lyricsService.GetLyricsAsync(request, _lifetime.Token);

            // 取词过程中设置若被改过（来源、署名行过滤），这次结果已经不属于当前配置，写入只会让用户以为设置没生效。
            // If the settings changed while this retrieval ran (sources, credit filtering), the result no longer belongs
            // to the current configuration and writing it would only make the setting look ineffective.
            if (!_disposed && generation == _lyricsCacheGeneration)
            {
                _lyricsCache.Set(key, result, LyricsCacheRetentionPolicy.Lifetime(result));
            }
        }
        catch
        {
            if (!_disposed && generation == _lyricsCacheGeneration)
            {
                _lyricsCache.Set(key, null, LyricsCacheRetentionPolicy.Lifetime(null));
            }
        }
        finally
        {
            _pendingLyrics.Remove(key);
            if (!_disposed) EnrichmentCompleted?.Invoke();
        }
    }

}
