using System.IO;
using System.Windows.Media.Imaging;
using TaskbarPlayer.Models;
using Windows.Media.Control;

namespace TaskbarPlayer.Services;

internal sealed class MediaSessionService : IDisposable
{
    private const int ArtworkDecodeWidth = 96;

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

            _selectedKey = entry.Key;
            SetSession(entry.Session);
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

            var selected = _entries.FirstOrDefault(entry =>
                ReferenceEquals(entry.Session, previousSession));
            selected ??= _entries.FirstOrDefault(entry => entry.Key == _selectedKey);
            if (selected is null)
            {
                var current = _manager.GetCurrentSession();
                selected = _entries.FirstOrDefault(entry =>
                    ReferenceEquals(entry.Session, current));
            }

            selected ??= _entries.FirstOrDefault(entry => IsPlaying(entry.Session));
            selected ??= _entries.FirstOrDefault();

            _selectedKey = selected?.Key;
            SetSession(selected?.Session);
            PublishSessions();
            await RefreshMediaPropertiesAsync();
        }
        finally
        {
            _sessionGate.Release();
        }
    }

    private void SetSession(GlobalSystemMediaTransportControlsSession? session)
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
        _lastSnapshot = MediaSnapshot.Disconnected;

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
            var artwork = await LoadArtworkAsync(mediaProperties.Thumbnail);
            if (version != _refreshVersion || !ReferenceEquals(session, _session))
            {
                return;
            }

            var playbackInfo = session.GetPlaybackInfo();
            var controls = playbackInfo.Controls;
            var artist = !string.IsNullOrWhiteSpace(mediaProperties.Artist)
                ? mediaProperties.Artist
                : mediaProperties.AlbumArtist;

            Publish(new MediaSnapshot(
                true,
                playbackInfo.PlaybackStatus ==
                    GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
                controls.IsPlayPauseToggleEnabled || controls.IsPlayEnabled || controls.IsPauseEnabled,
                controls.IsPreviousEnabled,
                controls.IsNextEnabled,
                string.IsNullOrWhiteSpace(mediaProperties.Title)
                    ? entry.DisplayName
                    : mediaProperties.Title,
                string.IsNullOrWhiteSpace(artist) ? "未知创作者" : artist,
                entry.SourceId,
                entry.DisplayName,
                artwork));
        }
        catch
        {
            if (version == _refreshVersion)
            {
                Publish(MediaSnapshot.Disconnected with
                {
                    Title = $"{entry.DisplayName} 暂无媒体",
                    Artist = "等待应用发布媒体信息",
                    SourceId = entry.SourceId,
                    SourceName = entry.DisplayName
                });
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
