using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using WindowsMediaController;
using static WindowsMediaController.MediaManager;

namespace AFMediaBar.Classes.Services;

/// <summary>
/// 维护当前媒体来源选择、自动跟随和浏览器会话重建缓冲。
/// Maintains the selected source, auto-follow behavior, and the browser session recreation grace period.
/// </summary>
public sealed class MediaSessionSelectionService : IDisposable
{
    private static readonly TimeSpan AutoSwitchGracePeriod = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MissingSessionGracePeriod = TimeSpan.FromSeconds(3);
    private readonly MediaSessionCatalog _catalog;
    private readonly DispatcherTimer _timer;
    private string? _pendingAutoSwitchKey;
    private DateTime _pendingAutoSwitchSinceUtc;
    private DateTime _missingSessionSinceUtc;
    private bool _isDisposed;

    public string? SelectedKey { get; private set; }
    public string? SelectedSourceId { get; private set; }

    public event Action? RefreshRequested;

    public MediaSessionSelectionService(MediaSessionCatalog catalog)
    {
        _catalog = catalog;
        _timer = new DispatcherTimer(DispatcherPriority.Background, Application.Current.Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(200)
        };
        _timer.Tick += OnTimerTick;
    }

    public bool Select(string key, IReadOnlyList<MediaSession> sessions)
    {
        var selected = sessions.FirstOrDefault(session =>
            string.Equals(session.Id, key, StringComparison.Ordinal));
        if (selected is null)
        {
            return false;
        }

        ClearPendingAutoSwitch();
        ClearMissingSession();
        SelectedKey = selected.Id;
        SelectedSourceId = selected.ControlSession.SourceAppUserModelId ?? string.Empty;
        return true;
    }

    public MediaSession? Resolve(IReadOnlyList<MediaSession> sessions)
    {
        var selected = sessions.FirstOrDefault(session =>
            string.Equals(session.Id, SelectedKey, StringComparison.Ordinal));
        if (selected is not null)
        {
            if (_missingSessionSinceUtc != default)
            {
                Debug.WriteLine($"[MediaSessionSelection] Browser source recovered: {SelectedSourceId}");
            }

            SelectedSourceId = selected.ControlSession.SourceAppUserModelId ?? SelectedSourceId;
            ClearMissingSession();
            return selected;
        }

        var restored = FindRestoredSession(sessions);
        if (restored is not null)
        {
            SelectedKey = restored.Id;
            SelectedSourceId = restored.ControlSession.SourceAppUserModelId ?? string.Empty;
            Debug.WriteLine($"[MediaSessionSelection] Browser source recreated: {SelectedSourceId}");
            ClearMissingSession();
            return restored;
        }

        if (TryHoldMissingSession())
        {
            return null;
        }

        ClearPendingAutoSwitch();
        ClearMissingSession();
        var focused = _catalog.GetFocusedSession();
        selected = focused is not null
            ? sessions.FirstOrDefault(session =>
                string.Equals(session.Id, focused.Id, StringComparison.Ordinal))
            : null;
        selected ??= sessions.FirstOrDefault(IsPlaying) ?? sessions.FirstOrDefault();
        if (selected is null)
        {
            SelectedKey = null;
            SelectedSourceId = null;
            return null;
        }

        SelectedKey = selected.Id;
        SelectedSourceId = selected.ControlSession.SourceAppUserModelId ?? string.Empty;
        return selected;
    }

    public bool TryAutoSwitchToPlaying(IReadOnlyList<MediaSession> sessions)
    {
        var current = sessions.FirstOrDefault(session =>
            string.Equals(session.Id, SelectedKey, StringComparison.Ordinal));
        if (current is null)
        {
            if (IsMissingSessionGraceActive)
            {
                _timer.Start();
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

        var currentSourceId = current.ControlSession.SourceAppUserModelId ?? string.Empty;
        if (IsBrowserSource(currentSourceId))
        {
            ClearPendingAutoSwitch();
            return false;
        }

        var replacement = sessions.FirstOrDefault(candidate =>
            !ReferenceEquals(candidate, current) && IsPlaying(candidate));
        if (replacement is null)
        {
            ClearPendingAutoSwitch();
            return false;
        }

        if (!string.Equals(_pendingAutoSwitchKey, replacement.Id, StringComparison.Ordinal))
        {
            _pendingAutoSwitchKey = replacement.Id;
            _pendingAutoSwitchSinceUtc = DateTime.UtcNow;
            _timer.Start();
            return false;
        }

        if (DateTime.UtcNow - _pendingAutoSwitchSinceUtc < AutoSwitchGracePeriod)
        {
            _timer.Start();
            return false;
        }

        ClearPendingAutoSwitch();
        SelectedKey = replacement.Id;
        SelectedSourceId = replacement.ControlSession.SourceAppUserModelId ?? string.Empty;
        return true;
    }

    public bool IsMissingSessionGraceActive =>
        _missingSessionSinceUtc != default &&
        DateTime.UtcNow - _missingSessionSinceUtc < MissingSessionGracePeriod &&
        IsBrowserSource(SelectedSourceId ?? string.Empty);

    /// <summary>
    /// 在浏览器会话暂时消失时启动或维持恢复缓冲。
    /// Starts or maintains the recovery grace period while a browser session is temporarily missing.
    /// </summary>
    public bool TryHoldMissingSession()
    {
        if (!IsBrowserSource(SelectedSourceId ?? string.Empty))
        {
            return false;
        }

        if (_missingSessionSinceUtc == default)
        {
            _missingSessionSinceUtc = DateTime.UtcNow;
            Debug.WriteLine($"[MediaSessionSelection] Holding missing browser source: {SelectedSourceId}");
        }

        if (DateTime.UtcNow - _missingSessionSinceUtc >= MissingSessionGracePeriod)
        {
            Debug.WriteLine($"[MediaSessionSelection] Missing browser source grace expired: {SelectedSourceId}");
            return false;
        }

        _timer.Start();
        return true;
    }

    public void ClearSelection()
    {
        SelectedKey = null;
        SelectedSourceId = null;
        ClearPendingAutoSwitch();
        ClearMissingSession();
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _timer.Stop();
        _timer.Tick -= OnTimerTick;
    }

    private MediaSession? FindRestoredSession(IReadOnlyList<MediaSession> sessions) =>
        IsMissingSessionGraceActive
            ? sessions.FirstOrDefault(session => IsSameBrowserSource(
                session.ControlSession.SourceAppUserModelId ?? string.Empty,
                SelectedSourceId ?? string.Empty))
            : null;

    private void ClearPendingAutoSwitch()
    {
        _pendingAutoSwitchKey = null;
        _pendingAutoSwitchSinceUtc = default;
        if (!IsMissingSessionGraceActive)
        {
            _timer.Stop();
        }
    }

    private void ClearMissingSession()
    {
        _missingSessionSinceUtc = default;
        if (string.IsNullOrEmpty(_pendingAutoSwitchKey))
        {
            _timer.Stop();
        }
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        if (_isDisposed)
        {
            _timer.Stop();
            return;
        }

        RefreshRequested?.Invoke();
    }

    private static bool IsPlaying(MediaSession session)
    {
        try
        {
            return session.ControlSession.GetPlaybackInfo().PlaybackStatus ==
                Windows.Media.Control.GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsBrowserSource(string sourceId) =>
        sourceId.Contains("chrome", StringComparison.OrdinalIgnoreCase) ||
        sourceId.Contains("msedge", StringComparison.OrdinalIgnoreCase) ||
        sourceId.Contains("microsoftedge", StringComparison.OrdinalIgnoreCase) ||
        sourceId.Contains("firefox", StringComparison.OrdinalIgnoreCase);

    private static bool IsSameBrowserSource(string leftSourceId, string rightSourceId)
    {
        var leftFamily = GetBrowserFamily(leftSourceId);
        var rightFamily = GetBrowserFamily(rightSourceId);
        return leftFamily is not null && string.Equals(leftFamily, rightFamily, StringComparison.Ordinal);
    }

    private static string? GetBrowserFamily(string sourceId)
    {
        if (sourceId.Contains("chrome", StringComparison.OrdinalIgnoreCase))
        {
            return "chrome";
        }

        if (sourceId.Contains("msedge", StringComparison.OrdinalIgnoreCase) ||
            sourceId.Contains("microsoftedge", StringComparison.OrdinalIgnoreCase))
        {
            return "edge";
        }

        return sourceId.Contains("firefox", StringComparison.OrdinalIgnoreCase)
            ? "firefox"
            : null;
    }
}
