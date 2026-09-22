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

    /// <summary>
    /// 底层媒体管理器。重建（<see cref="Restart"/>）会整只换掉它，而读路径在 UI 线程、重建在后台流程上，
    /// 因此字段是 volatile 的，且所有读路径都先取本地引用再访问——否则一次重建就能让并发的读踩到已被释放的旧实例。
    /// The underlying media manager. A rebuild (<see cref="Restart"/>) replaces it wholesale while reads happen on the UI thread and
    /// the rebuild runs on the background flow, so the field is volatile and every read path snapshots it into a local first —
    /// otherwise one rebuild could make a concurrent read touch the disposed instance.
    /// </summary>
    private volatile MediaManager _mediaManager = new();
    private bool _isDisposed;

    public bool IsStarted => _mediaManager.IsStarted;

    public event Action<MediaSession, GlobalSystemMediaTransportControlsSessionMediaProperties>? AnyMediaPropertyChanged;
    public event Action<MediaSession, GlobalSystemMediaTransportControlsSessionPlaybackInfo>? AnyPlaybackStateChanged;
    public event Action<MediaSession>? AnySessionOpened;
    public event Action<MediaSession>? AnySessionClosed;
    public event Action<MediaSession>? FocusedSessionChanged;
    public event Action<MediaSession, GlobalSystemMediaTransportControlsSessionTimelineProperties>? AnyTimelinePropertyChanged;

    /// <summary>
    /// 创建并启动唯一的 SMTC 会话目录，目录事件由本实例统一转发和释放。
    /// Creates and starts the sole SMTC session catalog whose events are forwarded and released by this instance.
    /// </summary>
    public MediaSessionCatalog()
    {
        AttachEvents();
        _mediaManager.Start();
    }

    /// <summary>订阅底层媒体管理器的目录事件；构造与重建共用同一份订阅清单。/ Attaches the catalog events of the underlying media manager; construction and a rebuild share this one list.</summary>
    private void AttachEvents()
    {
        _mediaManager.OnAnyMediaPropertyChanged += OnAnyMediaPropertyChanged;
        _mediaManager.OnAnyPlaybackStateChanged += OnAnyPlaybackStateChanged;
        _mediaManager.OnAnySessionOpened += OnAnySessionOpened;
        _mediaManager.OnAnySessionClosed += OnAnySessionClosed;
        _mediaManager.OnFocusedSessionChanged += OnFocusedSessionChanged;
        _mediaManager.OnAnyTimelinePropertyChanged += OnAnyTimelinePropertyChanged;
    }

    /// <summary>解除底层媒体管理器的目录事件，方向与 <see cref="AttachEvents"/> 严格对称。/ Detaches the catalog events in the exact reverse order of <see cref="AttachEvents"/>.</summary>
    private void DetachEvents()
    {
        _mediaManager.OnAnyMediaPropertyChanged -= OnAnyMediaPropertyChanged;
        _mediaManager.OnAnyPlaybackStateChanged -= OnAnyPlaybackStateChanged;
        _mediaManager.OnAnySessionOpened -= OnAnySessionOpened;
        _mediaManager.OnAnySessionClosed -= OnAnySessionClosed;
        _mediaManager.OnFocusedSessionChanged -= OnFocusedSessionChanged;
        _mediaManager.OnAnyTimelinePropertyChanged -= OnAnyTimelinePropertyChanged;
    }

    /// <summary>
    /// 重建底层媒体管理器。
    ///
    /// 第三方库存在 ForceUpdate 也救不回来的失效状态：系统仍发布着会话，但库的字典里残留着失效条目，
    /// 导致新会话既不加进来、旧条目也读不出内容。这时只能整只换掉管理器，让字典与订阅全部从零开始。
    /// Rebuilds the underlying media manager.
    ///
    /// The third-party library has a broken state that even ForceUpdate cannot fix: the OS still publishes sessions while the
    /// library's dictionary keeps a dead entry, so a new session is never added and the stale one reads nothing. The only cure is a
    /// fresh manager whose dictionary and subscriptions start from zero.
    ///
    /// 这是一条低频、有界的兜底路径：只有"看门狗确认系统真的发布了会话、却怎么都读不到"时才被调用。
    /// This is a low-frequency, bounded fallback: it runs only when the watchdog confirms the OS publishes sessions while the catalog
    /// stays unreadable.
    /// </summary>
    public void Restart()
    {
        if (_isDisposed)
        {
            return;
        }

        DetachEvents();
        _mediaManager.Dispose();
        _mediaManager = new MediaManager();
        AttachEvents();
        _mediaManager.Start();
    }

    /// <summary>
    /// 操作系统当前发布的会话数；目录不可用或读取失败时返回 -1。
    /// 看门狗用它区分"真的没有媒体"（0，不动作）与"库坏了但系统有会话"（&gt;0，触发重建）。
    /// Number of sessions the OS currently publishes, or -1 when the catalog is unavailable or the read fails.
    /// The watchdog uses it to tell "no media at all" (0, do nothing) from "the library is broken while the OS has sessions" (&gt;0,
    /// rebuild).
    /// </summary>
    public int GetOsSessionCount()
    {
        if (_isDisposed)
        {
            return -1;
        }

        var manager = _mediaManager;
        try
        {
            return manager.WindowsSessionManager?.GetSessions().Count ?? -1;
        }
        catch
        {
            return -1;
        }
    }

    /// <summary>
    /// 返回当前动态会话集合的稳定数组快照，并剔除已被第三方库关闭的会话；目录尚未就绪时返回 <see langword="false"/>。
    /// 字典由 WinRT 事件线程改写而本方法在 UI 线程读取，因此剔除只能缩小竞态窗口，消费者仍须通过
    /// <see cref="MediaSessionGuard"/> 判定可用性。
    /// Returns a stable array snapshot of the dynamic session collection with sessions already closed by the third-party
    /// library removed, or <see langword="false"/> while the catalog is unavailable. The dictionary is mutated on WinRT event
    /// threads while this method reads it on the UI thread, so filtering only narrows the race window and consumers must
    /// still check usability through <see cref="MediaSessionGuard"/>.
    /// </summary>
    public bool TryGetSnapshot(out MediaSession[] sessions)
    {
        // 先取本地引用：重建可能在枚举期间换掉字段，本地快照保证这次枚举始终对着同一个管理器。
        // Snapshot the field first: a rebuild may replace it mid-enumeration, and the local keeps this enumeration on one manager.
        var manager = _mediaManager;
        for (var attempt = 0; attempt < SnapshotRetryCount; attempt++)
        {
            try
            {
                sessions = manager.CurrentMediaSessions.Values
                    .Where(MediaSessionGuard.IsUsable)
                    .ToArray();
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

    /// <summary>
    /// 获取当前由 Windows 判定为焦点的媒体会话，底层目录异常时返回空值。
    /// Gets the media session currently focused by Windows, returning null if the underlying catalog is unavailable.
    /// </summary>
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

    /// <summary>
    /// 请求底层媒体管理器立即重新枚举会话。只允许从 AF 唯一的后台重同步流程调用：这样 AF 发起的写操作永远只有一个。
    /// Requests an immediate session enumeration from the underlying media manager. Only the single background reconcile flow may call
    /// this, which keeps AF-initiated writes to exactly one at a time.
    /// </summary>
    public void ForceUpdate() => _mediaManager.ForceUpdate();

    /// <summary>
    /// 幂等解除底层媒体管理器事件并释放会话目录。
    /// Idempotently detaches media-manager events and disposes the session catalog.
    /// </summary>
    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        DetachEvents();
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
