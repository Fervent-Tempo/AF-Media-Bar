// 播放器 companion 的跨模块契约：只描述外部播放器可提供的队列/播放能力，不持有网络或窗口资源。
namespace AFMediaBar.Classes.Models;

/// <summary>播放器 companion 的连接状态。/ Connection state of a player companion.</summary>
public enum PlayerCompanionConnectionState
{
    Disconnected = 0,
    Connecting = 1,
    Connected = 2,
    Reconnecting = 3,
    Unavailable = 4
}

/// <summary>companion 报告的播放状态。/ Playback status reported by a companion.</summary>
public enum PlayerCompanionPlaybackStatus
{
    Unknown = 0,
    Stopped = 1,
    Loading = 2,
    Playing = 3,
    Paused = 4,
    Buffering = 5,
    Error = 6
}

/// <summary>可稳定映射为界面文案的 companion 错误码；不直接展示外部播放器的原始错误文本。</summary>
public enum PlayerCompanionError
{
    None = 0,
    ServiceUnavailable = 1,
    IncompatibleProtocol = 2,
    Busy = 3,
    StaleQueue = 4,
    TrackNotFound = 5,
    InvalidRequest = 6,
    Timeout = 7,
    ConnectionLost = 8,
    Internal = 9
}

/// <summary>一个播放器 companion 支持的能力；未实现的能力保持 false，界面不得伪造入口。</summary>
public readonly record struct PlayerCompanionCapabilities(
    bool Queue,
    bool Jump,
    bool Shuffle,
    bool Playlist,
    bool QueueEdit,
    bool Favorite,
    bool OpenPlaylist)
{
    /// <summary>没有任何可用的 companion 能力。/ No companion capability is available.</summary>
    public static PlayerCompanionCapabilities None { get; } = new(false, false, false, false, false, false, false);
}

/// <summary>外部播放器返回的实体引用：来源、实体类型和来源内 ID。/ Entity reference returned by the external player.</summary>
public sealed record PlayerCompanionReference(string Source, string Kind, string? Id);

/// <summary>队列中一首可展示、可跳转的歌曲；trackId 是外部播放器提供的不透明稳定标识。</summary>
public sealed record PlayerCompanionTrack(
    string TrackId,
    string Title,
    IReadOnlyList<string> Artists,
    string Album,
    long DurationMs,
    string Source,
    PlayerCompanionReference Reference,
    string? CoverUrl);

/// <summary>companion 当前播放状态；不包含音频资源或上游凭据。</summary>
public sealed record PlayerCompanionPlayer(
    PlayerCompanionPlaybackStatus Status,
    int? Index,
    string? CurrentTrackId,
    bool Shuffle,
    MediaRepeatMode RepeatMode,
    long PositionMs,
    long DurationMs,
    double Volume,
    bool HasPlaybackError);

/// <summary>
/// 播放器 companion 的完整不可变快照。队列结构变化时才替换 Queue 列表；普通状态变化复用同一份歌曲数据。
/// Complete immutable companion snapshot. The Queue list is replaced only when the queue structure changes.
/// </summary>
public sealed record PlayerCompanionSnapshot(
    PlayerCompanionConnectionState ConnectionState,
    PlayerCompanionCapabilities Capabilities,
    int ProtocolVersion,
    string PlayerVersion,
    PlayerCompanionPlayer? Player,
    IReadOnlyList<PlayerCompanionTrack> Queue,
    IReadOnlyList<int> ShuffleOrder,
    long QueueRevision,
    PlayerCompanionError Error,
    string? ErrorDetail,
    bool IsStale,
    DateTimeOffset UpdatedAt)
{
    /// <summary>未连接的空快照；队列/账号上下文均为空，界面据此显示未连接状态。</summary>
    public static PlayerCompanionSnapshot Empty { get; } = new(
        PlayerCompanionConnectionState.Disconnected,
        PlayerCompanionCapabilities.None,
        0,
        string.Empty,
        null,
        Array.Empty<PlayerCompanionTrack>(),
        Array.Empty<int>(),
        0,
        PlayerCompanionError.None,
        null,
        true,
        DateTimeOffset.MinValue);

    /// <summary>当前连接是否处于已同步状态。/ Whether the companion is connected and synchronized.</summary>
    public bool IsConnected => ConnectionState == PlayerCompanionConnectionState.Connected;

    /// <summary>按外部播放器的当前索引取得歌曲；索引为空或越界时返回 null。</summary>
    public PlayerCompanionTrack? CurrentTrack =>
        Player?.Index is int index && index >= 0 && index < Queue.Count ? Queue[index] : null;
}

/// <summary>连接、刷新等非命令操作的结果。/ Result of a non-command operation such as connect or refresh.</summary>
public sealed record PlayerCompanionResult(bool Success, PlayerCompanionError Error = PlayerCompanionError.None, string? Detail = null)
{
    public static PlayerCompanionResult Ok { get; } = new(true);

    public static PlayerCompanionResult Fail(PlayerCompanionError error, string? detail = null) => new(false, error, detail);
}

/// <summary>
/// 命令结果。Accepted 只表示外部播放器已接收请求，不表示歌曲已经开始播放；实际状态由后续快照确认。
/// Command result. Accepted only means the external player accepted the request, not that playback started.
/// </summary>
public sealed record PlayerCompanionCommandResult(
    bool Accepted,
    PlayerCompanionError Error,
    bool Refreshed,
    long QueueRevision,
    string? Detail = null)
{
    public static PlayerCompanionCommandResult Accept(long queueRevision, bool refreshed = false) =>
        new(true, PlayerCompanionError.None, refreshed, queueRevision);

    public static PlayerCompanionCommandResult Fail(PlayerCompanionError error, long queueRevision, bool refreshed = false, string? detail = null) =>
        new(false, error, refreshed, queueRevision, detail);
}

/// <summary>把稳定错误码映射到本地化资源键；原始外部错误只留在日志/诊断中。</summary>
public static class PlayerCompanionErrorPresentation
{
    public static string ResourceKey(PlayerCompanionError error) => error switch
    {
        PlayerCompanionError.ServiceUnavailable => "Panel.Queue.Error.Unavailable",
        PlayerCompanionError.IncompatibleProtocol => "Panel.Queue.Error.Incompatible",
        PlayerCompanionError.Busy => "Panel.Queue.Error.Busy",
        PlayerCompanionError.StaleQueue => "Panel.Queue.Error.Stale",
        PlayerCompanionError.TrackNotFound => "Panel.Queue.Error.TrackMissing",
        PlayerCompanionError.InvalidRequest => "Panel.Queue.Error.Invalid",
        PlayerCompanionError.Timeout => "Panel.Queue.Error.Timeout",
        PlayerCompanionError.ConnectionLost => "Panel.Queue.Error.ConnectionLost",
        _ => "Panel.Queue.Error.Internal"
    };
}
