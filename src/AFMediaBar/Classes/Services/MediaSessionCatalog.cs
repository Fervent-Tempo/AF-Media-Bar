using System.Diagnostics;
using System.Threading;
using Windows.Media.Control;
using WindowsMediaController;
using static WindowsMediaController.MediaManager;

namespace AFMediaBar.Classes.Services;

/// <summary>
/// 管理 SMTC 媒体会话生命周期，并将第三方动态集合复制为稳定快照。
/// Owns the SMTC session lifecycle and copies the third-party dynamic collection into stable snapshots.
/// </summary>
public sealed class MediaSessionCatalog : IDisposable
{
    private const int SnapshotRetryCount = 4;
    private readonly MediaManager _mediaManager = new();
    private bool _isDisposed;

    public bool IsStarted => _mediaManager.IsStarted;

    public event Action<MediaSession, GlobalSystemMediaTransportControlsSessionMediaProperties>? AnyMediaPropertyChanged;
    public event Action<MediaSession, GlobalSystemMediaTransportControlsSessionPlaybackInfo>? AnyPlaybackStateChanged;
    public event Action<MediaSession>? AnySessionOpened;
    public event Action<MediaSession>? AnySessionClosed;
    public event Action<MediaSession>? FocusedSessionChanged;
    public event Action<MediaSession, GlobalSystemMediaTransportControlsSessionTimelineProperties>? AnyTimelinePropertyChanged;

    public MediaSessionCatalog()
    {
        _mediaManager.OnAnyMediaPropertyChanged += OnAnyMediaPropertyChanged;
        _mediaManager.OnAnyPlaybackStateChanged += OnAnyPlaybackStateChanged;
        _mediaManager.OnAnySessionOpened += OnAnySessionOpened;
        _mediaManager.OnAnySessionClosed += OnAnySessionClosed;
        _mediaManager.OnFocusedSessionChanged += OnFocusedSessionChanged;
        _mediaManager.OnAnyTimelinePropertyChanged += OnAnyTimelinePropertyChanged;
        _mediaManager.Start();
    }

    public bool TryGetSnapshot(out MediaSession[] sessions)
    {
        for (var attempt = 0; attempt < SnapshotRetryCount; attempt++)
        {
            try
            {
                sessions = _mediaManager.CurrentMediaSessions.Values.ToArray();
                return true;
            }
            catch (InvalidOperationException)
            {
                if (attempt + 1 < SnapshotRetryCount)
                {
                    Thread.Yield();
                }
            }
        }

        sessions = Array.Empty<MediaSession>();
        Debug.WriteLine("[MediaSessionCatalog] Failed to copy CurrentMediaSessions after retries.");
        return false;
    }

    public MediaSession? GetFocusedSession()
    {
        try
        {
            return _mediaManager.GetFocusedSession();
        }
        catch (InvalidOperationException ex)
        {
            Debug.WriteLine($"[MediaSessionCatalog] Failed to get focused session: {ex.Message}");
            return null;
        }
    }

    public void ForceUpdate() => _mediaManager.ForceUpdate();

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _mediaManager.OnAnyMediaPropertyChanged -= OnAnyMediaPropertyChanged;
        _mediaManager.OnAnyPlaybackStateChanged -= OnAnyPlaybackStateChanged;
        _mediaManager.OnAnySessionOpened -= OnAnySessionOpened;
        _mediaManager.OnAnySessionClosed -= OnAnySessionClosed;
        _mediaManager.OnFocusedSessionChanged -= OnFocusedSessionChanged;
        _mediaManager.OnAnyTimelinePropertyChanged -= OnAnyTimelinePropertyChanged;
        _mediaManager.Dispose();
    }

    private void OnAnyMediaPropertyChanged(
        MediaSession session,
        GlobalSystemMediaTransportControlsSessionMediaProperties properties) =>
        AnyMediaPropertyChanged?.Invoke(session, properties);

    private void OnAnyPlaybackStateChanged(
        MediaSession session,
        GlobalSystemMediaTransportControlsSessionPlaybackInfo playbackInfo) =>
        AnyPlaybackStateChanged?.Invoke(session, playbackInfo);

    private void OnAnySessionOpened(MediaSession session) => AnySessionOpened?.Invoke(session);

    private void OnAnySessionClosed(MediaSession session) => AnySessionClosed?.Invoke(session);

    private void OnFocusedSessionChanged(MediaSession session) => FocusedSessionChanged?.Invoke(session);

    private void OnAnyTimelinePropertyChanged(
        MediaSession session,
        GlobalSystemMediaTransportControlsSessionTimelineProperties timelineProperties) =>
        AnyTimelinePropertyChanged?.Invoke(session, timelineProperties);
}
