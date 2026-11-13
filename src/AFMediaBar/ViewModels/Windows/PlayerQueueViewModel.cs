// 播放队列面板的呈现状态与命令协调；只读取 companion 快照，不持有窗口或网络资源。
using System.Threading;
using System.Threading.Tasks;
using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services;
using AFMediaBar.Resources;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AFMediaBar.ViewModels.Windows;

/// <summary>播放队列面板的状态、列表投影和双击/重试命令。</summary>
public partial class PlayerQueueViewModel : ObservableObject, IDisposable
{
    private readonly IPlayerCompanion _companion;
    private readonly AppLogService? _log;
    private PlayerCompanionSnapshot _snapshot = PlayerCompanionSnapshot.Empty;
    private CancellationTokenSource? _lifetime;
    private int _active;
    private int _disposed;
    private long _lastQueueRevision = -1;
    private PlayerCompanionConnectionState _lastConnectionState = PlayerCompanionConnectionState.Disconnected;

    public PlayerQueueViewModel(IPlayerCompanion companion, AppLogService? log = null)
    {
        _companion = companion;
        _log = log;
    }

    /// <summary>窗口关闭时由窗口订阅；面板本身不直接关闭窗口。</summary>
    public event EventHandler? CloseRequested;

    [ObservableProperty] private IReadOnlyList<PlayerQueueItemViewModel> _items = Array.Empty<PlayerQueueItemViewModel>();

    /// <summary>当前歌曲对应的行，供窗口首次打开时滚动到可见位置。</summary>
    [ObservableProperty] private PlayerQueueItemViewModel? _currentItem;

    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private string _actionStatus = string.Empty;
    [ObservableProperty] private string _currentTitle = string.Empty;
    [ObservableProperty] private string _currentSubtitle = string.Empty;
    [ObservableProperty] private string _currentStatusText = string.Empty;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private bool _isStale;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _canJump;
    [ObservableProperty] private bool _canRetry;
    [ObservableProperty] private bool _hasCurrent;
    [ObservableProperty] private bool _hasError;
    [ObservableProperty] private bool _isShuffleOn;

    public string TitleText => Translations.Format("Panel.Queue.TitleFormat", _companion.DisplayName);
    public string CountText => Translations.Format("Panel.Queue.CountFormat", Items.Count);
    public bool IsEmpty => IsConnected && !IsLoading && Items.Count == 0;
    public bool HasActionStatus => !string.IsNullOrEmpty(ActionStatus);
    public string ShuffleText => IsShuffleOn ? Translations.Get("Panel.Queue.ShuffleOn") : string.Empty;

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
        SetActionStatus(string.Empty);
        HasError = false;
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
        SetActionStatus(string.Empty);
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
        SetActionStatus(Translations.Get("Panel.Queue.Jumping"));

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
                SetActionStatus(Translations.Get(PlayerCompanionErrorPresentation.ResourceKey(result.Error)));
                return;
            }

            var confirmed = await WaitForCurrentTrackAsync(item.TrackId, token);
            if (!IsActive(token))
                return;

            UpdateSnapshot(_companion.Current, result.Refreshed);
            SetActionStatus(Translations.Get(confirmed ? "Panel.Queue.JumpConfirmed" : "Panel.Queue.AcceptedPending"));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _log?.Warn("Nons", "queue jump failed", exception);
            if (IsActive(token))
                SetActionStatus(Translations.Get("Panel.Queue.Error.Internal"));
        }
        finally
        {
            IsBusy = false;
            if (IsActive(token))
                UpdateCommandState();
        }
    }

    [RelayCommand]
    private void Close() => CloseRequested?.Invoke(this, EventArgs.Empty);

    private async Task LoadAsync(CancellationToken token)
    {
        IsLoading = Items.Count == 0;
        HasError = false;
        SetActionStatus(string.Empty);

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

        // 旧 ActionStatus 是上一语言拼出的字符串；清掉后按新语言重算状态与当前曲目卡。
        SetActionStatus(string.Empty);
        UpdateSnapshot(_snapshot, forceQueue: false);
    }

    private void UpdateSnapshot(PlayerCompanionSnapshot snapshot, bool forceQueue = false)
    {
        _snapshot = snapshot;
        IsConnected = snapshot.IsConnected;
        IsStale = snapshot.IsStale;
        IsShuffleOn = snapshot.Player?.Shuffle == true;

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

        UpdateCurrentCard(snapshot);
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

    private void UpdateCurrentCard(PlayerCompanionSnapshot snapshot)
    {
        var track = snapshot.CurrentTrack ?? CurrentItem?.Track;
        HasCurrent = track is not null;
        CurrentTitle = track?.Title ?? string.Empty;
        CurrentSubtitle = track is null ? string.Empty : FormatSubtitle(track);
        CurrentStatusText = PlaybackStatusText(snapshot.Player?.Status);
    }

    private void UpdateStatus(PlayerCompanionSnapshot snapshot)
    {
        if (snapshot.ConnectionState == PlayerCompanionConnectionState.Connected)
        {
            HasError = false;
            CanRetry = false;
        }
        else if (snapshot.Error != PlayerCompanionError.None)
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
            PlayerCompanionConnectionState.Connected when snapshot.IsStale => Translations.Get("Panel.Queue.Status.Stale"),
            PlayerCompanionConnectionState.Connected => Translations.Format(
                "Panel.Queue.Status.ConnectedFormat",
                string.IsNullOrWhiteSpace(snapshot.PlayerVersion) ? _companion.DisplayName : snapshot.PlayerVersion,
                Items.Count),
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

    private async Task<bool> WaitForCurrentTrackAsync(string trackId, CancellationToken token)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            if (IsCurrentTrack(trackId))
                return true;
            await Task.Delay(100, token);
        }

        var refresh = await _companion.RefreshAsync(token);
        if (!refresh.Success)
            return false;
        UpdateSnapshot(_companion.Current, forceQueue: true);
        return IsCurrentTrack(trackId);
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

    private void SetActionStatus(string text)
    {
        ActionStatus = text;
        OnPropertyChanged(nameof(HasActionStatus));
    }

    private bool IsActive(CancellationToken token) =>
        Volatile.Read(ref _disposed) == 0 && Volatile.Read(ref _active) != 0 && !token.IsCancellationRequested;

    private static string FormatSubtitle(PlayerCompanionTrack track)
    {
        var artists = string.Join(" / ", track.Artists);
        if (string.IsNullOrWhiteSpace(track.Album))
            return artists;
        return string.IsNullOrWhiteSpace(artists) ? track.Album : $"{artists} · {track.Album}";
    }

    private static string PlaybackStatusText(PlayerCompanionPlaybackStatus? status) => status switch
    {
        PlayerCompanionPlaybackStatus.Playing => Translations.Get("Panel.Queue.Status.Playing"),
        PlayerCompanionPlaybackStatus.Paused => Translations.Get("Panel.Queue.Status.Paused"),
        PlayerCompanionPlaybackStatus.Stopped => Translations.Get("Panel.Queue.Status.Stopped"),
        PlayerCompanionPlaybackStatus.Loading => Translations.Get("Panel.Queue.Status.Loading"),
        PlayerCompanionPlaybackStatus.Buffering => Translations.Get("Panel.Queue.Status.Buffering"),
        PlayerCompanionPlaybackStatus.Error => Translations.Get("Panel.Queue.Status.Error"),
        _ => Translations.Get("Panel.Queue.Status.Unknown")
    };

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        Deactivate();
        _lifetime?.Dispose();
        _lifetime = null;
        CloseRequested = null;
    }
}
