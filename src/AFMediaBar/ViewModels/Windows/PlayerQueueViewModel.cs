// 播放队列面板的呈现状态与命令协调；只读取 companion 快照，不持有窗口或音频资源。
using System.Threading;
using System.Threading.Tasks;
using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Services.Media.Companions.Nons;
using AFMediaBar.Resources;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AFMediaBar.ViewModels.Windows;

/// <summary>播放队列面板的状态、列表投影、单击播放和后端封面缩略图。</summary>
public partial class PlayerQueueViewModel : ObservableObject, IDisposable
{
    private readonly IPlayerCompanion _companion;
    private readonly AppLogService? _log;
    private readonly NonsCoverThumbnailService? _coverThumbnails;
    private PlayerCompanionSnapshot _snapshot = PlayerCompanionSnapshot.Empty;
    private CancellationTokenSource? _lifetime;
    private int _active;
    private int _disposed;
    private long _lastQueueRevision = -1;
    private PlayerCompanionConnectionState _lastConnectionState = PlayerCompanionConnectionState.Disconnected;

    public PlayerQueueViewModel(
        IPlayerCompanion companion,
        AppLogService? log = null,
        NonsCoverThumbnailService? coverThumbnails = null)
    {
        _companion = companion;
        _log = log;
        _coverThumbnails = coverThumbnails;
    }

    [ObservableProperty] private IReadOnlyList<PlayerQueueItemViewModel> _items = Array.Empty<PlayerQueueItemViewModel>();

    /// <summary>当前歌曲对应的行，供窗口首次打开时把列表中心对齐到当前歌曲。</summary>
    [ObservableProperty] private PlayerQueueItemViewModel? _currentItem;

    /// <summary>只在连接中、重连、队列失效或错误时显示；连接正常时为空。</summary>
    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private bool _isStale;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _canJump;
    [ObservableProperty] private bool _canRetry;
    [ObservableProperty] private bool _hasError;

    public string TitleText => Translations.Get("Panel.Queue.WindowTitle");
    public bool IsEmpty => IsConnected && !IsLoading && Items.Count == 0;
    public bool HasStatusText => !string.IsNullOrEmpty(StatusText);

    /// <summary>窗口显示后调用；只连接和读取，不修改真实队列。</summary>
    public async Task ActivateAsync()
    {
        if (Volatile.Read(ref _disposed) != 0 || Interlocked.Exchange(ref _active, 1) != 0)
            return;

        _lifetime?.Cancel();
        _lifetime?.Dispose();
        _lifetime = new CancellationTokenSource();
        var token = _lifetime.Token;
        _companion.Changed += OnCompanionChanged;
        Translations.LanguageChanged += OnLanguageChanged;
        HasError = false;
        StatusText = string.Empty;
        UpdateSnapshot(_companion.Current, forceQueue: true);

        try
        {
            await LoadAsync(token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _log?.Warn("Nons", "queue panel activation failed", exception);
            if (IsActive(token))
                ApplyFailure(PlayerCompanionError.Internal);
        }
    }

    /// <summary>窗口关闭时退订并取消在途等待；重复调用安全。</summary>
    public void Deactivate()
    {
        if (Interlocked.Exchange(ref _active, 0) == 0)
            return;

        _lifetime?.Cancel();
        _companion.Changed -= OnCompanionChanged;
        Translations.LanguageChanged -= OnLanguageChanged;
    }

    /// <summary>行进入可视树或复用容器时按需加载低清封面；失败保留占位图。</summary>
    public async Task LoadArtworkAsync(PlayerQueueItemViewModel item)
    {
        if (_coverThumbnails is null || item.Artwork is not null || Volatile.Read(ref _disposed) != 0)
            return;

        var token = _lifetime?.Token ?? CancellationToken.None;
        if (!IsActive(token))
            return;

        try
        {
            var image = await _coverThumbnails.LoadAsync(item.Track, token);
            if (image is not null && IsActive(token))
                item.Artwork = image;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _log?.Warn("Nons", "queue cover thumbnail failed", exception);
        }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        var token = _lifetime?.Token ?? CancellationToken.None;
        if (!IsActive(token) || IsBusy)
            return;

        try
        {
            await LoadAsync(token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _log?.Warn("Nons", "queue panel refresh failed", exception);
            if (IsActive(token))
                ApplyFailure(PlayerCompanionError.Internal);
        }
    }

    [RelayCommand]
    private async Task RetryAsync()
    {
        var token = _lifetime?.Token ?? CancellationToken.None;
        if (!IsActive(token) || IsBusy)
            return;

        HasError = false;
        StatusText = string.Empty;
        try
        {
            await LoadAsync(token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _log?.Warn("Nons", "queue panel retry failed", exception);
            if (IsActive(token))
                ApplyFailure(PlayerCompanionError.Internal);
        }
    }

    [RelayCommand(CanExecute = nameof(CanJumpItem))]
    private async Task JumpAsync(PlayerQueueItemViewModel? item)
    {
        var token = _lifetime?.Token ?? CancellationToken.None;
        if (item is null || !IsActive(token) || !CanJump)
            return;

        IsBusy = true;
        UpdateCommandState();
        try
        {
            var expectedRevision = _snapshot.QueueRevision;
            var result = await _companion.JumpAsync(item.TrackId, expectedRevision, token);
            if (!IsActive(token))
                return;

            if (result.Refreshed)
                UpdateSnapshot(_companion.Current, forceQueue: true);
            if (!result.Accepted)
            {
                ShowCommandError(result.Error);
                return;
            }

            // 已接收还不是播放成功；等待实际当前曲目变化，超时后由服务刷新并更新列表。
            await WaitForCurrentTrackAsync(item.TrackId, token);
            if (IsActive(token))
                UpdateSnapshot(_companion.Current, result.Refreshed);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _log?.Warn("Nons", "queue jump failed", exception);
            if (IsActive(token))
                ShowCommandError(PlayerCompanionError.Internal);
        }
        finally
        {
            IsBusy = false;
            if (IsActive(token))
                UpdateCommandState();
        }
    }

    private async Task LoadAsync(CancellationToken token)
    {
        IsLoading = Items.Count == 0;
        HasError = false;
        StatusText = string.Empty;

        var connected = await _companion.EnsureConnectedAsync(token);
        if (!IsActive(token))
            return;
        if (!connected.Success)
        {
            ApplyFailure(connected.Error);
            return;
        }

        var refreshed = await _companion.RefreshAsync(token);
        if (!IsActive(token))
            return;
        if (!refreshed.Success)
        {
            ApplyFailure(refreshed.Error);
            return;
        }

        IsLoading = false;
        UpdateSnapshot(_companion.Current, forceQueue: true);
    }

    private void OnCompanionChanged(object? sender, PlayerCompanionSnapshot snapshot)
    {
        if (!IsActive(_lifetime?.Token ?? CancellationToken.None))
            return;

        if (snapshot.IsConnected)
            IsLoading = false;
        UpdateSnapshot(snapshot);
    }

    private void OnLanguageChanged(object? sender, EventArgs args)
    {
        if (!IsActive(_lifetime?.Token ?? CancellationToken.None))
            return;

        UpdateSnapshot(_snapshot, forceQueue: false);
    }

    private void UpdateSnapshot(PlayerCompanionSnapshot snapshot, bool forceQueue = false)
    {
        _snapshot = snapshot;
        IsConnected = snapshot.IsConnected;
        IsStale = snapshot.IsStale;

        var connectionChanged = snapshot.ConnectionState != _lastConnectionState;
        _lastConnectionState = snapshot.ConnectionState;
        var rebuild = forceQueue || connectionChanged ||
                      snapshot.QueueRevision != _lastQueueRevision ||
                      Items.Count != snapshot.Queue.Count;
        if (rebuild)
        {
            RebuildItems(snapshot);
            _lastQueueRevision = snapshot.QueueRevision;
        }
        else
        {
            ApplyCurrent(snapshot);
        }

        UpdateStatus(snapshot);
        UpdateCommandState();
        OnPropertyChanged(string.Empty);
    }

    private void RebuildItems(PlayerCompanionSnapshot snapshot)
    {
        var currentIndex = snapshot.Player?.Index;
        var items = new PlayerQueueItemViewModel[snapshot.Queue.Count];
        for (var index = 0; index < snapshot.Queue.Count; index++)
            items[index] = new PlayerQueueItemViewModel(index + 1, snapshot.Queue[index], index == currentIndex);

        Items = items;
        CurrentItem = currentIndex is int index2 && index2 >= 0 && index2 < items.Length ? items[index2] : null;
    }

    private void ApplyCurrent(PlayerCompanionSnapshot snapshot)
    {
        var index = snapshot.Player?.Index;
        var target = index is int value && value >= 0 && value < Items.Count ? Items[value] : null;
        if (CurrentItem is { } previous && !ReferenceEquals(previous, target))
            previous.IsCurrent = false;
        if (target is not null)
            target.IsCurrent = true;
        CurrentItem = target;
    }

    private void UpdateStatus(PlayerCompanionSnapshot snapshot)
    {
        if (snapshot.ConnectionState == PlayerCompanionConnectionState.Connected)
        {
            HasError = false;
            CanRetry = false;
            StatusText = snapshot.IsStale ? Translations.Get("Panel.Queue.Status.Stale") : string.Empty;
            return;
        }

        if (snapshot.Error != PlayerCompanionError.None)
        {
            HasError = true;
            CanRetry = true;
            StatusText = Translations.Get(PlayerCompanionErrorPresentation.ResourceKey(snapshot.Error));
            return;
        }

        StatusText = snapshot.ConnectionState switch
        {
            PlayerCompanionConnectionState.Connecting => Translations.Get("Panel.Queue.Status.Connecting"),
            PlayerCompanionConnectionState.Reconnecting => Translations.Get("Panel.Queue.Status.Reconnecting"),
            _ => Translations.Get("Panel.Queue.Status.Disconnected")
        };
        CanRetry = snapshot.ConnectionState is PlayerCompanionConnectionState.Unavailable or PlayerCompanionConnectionState.Disconnected;
    }

    private void UpdateCommandState()
    {
        CanJump = _snapshot.IsConnected && !_snapshot.IsStale && !IsBusy && _snapshot.Capabilities.Jump && Items.Count > 0;
        JumpCommand.NotifyCanExecuteChanged();
    }

    private bool CanJumpItem(PlayerQueueItemViewModel? item) => item is not null && CanJump;

    private async Task WaitForCurrentTrackAsync(string trackId, CancellationToken token)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            if (IsCurrentTrack(trackId))
                return;
            await Task.Delay(100, token);
        }

        var refresh = await _companion.RefreshAsync(token);
        if (refresh.Success)
            UpdateSnapshot(_companion.Current, forceQueue: true);
    }

    private bool IsCurrentTrack(string trackId) =>
        string.Equals(_companion.Current.Player?.CurrentTrackId, trackId, StringComparison.Ordinal);

    private void ApplyFailure(PlayerCompanionError error)
    {
        HasError = true;
        CanRetry = true;
        IsLoading = false;
        StatusText = Translations.Get(PlayerCompanionErrorPresentation.ResourceKey(error));
        OnPropertyChanged(string.Empty);
    }

    private void ShowCommandError(PlayerCompanionError error)
    {
        HasError = true;
        CanRetry = error is PlayerCompanionError.ServiceUnavailable or PlayerCompanionError.ConnectionLost or
            PlayerCompanionError.Timeout or PlayerCompanionError.Busy;
        IsLoading = false;
        StatusText = Translations.Get(PlayerCompanionErrorPresentation.ResourceKey(error));
        OnPropertyChanged(string.Empty);
    }

    private bool IsActive(CancellationToken token) =>
        Volatile.Read(ref _disposed) == 0 && Volatile.Read(ref _active) != 0 && !token.IsCancellationRequested;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        Deactivate();
        _lifetime?.Dispose();
        _lifetime = null;
    }
}
