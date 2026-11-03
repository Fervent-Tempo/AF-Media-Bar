using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Settings;

namespace AFMediaBar.Classes.Services;

/// <summary>
/// 协调曲目切换判定、设置、全屏抑制和目标显示器解析，并向宿主发布通知请求。
/// Coordinates track-change detection, settings, fullscreen suppression, and target-display resolution,
/// then publishes requests to the window host.
/// </summary>
public sealed class TrackChangeNotificationCoordinator : IDisposable
{
    private readonly MediaSessionService _mediaSessionService;
    private readonly IDisplayMonitorService _displayMonitorService;
    private readonly TrackChangeNotificationPolicy _policy = new();
    private string? _presentedTrackIdentity;
    private MediaSnapshot? _presentedSnapshot;
    private bool _disposed;

    /// <summary>应显示新的曲目切换通知时触发。 / Raised when a track-change notification should be shown.</summary>
    public event EventHandler<TrackChangeNotificationRequest>? NotificationRequested;

    /// <summary>当前可见通知的媒体内容有补全时触发；不会重新显示或重置计时。 / Raised when media content for the current notification is enriched, without re-showing it or resetting its timer.</summary>
    public event EventHandler<MediaSnapshot>? NotificationContentUpdated;

    /// <summary>当前通知应立即隐藏时触发。 / Raised when the current notification should be hidden immediately.</summary>
    public event EventHandler? DismissRequested;

    /// <summary>创建协调器并订阅媒体和通知设置变化。 / Creates the coordinator and subscribes to media and notification-setting changes.</summary>
    public TrackChangeNotificationCoordinator(
        MediaSessionService mediaSessionService,
        IDisplayMonitorService displayMonitorService)
    {
        _mediaSessionService = mediaSessionService;
        _displayMonitorService = displayMonitorService;
        _mediaSessionService.SnapshotChanged += OnSnapshotChanged;
        SettingsManager.TrackChangeNotificationSettingsChanged += OnNotificationSettingsChanged;
    }

    /// <summary>显式请求一次示例通知，不改变自动通知开关或切歌判定基线。</summary>
    public bool RequestPreview(MediaSnapshot snapshot, TrackChangeNotificationSettings settings)
    {
        if (_disposed || NotificationRequested is not { } handler) return false;
        settings = settings.Normalize();
        var monitor = _displayMonitorService.ResolveNotificationMonitor(settings.TargetMode, settings.FixedMonitorDeviceId);
        if (monitor is null) return false;
        // 示例替换当前通知后，不再把旧曲目的迟到封面或文字补全应用到它。
        _presentedTrackIdentity = null;
        _presentedSnapshot = null;
        handler.Invoke(this, new TrackChangeNotificationRequest(snapshot, settings, monitor));
        return true;
    }

    private void OnSnapshotChanged(object? sender, MediaSnapshot snapshot)
    {
        if (_disposed)
            return;

        var identity = TrackChangeNotificationPolicy.CreateIdentity(snapshot);
        var shouldNotify = _policy.ShouldNotify(snapshot);
        if (!string.Equals(_presentedTrackIdentity, identity, StringComparison.Ordinal))
        {
            _presentedTrackIdentity = null;
            _presentedSnapshot = null;
        }

        if (!shouldNotify)
        {
            if (identity is not null &&
                string.Equals(_presentedTrackIdentity, identity, StringComparison.Ordinal) &&
                HasDisplayContentChanged(_presentedSnapshot, snapshot))
            {
                _presentedSnapshot = snapshot;
                NotificationContentUpdated?.Invoke(this, snapshot);
            }
            return;
        }

        var settings = SettingsManager.Current.TrackChangeNotification.Normalize();
        if (!settings.Enabled)
            return;

        if (!TrackChangeNotificationPresentationPolicy.ShouldPresent(
                settings,
                _displayMonitorService.IsForegroundWindowFullscreen()))
        {
            return;
        }

        _displayMonitorService.Refresh();
        var monitor = _displayMonitorService.ResolveNotificationMonitor(
            settings.TargetMode,
            settings.FixedMonitorDeviceId);
        if (monitor is null)
            return;

        _presentedTrackIdentity = identity;
        _presentedSnapshot = snapshot;
        NotificationRequested?.Invoke(this, new TrackChangeNotificationRequest(snapshot, settings, monitor));
    }

    private static bool HasDisplayContentChanged(MediaSnapshot? previous, MediaSnapshot current) =>
        previous is null ||
        !string.Equals(previous.Title, current.Title, StringComparison.Ordinal) ||
        !string.Equals(previous.Artist, current.Artist, StringComparison.Ordinal) ||
        !string.Equals(previous.SourceName, current.SourceName, StringComparison.Ordinal) ||
        !ReferenceEquals(previous.Artwork, current.Artwork);

    private void OnNotificationSettingsChanged(object? sender, EventArgs e)
    {
        if (!SettingsManager.Current.TrackChangeNotification.Enabled)
        {
            _presentedTrackIdentity = null;
            _presentedSnapshot = null;
            DismissRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>取消媒体与设置订阅。 / Unsubscribes from media and settings events.</summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _mediaSessionService.SnapshotChanged -= OnSnapshotChanged;
        SettingsManager.TrackChangeNotificationSettingsChanged -= OnNotificationSettingsChanged;
        NotificationRequested = null;
        NotificationContentUpdated = null;
        DismissRequested = null;
    }
}
