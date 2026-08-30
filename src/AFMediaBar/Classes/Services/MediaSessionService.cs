using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Utils;
using Windows.Media.Control;
using WindowsMediaController;
using static WindowsMediaController.MediaManager;

namespace AFMediaBar.Classes.Services;

/// <summary>
/// 监听 SMTC 媒体会话，构建统一的 MediaSnapshot 并通过 SnapshotChanged 发布。
/// Listens to SMTC media sessions, builds a unified MediaSnapshot and publishes it via SnapshotChanged.
/// </summary>
public sealed class MediaSessionService : IDisposable
{
    private readonly MediaManager _mediaManager = new();
    private readonly Dispatcher _dispatcher;
    private bool _isDisposed;

    /// <summary>最新的媒体快照；服务启动后尚未构建过时为空。 / Latest snapshot; null until the first refresh.</summary>
    public MediaSnapshot? CurrentSnapshot { get; private set; }

    /// <summary>在 UI 线程上触发。 / Raised on the UI thread.</summary>
    public event EventHandler<MediaSnapshot>? SnapshotChanged;

    public MediaSessionService()
    {
        _dispatcher = Application.Current.Dispatcher;
        _mediaManager.OnAnyMediaPropertyChanged += MediaManager_OnAnyMediaPropertyChanged;
        _mediaManager.OnAnyPlaybackStateChanged += MediaManager_OnAnyPlaybackStateChanged;
        _mediaManager.OnAnySessionOpened += MediaManager_OnAnySessionOpened;
        _mediaManager.OnAnySessionClosed += MediaManager_OnAnySessionClosed;
        _mediaManager.Start();
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _mediaManager.OnAnyMediaPropertyChanged -= MediaManager_OnAnyMediaPropertyChanged;
        _mediaManager.OnAnyPlaybackStateChanged -= MediaManager_OnAnyPlaybackStateChanged;
        _mediaManager.OnAnySessionOpened -= MediaManager_OnAnySessionOpened;
        _mediaManager.OnAnySessionClosed -= MediaManager_OnAnySessionClosed;
        _mediaManager.Dispose();
    }

    /// <summary>
    /// 立即同步构建并发布一次快照（例如任务栏窗口重建后重放当前状态）。
    /// Synchronously builds and publishes one snapshot, e.g. to replay state after the taskbar window is recreated.
    /// </summary>
    public void RefreshNow() => RefreshSnapshot();

    private void MediaManager_OnAnyMediaPropertyChanged(
        MediaSession mediaSession,
        GlobalSystemMediaTransportControlsSessionMediaProperties mediaProperties) => ScheduleRefresh();

    private void MediaManager_OnAnyPlaybackStateChanged(
        MediaSession mediaSession,
        GlobalSystemMediaTransportControlsSessionPlaybackInfo playbackInfo) => ScheduleRefresh();

    private void MediaManager_OnAnySessionOpened(MediaSession mediaSession) => ScheduleRefresh();

    private void MediaManager_OnAnySessionClosed(MediaSession mediaSession) => ScheduleRefresh();

    private void ScheduleRefresh()
    {
        if (_dispatcher.HasShutdownStarted)
        {
            return;
        }

        // MediaManager 事件来自后台线程；快照构建（缩略图解码、主色提取）保持与旧实现一致在 UI 线程执行。
        _dispatcher.BeginInvoke(RefreshSnapshot, DispatcherPriority.Normal);
    }

    private void RefreshSnapshot()
    {
        try
        {
            var snapshot = BuildSnapshot();
            if (snapshot is null)
            {
                return;
            }

            CurrentSnapshot = snapshot;
            SnapshotChanged?.Invoke(this, snapshot);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MediaSessionService] Failed to refresh snapshot: {ex}");
        }
    }

    /// <summary>构建快照；元数据拉取失败时返回 null（保持上一次已发布的快照）。</summary>
    private MediaSnapshot? BuildSnapshot()
    {
        var activeSession = GetActiveMediaSession();
        if (!_mediaManager.IsStarted || activeSession is null)
        {
            return MediaSnapshot.Disconnected;
        }

        var songInfo = TryGetMediaProperties(activeSession.ControlSession);
        if (songInfo is null)
        {
            return null;
        }

        var playbackInfo = activeSession.ControlSession.GetPlaybackInfo();
        var timelineProperties = activeSession.ControlSession.GetTimelineProperties();
        var artwork = BitmapHelper.GetThumbnail(songInfo.Thumbnail);
        BitmapHelper.GetDominantColors(1);

        return new MediaSnapshot(
            true,
            playbackInfo.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
            playbackInfo.Controls?.IsPlayPauseToggleEnabled ?? false,
            playbackInfo.Controls?.IsPreviousEnabled ?? false,
            playbackInfo.Controls?.IsNextEnabled ?? false,
            songInfo.Title ?? string.Empty,
            songInfo.Artist ?? string.Empty,
            activeSession.Id,
            activeSession.ControlSession.SourceAppUserModelId ?? string.Empty,
            artwork,
            Lyrics: null,
            timelineProperties.Position.TotalSeconds);
    }

    private MediaSession? GetActiveMediaSession()
    {
        var validSessions = _mediaManager.CurrentMediaSessions.Values.Where(IsSessionAllowed).ToList();
        if (validSessions.Count == 0)
        {
            return null;
        }

        var focused = _mediaManager.GetFocusedSession();
        if (focused != null && validSessions.Any(session => session.Id == focused.Id))
        {
            return focused;
        }

        return validSessions.FirstOrDefault();
    }

    private static bool IsSessionAllowed(MediaSession? session) => session != null;

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
