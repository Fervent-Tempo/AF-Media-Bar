using System.IO;
using System.Windows.Media.Imaging;
using TaskbarPlayer.Models;
using Windows.Media.Control;

namespace TaskbarPlayer.Services;

internal sealed class MediaSessionService : IDisposable
{
    private const int ArtworkDecodeWidth = 96;
    private static readonly string[] CloudMusicTokens = ["cloudmusic", "netease", "163music"];

    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _session;
    private MediaSnapshot _lastSnapshot = MediaSnapshot.Disconnected;
    private int _refreshVersion;
    private bool _disposed;

    internal event EventHandler<MediaSnapshot>? SnapshotChanged;

    internal async Task InitializeAsync()
    {
        if (_manager is null)
        {
            _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            _manager.CurrentSessionChanged += OnManagerSessionsChanged;
            _manager.SessionsChanged += OnManagerSessionsChanged;
        }

        await SelectSessionAndRefreshAsync();
    }

    internal Task ReconnectAsync()
    {
        return SelectSessionAndRefreshAsync();
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

    private async void OnManagerSessionsChanged(
        GlobalSystemMediaTransportControlsSessionManager sender,
        object args)
    {
        await SelectSessionAndRefreshAsync();
    }

    private async Task SelectSessionAndRefreshAsync()
    {
        if (_manager is null || _disposed)
        {
            return;
        }

        var sessions = _manager.GetSessions();
        var selected = sessions.FirstOrDefault(IsCloudMusicSession);
        if (selected is null)
        {
            var current = _manager.GetCurrentSession();
            if (current is not null && IsCloudMusicSession(current))
            {
                selected = current;
            }
        }

        SetSession(selected);
        await RefreshMediaPropertiesAsync();
    }

    private static bool IsCloudMusicSession(GlobalSystemMediaTransportControlsSession session)
    {
        var sourceId = session.SourceAppUserModelId ?? string.Empty;
        return CloudMusicTokens.Any(token =>
            sourceId.Contains(token, StringComparison.OrdinalIgnoreCase));
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
            _session.PlaybackInfoChanged -= OnPlaybackInfoChanged;
        }

        _session = session;
        _lastSnapshot = MediaSnapshot.Disconnected;

        if (_session is not null)
        {
            _session.MediaPropertiesChanged += OnMediaPropertiesChanged;
            _session.PlaybackInfoChanged += OnPlaybackInfoChanged;
        }
    }

    private async void OnMediaPropertiesChanged(
        GlobalSystemMediaTransportControlsSession sender,
        object args)
    {
        await RefreshMediaPropertiesAsync();
    }

    private void OnPlaybackInfoChanged(
        GlobalSystemMediaTransportControlsSession sender,
        object args)
    {
        RefreshPlaybackInfo();
    }

    private async Task RefreshMediaPropertiesAsync()
    {
        var version = Interlocked.Increment(ref _refreshVersion);
        var session = _session;
        if (session is null)
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
                playbackInfo.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
                controls.IsPlayPauseToggleEnabled || controls.IsPlayEnabled || controls.IsPauseEnabled,
                controls.IsPreviousEnabled,
                controls.IsNextEnabled,
                string.IsNullOrWhiteSpace(mediaProperties.Title) ? "网易云音乐" : mediaProperties.Title,
                string.IsNullOrWhiteSpace(artist) ? "未知歌手" : artist,
                session.SourceAppUserModelId,
                artwork));
        }
        catch
        {
            if (version == _refreshVersion)
            {
                SetSession(null);
                Publish(MediaSnapshot.Disconnected);
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
            SetSession(null);
            Publish(MediaSnapshot.Disconnected);
        }
    }

    private void Publish(MediaSnapshot snapshot)
    {
        _lastSnapshot = snapshot;
        SnapshotChanged?.Invoke(this, snapshot);
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
        if (_manager is not null)
        {
            _manager.CurrentSessionChanged -= OnManagerSessionsChanged;
            _manager.SessionsChanged -= OnManagerSessionsChanged;
        }
    }
}
