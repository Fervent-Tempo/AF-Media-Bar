// Owns the SMTC manager, subscriptions and retirement leases; disposal retires active resources.
using System.Diagnostics;
using System.Threading;
using Windows.Media.Control;
using WindowsMediaController;
using static WindowsMediaController.MediaManager;

namespace AFMediaBar.Classes.Services.Media.Smtc;

/// <summary>
/// 管理 SMTC 媒体会话生命周期，并将第三方动态集合复制为稳定快照。
/// Owns the SMTC session lifecycle and copies the third-party dynamic collection into stable snapshots.
/// </summary>
public sealed class MediaSessionCatalog : IDisposable
{
    private const int SnapshotRetryCount = 4;

    /// <summary>
    /// 所有读取与后台更新都通过短时租约使用底层管理器；重建或退出只撤销新租约，旧实例等最后一个使用者退出后在后台释放。
    /// Reads and background updates lease the underlying manager; replacement or shutdown stops new leases and retires the old
    /// instance on a background thread after its final user exits.
    /// </summary>
    private readonly ReplaceableResource<ManagerRegistration> _managers;
    private int _isDisposed;

    public bool IsStarted
    {
        get
        {
            using var lease = _managers.TryAcquire();
            return lease?.Value.Manager.IsStarted ?? false;
        }
    }

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
        var manager = new ManagerRegistration(this, new MediaManager());
        _managers = new ReplaceableResource<ManagerRegistration>(manager, RetireManager);
        try
        {
            manager.Attach();
            manager.Manager.Start();
        }
        catch
        {
            _managers.Dispose();
            throw;
        }
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
        if (Volatile.Read(ref _isDisposed) != 0)
        {
            return;
        }

        var replacement = new ManagerRegistration(this, new MediaManager());
        try
        {
            replacement.Attach();
            replacement.Manager.Start();
        }
        catch
        {
            RetireManager(replacement);
            throw;
        }

        if (!_managers.TryReplace(replacement))
        {
            // Exit may have retired the catalog while the replacement was starting.
            RetireManager(replacement);
        }
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
        using var lease = _managers.TryAcquire();
        if (lease is null)
        {
            return -1;
        }

        try
        {
            return lease.Value.Manager.WindowsSessionManager?.GetSessions().Count ?? -1;
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
        using var lease = _managers.TryAcquire();
        if (lease is null)
        {
            sessions = Array.Empty<MediaSession>();
            return false;
        }

        var manager = lease.Value.Manager;
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

    /// <summary>保持当前会话所属的管理器存活到实际读取结束；不接管库内会话关闭和异步事件的生命周期。</summary>
    internal IDisposable? TryAcquireSession(MediaSession session)
    {
        var lease = _managers.TryAcquire();
        if (lease is null) return null;
        try
        {
            if (lease.Value.Manager.CurrentMediaSessions.TryGetValue(session.Id, out var current) &&
                ReferenceEquals(current, session) && MediaSessionGuard.IsUsable(session))
                return lease;
        }
        catch (InvalidOperationException)
        {
            // 第三方集合仍由其事件线程修改；本轮失败交由下一次刷新恢复。
        }
        lease.Dispose();
        return null;
    }

    internal bool IsCurrentSession(MediaSession session)
    {
        using var lease = TryAcquireSession(session);
        return lease is not null;
    }

    /// <summary>
    /// 获取当前由 Windows 判定为焦点的媒体会话，底层目录异常时返回空值。
    /// Gets the media session currently focused by Windows, returning null if the underlying catalog is unavailable.
    /// </summary>
    public MediaSession? GetFocusedSession()
    {
        using var lease = _managers.TryAcquire();
        if (lease is null)
        {
            return null;
        }

        try
        {
            return lease.Value.Manager.GetFocusedSession();
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
    public void ForceUpdate()
    {
        using var lease = _managers.TryAcquire();
        lease?.Value.Manager.ForceUpdate();
    }

    /// <summary>
    /// 幂等解除底层媒体管理器事件并释放会话目录。
    /// Idempotently detaches media-manager events and disposes the session catalog.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) != 0)
        {
            return;
        }

        _managers.Dispose();
    }

    private void RetireManager(ManagerRegistration registration)
    {
        // MediaManager.Dispose can block on third-party work; never run it under the ownership gate or on the UI read path.
        _ = Task.Run(() =>
        {
            try
            {
                registration.Detach();
            }
            catch (Exception ex)
            {
                LogRetirementFailure("媒体目录退订失败", ex);
            }

            try
            {
                registration.Manager.Dispose();
            }
            catch (Exception ex)
            {
                LogRetirementFailure("媒体目录释放失败", ex);
            }
        });
    }

    private static void LogRetirementFailure(string message, Exception ex)
    {
        try
        {
            if (AppLogService.Current is { IsDisposed: false } log) log.Error("Media", message, ex);
            else Debug.WriteLine($"[MediaSessionCatalog] {message}: {ex}");
        }
        catch
        {
            // Logging must not fault the detached cleanup task during application shutdown.
        }
    }

    private void OnAnyMediaPropertyChanged(
        MediaSession session,
        GlobalSystemMediaTransportControlsSessionMediaProperties properties)
    {
        if (Volatile.Read(ref _isDisposed) == 0)
        {
            AnyMediaPropertyChanged?.Invoke(session, properties);
        }
    }

    private void OnAnyPlaybackStateChanged(
        MediaSession session,
        GlobalSystemMediaTransportControlsSessionPlaybackInfo playbackInfo)
    {
        if (Volatile.Read(ref _isDisposed) == 0)
        {
            AnyPlaybackStateChanged?.Invoke(session, playbackInfo);
        }
    }

    private void OnAnySessionOpened(MediaSession session)
    {
        if (Volatile.Read(ref _isDisposed) == 0)
        {
            AnySessionOpened?.Invoke(session);
        }
    }

    private void OnAnySessionClosed(MediaSession session)
    {
        if (Volatile.Read(ref _isDisposed) == 0)
        {
            AnySessionClosed?.Invoke(session);
        }
    }

    private void OnFocusedSessionChanged(MediaSession session)
    {
        if (Volatile.Read(ref _isDisposed) == 0)
        {
            FocusedSessionChanged?.Invoke(session);
        }
    }

    private void OnAnyTimelinePropertyChanged(
        MediaSession session,
        GlobalSystemMediaTransportControlsSessionTimelineProperties timelineProperties)
    {
        if (Volatile.Read(ref _isDisposed) == 0)
        {
            AnyTimelinePropertyChanged?.Invoke(session, timelineProperties);
        }
    }

    // 每一代独占订阅委托；旧管理器等待读租约或后台退订期间，不再转发其迟到事件。
    internal sealed class ManagerRegistration
    {
        internal MediaManager Manager { get; }
        private readonly SongChangeDelegate _media;
        private readonly PlaybackChangeDelegate _playback;
        private readonly SessionChangeDelegate _opened;
        private readonly SessionChangeDelegate _closed;
        private readonly SessionChangeDelegate _focused;
        private readonly TimelineChangeDelegate _timeline;

        internal ManagerRegistration(MediaSessionCatalog owner, MediaManager manager)
        {
            Manager = manager;
            bool IsCurrent() => Volatile.Read(ref owner._isDisposed) == 0 && owner._managers.IsCurrent(this);
            _media = (session, properties) => { if (IsCurrent()) owner.OnAnyMediaPropertyChanged(session, properties); };
            _playback = (session, info) => { if (IsCurrent()) owner.OnAnyPlaybackStateChanged(session, info); };
            _opened = session => { if (IsCurrent()) owner.OnAnySessionOpened(session); };
            _closed = session => { if (IsCurrent()) owner.OnAnySessionClosed(session); };
            _focused = session => { if (IsCurrent()) owner.OnFocusedSessionChanged(session); };
            _timeline = (session, properties) => { if (IsCurrent()) owner.OnAnyTimelinePropertyChanged(session, properties); };
        }

        internal void Attach()
        {
            Manager.OnAnyMediaPropertyChanged += _media;
            Manager.OnAnyPlaybackStateChanged += _playback;
            Manager.OnAnySessionOpened += _opened;
            Manager.OnAnySessionClosed += _closed;
            Manager.OnFocusedSessionChanged += _focused;
            Manager.OnAnyTimelinePropertyChanged += _timeline;
        }

        internal void Detach()
        {
            Manager.OnAnyMediaPropertyChanged -= _media;
            Manager.OnAnyPlaybackStateChanged -= _playback;
            Manager.OnAnySessionOpened -= _opened;
            Manager.OnAnySessionClosed -= _closed;
            Manager.OnFocusedSessionChanged -= _focused;
            Manager.OnAnyTimelinePropertyChanged -= _timeline;
        }
    }
}
