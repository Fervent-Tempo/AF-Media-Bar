using System.IO;
using System.Windows.Media.Imaging;
using TaskbarPlayer.Models;
using Windows.Media.Control;

namespace TaskbarPlayer.Services;

internal sealed class MediaSessionService : IDisposable
{
    private const int ArtworkDecodeWidth = 96;
    private static readonly TimeSpan SessionReconnectGracePeriod = TimeSpan.FromSeconds(6);

    private static readonly (string Name, string[] Tokens)[] SourceNames =
    [
        ("网易云音乐", ["cloudmusic", "netease", "163music"]),
        ("QQ音乐", ["qqmusic"]),
        ("酷狗音乐", ["kugou", "kgmusic"]),
        ("Spotify", ["spotify"]),
        ("Google Chrome", ["chrome"]),
        ("Microsoft Edge", ["msedge", "microsoftedge"]),
        ("Firefox", ["firefox"]),
        ("VLC", ["vlc"]),
        ("PotPlayer", ["potplayer", "daum"]),
        ("Windows Media Player", ["zunemusic", "media.player", "wmplayer"]),
        ("mpv", ["mpv"]),
        ("foobar2000", ["foobar"])
    ];

    private readonly SemaphoreSlim _sessionGate = new(1, 1);
    private readonly List<SessionEntry> _entries = [];
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _session;
    private string? _selectedKey;
    private string? _preferredSourceId;
    private string? _preferredSourceName;
    private DateTime? _sessionMissingSinceUtc;
    private CancellationTokenSource? _sessionReconnectCancellation;
    private MediaSnapshot _lastSnapshot = MediaSnapshot.Disconnected;
    private int _refreshVersion;
    private bool _disposed;

    internal event EventHandler<MediaSnapshot>? SnapshotChanged;
    internal event Action<IReadOnlyList<MediaSessionOption>>? SessionsChanged;

    internal string SelectedSourceId => _lastSnapshot.SourceId;
    internal string SelectedSourceName => _lastSnapshot.SourceName;

    internal async Task InitializeAsync()
    {
        if (_manager is null)
        {
            _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            _manager.CurrentSessionChanged += OnManagerSessionsChanged;
            _manager.SessionsChanged += OnManagerSessionsChanged;
        }

        await RefreshSessionListAsync();
    }

    internal Task ReconnectAsync()
    {
        return RefreshSessionListAsync();
    }

    internal async Task SelectSessionAsync(string key)
    {
        await _sessionGate.WaitAsync();
        try
        {
            var entry = _entries.FirstOrDefault(candidate => candidate.Key == key);
            if (entry is null)
            {
                return;
            }

            SelectEntry(entry);
            PublishSessions();
            await RefreshMediaPropertiesAsync();
        }
        finally
        {
            _sessionGate.Release();
        }
    }

    internal Task SelectNextSessionAsync()
    {
        return SelectRelativeSessionAsync(1);
    }

    internal Task SelectPreviousSessionAsync()
    {
        return SelectRelativeSessionAsync(-1);
    }

    internal async Task TogglePlayPauseAsync()
    {
        if (_session is not null)
        {
            await _session.TryTogglePlayPauseAsync();
        }
    }

    internal async Task SkipPreviousAsync()
    {
        if (_session is not null)
        {
            await _session.TrySkipPreviousAsync();
        }
    }

    internal async Task SkipNextAsync()
    {
        if (_session is not null)
        {
            await _session.TrySkipNextAsync();
        }
    }

    internal static string GetDisplayName(string? sourceId)
    {
        var value = sourceId?.Trim() ?? string.Empty;
        foreach (var mapping in SourceNames)
        {
            if (mapping.Tokens.Any(token =>
                value.Contains(token, StringComparison.OrdinalIgnoreCase)))
            {
                return mapping.Name;
            }
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            return "未知媒体";
        }

        var bangIndex = value.LastIndexOf('!');
        if (bangIndex >= 0 && bangIndex < value.Length - 1)
        {
            value = value[(bangIndex + 1)..];
        }

        value = Path.GetFileNameWithoutExtension(value);
        var packageIndex = value.IndexOf('_');
        if (packageIndex > 0)
        {
            value = value[..packageIndex];
        }

        return string.IsNullOrWhiteSpace(value) ? "未知媒体" : value;
    }

    private async Task SelectRelativeSessionAsync(int direction)
    {
        string? key = null;
        await _sessionGate.WaitAsync();
        try
        {
            if (_entries.Count < 2)
            {
                return;
            }

            var currentIndex = Math.Max(
                0,
                _entries.FindIndex(entry => entry.Key == _selectedKey));
            var nextIndex = (currentIndex + direction + _entries.Count) % _entries.Count;
            key = _entries[nextIndex].Key;
        }
        finally
        {
            _sessionGate.Release();
        }

        if (key is not null)
        {
            await SelectSessionAsync(key);
        }
    }

    private async void OnManagerSessionsChanged(
        GlobalSystemMediaTransportControlsSessionManager sender,
        object args)
    {
        await RefreshSessionListAsync();
    }

    private async Task RefreshSessionListAsync()
    {
        if (_manager is null || _disposed)
        {
            return;
        }

        await _sessionGate.WaitAsync();
        try
        {
            foreach (var entry in _entries)
            {
                entry.Session.PlaybackInfoChanged -= OnAnyPlaybackInfoChanged;
            }

            var previousSession = _session;
            _entries.Clear();
            var occurrences = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var session in _manager.GetSessions())
            {
                var sourceId = session.SourceAppUserModelId ?? string.Empty;
                occurrences.TryGetValue(sourceId, out var occurrence);
                occurrence++;
                occurrences[sourceId] = occurrence;
                var key = $"{sourceId}\u001f{occurrence}";
                var displayName = GetDisplayName(sourceId);
                if (occurrence > 1)
                {
                    displayName = $"{displayName} ({occurrence})";
                }

                var entry = new SessionEntry(key, sourceId, displayName, session);
                _entries.Add(entry);
                session.PlaybackInfoChanged += OnAnyPlaybackInfoChanged;
            }

            // An explicitly closed last source is represented by an empty session
            // list. Clear the old artwork/title immediately instead of keeping a
            // stale snapshot behind the reconnect grace period.
            if (_entries.Count == 0)
            {
                CancelSessionReconnectGrace();
                _selectedKey = null;
                _preferredSourceId = null;
                _preferredSourceName = null;
                SetSession(null);
                PublishSessions();
                Publish(MediaSnapshot.Disconnected);
                return;
            }

            var selected = _entries.FirstOrDefault(entry =>
                ReferenceEquals(entry.Session, previousSession));
            selected ??= _entries.FirstOrDefault(entry => entry.Key == _selectedKey);
            selected ??= _entries.FirstOrDefault(entry =>
                !string.IsNullOrWhiteSpace(_preferredSourceId) &&
                string.Equals(
                    entry.SourceId,
                    _preferredSourceId,
                    StringComparison.OrdinalIgnoreCase));

            if (selected is null && ShouldHoldPreferredSource())
            {
                HoldPreferredSource();
                return;
            }

            CancelSessionReconnectGrace();
            if (selected is null)
            {
                var current = _manager.GetCurrentSession();
                selected = _entries.FirstOrDefault(entry =>
                    ReferenceEquals(entry.Session, current));
            }

            selected ??= _entries.FirstOrDefault(entry => IsPlaying(entry.Session));
            selected ??= _entries.FirstOrDefault();

            if (selected is not null)
            {
                SelectEntry(selected);
            }
            else
            {
                _selectedKey = null;
                _preferredSourceId = null;
                _preferredSourceName = null;
                SetSession(null);
            }

            PublishSessions();
            await RefreshMediaPropertiesAsync();
        }
        finally
        {
            _sessionGate.Release();
        }
    }

    private void SelectEntry(SessionEntry entry)
    {
        CancelSessionReconnectGrace();
        _selectedKey = entry.Key;
        _preferredSourceId = entry.SourceId;
        _preferredSourceName = entry.DisplayName;
        SetSession(entry.Session);
    }

    private bool ShouldHoldPreferredSource()
    {
        if (string.IsNullOrWhiteSpace(_preferredSourceId))
        {
            return false;
        }

        var now = DateTime.UtcNow;
        if (!_sessionMissingSinceUtc.HasValue)
        {
            _sessionMissingSinceUtc = now;
            _sessionReconnectCancellation = new CancellationTokenSource();
            _ = RefreshAfterReconnectGraceAsync(_sessionReconnectCancellation.Token);
            return true;
        }

        return now - _sessionMissingSinceUtc.Value < SessionReconnectGracePeriod;
    }

    private void HoldPreferredSource()
    {
        SetSession(null, resetSnapshot: false);
        PublishSessions();
        Publish(_lastSnapshot with
        {
            IsConnected = true,
            IsPlaying = false,
            CanPlayPause = false,
            CanSkipPrevious = false,
            CanSkipNext = false,
            Artist = "正在加载媒体…",
            SourceId = _preferredSourceId ?? _lastSnapshot.SourceId,
            SourceName = _preferredSourceName ?? _lastSnapshot.SourceName
        });
    }

    private async Task RefreshAfterReconnectGraceAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(SessionReconnectGracePeriod, cancellationToken);
            if (!_disposed)
            {
                await RefreshSessionListAsync();
            }
        }
        catch (OperationCanceledException)
        {
            // The preferred source returned or the user selected another source.
        }
    }

    private void CancelSessionReconnectGrace()
    {
        _sessionMissingSinceUtc = null;
        _sessionReconnectCancellation?.Cancel();
        _sessionReconnectCancellation?.Dispose();
        _sessionReconnectCancellation = null;
    }

    private void SetSession(
        GlobalSystemMediaTransportControlsSession? session,
        bool resetSnapshot = true)
    {
        if (ReferenceEquals(_session, session))
        {
            return;
        }

        if (_session is not null)
        {
            _session.MediaPropertiesChanged -= OnMediaPropertiesChanged;
        }

        _session = session;
        if (resetSnapshot)
        {
            _lastSnapshot = MediaSnapshot.Disconnected;
        }

        if (_session is not null)
        {
            _session.MediaPropertiesChanged += OnMediaPropertiesChanged;
        }
    }

    private async void OnMediaPropertiesChanged(
        GlobalSystemMediaTransportControlsSession sender,
        object args)
    {
        await RefreshMediaPropertiesAsync();
    }

    private async void OnAnyPlaybackInfoChanged(
        GlobalSystemMediaTransportControlsSession sender,
        object args)
    {
        if (_disposed)
        {
            return;
        }

        await _sessionGate.WaitAsync();
        try
        {
            PublishSessions();
            if (ReferenceEquals(sender, _session))
            {
                RefreshPlaybackInfo();
            }
        }
        finally
        {
            _sessionGate.Release();
        }
    }

    private async Task RefreshMediaPropertiesAsync()
    {
        var version = Interlocked.Increment(ref _refreshVersion);
        var session = _session;
        var entry = _entries.FirstOrDefault(candidate =>
            ReferenceEquals(candidate.Session, session));
        if (session is null || entry is null)
        {
            Publish(MediaSnapshot.Disconnected);
            return;
        }

        try
        {
            var mediaProperties = await session.TryGetMediaPropertiesAsync();
            var title = string.IsNullOrWhiteSpace(mediaProperties.Title)
                ? entry.DisplayName
                : mediaProperties.Title;
            var artist = !string.IsNullOrWhiteSpace(mediaProperties.Artist)
                ? mediaProperties.Artist
                : mediaProperties.AlbumArtist;
            artist = string.IsNullOrWhiteSpace(artist) ? "未知创作者" : artist;
            var canReuseArtwork = _lastSnapshot.Artwork is not null &&
                _lastSnapshot.IsConnected &&
                string.Equals(
                    _lastSnapshot.SourceId,
                    entry.SourceId,
                    StringComparison.OrdinalIgnoreCase) &&
                string.Equals(_lastSnapshot.Title, title, StringComparison.Ordinal) &&
                string.Equals(_lastSnapshot.Artist, artist, StringComparison.Ordinal);
            var artwork = canReuseArtwork
                ? _lastSnapshot.Artwork
                : await LoadArtworkAsync(mediaProperties.Thumbnail);
            if (version != _refreshVersion || !ReferenceEquals(session, _session))
            {
                return;
            }

            var playbackInfo = session.GetPlaybackInfo();
            var controls = playbackInfo.Controls;

            Publish(new MediaSnapshot(
                true,
                playbackInfo.PlaybackStatus ==
                    GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
                controls.IsPlayPauseToggleEnabled || controls.IsPlayEnabled || controls.IsPauseEnabled,
                controls.IsPreviousEnabled,
                controls.IsNextEnabled,
                title,
                artist,
                entry.SourceId,
                entry.DisplayName,
                artwork));
        }
        catch
        {
            if (version == _refreshVersion && ReferenceEquals(session, _session))
            {
                SetSession(null);
                _selectedKey = null;
                _preferredSourceId = null;
                _preferredSourceName = null;
                Publish(MediaSnapshot.Disconnected);
                _ = RefreshSessionListAsync();
            }
        }
    }

    private void RefreshPlaybackInfo()
    {
        var session = _session;
        if (session is null || !_lastSnapshot.IsConnected)
        {
            return;
        }

        try
        {
            var playbackInfo = session.GetPlaybackInfo();
            var controls = playbackInfo.Controls;
            Publish(_lastSnapshot with
            {
                IsPlaying = playbackInfo.PlaybackStatus ==
                    GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
                CanPlayPause = controls.IsPlayPauseToggleEnabled ||
                    controls.IsPlayEnabled ||
                    controls.IsPauseEnabled,
                CanSkipPrevious = controls.IsPreviousEnabled,
                CanSkipNext = controls.IsNextEnabled
            });
        }
        catch
        {
            _ = RefreshSessionListAsync();
        }
    }

    private void PublishSessions()
    {
        var options = _entries
            .Select(entry => new MediaSessionOption(
                entry.Key,
                entry.SourceId,
                entry.DisplayName,
                IsPlaying(entry.Session),
                entry.Key == _selectedKey))
            .ToArray();
        SessionsChanged?.Invoke(options);
    }

    private void Publish(MediaSnapshot snapshot)
    {
        _lastSnapshot = snapshot;
        SnapshotChanged?.Invoke(this, snapshot);
    }

    private static bool IsPlaying(GlobalSystemMediaTransportControlsSession session)
    {
        try
        {
            return session.GetPlaybackInfo().PlaybackStatus ==
                GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<BitmapImage?> LoadArtworkAsync(
        Windows.Storage.Streams.IRandomAccessStreamReference? thumbnail)
    {
        if (thumbnail is null)
        {
            return null;
        }

        using var randomAccessStream = await thumbnail.OpenReadAsync();
        using var sourceStream = randomAccessStream.AsStreamForRead();
        // WPF's decoder can receive a non-seekable WinRT stream on the first
        // media-properties callback. Buffer only this small 96px artwork so
        // initial loads and source switching use the same reliable path.
        using var memoryStream = new MemoryStream();
        await sourceStream.CopyToAsync(memoryStream);
        memoryStream.Position = 0;

        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.DecodePixelWidth = ArtworkDecodeWidth;
        bitmap.StreamSource = memoryStream;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        CancelSessionReconnectGrace();
        SetSession(null);
        foreach (var entry in _entries)
        {
            entry.Session.PlaybackInfoChanged -= OnAnyPlaybackInfoChanged;
        }

        _entries.Clear();
        if (_manager is not null)
        {
            _manager.CurrentSessionChanged -= OnManagerSessionsChanged;
            _manager.SessionsChanged -= OnManagerSessionsChanged;
        }
    }

    private sealed record SessionEntry(
        string Key,
        string SourceId,
        string DisplayName,
        GlobalSystemMediaTransportControlsSession Session);
}
