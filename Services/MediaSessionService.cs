using System.IO;
using System.Windows.Media.Imaging;
using TaskbarPlayer.Models;
using Windows.Media.Control;

namespace TaskbarPlayer.Services;

internal sealed class MediaSessionService : IDisposable
{
    private static readonly string[] CloudMusicTokens = ["cloudmusic", "netease", "163music"];

    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _session;
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

    internal async Task ReconnectAsync()
    {
        await SelectSessionAndRefreshAsync();
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
        await RefreshAsync();
    }

    private static bool IsCloudMusicSession(GlobalSystemMediaTransportControlsSession session)
    {
        var sourceId = session.SourceAppUserModelId ?? string.Empty;
        return CloudMusicTokens.Any(token => sourceId.Contains(token, StringComparison.OrdinalIgnoreCase));
    }

    private void SetSession(GlobalSystemMediaTransportControlsSession? session)
    {
        if (ReferenceEquals(_session, session))
        {
            return;
        }

        if (_session is not null)
        {
            _session.MediaPropertiesChanged -= OnSessionChanged;
            _session.PlaybackInfoChanged -= OnSessionChanged;
            _session.TimelinePropertiesChanged -= OnSessionChanged;
        }

        _session = session;

        if (_session is not null)
        {
            _session.MediaPropertiesChanged += OnSessionChanged;
            _session.PlaybackInfoChanged += OnSessionChanged;
            _session.TimelinePropertiesChanged += OnSessionChanged;
        }
    }

    private async void OnSessionChanged(GlobalSystemMediaTransportControlsSession sender, object args)
    {
        await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        var version = Interlocked.Increment(ref _refreshVersion);
        var session = _session;
        if (session is null)
        {
            SnapshotChanged?.Invoke(this, MediaSnapshot.Disconnected);
            return;
        }

        try
        {
            var mediaProperties = await session.TryGetMediaPropertiesAsync();
            var playbackInfo = session.GetPlaybackInfo();
            var controls = playbackInfo.Controls;
            var artwork = await LoadArtworkAsync(mediaProperties.Thumbnail);

            if (version != _refreshVersion || !ReferenceEquals(session, _session))
            {
                return;
            }

            var artist = !string.IsNullOrWhiteSpace(mediaProperties.Artist)
                ? mediaProperties.Artist
                : mediaProperties.AlbumArtist;

            SnapshotChanged?.Invoke(this, new MediaSnapshot(
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
                SnapshotChanged?.Invoke(this, MediaSnapshot.Disconnected);
            }
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
