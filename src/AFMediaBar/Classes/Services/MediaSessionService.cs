using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services.Lyrics;
using Windows.Media.Control;
using WindowsMediaController;
using static WindowsMediaController.MediaManager;

namespace AFMediaBar.Classes.Services;

/// <summary>
/// 协调媒体会话目录、来源选择、快照构建和来源扩展，并向 ViewModel 发布统一状态。
/// Coordinates the session catalog, source selection, snapshot building, and source enrichers,
/// publishing a unified state to ViewModels.
/// </summary>
public sealed class MediaSessionService : IDisposable
{
    private readonly MediaSessionCatalog _catalog;
    private readonly MediaSessionSelectionService _selection;
    private readonly MediaSnapshotBuilder _snapshotBuilder;
    private readonly IReadOnlyList<IMediaSourceProvider> _sourceProviders;
    private readonly MediaSourceActivationService _sourceActivator;
    private readonly Dispatcher _dispatcher;
    private readonly object _publishGate = new();
    private IReadOnlyList<MediaSessionOption> _lastSessionOptions = Array.Empty<MediaSessionOption>();
    private MediaSnapshot _sessionSnapshot = MediaSnapshot.Disconnected;
    private readonly Dictionary<IMediaSourceProvider, MediaSnapshot?> _sourceSnapshots = new();
    private MediaSnapshot _lastSnapshot = MediaSnapshot.Disconnected;
    private bool _isDisposed;

    /// <summary>最新的媒体快照。 / Latest published media snapshot.</summary>
    public MediaSnapshot? CurrentSnapshot { get; private set; }

    /// <summary>在 UI 线程上触发。 / Raised on the UI thread.</summary>
    public event EventHandler<MediaSnapshot>? SnapshotChanged;

    /// <summary>会话列表变化时在 UI 线程触发。 / Raised on the UI thread when the session list changes.</summary>
    public event Action<IReadOnlyList<MediaSessionOption>>? SessionsChanged;

    public string SelectedSourceId => _lastSnapshot.SourceId;
    public string SelectedSourceName => _lastSnapshot.SourceName;
    public IReadOnlyList<MediaSessionOption> CurrentSessionOptions => _lastSessionOptions;

    public MediaSessionService(
        MediaSessionCatalog catalog,
        MediaSessionSelectionService selection,
        MediaSnapshotBuilder snapshotBuilder,
        IEnumerable<IMediaSourceProvider> sourceProviders,
        MediaSourceActivationService sourceActivator)
    {
        _catalog = catalog;
        _selection = selection;
        _snapshotBuilder = snapshotBuilder;
        _sourceProviders = sourceProviders.ToArray();
        _sourceActivator = sourceActivator;
        _dispatcher = Application.Current.Dispatcher;

        _catalog.AnyMediaPropertyChanged += OnAnyMediaPropertyChanged;
        _catalog.AnyPlaybackStateChanged += OnAnyPlaybackStateChanged;
        _catalog.AnySessionOpened += OnAnySessionOpened;
        _catalog.AnySessionClosed += OnAnySessionClosed;
        _catalog.FocusedSessionChanged += OnFocusedSessionChanged;
        _catalog.AnyTimelinePropertyChanged += OnAnyTimelinePropertyChanged;
        _selection.RefreshRequested += OnSelectionRefreshRequested;
        _snapshotBuilder.EnrichmentCompleted += OnSnapshotEnrichmentCompleted;
        foreach (var provider in _sourceProviders)
        {
            provider.SnapshotChanged += OnSourceSnapshotChanged;
            provider.Start();
        }

        ScheduleSessionsRefresh();
    }

    public void RefreshNow() => RefreshSnapshot();

    public Task ReconnectAsync()
    {
        _catalog.ForceUpdate();
        RefreshSessionList();
        RefreshSnapshot();
        return Task.CompletedTask;
    }

    public void SelectSession(string key)
    {
        if (string.IsNullOrEmpty(key) || !_catalog.TryGetSnapshot(out var sessions))
        {
            return;
        }

        if (!_selection.Select(key, sessions))
        {
            return;
        }

        PublishSessions(sessions);
        RefreshSnapshot(sessions);
    }

    public Task TogglePlayPauseAsync() => ExecuteOnSelectedAsync(async session =>
    {
        await session.ControlSession.TryTogglePlayPauseAsync();
    });

    public Task SkipPreviousAsync() => ExecuteOnSelectedAsync(async session =>
    {
        await session.ControlSession.TrySkipPreviousAsync();
    });

    public Task SkipNextAsync() => ExecuteOnSelectedAsync(async session =>
    {
        await session.ControlSession.TrySkipNextAsync();
    });

    public void ActivateSelectedSource() => _sourceActivator.Activate(SelectedSourceId);

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _catalog.AnyMediaPropertyChanged -= OnAnyMediaPropertyChanged;
        _catalog.AnyPlaybackStateChanged -= OnAnyPlaybackStateChanged;
        _catalog.AnySessionOpened -= OnAnySessionOpened;
        _catalog.AnySessionClosed -= OnAnySessionClosed;
        _catalog.FocusedSessionChanged -= OnFocusedSessionChanged;
        _catalog.AnyTimelinePropertyChanged -= OnAnyTimelinePropertyChanged;
        _selection.RefreshRequested -= OnSelectionRefreshRequested;
        _snapshotBuilder.EnrichmentCompleted -= OnSnapshotEnrichmentCompleted;
        foreach (var provider in _sourceProviders)
        {
            provider.SnapshotChanged -= OnSourceSnapshotChanged;
            provider.Dispose();
        }
        _selection.Dispose();
        _catalog.Dispose();
    }

    private async Task ExecuteOnSelectedAsync(
        Func<MediaSession, Task> action)
    {
        if (!_catalog.TryGetSnapshot(out var sessions))
        {
            return;
        }

        var selected = sessions.FirstOrDefault(session =>
            string.Equals(session.Id, _selection.SelectedKey, StringComparison.Ordinal));
        if (selected is null)
        {
            return;
        }

        try
        {
            await action(selected);
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            Debug.WriteLine($"[MediaSessionService] Media command failed: {ex}");
        }
    }

    private void OnAnyMediaPropertyChanged(
        MediaSession session,
        GlobalSystemMediaTransportControlsSessionMediaProperties properties) => ScheduleRefresh();

    private void OnAnyPlaybackStateChanged(
        MediaSession session,
        GlobalSystemMediaTransportControlsSessionPlaybackInfo playbackInfo) => ScheduleSessionsRefresh();

    private void OnAnySessionOpened(MediaSession session) => ScheduleSessionsRefresh();

    private void OnAnySessionClosed(MediaSession session) => ScheduleSessionsRefresh();

    private void OnFocusedSessionChanged(MediaSession session) => ScheduleSessionsRefresh();

    private void OnAnyTimelinePropertyChanged(
        MediaSession session,
        GlobalSystemMediaTransportControlsSessionTimelineProperties properties) => ScheduleRefresh();

    private void OnSelectionRefreshRequested() => ScheduleSessionsRefresh();

    private void OnSnapshotEnrichmentCompleted() => ScheduleRefresh();

    private void OnSourceSnapshotChanged(IMediaSourceProvider provider, MediaSnapshot? snapshot)
    {
        _sourceSnapshots[provider] = snapshot;
        PublishResolved(ResolveSnapshot(_sessionSnapshot));
    }

    private void ScheduleRefresh()
    {
        if (_dispatcher.HasShutdownStarted || _isDisposed)
        {
            return;
        }

        _dispatcher.BeginInvoke(RefreshSnapshot, DispatcherPriority.Normal);
    }

    private void ScheduleSessionsRefresh()
    {
        if (_dispatcher.HasShutdownStarted || _isDisposed)
        {
            return;
        }

        _dispatcher.BeginInvoke(RefreshSessionList, DispatcherPriority.Normal);
    }

    private void RefreshSessionList()
    {
        if (_isDisposed || !_catalog.TryGetSnapshot(out var sessions))
        {
            return;
        }

        if (sessions.Length == 0)
        {
            if (_selection.IsMissingSessionGraceActive)
            {
                return;
            }

            _selection.ClearSelection();
            PublishSessions(sessions);
            Publish(MediaSnapshot.Disconnected);
            return;
        }

        var selected = _selection.Resolve(sessions);
        if (selected is null && _selection.IsMissingSessionGraceActive)
        {
            return;
        }

        PublishSessions(sessions);
        RefreshSnapshot(sessions);
    }

    private void RefreshSnapshot()
    {
        if (_catalog.TryGetSnapshot(out var sessions))
        {
            RefreshSnapshot(sessions);
        }
    }

    private void RefreshSnapshot(IReadOnlyList<MediaSession> sessions)
    {
        if (_isDisposed)
        {
            return;
        }

        try
        {
            var selected = _selection.Resolve(sessions);
            if (selected is null && _selection.IsMissingSessionGraceActive)
            {
                return;
            }

            if (_selection.TryAutoSwitchToPlaying(sessions))
            {
                selected = sessions.FirstOrDefault(session =>
                    string.Equals(session.Id, _selection.SelectedKey, StringComparison.Ordinal));
            }

            var snapshot = _snapshotBuilder.Build(selected, _catalog.IsStarted);
            if (snapshot is null)
            {
                return;
            }

            Publish(snapshot);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MediaSessionService] Failed to refresh snapshot: {ex}");
        }
    }

    private void PublishSessions(IReadOnlyList<MediaSession> sessions)
    {
        var occurrences = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var options = sessions.Select(session =>
        {
            var sourceId = session.ControlSession.SourceAppUserModelId ?? string.Empty;
            occurrences.TryGetValue(sourceId, out var occurrence);
            occurrence++;
            occurrences[sourceId] = occurrence;
            var displayName = MediaSourceNameFormatter.GetDisplayName(sourceId, "未知来源");
            if (occurrence > 1)
            {
                displayName = $"{displayName} ({occurrence})";
            }

            return new MediaSessionOption(
                session.Id,
                sourceId,
                displayName,
                IsPlaying(session),
                string.Equals(session.Id, _selection.SelectedKey, StringComparison.Ordinal));
        }).ToArray();

        if (options.SequenceEqual(_lastSessionOptions))
        {
            return;
        }

        _lastSessionOptions = options;
        SessionsChanged?.Invoke(options);
    }

    private void Publish(MediaSnapshot snapshot)
    {
        _sessionSnapshot = snapshot;
        foreach (var provider in _sourceProviders)
        {
            provider.UpdateSessionSnapshot(snapshot);
        }
        PublishResolved(ResolveSnapshot(snapshot));
    }

    private MediaSnapshot ResolveSnapshot(MediaSnapshot snapshot)
    {
        MediaSnapshot? providerSnapshot;
        if (snapshot.IsConnected)
        {
            var provider = _sourceProviders.FirstOrDefault(candidate => candidate.CanHandle(snapshot.SourceId));
            providerSnapshot = provider is not null && _sourceSnapshots.TryGetValue(provider, out var matched)
                ? matched
                : null;
        }
        else
        {
            providerSnapshot = _sourceProviders
                .Select(provider => _sourceSnapshots.GetValueOrDefault(provider))
                .Where(candidate => candidate is not null)
                .OrderByDescending(candidate => candidate!.IsPlaying)
                .FirstOrDefault();
        }

        if (providerSnapshot is null)
        {
            return snapshot;
        }

        return providerSnapshot with
        {
            CanPlayPause = snapshot.CanPlayPause,
            CanSkipPrevious = snapshot.CanSkipPrevious,
            CanSkipNext = snapshot.CanSkipNext
        };
    }

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
}
