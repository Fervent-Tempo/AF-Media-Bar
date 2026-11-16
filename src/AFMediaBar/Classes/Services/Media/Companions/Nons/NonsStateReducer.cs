// 纯状态合并：只把 Nons 的 player/queue/progress 事件归并为可发布快照，不做 I/O 或 UI 调度。
using AFMediaBar.Classes.Models;

namespace AFMediaBar.Classes.Services.Media.Companions.Nons;

internal sealed class NonsStateReducer
{
    public NonsPlayer? Player { get; private set; }
    public NonsQueue? Queue { get; private set; }
    public NonsProgress? Progress { get; private set; }

    public bool IsQueueStale => Player is { } player && Queue is { } queue && player.QueueRevision != queue.QueueRevision;

    public long QueueRevision => Queue?.QueueRevision ?? Player?.QueueRevision ?? 0;

    public void Reset(NonsPlayer player, NonsQueue queue, NonsProgress? progress = null)
    {
        Player = ApplyQueueToPlayer(player, queue);
        Queue = queue;
        Progress = progress;
    }

    public void ApplyPlayer(NonsPlayer player)
    {
        Player = ApplyQueueToPlayer(player, Queue);
    }

    public void ApplyState(NonsState state)
    {
        Player = Player is null
            ? ApplyQueueToPlayer(new NonsPlayer(
                state.Sequence, state.QueueRevision, state.Revision, state.Status, state.Index,
                Queue?.Items.Length ?? 0, null, 0, 0, state.Volume, state.Shuffle, state.RepeatMode,
                state.ActualQuality, state.PlaybackMode, state.Error), Queue)
            : ApplyQueueToPlayer(Player with
            {
                Sequence = state.Sequence,
                QueueRevision = state.QueueRevision,
                Revision = state.Revision,
                Status = state.Status,
                Index = state.Index,
                Volume = state.Volume,
                Shuffle = state.Shuffle,
                RepeatMode = state.RepeatMode,
                ActualQuality = state.ActualQuality,
                PlaybackMode = state.PlaybackMode,
                Error = state.Error
            }, Queue);
    }

    public void ApplyQueue(NonsQueue queue)
    {
        Queue = queue;
        if (Player is { } player)
            Player = ApplyQueueToPlayer(player, queue);
    }

    public void ApplyProgress(NonsProgress progress)
    {
        Progress = progress;
        if (Player is not { } player || progress.Revision < player.Revision)
            return;

        Player = player with
        {
            Revision = progress.Revision,
            Status = progress.Status,
            PositionMs = progress.PositionMs,
            DurationMs = progress.DurationMs
        };
    }

    public PlayerCompanionSnapshot ToSnapshot(
        PlayerCompanionConnectionState connectionState,
        PlayerCompanionCapabilities capabilities,
        int protocolVersion,
        string playerVersion,
        PlayerCompanionError error,
        string? errorDetail,
        bool stale,
        DateTimeOffset updatedAt)
    {
        var queueRevision = QueueRevision;
        var items = Queue is null
            ? Array.Empty<PlayerCompanionTrack>()
            : Queue.Items.Select(MapTrack).ToArray();
        var shuffleOrder = Queue?.ShuffleOrder?.ToArray() ?? Array.Empty<int>();
        var player = MapPlayer();
        var isStale = stale || connectionState is not PlayerCompanionConnectionState.Connected || IsQueueStale;
        return new PlayerCompanionSnapshot(
            connectionState,
            capabilities,
            protocolVersion,
            playerVersion,
            player,
            items,
            shuffleOrder,
            queueRevision,
            error,
            errorDetail,
            isStale,
            updatedAt);
    }

    private PlayerCompanionPlayer? MapPlayer()
    {
        if (Player is not { } player)
            return null;

        var progress = Progress;
        var currentTrackId = player.Current?.TrackId;
        if (currentTrackId is null && Queue is { } queue && player.Index is int index && index >= 0 && index < queue.Items.Length)
            currentTrackId = queue.Items[index].TrackId;

        return new PlayerCompanionPlayer(
            MapStatus(player.Status),
            player.Index,
            currentTrackId,
            player.Shuffle,
            MapRepeat(player.RepeatMode),
            Math.Max(0, progress?.PositionMs ?? player.PositionMs),
            Math.Max(0, progress?.DurationMs ?? player.DurationMs),
            player.Volume,
            !string.IsNullOrEmpty(player.Error) || string.Equals(player.Status, "error", StringComparison.OrdinalIgnoreCase));
    }

    private static NonsPlayer ApplyQueueToPlayer(NonsPlayer player, NonsQueue? queue)
    {
        if (queue is null)
            return player;

        var current = player.Index is int index && index >= 0 && index < queue.Items.Length
            ? queue.Items[index]
            : player.Current;
        return player with
        {
            Current = current,
            QueueLength = queue.Items.Length
        };
    }

    private static PlayerCompanionTrack MapTrack(NonsTrack track) => new(
        track.TrackId,
        track.Title,
        track.Artists ?? Array.Empty<string>(),
        track.Album,
        Math.Max(0, track.DurationMs),
        track.Source,
        track.Reference is { } reference
            ? new PlayerCompanionReference(reference.Source, reference.Kind, reference.Id)
            : new PlayerCompanionReference(track.Source, "track", null),
        track.CoverUrl);

    private static PlayerCompanionPlaybackStatus MapStatus(string? status) => status switch
    {
        "stopped" => PlayerCompanionPlaybackStatus.Stopped,
        "loading" => PlayerCompanionPlaybackStatus.Loading,
        "playing" => PlayerCompanionPlaybackStatus.Playing,
        "paused" => PlayerCompanionPlaybackStatus.Paused,
        "buffering" => PlayerCompanionPlaybackStatus.Buffering,
        "error" => PlayerCompanionPlaybackStatus.Error,
        _ => PlayerCompanionPlaybackStatus.Unknown
    };

    private static MediaRepeatMode MapRepeat(string? repeatMode) => repeatMode switch
    {
        "off" => MediaRepeatMode.Off,
        "all" => MediaRepeatMode.All,
        "one" => MediaRepeatMode.One,
        _ => MediaRepeatMode.Unavailable
    };
}
