// 拥有本次运行的媒体先后记录与浏览器恢复计时器；Dispose 停止计时和事件发布。
using AFMediaBar.Classes.Services.Media.Smtc;
using System.Windows;
using System.Windows.Threading;
using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Settings;

namespace AFMediaBar.Classes.Services;

/// <summary>协调手动选择、自动策略与浏览器会话恢复，不保存播放历史。</summary>
public sealed class MediaSessionSelectionService : IDisposable, IMemoryPrunable
{
    private static readonly TimeSpan _missingSessionGracePeriod = TimeSpan.FromSeconds(3);
    private readonly Func<string?> _getFocusedSessionKey;
    private readonly Func<string, string> _normalizeSourceId;
    private readonly Func<DateTime> _utcNow;
    private readonly DispatcherTimer _timer;
    private readonly Dictionary<string, Observation> _observations = new(StringComparer.Ordinal);
    private long _sequence;
    private bool _hasObservation;
    private bool _isDisposed;
    private DateTime? _missingSessionSinceUtc;
    private MediaSelectionSettings _settings = MediaSelectionSettings.Default;

    public string? SelectedKey { get; private set; }
    public string? SelectedSourceId { get; private set; }
    public bool IsManualSelection { get; private set; }
    public event Action? RefreshRequested;

    public MediaSessionSelectionService(MediaSessionCatalog catalog, MediaSourceRegistry sources)
        : this(() => catalog.GetFocusedSession()?.Id, Application.Current.Dispatcher, () => DateTime.UtcNow,
            sources.NormalizeSourceId)
    {
    }

    internal MediaSessionSelectionService(Func<string?> getFocusedSessionKey, Dispatcher dispatcher,
        Func<DateTime>? utcNow = null, Func<string, string>? normalizeSourceId = null)
    {
        _getFocusedSessionKey = getFocusedSessionKey;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
        _normalizeSourceId = normalizeSourceId ?? (source => source);
        _timer = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = TimeSpan.FromMilliseconds(200) };
        _timer.Tick += OnTimerTick;
    }

    public bool Select(string key, IReadOnlyList<MediaSourceCandidate> sessions)
    {
        if (_isDisposed) return false;
        var selected = sessions.FirstOrDefault(session => session.Key == key);
        if (selected is null) return false;
        ClearMissingSession();
        SetSelected(selected);
        IsManualSelection = true;
        return true;
    }

    /// <summary>一次处理候选变化并作出选择；调用方先过滤、合并候选，且必须在 UI 线程调用。</summary>
    public MediaSourceCandidate? Resolve(IReadOnlyList<MediaSourceCandidate> sessions, MediaSelectionSettings? settings = null)
    {
        if (_isDisposed) return null;
        settings = (settings ?? MediaSelectionSettings.Default).Normalize();
        settings = settings with { PreferredSourceId = settings.PreferredSourceId is { } id ? _normalizeSourceId(id) : null };
        if (_settings != settings)
        {
            IsManualSelection = false;
            ClearMissingSession();
            _settings = settings;
        }

        var (added, playback) = Observe(sessions);
        var hasActivity = added || settings.Mode != MediaSelectionMode.LatestSession && playback;
        if (hasActivity) IsManualSelection = false;
        var current = sessions.FirstOrDefault(session => session.Key == SelectedKey);
        if (current is not null)
        {
            ClearMissingSession();
            if (IsManualSelection || current.IsRecovering && !hasActivity) { SetSelected(current); return current; }
        }
        else if (!hasActivity && _observations.TryGetValue(SelectedKey ?? string.Empty, out var missing) &&
                 missing.MissingSince is { } since && IsBrowserSource(missing.SourceId))
        {
            _missingSessionSinceUtc = since;
            if (IsMissingSessionGraceActive) { _timer.Start(); return null; }
        }

        ClearMissingSession();
        IsManualSelection = false;
        var entries = sessions.Where(session => _observations.TryGetValue(session.Key, out var observation) && observation.Established)
            .Select(session =>
            {
                var observation = _observations[session.Key];
                return new MediaSelectionEntry(session, observation.SourceId, observation.Added, observation.Playback);
            }).ToArray();
        var selected = MediaAutoSwitchPolicy.Select(entries, settings, SelectedKey, _getFocusedSessionKey());
        SetSelected(selected);
        return selected;
    }

    private (bool Added, bool Playback) Observe(IReadOnlyList<MediaSourceCandidate> sessions)
    {
        var now = _utcNow();
        var keys = sessions.Select(session => session.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var (key, observation) in _observations.ToArray())
        {
            if (observation.MissingSince is { } missingSince && now - missingSince >= _missingSessionGracePeriod)
            {
                _observations.Remove(key);
                continue;
            }
            if (keys.Contains(key)) continue;
            observation.MissingSince ??= now;
            if (!IsBrowserSource(observation.SourceId) || now - observation.MissingSince.Value >= _missingSessionGracePeriod)
                _observations.Remove(key);
        }

        var sequence = _hasObservation ? ++_sequence : 0;
        var added = false;
        var playback = false;
        var fresh = sessions.Where(session => !_observations.ContainsKey(session.Key)).ToArray();
        foreach (var session in sessions)
        {
            var sourceId = _normalizeSourceId(session.SourceId);
            var restored = false;
            if (!_observations.TryGetValue(session.Key, out var observation))
            {
                var sameSource = _observations.Where(pair => string.Equals(pair.Value.SourceId, sourceId, StringComparison.OrdinalIgnoreCase)).ToArray();
                // 仅续接唯一来源，不能把同一浏览器的不同标签页合并。
                if (IsBrowserSource(sourceId) && sameSource.Length == 1 && sameSource[0].Value.MissingSince is not null &&
                    sessions.Count(item => string.Equals(_normalizeSourceId(item.SourceId), sourceId, StringComparison.OrdinalIgnoreCase)) == 1 &&
                    fresh.Count(item => string.Equals(_normalizeSourceId(item.SourceId), sourceId, StringComparison.OrdinalIgnoreCase)) == 1)
                {
                    var previous = sameSource[0];
                    observation = previous.Value;
                    _observations.Remove(previous.Key);
                    if (SelectedKey == previous.Key) SelectedKey = session.Key;
                    restored = true;
                }
                else observation = new Observation(sourceId);
                _observations[session.Key] = observation;
            }

            restored |= observation.MissingSince is not null;
            observation.MissingSince = null;
            if (session.IsRecovering) continue;
            if (!observation.Established)
            {
                observation.Established = true;
                observation.Added = sequence;
                observation.Playback = session.IsPlaying ? sequence : 0;
                added |= _hasObservation && session.Key != SelectedKey;
                playback |= _hasObservation && session.IsPlaying && session.Key != SelectedKey;
            }
            else if (!restored && !observation.WasPlaying && session.IsPlaying)
            {
                observation.Playback = sequence;
                playback |= session.Key != SelectedKey;
            }
            observation.WasPlaying = session.IsPlaying;
        }
        _hasObservation = true;
        return (added, playback);
    }

    public bool IsMissingSessionGraceActive => !_isDisposed && _missingSessionSinceUtc is { } since &&
        _utcNow() - since < _missingSessionGracePeriod;

    /// <summary>清除选择和手动覆盖，保留本次运行的先后记录。</summary>
    public void ClearSelection()
    {
        SetSelected(null);
        IsManualSelection = false;
        ClearMissingSession();
    }

    private void SetSelected(MediaSourceCandidate? source)
    {
        SelectedKey = source?.Key;
        SelectedSourceId = source?.SourceId;
    }

    private void ClearMissingSession()
    {
        _missingSessionSinceUtc = null;
        _timer.Stop();
    }

    public string PruneParticipantName => "session-selection";

    public void Prune(MemoryPruneLevel level)
    {
        if (!_isDisposed) _timer.Interval = TimeSpan.FromMilliseconds(level == MemoryPruneLevel.None ? 200 : 1000);
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        _timer.Stop();
        _timer.Tick -= OnTimerTick;
        _observations.Clear();
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        if (!_isDisposed) RefreshRequested?.Invoke();
    }

    private static bool IsBrowserSource(string sourceId) =>
        sourceId.Contains("chrome", StringComparison.OrdinalIgnoreCase) ||
        sourceId.Contains("msedge", StringComparison.OrdinalIgnoreCase) ||
        sourceId.Contains("microsoftedge", StringComparison.OrdinalIgnoreCase) ||
        sourceId.Contains("firefox", StringComparison.OrdinalIgnoreCase);

    private sealed class Observation(string sourceId)
    {
        public string SourceId { get; } = sourceId;
        public long Added { get; set; }
        public long Playback { get; set; }
        public bool WasPlaying { get; set; }
        public bool Established { get; set; }
        public DateTime? MissingSince { get; set; }
    }
}
