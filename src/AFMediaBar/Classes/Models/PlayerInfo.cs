namespace AFMediaBar.Classes.Models;

/// <summary>
/// 内存播放器（网易云）快照数据：曲目标识、元数据与播放进度。
/// Memory player (NetEase) snapshot data: track identity, metadata and playback position.
/// </summary>
public readonly record struct PlayerInfo
{
    public required string Identity { get; init; }
    public required string Title { get; init; }
    public required string Artists { get; init; }
    public required string Album { get; init; }
    public required string Cover { get; init; }
    public required double Schedule { get; init; }
    public required double Duration { get; init; }
    public required string Url { get; init; }

    public required bool Pause { get; init; }

    public override int GetHashCode()
        => HashCode.Combine(Identity, Pause);
}
