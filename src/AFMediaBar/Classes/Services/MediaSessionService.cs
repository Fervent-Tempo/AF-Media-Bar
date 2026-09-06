using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AFMediaBar.Classes.Interop;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services.Lyrics;
using AFMediaBar.Classes.Services.Players;
using AFMediaBar.Classes.Utils;
using Windows.Media.Control;
using WindowsMediaController;
using static WindowsMediaController.MediaManager;

namespace AFMediaBar.Classes.Services;

/// <summary>
/// 监听 SMTC 媒体会话，选择活跃会话，构建统一 MediaSnapshot 并通过 SnapshotChanged 发布；
/// 同时提供控制命令（播放/暂停、上一首、下一首、切换会话、重连）。
/// 网易云会话优先使用内存轮询（进度、歌词、song id），轮询失败时回退到 SMTC 读取。
/// Listens to SMTC media sessions, selects the active session, builds a unified MediaSnapshot
/// published via SnapshotChanged, and exposes control commands (play/pause, prev/next,
/// session switch, reconnect). NetEase sessions prefer the memory poll (position, lyrics,
/// song id) and fall back to SMTC reads when the poll fails.
/// </summary>
public sealed class MediaSessionService : IDisposable
{
    private const string MemoryPlayerSourceId = "cloudmusic";
    private const string NetEaseWindowClass = "OrpheusBrowserHost";
    private const string UnknownSourceName = "未知来源";
    private const string UnknownArtistName = "未知艺术家";
    private static readonly TimeSpan AutoSwitchGracePeriod = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MissingSessionGracePeriod = TimeSpan.FromSeconds(3);
    private const int SessionSnapshotRetryCount = 4;
    private static readonly TimeSpan MemoryPlayerPollInterval = TimeSpan.FromMilliseconds(233);
    private static readonly (string[] Tokens, string[] ProcessNames)[] SourceProcesses =
    [
        (["cloudmusic", "netease", "163music"], ["cloudmusic"]),
        (["qqmusic"], ["QQMusic"]),
        (["kugou", "kgmusic"], ["KuGou", "KuGouMusic"]),
        (["spotify"], ["Spotify"]),
        (["chrome"], ["chrome"]),
        (["msedge", "microsoftedge"], ["msedge"]),
        (["firefox"], ["firefox"]),
        (["vlc"], ["vlc"]),
        (["potplayer", "daum"], ["PotPlayerMini64", "PotPlayerMini"]),
        (["zunemusic", "media.player", "wmplayer"], ["Microsoft.Media.Player", "Music.UI", "wmplayer"]),
        (["mpv"], ["mpv"]),
        (["foobar"], ["foobar2000"])
    ];

    private readonly MediaManager _mediaManager = new();
    private readonly Dispatcher _dispatcher;
    private readonly object _publishGate = new();

    // 本服务的选择状态在 UI 线程串行更新；第三方会话集合仍需复制为稳定快照。
    // Selection state is serialized on the UI thread; the third-party session collection still needs a stable copy.
    private string? _selectedKey;
    private string? _selectedSourceId;
    private IReadOnlyList<MediaSessionOption>? _lastSessionOptions;
    private string? _pendingAutoSwitchKey;
    private DateTime _pendingAutoSwitchSinceUtc;
    private DateTime _missingSessionSinceUtc;
    private readonly DispatcherTimer _autoSwitchTimer;

    // 快照状态：sessionSnapshot 来自 SMTC，memorySnapshot 来自网易云内存轮询（存在时优先）。
    // Snapshot state: sessionSnapshot comes from SMTC; memorySnapshot from the NetEase poll wins when present.
    private MediaSnapshot _sessionSnapshot = MediaSnapshot.Disconnected;
    private MediaSnapshot? _memorySnapshot;
    private MediaSnapshot _lastSnapshot = MediaSnapshot.Disconnected;

    // 内存播放器（网易云）轮询状态
    private NetEase? _memoryPlayer;
    private PlayerInfo? _memoryPlayerInfo;
    private int _memoryPlayerVersion;
    private CancellationTokenSource? _memoryPlayerCancellation;
    private readonly Dictionary<string, BitmapImage?> _artworkCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _pendingArtwork = new(StringComparer.OrdinalIgnoreCase);

    // 歌词按标识缓存（null 表示已尝试但无歌词）；pending 集合去重并发拉取。
    // Lyrics cache per identity (null marks an attempted miss); pending set deduplicates in-flight fetches.
    private readonly Dictionary<string, LyricsResult?> _lyricsCache = new(StringComparer.Ordinal);
    private readonly HashSet<string> _pendingLyrics = new(StringComparer.Ordinal);
    private readonly LyricsService _lyricsService =
        new(new NetEaseLyricsProvider(), new LrclibLyricsProvider());

    private bool _isDisposed;

    /// <summary>最新的媒体快照；服务启动后尚未构建过时为空。 / Latest snapshot; null until the first refresh.</summary>
    public MediaSnapshot? CurrentSnapshot { get; private set; }

    /// <summary>在 UI 线程上触发。 / Raised on the UI thread.</summary>
    public event EventHandler<MediaSnapshot>? SnapshotChanged;

    /// <summary>会话列表变化（打开/关闭/选择/播放状态变化）时在 UI 线程触发。 / Raised on the UI thread when the session list changes.</summary>
    public event Action<IReadOnlyList<MediaSessionOption>>? SessionsChanged;

    /// <summary>当前选中会话的来源标识（AppUserModelId）。 / Source id (AppUserModelId) of the selected session.</summary>
    public string SelectedSourceId => _lastSnapshot.SourceId;

    /// <summary>当前选中会话的显示名称。 / Display name of the selected session.</summary>
    public string SelectedSourceName => _lastSnapshot.SourceName;

    /// <summary>当前可选媒体会话列表。/ Current selectable media-session list.</summary>
    public IReadOnlyList<MediaSessionOption> CurrentSessionOptions => _lastSessionOptions ?? Array.Empty<MediaSessionOption>();

    public MediaSessionService()
    {
        _dispatcher = Application.Current.Dispatcher;
        _autoSwitchTimer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(200)
        };
        _autoSwitchTimer.Tick += AutoSwitchTimer_Tick;
        _mediaManager.OnAnyMediaPropertyChanged += MediaManager_OnAnyMediaPropertyChanged;
        _mediaManager.OnAnyPlaybackStateChanged += MediaManager_OnAnyPlaybackStateChanged;
        _mediaManager.OnAnySessionOpened += MediaManager_OnAnySessionOpened;
        _mediaManager.OnAnySessionClosed += MediaManager_OnAnySessionClosed;
        _mediaManager.OnFocusedSessionChanged += MediaManager_OnFocusedSessionChanged;
        _mediaManager.OnAnyTimelinePropertyChanged += MediaManager_OnAnyTimelinePropertyChanged;
        _mediaManager.Start();
        StartMemoryPlayerPoll();

        // 建立初始会话选择（焦点 → 播放中 → 第一个）。
        ScheduleSessionsRefresh();
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        CancelMemoryPlayerPoll();
        _autoSwitchTimer.Stop();
        _autoSwitchTimer.Tick -= AutoSwitchTimer_Tick;
        _memoryPlayer?.Dispose();
        _memoryPlayer = null;

        _mediaManager.OnAnyMediaPropertyChanged -= MediaManager_OnAnyMediaPropertyChanged;
        _mediaManager.OnAnyPlaybackStateChanged -= MediaManager_OnAnyPlaybackStateChanged;
        _mediaManager.OnAnySessionOpened -= MediaManager_OnAnySessionOpened;
        _mediaManager.OnAnySessionClosed -= MediaManager_OnAnySessionClosed;
        _mediaManager.OnFocusedSessionChanged -= MediaManager_OnFocusedSessionChanged;
        _mediaManager.OnAnyTimelinePropertyChanged -= MediaManager_OnAnyTimelinePropertyChanged;
        _mediaManager.Dispose();
    }

    /// <summary>
    /// 立即同步构建并发布一次快照（例如任务栏窗口重建后重放当前状态）。
    /// Synchronously builds and publishes one snapshot, e.g. to replay state after the taskbar window is recreated.
    /// </summary>
    public void RefreshNow() => RefreshSnapshot();

    /// <summary>
    /// 重新扫描 SMTC 会话并刷新（旧版 ReconnectAsync 的等价物）。
    /// Re-scans SMTC sessions and refreshes (the counterpart of the legacy ReconnectAsync).
    /// </summary>
    public Task ReconnectAsync()
    {
        _mediaManager.ForceUpdate();
        RefreshSnapshot();
        return Task.CompletedTask;
    }

    /// <summary>切换到指定会话。 / Switches to the session identified by key.</summary>
    public void SelectSession(string key)
    {
        if (string.IsNullOrEmpty(key) || !TryGetStableSessions(out var sessions))
        {
            return;
        }

        var selected = sessions.FirstOrDefault(session =>
            string.Equals(session.Id, key, StringComparison.Ordinal));
        if (selected is null)
        {
            return;
        }

        ClearPendingAutoSwitch();
        ClearMissingSession();
        _selectedKey = key;
        _selectedSourceId = selected.ControlSession.SourceAppUserModelId ?? string.Empty;
        PublishSessions(sessions);
        RefreshSnapshot();
    }

    public async Task TogglePlayPauseAsync()
    {
        if (GetSelectedSession() is not { } session)
        {
            return;
        }

        try
        {
            await session.ControlSession.TryTogglePlayPauseAsync();
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            Debug.WriteLine($"[MediaSessionService] TogglePlayPause failed: {ex}");
        }
    }

    public async Task SkipPreviousAsync()
    {
        if (GetSelectedSession() is not { } session)
        {
            return;
        }

        try
        {
            await session.ControlSession.TrySkipPreviousAsync();
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            Debug.WriteLine($"[MediaSessionService] SkipPrevious failed: {ex}");
        }
    }

    public async Task SkipNextAsync()
    {
        if (GetSelectedSession() is not { } session)
        {
            return;
        }

        try
        {
            await session.ControlSession.TrySkipNextAsync();
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            Debug.WriteLine($"[MediaSessionService] SkipNext failed: {ex}");
        }
    }

    /// <summary>
    /// 激活当前媒体来源；窗口已关闭到托盘时，重新启动同一可执行文件以请求应用恢复前台。
    /// Activates the current media source; when its window is closed to tray, starts the same executable to request foreground restoration.
    /// </summary>
    public void ActivateSelectedSource()
    {
        var sourceId = SelectedSourceId;
        if (string.IsNullOrWhiteSpace(sourceId))
            return;

        var executablePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var processName in ResolveProcessNames(sourceId))
        {
            foreach (var process in Process.GetProcessesByName(processName))
            {
                using (process)
                {
                    try
                    {
                        var handle = process.MainWindowHandle;
                        if (handle == IntPtr.Zero)
                        {
                            var executablePath = process.MainModule?.FileName;
                            if (!string.IsNullOrWhiteSpace(executablePath))
                                executablePaths.Add(executablePath);
                            continue;
                        }

                        NativeMethods.ShowWindow(handle, NativeMethods.SW_RESTORE);
                        NativeMethods.SetForegroundWindow(handle);
                        return;
                    }
                    catch (Exception ex) when (ex is InvalidOperationException or
                                                   System.ComponentModel.Win32Exception or
                                                   NotSupportedException)
                    {
                        // 解析窗口或可执行文件时进程可能已经退出。
                        // The process can exit while its window or executable is being resolved.
                    }
                }
            }
        }

        try
        {
            if (sourceId.Contains('!'))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"shell:AppsFolder\\{sourceId}",
                    UseShellExecute = true
                });
                return;
            }

            var executablePath = executablePaths.FirstOrDefault(File.Exists);
            if (executablePath is not null)
                Process.Start(new ProcessStartInfo(executablePath) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            Debug.WriteLine($"[MediaSessionService] Activate source failed: {ex}");
        }
    }

    private static IEnumerable<string> ResolveProcessNames(string sourceId)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var mapping in SourceProcesses)
        {
            if (mapping.Tokens.Any(token => sourceId.Contains(token, StringComparison.OrdinalIgnoreCase)))
            {
                foreach (var processName in mapping.ProcessNames)
                    names.Add(processName);
            }
        }

        if (sourceId.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            names.Add(Path.GetFileNameWithoutExtension(sourceId));

        return names;
    }

    #region MediaManager events

    private void MediaManager_OnAnyMediaPropertyChanged(
        MediaSession mediaSession,
        GlobalSystemMediaTransportControlsSessionMediaProperties mediaProperties) => ScheduleRefresh();

    private void MediaManager_OnAnyPlaybackStateChanged(
        MediaSession mediaSession,
        GlobalSystemMediaTransportControlsSessionPlaybackInfo playbackInfo) => ScheduleSessionsRefresh();

    private void MediaManager_OnAnySessionOpened(MediaSession mediaSession) => ScheduleSessionsRefresh();

    private void MediaManager_OnAnySessionClosed(MediaSession mediaSession) => ScheduleSessionsRefresh();

    private void MediaManager_OnFocusedSessionChanged(MediaSession mediaSession) => ScheduleSessionsRefresh();

    private void MediaManager_OnAnyTimelinePropertyChanged(
        MediaSession mediaSession,
        GlobalSystemMediaTransportControlsSessionTimelineProperties timelineProperties) => ScheduleRefresh();

    private void ScheduleRefresh()
    {
        if (_dispatcher.HasShutdownStarted || _isDisposed)
        {
            return;
        }

        // MediaManager 事件来自后台线程；快照构建（缩略图解码、主色提取）在 UI 线程执行。
        _dispatcher.BeginInvoke(RefreshSnapshot, DispatcherPriority.Normal);
    }

    private void ScheduleSessionsRefresh()
    {
        if (_dispatcher.HasShutdownStarted || _isDisposed)
        {
            return;
        }

        _dispatcher.BeginInvoke(RefreshSessionListAsync, DispatcherPriority.Normal);
    }

    #endregion

    #region Session list & selection

    private void RefreshSessionListAsync()
    {
        if (!TryGetStableSessions(out var sessions))
        {
            // 第三方集合正在变更时保留现有状态，等待下一次事件重试。
            // Keep the current state while the third-party collection is changing and retry on the next event.
            return;
        }

        if (sessions.Length == 0)
        {
            if (TryHoldMissingSession())
            {
                return;
            }

            // 最后一个来源关闭时列表为空，立即清除旧状态。
            _selectedKey = null;
            _selectedSourceId = null;
            ClearMissingSession();
            PublishSessions(sessions);
            Publish(MediaSnapshot.Disconnected);
            return;
        }

        // 优先保留当前选择；否则焦点会话 → 播放中的会话 → 第一个。
        var selected = sessions.FirstOrDefault(session =>
            string.Equals(session.Id, _selectedKey, StringComparison.Ordinal));
        if (selected is not null)
        {
            _selectedSourceId = selected.ControlSession.SourceAppUserModelId ?? _selectedSourceId;
            ClearMissingSession();
        }

        var restored = selected is null ? FindRestoredMissingSession(sessions) : null;
        if (restored is not null)
        {
            selected = restored;
            _selectedKey = selected.Id;
            _selectedSourceId = selected.ControlSession.SourceAppUserModelId ?? string.Empty;
            ClearMissingSession();
        }

        if (selected is null)
        {
            if (TryHoldMissingSession())
            {
                return;
            }

            ClearPendingAutoSwitch();
            ClearMissingSession();
            var focused = _mediaManager.GetFocusedSession();
            selected = focused is not null
                ? sessions.FirstOrDefault(session =>
                    string.Equals(session.Id, focused.Id, StringComparison.Ordinal))
                : null;
            selected ??= sessions.FirstOrDefault(IsPlaying) ?? sessions[0];
            _selectedKey = selected.Id;
            _selectedSourceId = selected.ControlSession.SourceAppUserModelId ?? string.Empty;
        }

        PublishSessions(sessions);
        RefreshSnapshot();
    }

    /// <summary>当前选择的会话不存在时回退到焦点会话。 / Falls back to the focused session when the selection is gone.</summary>
    private MediaSession? GetSelectedSession()
    {
        if (TryGetStableSessions(out var sessions))
        {
            var session = sessions.FirstOrDefault(candidate =>
                string.Equals(candidate.Id, _selectedKey, StringComparison.Ordinal));
            if (session is not null)
            {
                _selectedSourceId = session.ControlSession.SourceAppUserModelId ?? _selectedSourceId;
                return session;
            }

            if (IsMissingSessionGraceActive())
            {
                return null;
            }
        }

        if (TryHoldMissingSession())
        {
            return null;
        }

        return _mediaManager.GetFocusedSession();
    }

    /// <summary>当前会话未在播放而其他会话在播放时自动切换（自动跟随播放）。 / Auto-switches when the selected session stops and another is playing.</summary>
    private bool TryAutoSwitchToPlaying()
    {
        var current = GetSelectedSession();
        if (current is null)
        {
            // 会话消失缓冲期间保持计时器运行，不能被“无当前会话”分支取消。
            // Keep the timer alive while the selected session is temporarily absent.
            if (IsMissingSessionGraceActive())
            {
                _autoSwitchTimer.Start();
            }
            else
            {
                ClearPendingAutoSwitch();
            }

            return false;
        }

        if (IsPlaying(current))
        {
            ClearPendingAutoSwitch();
            return false;
        }

        // 浏览器视频切换时会短暂报告暂停；只要浏览器会话仍存在，就保持当前来源。
        // Browser videos can report paused while switching; keep the browser source while its session exists.
        var currentSourceId = current.ControlSession.SourceAppUserModelId ?? string.Empty;
        if (IsBrowserSource(currentSourceId))
        {
            ClearPendingAutoSwitch();
            return false;
        }

        if (!TryGetStableSessions(out var sessions))
        {
            return false;
        }

        var replacement = sessions
            .FirstOrDefault(candidate => !ReferenceEquals(candidate, current) && IsPlaying(candidate));
        if (replacement is null)
        {
            ClearPendingAutoSwitch();
            return false;
        }

        if (!string.Equals(_pendingAutoSwitchKey, replacement.Id, StringComparison.Ordinal))
        {
            _pendingAutoSwitchKey = replacement.Id;
            _pendingAutoSwitchSinceUtc = DateTime.UtcNow;
            _autoSwitchTimer.Start();
            return false;
        }

        var elapsed = DateTime.UtcNow - _pendingAutoSwitchSinceUtc;
        if (elapsed < AutoSwitchGracePeriod)
        {
            _autoSwitchTimer.Start();
            return false;
        }

        ClearPendingAutoSwitch();
        _selectedKey = replacement.Id;
        _selectedSourceId = replacement.ControlSession.SourceAppUserModelId ?? string.Empty;
        PublishSessions(sessions);
        return true;
    }

    private void AutoSwitchTimer_Tick(object? sender, EventArgs e)
    {
        if (_isDisposed)
        {
            _autoSwitchTimer.Stop();
            return;
        }

        if (_pendingAutoSwitchSinceUtc != default &&
            DateTime.UtcNow - _pendingAutoSwitchSinceUtc >= AutoSwitchGracePeriod)
            _autoSwitchTimer.Stop();

        RefreshSessionListAsync();
    }

    private void ClearPendingAutoSwitch()
    {
        _pendingAutoSwitchKey = null;
        _pendingAutoSwitchSinceUtc = default;
        _autoSwitchTimer.Stop();
    }

    private void PublishSessions(IReadOnlyList<MediaSession> sessions)
    {
        var occurrences = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var options = sessions
            .Select(session =>
            {
                var sourceId = session.ControlSession.SourceAppUserModelId ?? string.Empty;
                occurrences.TryGetValue(sourceId, out var occurrence);
                occurrence++;
                occurrences[sourceId] = occurrence;
                var displayName = MediaSourceNameFormatter.GetDisplayName(sourceId, UnknownSourceName);
                if (occurrence > 1)
                {
                    displayName = $"{displayName} ({occurrence})";
                }

                return new MediaSessionOption(
                    session.Id,
                    sourceId,
                    displayName,
                    IsPlaying(session),
                    session.Id == _selectedKey);
            })
            .ToArray();

        if (_lastSessionOptions is not null && options.SequenceEqual(_lastSessionOptions))
        {
            return;
        }

        _lastSessionOptions = options;
        SessionsChanged?.Invoke(options);
    }

    private bool TryGetStableSessions(out MediaSession[] sessions)
    {
        for (var attempt = 0; attempt < SessionSnapshotRetryCount; attempt++)
        {
            try
            {
                sessions = _mediaManager.CurrentMediaSessions.Values.ToArray();
                return true;
            }
            catch (InvalidOperationException)
            {
                if (attempt + 1 < SessionSnapshotRetryCount)
                {
                    Thread.Yield();
                }
            }
        }

        sessions = Array.Empty<MediaSession>();
        Debug.WriteLine("[MediaSessionService] Failed to copy CurrentMediaSessions after retries.");
        return false;
    }

    private bool TryHoldMissingSession()
    {
        if (!IsBrowserSource(_selectedSourceId ?? _lastSnapshot.SourceId))
        {
            return false;
        }

        if (_missingSessionSinceUtc == default)
        {
            _missingSessionSinceUtc = DateTime.UtcNow;
        }

        if (DateTime.UtcNow - _missingSessionSinceUtc >= MissingSessionGracePeriod)
        {
            return false;
        }

        // 浏览器切换视频时 SMTC 可能先关闭旧会话，再创建新会话；此期间不切到后台音乐。
        // During browser video switching SMTC may close the old session before creating the new one; do not switch to background music meanwhile.
        _autoSwitchTimer.Start();
        return true;
    }

    private bool IsMissingSessionGraceActive() =>
        _missingSessionSinceUtc != default &&
        DateTime.UtcNow - _missingSessionSinceUtc < MissingSessionGracePeriod &&
        IsBrowserSource(_selectedSourceId ?? _lastSnapshot.SourceId);

    private void ClearMissingSession()
    {
        _missingSessionSinceUtc = default;
        if (string.IsNullOrEmpty(_pendingAutoSwitchKey))
        {
            _autoSwitchTimer.Stop();
        }
    }

    private MediaSession? FindRestoredMissingSession(IReadOnlyList<MediaSession> sessions)
    {
        if (!IsMissingSessionGraceActive())
        {
            return null;
        }

        return sessions.FirstOrDefault(session =>
            string.Equals(
                session.ControlSession.SourceAppUserModelId,
                _selectedSourceId,
                StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsPlaying(MediaSession session)
    {
        try
        {
            return session.ControlSession.GetPlaybackInfo().PlaybackStatus ==
                GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
        }
        catch
        {
            return false;
        }
    }

    #endregion

    #region Snapshot

    private void RefreshSnapshot()
    {
        if (_isDisposed)
        {
            return;
        }

        try
        {
            var snapshot = BuildSessionSnapshot();
            if (snapshot is null)
            {
                return;
            }

            if (TryAutoSwitchToPlaying())
            {
                snapshot = BuildSessionSnapshot();
                if (snapshot is null)
                {
                    return;
                }
            }

            Publish(snapshot);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MediaSessionService] Failed to refresh snapshot: {ex}");
        }
    }

    /// <summary>构建会话快照；元数据拉取失败时返回 null（保持上一次已发布的快照）。</summary>
    private MediaSnapshot? BuildSessionSnapshot()
    {
        var session = GetSelectedSession();
        if (session is null && IsMissingSessionGraceActive())
        {
            return null;
        }

        if (session is null || !_mediaManager.IsStarted)
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
        var artwork = BitmapHelper.GetThumbnail(songInfo.Thumbnail);
        BitmapHelper.GetDominantColors(1);

        var sourceId = controlSession.SourceAppUserModelId ?? string.Empty;
        var title = songInfo.Title ?? string.Empty;
        var artist = songInfo.Artist ?? string.Empty;

        // 非网易云来源按标题/艺术家走 Lrclib 兜底；网易云由内存路径按 song id 精确取词。
        var lyricsKey = $"{session.Id}\u001f{title}\u001f{artist}";
        var lyrics = GetSessionLyrics(lyricsKey, sourceId, title, artist, songInfo, timelineProperties);

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

    /// <summary>会话歌词：按 来源+标题+艺术家 缓存；网易云来源跳过（内存路径负责）。</summary>
    private LyricsResult? GetSessionLyrics(
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
            MemoryPlayerControlsMatch(sourceId) ||
            string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        _pendingLyrics.Add(key);
        _ = LoadSessionLyricsAsync(key, title, artist, songInfo, timelineProperties);
        return null;
    }

    private async Task LoadSessionLyricsAsync(
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
            var result = await _lyricsService.GetLyricsAsync(request, CancellationToken.None);
            _lyricsCache[key] = result;
        }
        catch
        {
            _lyricsCache[key] = null;
        }
        finally
        {
            _pendingLyrics.Remove(key);
            RefreshSnapshot();
        }
    }

    /// <summary>发布会话快照；仅在当前来源匹配网易云时使用内存快照。/ Publishes a session snapshot; memory data is used only when the selected source matches NetEase.</summary>
    private void Publish(MediaSnapshot snapshot)
    {
        _sessionSnapshot = snapshot;
        PublishResolved(ShouldUseMemorySnapshot(snapshot) && _memorySnapshot is { } memory
            ? memory
            : snapshot);
    }

    private static bool ShouldUseMemorySnapshot(MediaSnapshot snapshot) =>
        !snapshot.IsConnected || MemoryPlayerControlsMatch(snapshot.SourceId);

    private static bool IsBrowserSource(string sourceId) =>
        sourceId.Contains("chrome", StringComparison.OrdinalIgnoreCase) ||
        sourceId.Contains("msedge", StringComparison.OrdinalIgnoreCase) ||
        sourceId.Contains("microsoftedge", StringComparison.OrdinalIgnoreCase) ||
        sourceId.Contains("firefox", StringComparison.OrdinalIgnoreCase);

    private void PublishResolved(MediaSnapshot snapshot)
    {
        if (_isDisposed)
        {
            return;
        }

        lock (_publishGate)
        {
            if (Equals(_lastSnapshot, snapshot))
            {
                return;
            }

            _lastSnapshot = snapshot;
            CurrentSnapshot = snapshot;
            SnapshotChanged?.Invoke(this, snapshot);
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

    #endregion

    #region Memory player (NetEase poll)

    private void StartMemoryPlayerPoll()
    {
        if (_isDisposed || _memoryPlayerCancellation is not null)
        {
            return;
        }

        var cancellation = new CancellationTokenSource();
        _memoryPlayerCancellation = cancellation;
        _ = PollMemoryPlayersAsync(cancellation, cancellation.Token);
    }

    private async Task PollMemoryPlayersAsync(
        CancellationTokenSource cancellation,
        CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                PlayerInfo? playerInfo = null;
                try
                {
                    playerInfo = ReadMemoryPlayerInfo();
                }
                catch
                {
                    ResetMemoryPlayer();
                }

                if (_isDisposed)
                {
                    return;
                }

                if (playerInfo is { } info)
                {
                    // 来源回退：内存轮询失败时下面的 PublishMemorySnapshot(null) 会回到 SMTC 快照。
                    if (ShouldUseMemoryPlayerInfo(info))
                    {
                        PublishMemoryPlayerInfo(info, cancellationToken);
                    }
                    else
                    {
                        PublishMemorySnapshot(null);
                    }
                }
                else
                {
                    PublishMemorySnapshot(null);
                }

                await Task.Delay(MemoryPlayerPollInterval, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutdown or service disposal stops the polling loop.
        }
        finally
        {
            if (ReferenceEquals(_memoryPlayerCancellation, cancellation))
            {
                _memoryPlayerCancellation = null;
            }

            cancellation.Dispose();
        }
    }

    private PlayerInfo? ReadMemoryPlayerInfo()
    {
        var hwnd = NativeMethods.FindWindow(NetEaseWindowClass, null);
        if (hwnd == IntPtr.Zero)
        {
            ResetMemoryPlayer();
            return null;
        }

        NativeMethods.GetWindowThreadProcessId(hwnd, out var processId);
        if (processId <= 0)
        {
            ResetMemoryPlayer();
            return null;
        }

        if (_memoryPlayer is null || !_memoryPlayer.Validate(processId))
        {
            _memoryPlayer?.Dispose();
            _memoryPlayer = new NetEase(processId);
        }

        return _memoryPlayer.GetPlayerInfo();
    }

    private void ResetMemoryPlayer()
    {
        _memoryPlayer?.Dispose();
        _memoryPlayer = null;
        _memoryPlayerInfo = null;
        _memoryPlayerVersion++;
    }

    /// <summary>暂停且当前 SMTC 会话不是网易云时，内存快照不发布（避免盖住其他来源）。</summary>
    private bool ShouldUseMemoryPlayerInfo(PlayerInfo playerInfo)
    {
        if (!playerInfo.Pause)
        {
            return true;
        }

        var sessionSnapshot = _sessionSnapshot;
        return !sessionSnapshot.IsConnected ||
            MemoryPlayerControlsMatch(sessionSnapshot.SourceId) ||
            string.Equals(
                sessionSnapshot.SourceName,
                MediaSourceNameFormatter.GetDisplayName(MemoryPlayerSourceId, UnknownSourceName),
                StringComparison.OrdinalIgnoreCase);
    }

    private void PublishMemoryPlayerInfo(PlayerInfo playerInfo, CancellationToken cancellationToken)
    {
        var version = _memoryPlayerInfo is { } current &&
            string.Equals(current.Identity, playerInfo.Identity, StringComparison.Ordinal) &&
            string.Equals(current.Cover, playerInfo.Cover, StringComparison.Ordinal)
                ? _memoryPlayerVersion
                : ++_memoryPlayerVersion;
        _memoryPlayerInfo = playerInfo;

        var coverUrl = playerInfo.Cover;
        ImageSource? artwork = null;
        var shouldDownloadArtwork = false;
        if (_artworkCache.TryGetValue(coverUrl, out var cachedArtwork))
        {
            artwork = cachedArtwork;
        }
        else if (!string.IsNullOrWhiteSpace(coverUrl))
        {
            shouldDownloadArtwork = _pendingArtwork.Add(coverUrl);
        }

        if (shouldDownloadArtwork)
        {
            _ = LoadMemoryArtworkAsync(coverUrl, version, cancellationToken);
        }

        _lyricsCache.TryGetValue(playerInfo.Identity, out var lyrics);
        var shouldLoadLyrics = !_lyricsCache.ContainsKey(playerInfo.Identity) &&
            _pendingLyrics.Add(playerInfo.Identity);

        PublishMemorySnapshot(CreateMemorySnapshot(playerInfo, artwork) with { Lyrics = lyrics });

        if (shouldLoadLyrics)
        {
            _ = LoadLyricsAsync(playerInfo, cancellationToken);
        }
    }

    private MediaSnapshot CreateMemorySnapshot(PlayerInfo playerInfo, ImageSource? artwork)
    {
        var sourceName = MediaSourceNameFormatter.GetDisplayName(MemoryPlayerSourceId, UnknownSourceName);
        var controlsAvailable = _sessionSnapshot.IsConnected &&
            (MemoryPlayerControlsMatch(_sessionSnapshot.SourceId) ||
                string.Equals(_sessionSnapshot.SourceName, sourceName, StringComparison.OrdinalIgnoreCase));

        return new MediaSnapshot(
            true,
            !playerInfo.Pause,
            controlsAvailable && _sessionSnapshot.CanPlayPause,
            controlsAvailable && _sessionSnapshot.CanSkipPrevious,
            controlsAvailable && _sessionSnapshot.CanSkipNext,
            playerInfo.Title,
            string.IsNullOrWhiteSpace(playerInfo.Artists) ? UnknownArtistName : playerInfo.Artists,
            MemoryPlayerSourceId,
            sourceName,
            artwork,
            null,
            playerInfo.Schedule);
    }

    private static bool MemoryPlayerControlsMatch(string sourceId) =>
        sourceId.Contains("cloudmusic", StringComparison.OrdinalIgnoreCase) ||
        sourceId.Contains("netease", StringComparison.OrdinalIgnoreCase);

    private void PublishMemorySnapshot(MediaSnapshot? snapshot)
    {
        if (snapshot is null)
        {
            _memoryPlayerVersion++;
        }

        _memorySnapshot = snapshot;
        PublishResolved(ShouldUseMemorySnapshot(_sessionSnapshot) && snapshot is { } memory
            ? memory
            : _sessionSnapshot);
    }

    private async Task LoadMemoryArtworkAsync(string coverUrl, int version, CancellationToken cancellationToken)
    {
        try
        {
            var artwork = await BitmapHelper.GetImageFromUrlAsync(coverUrl, cancellationToken);
            _artworkCache[coverUrl] = artwork;

            if (artwork is not null &&
                !_isDisposed &&
                version == _memoryPlayerVersion &&
                _memoryPlayerInfo is { } info &&
                string.Equals(info.Cover, coverUrl, StringComparison.OrdinalIgnoreCase))
            {
                _lyricsCache.TryGetValue(info.Identity, out var lyrics);
                PublishMemorySnapshot(CreateMemorySnapshot(info, artwork) with { Lyrics = lyrics });
            }
        }
        catch (OperationCanceledException)
        {
            // The service switched tracks or is shutting down.
        }
        catch
        {
            // Cache the miss so an unreachable cover URL is not retried on every poll.
            _artworkCache[coverUrl] = null;
        }
        finally
        {
            _pendingArtwork.Remove(coverUrl);
        }
    }

    private async Task LoadLyricsAsync(PlayerInfo playerInfo, CancellationToken cancellationToken)
    {
        try
        {
            var request = new LyricsRequest(
                playerInfo.Title,
                playerInfo.Artists,
                playerInfo.Album,
                playerInfo.Duration,
                playerInfo.Identity);
            var result = await _lyricsService.GetLyricsAsync(request, cancellationToken);
            _lyricsCache[playerInfo.Identity] = result;

            if (!_isDisposed &&
                _memoryPlayerInfo is { } info &&
                string.Equals(info.Identity, playerInfo.Identity, StringComparison.Ordinal))
            {
                _artworkCache.TryGetValue(info.Cover, out var artwork);
                PublishMemorySnapshot(CreateMemorySnapshot(info, artwork) with { Lyrics = result });
            }
        }
        catch (OperationCanceledException)
        {
            // The service switched tracks or is shutting down.
        }
        catch
        {
            _lyricsCache[playerInfo.Identity] = null;
        }
        finally
        {
            _pendingLyrics.Remove(playerInfo.Identity);
        }
    }

    private void CancelMemoryPlayerPoll()
    {
        _memoryPlayerCancellation?.Cancel();
    }

    #endregion
}
