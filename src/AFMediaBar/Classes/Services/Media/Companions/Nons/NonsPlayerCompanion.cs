// Nons 开放 API 的 companion：拥有 HTTP/WS 连接、重连、队列版本和 UI 线程快照发布，不持有音频或窗口。
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services;

namespace AFMediaBar.Classes.Services.Media.Companions.Nons;

/// <summary>
/// 通过 Nons 本机开放 API 提供真实播放队列、跳转和随机状态；Nons 主界面/WebView 隐藏时仍独立工作。
/// Provides the real queue, jump, and shuffle through Nons' local open API, independent of its window/WebView.
/// </summary>
public sealed class NonsPlayerCompanion : IPlayerCompanion
{
    private const int MaxReconnectAttempts = 3;
    private const int MaxEventBytes = 16 * 1024 * 1024;
    private static readonly TimeSpan FirstReconnectDelay = TimeSpan.FromSeconds(2);
    private static readonly string[] MatchingSourceIds = ["Nons", "NonsPlayer"];

    private readonly NonsApiClient _api;
    private readonly AppLogService? _log;
    private readonly bool _ownsApi;
    private readonly Dispatcher? _dispatcher;
    private readonly object _gate = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _connectGate = new(1, 1);
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly NonsStateReducer _state = new();
    private PlayerCompanionSnapshot _current = PlayerCompanionSnapshot.Empty;
    private PlayerCompanionCapabilities _capabilities = PlayerCompanionCapabilities.None;
    private PlayerCompanionConnectionState _connectionState = PlayerCompanionConnectionState.Disconnected;
    private int _protocolVersion;
    private string _playerVersion = string.Empty;
    private Task<PlayerCompanionResult>? _connectTask;
    private Task? _consistencyRefreshTask;
    private ClientWebSocket? _socket;
    private int _connectionGeneration;
    private int _reconnectInFlight;
    private int _disposed;

    public NonsPlayerCompanion(AppLogService? log = null)
        : this(new NonsApiClient(), log, ownsApi: true)
    {
    }

    internal NonsPlayerCompanion(NonsApiClient api, AppLogService? log = null, bool ownsApi = false)
    {
        _api = api;
        _log = log;
        _ownsApi = ownsApi;
        _dispatcher = System.Windows.Application.Current?.Dispatcher;
    }

    public string DisplayName => "Nons";

    public PlayerCompanionCapabilities Capabilities => Current.Capabilities;

    public PlayerCompanionSnapshot Current => Volatile.Read(ref _current);

    public event EventHandler<PlayerCompanionSnapshot>? Changed;

    public bool MatchesSource(string? sourceId, string? sourceName) =>
        MatchingSourceIds.Any(id => string.Equals(id, sourceId, StringComparison.OrdinalIgnoreCase) ||
                                    string.Equals(id, sourceName, StringComparison.OrdinalIgnoreCase));

    public async Task<PlayerCompanionResult> EnsureConnectedAsync(CancellationToken cancellationToken = default)
    {
        if (Volatile.Read(ref _disposed) != 0)
            return PlayerCompanionResult.Fail(PlayerCompanionError.ServiceUnavailable, "companion disposed");

        if (Current.IsConnected)
            return PlayerCompanionResult.Ok;

        Task<PlayerCompanionResult> task;
        lock (_gate)
        {
            _connectTask ??= ConnectCoreAsync();
            task = _connectTask;
        }

        try
        {
            return await task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return PlayerCompanionResult.Fail(PlayerCompanionError.Timeout, "等待连接被取消。");
        }
        finally
        {
            lock (_gate)
            {
                if (ReferenceEquals(_connectTask, task) && task.IsCompleted)
                    _connectTask = null;
            }
        }
    }

    public async Task<PlayerCompanionResult> RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (!Current.IsConnected)
        {
            var connected = await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);
            if (!connected.Success)
                return connected;
        }

        return await RefreshCoreAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<PlayerCompanionCommandResult> JumpAsync(
        string trackId,
        long expectedQueueRevision,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(trackId))
            return PlayerCompanionCommandResult.Fail(PlayerCompanionError.InvalidRequest, Current.QueueRevision);

        var connected = await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);
        if (!connected.Success)
            return PlayerCompanionCommandResult.Fail(connected.Error, Current.QueueRevision, detail: connected.Detail);
        if (!Capabilities.Jump)
            return PlayerCompanionCommandResult.Fail(PlayerCompanionError.InvalidRequest, Current.QueueRevision);

        var snapshot = Current;
        var refreshed = false;
        if (snapshot.QueueRevision != expectedQueueRevision)
        {
            var refresh = await RefreshCoreAsync(cancellationToken).ConfigureAwait(false);
            refreshed = true;
            if (!refresh.Success)
                return PlayerCompanionCommandResult.Fail(refresh.Error, Current.QueueRevision, true, refresh.Detail);
            snapshot = Current;
        }

        if (!NonsCommandPolicy.ContainsTrack(snapshot.Queue, trackId))
            return PlayerCompanionCommandResult.Fail(PlayerCompanionError.TrackNotFound, snapshot.QueueRevision, refreshed);

        try
        {
            await _api.SendJumpAsync(snapshot.QueueRevision, trackId, cancellationToken).ConfigureAwait(false);
            return PlayerCompanionCommandResult.Accept(snapshot.QueueRevision, refreshed);
        }
        catch (NonsApiException exception) when (exception.Code == "stale_queue")
        {
            var refresh = await RefreshCoreAsync(cancellationToken).ConfigureAwait(false);
            refreshed = true;
            if (!refresh.Success)
                return PlayerCompanionCommandResult.Fail(refresh.Error, Current.QueueRevision, true, refresh.Detail);

            var latest = Current;
            if (!NonsCommandPolicy.ShouldRetryAfterStale(NonsCommandPolicy.ContainsTrack(latest.Queue, trackId), alreadyRetried: false))
                return PlayerCompanionCommandResult.Fail(PlayerCompanionError.TrackNotFound, latest.QueueRevision, true);

            try
            {
                await _api.SendJumpAsync(latest.QueueRevision, trackId, cancellationToken).ConfigureAwait(false);
                return PlayerCompanionCommandResult.Accept(latest.QueueRevision, true);
            }
            catch (Exception retryException)
            {
                return PlayerCompanionCommandResult.Fail(NonsErrorMapper.Map(retryException), Current.QueueRevision, true,
                    retryException.Message);
            }
        }
        catch (Exception exception)
        {
            return PlayerCompanionCommandResult.Fail(NonsErrorMapper.Map(exception), Current.QueueRevision, refreshed,
                exception.Message);
        }
    }

    public async Task<PlayerCompanionCommandResult> SetShuffleAsync(
        bool enabled,
        long expectedQueueRevision,
        CancellationToken cancellationToken = default)
    {
        var connected = await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);
        if (!connected.Success)
            return PlayerCompanionCommandResult.Fail(connected.Error, Current.QueueRevision, detail: connected.Detail);
        if (!Capabilities.Shuffle)
            return PlayerCompanionCommandResult.Fail(PlayerCompanionError.InvalidRequest, Current.QueueRevision);

        var snapshot = Current;
        var refreshed = false;
        if (snapshot.QueueRevision != expectedQueueRevision)
        {
            var refresh = await RefreshCoreAsync(cancellationToken).ConfigureAwait(false);
            refreshed = true;
            if (!refresh.Success)
                return PlayerCompanionCommandResult.Fail(refresh.Error, Current.QueueRevision, true, refresh.Detail);
            snapshot = Current;
        }

        try
        {
            await _api.SendShuffleAsync(snapshot.QueueRevision, enabled, cancellationToken).ConfigureAwait(false);
            return PlayerCompanionCommandResult.Accept(snapshot.QueueRevision, refreshed);
        }
        catch (NonsApiException exception) when (exception.Code == "stale_queue")
        {
            var refresh = await RefreshCoreAsync(cancellationToken).ConfigureAwait(false);
            refreshed = true;
            if (!refresh.Success)
                return PlayerCompanionCommandResult.Fail(refresh.Error, Current.QueueRevision, true, refresh.Detail);

            var latest = Current;
            try
            {
                await _api.SendShuffleAsync(latest.QueueRevision, enabled, cancellationToken).ConfigureAwait(false);
                return PlayerCompanionCommandResult.Accept(latest.QueueRevision, true);
            }
            catch (Exception retryException)
            {
                return PlayerCompanionCommandResult.Fail(NonsErrorMapper.Map(retryException), Current.QueueRevision, true,
                    retryException.Message);
            }
        }
        catch (Exception exception)
        {
            return PlayerCompanionCommandResult.Fail(NonsErrorMapper.Map(exception), Current.QueueRevision, refreshed,
                exception.Message);
        }
    }

    private async Task<PlayerCompanionResult> ConnectCoreAsync()
    {
        // EnsureConnectedAsync 在状态锁内调用本方法；先让出同步段，避免锁内执行连接或发布事件。
        await Task.Yield();

        try
        {
            await _connectGate.WaitAsync(_lifetime.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return PlayerCompanionResult.Fail(PlayerCompanionError.Timeout, "连接已取消。");
        }

        try
        {
            if (Current.IsConnected)
                return PlayerCompanionResult.Ok;

            var generation = Interlocked.Increment(ref _connectionGeneration);
            SetConnectionState(PlayerCompanionConnectionState.Connecting, PlayerCompanionError.None, null, stale: true);

            NonsHealth health;
            try
            {
                health = await _api.GetHealthAsync(_lifetime.Token).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                return FailConnect(exception);
            }

            var capabilities = ResolveCapabilities(health);
            lock (_gate)
            {
                _capabilities = capabilities;
                _protocolVersion = health.ProtocolVersion;
                _playerVersion = health.Version ?? string.Empty;
            }

            if (health.ProtocolVersion != 1 || !capabilities.Queue)
            {
                const string detail = "Nons 开放 API 协议或队列能力不兼容。";
                SetConnectionState(PlayerCompanionConnectionState.Unavailable,
                    PlayerCompanionError.IncompatibleProtocol, detail, stale: true);
                _log?.Warn("Nons", $"incompatible protocol={health.ProtocolVersion} capabilities={string.Join(',', health.Capabilities ?? [])}");
                return PlayerCompanionResult.Fail(PlayerCompanionError.IncompatibleProtocol, detail);
            }

            NonsPlayer player;
            NonsQueue queue;
            try
            {
                (player, queue) = await FetchSnapshotsAsync(_lifetime.Token).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                return FailConnect(exception);
            }

            lock (_gate)
            {
                _state.Reset(player, queue);
            }

            var socket = new ClientWebSocket();
            socket.Options.Proxy = null;
            socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(15);
            try
            {
                var eventUri = new UriBuilder(NonsApiClient.DefaultBaseAddress)
                {
                    Scheme = "ws",
                    Path = "v1/events"
                }.Uri;
                await socket.ConnectAsync(eventUri, _lifetime.Token).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                socket.Dispose();
                return FailConnect(exception);
            }

            ClientWebSocket? previous;
            lock (_gate)
            {
                previous = _socket;
                _socket = socket;
            }
            if (previous is not null)
            {
                try { previous.Abort(); } catch (Exception) { }
                previous.Dispose();
            }

            SetConnectionState(PlayerCompanionConnectionState.Connected, PlayerCompanionError.None, null, stale: false);
            long queueRevision;
            lock (_gate)
            {
                queueRevision = _state.QueueRevision;
            }
            _log?.Info("Nons", $"connected version={health.Version} protocol={health.ProtocolVersion} queueRevision={queueRevision}");
            _ = ReceiveLoopAsync(socket, generation);
            return PlayerCompanionResult.Ok;
        }
        finally
        {
            _connectGate.Release();
        }
    }

    private PlayerCompanionResult FailConnect(Exception exception)
    {
        var error = NonsErrorMapper.Map(exception);
        var detail = exception.Message;
        SetConnectionState(PlayerCompanionConnectionState.Unavailable, error, detail, stale: true);
        _log?.Warn("Nons", $"connect failed code={error}", exception);
        return PlayerCompanionResult.Fail(error, detail);
    }

    private async Task<(NonsPlayer Player, NonsQueue Queue)> FetchSnapshotsAsync(CancellationToken cancellationToken)
    {
        NonsPlayer? lastPlayer = null;
        NonsQueue? lastQueue = null;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var playerTask = _api.GetPlayerAsync(cancellationToken);
            var queueTask = _api.GetQueueAsync(cancellationToken);
            await Task.WhenAll(playerTask, queueTask).ConfigureAwait(false);
            lastPlayer = await playerTask.ConfigureAwait(false);
            lastQueue = await queueTask.ConfigureAwait(false);
            if (lastPlayer.QueueRevision == lastQueue.QueueRevision)
                return (lastPlayer, lastQueue);

            await Task.Delay(50, cancellationToken).ConfigureAwait(false);
        }

        return (lastPlayer!, lastQueue!);
    }

    private async Task<PlayerCompanionResult> RefreshCoreAsync(CancellationToken cancellationToken)
    {
        if (_connectionState != PlayerCompanionConnectionState.Connected)
            return PlayerCompanionResult.Fail(PlayerCompanionError.ConnectionLost, "尚未连接 Nons。");

        try
        {
            await _refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return PlayerCompanionResult.Fail(PlayerCompanionError.Timeout, "刷新已取消。");
        }

        try
        {
            NonsPlayer player;
            NonsQueue queue;
            try
            {
                (player, queue) = await FetchSnapshotsAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                return PlayerCompanionResult.Fail(NonsErrorMapper.Map(exception), exception.Message);
            }

            PlayerCompanionSnapshot snapshot;
            lock (_gate)
            {
                if (_connectionState != PlayerCompanionConnectionState.Connected)
                    return PlayerCompanionResult.Fail(PlayerCompanionError.ConnectionLost, "连接已断开。");
                _state.Reset(player, queue);
                snapshot = BuildSnapshot(PlayerCompanionError.None, null, stale: false);
                Volatile.Write(ref _current, snapshot);
            }
            RaiseChanged(snapshot);
            return PlayerCompanionResult.Ok;
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    private async Task ReceiveLoopAsync(ClientWebSocket socket, int generation)
    {
        try
        {
            var buffer = new byte[16 * 1024];
            using var message = new MemoryStream();
            while (!_lifetime.IsCancellationRequested && socket.State == WebSocketState.Open)
            {
                message.SetLength(0);
                WebSocketReceiveResult result;
                do
                {
                    result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), _lifetime.Token)
                        .ConfigureAwait(false);
                    if (result.MessageType == WebSocketMessageType.Close)
                        return;
                    if (result.Count > 0)
                        message.Write(buffer, 0, result.Count);
                    if (message.Length > MaxEventBytes)
                        throw new InvalidOperationException("Nons 事件超过大小上限。");
                } while (!result.EndOfMessage);

                if (result.MessageType == WebSocketMessageType.Text)
                    HandleEvent(Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length), generation);
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _log?.Warn("Nons", "event stream ended", exception);
        }
        finally
        {
            socket.Dispose();
            lock (_gate)
            {
                if (ReferenceEquals(_socket, socket))
                    _socket = null;
            }
            if (!_lifetime.IsCancellationRequested && generation == Volatile.Read(ref _connectionGeneration))
                StartReconnect();
        }
    }

    private void HandleEvent(string json, int generation)
    {
        if (Volatile.Read(ref _disposed) != 0 || generation != Volatile.Read(ref _connectionGeneration))
            return;

        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("type", out var typeElement))
            return;

        switch (typeElement.GetString())
        {
            case "player.queue":
                var queue = JsonSerializer.Deserialize<NonsQueue>(json, NonsApiClient.JsonOptions);
                if (queue is not null)
                    ApplyQueue(queue);
                break;
            case "player.state":
                var state = JsonSerializer.Deserialize<NonsState>(json, NonsApiClient.JsonOptions);
                if (state is not null)
                    ApplyState(state);
                break;
            case "player.progress":
                var progress = JsonSerializer.Deserialize<NonsProgress>(json, NonsApiClient.JsonOptions);
                if (progress is not null)
                {
                    lock (_gate)
                    {
                        _state.ApplyProgress(progress);
                    }
                }
                break;
            case "events.lagged":
                throw new InvalidOperationException("Nons 事件流失步。");
        }
    }

    private void ApplyQueue(NonsQueue queue)
    {
        PlayerCompanionSnapshot snapshot;
        bool stale;
        lock (_gate)
        {
            _state.ApplyQueue(queue);
            stale = _state.IsQueueStale;
            snapshot = BuildSnapshot(PlayerCompanionError.None, null, stale);
            Volatile.Write(ref _current, snapshot);
        }
        RaiseChanged(snapshot);
        if (stale)
            ScheduleConsistencyRefresh();
    }

    private void ApplyState(NonsState state)
    {
        PlayerCompanionSnapshot snapshot;
        bool stale;
        lock (_gate)
        {
            _state.ApplyState(state);
            stale = _state.IsQueueStale;
            snapshot = BuildSnapshot(PlayerCompanionError.None, null, stale);
            Volatile.Write(ref _current, snapshot);
        }
        RaiseChanged(snapshot);
        if (stale)
            ScheduleConsistencyRefresh();
    }

    private void ScheduleConsistencyRefresh()
    {
        if (Volatile.Read(ref _disposed) != 0 || _lifetime.IsCancellationRequested)
            return;

        lock (_gate)
        {
            if (_consistencyRefreshTask is { IsCompleted: false })
                return;
            _consistencyRefreshTask = RefreshObservedAsync();
        }
    }

    private async Task RefreshObservedAsync()
    {
        try
        {
            await Task.Yield();
            await RefreshCoreAsync(_lifetime.Token).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _log?.Warn("Nons", "consistency refresh failed", exception);
        }
    }

    private void StartReconnect()
    {
        if (Volatile.Read(ref _disposed) != 0 || _lifetime.IsCancellationRequested)
            return;
        if (Interlocked.CompareExchange(ref _reconnectInFlight, 1, 0) != 0)
            return;

        _ = ReconnectLoopAsync();
    }

    private async Task ReconnectLoopAsync()
    {
        try
        {
            SetConnectionState(PlayerCompanionConnectionState.Reconnecting, PlayerCompanionError.ConnectionLost, null, stale: true);
            for (var attempt = 0; attempt < MaxReconnectAttempts && !_lifetime.IsCancellationRequested; attempt++)
            {
                await Task.Delay(TimeSpan.FromSeconds(FirstReconnectDelay.TotalSeconds * (attempt + 1)), _lifetime.Token)
                    .ConfigureAwait(false);
                var result = await ConnectCoreAsync().ConfigureAwait(false);
                if (result.Success)
                    return;
            }

            SetConnectionState(PlayerCompanionConnectionState.Unavailable, PlayerCompanionError.ConnectionLost,
                "Nons 连接已断开。", stale: true);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        finally
        {
            Interlocked.Exchange(ref _reconnectInFlight, 0);
        }
    }

    private PlayerCompanionSnapshot BuildSnapshot(PlayerCompanionError error, string? detail, bool stale) =>
        _state.ToSnapshot(_connectionState, _capabilities, _protocolVersion, _playerVersion, error, detail, stale,
            DateTimeOffset.UtcNow);

    private void SetConnectionState(
        PlayerCompanionConnectionState state,
        PlayerCompanionError error,
        string? detail,
        bool stale)
    {
        PlayerCompanionSnapshot snapshot;
        lock (_gate)
        {
            _connectionState = state;
            snapshot = BuildSnapshot(error, detail, stale);
            Volatile.Write(ref _current, snapshot);
        }
        RaiseChanged(snapshot);
    }

    private void RaiseChanged(PlayerCompanionSnapshot snapshot)
    {
        if (Volatile.Read(ref _disposed) != 0)
            return;

        var dispatcher = _dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            Changed?.Invoke(this, snapshot);
            return;
        }

        try
        {
            dispatcher.BeginInvoke(new Action(() =>
            {
                if (Volatile.Read(ref _disposed) == 0)
                    Changed?.Invoke(this, snapshot);
            }));
        }
        catch (Exception exception) when (exception is InvalidOperationException or TaskCanceledException)
        {
            // Dispatcher 已关闭：退出期间不再发布。
        }
    }

    private static PlayerCompanionCapabilities ResolveCapabilities(NonsHealth health)
    {
        var capabilities = (health.Capabilities ?? []).ToHashSet(StringComparer.Ordinal);
        var canReadQueue = capabilities.Contains("player.read") &&
                           capabilities.Contains("queue.read") &&
                           capabilities.Contains("events");
        var canControl = capabilities.Contains("player.control");
        return new PlayerCompanionCapabilities(
            Queue: canReadQueue,
            Jump: canReadQueue && canControl,
            Shuffle: canReadQueue && canControl,
            Playlist: false,
            QueueEdit: false,
            Favorite: false,
            OpenPlaylist: false);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _lifetime.Cancel();
        ClientWebSocket? socket;
        lock (_gate)
        {
            socket = _socket;
            _socket = null;
        }
        try { socket?.Abort(); } catch (Exception) { }
        socket?.Dispose();
        if (_ownsApi)
            _api.Dispose();
        _log?.Info("Nons", "companion disposed");
    }
}
