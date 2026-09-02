namespace AFMediaBar.Classes.Models;

/// <summary>
/// 播放器信息：从网易云客户端内存读取的播放状态快照。
/// Player info: playback state snapshot read from NetEase client memory.
///
/// 职责 Responsibilities:
/// 1. 存储从网易云进程内存读取的实时播放信息
///    Store real-time playback info read from NetEase process memory
/// 2. 提供比 SMTC 更精确的播放进度（Schedule）和歌曲标识（Identity = song id）
///    Provide more accurate playback position (Schedule) and song identity (Identity = song id) than SMTC
/// 3. 用于内存轮询路径，优先于 SMTC 快照
///    Used in memory polling path, takes precedence over SMTC snapshot
///
/// 数据来源 Data Source:
/// Players/NetEase.cs 通过读取进程内存获取，轮询间隔 233ms。
/// Obtained by Players/NetEase.cs via process memory reading, polling interval 233ms.
///
/// ⚠️ 注意 Note:
/// Identity 字段为网易云 song id，用于精确匹配歌词；SMTC 路径无此信息。
/// Identity field is NetEase song id, used for precise lyric matching; SMTC path lacks this.
/// </summary>
public readonly record struct PlayerInfo
{
    /// <summary>歌曲唯一标识（网易云 song id）Unique song identifier (NetEase song id)</summary>
    public required string Identity { get; init; }

    /// <summary>歌曲标题 Song title</summary>
    public required string Title { get; init; }

    /// <summary>艺术家名称 Artist name</summary>
    public required string Artists { get; init; }

    /// <summary>专辑名称 Album name</summary>
    public required string Album { get; init; }

    /// <summary>封面 URL Cover URL</summary>
    public required string Cover { get; init; }

    /// <summary>当前播放进度（秒）Current playback position (seconds)</summary>
    public required double Schedule { get; init; }

    /// <summary>歌曲总时长（秒）Total duration (seconds)</summary>
    public required double Duration { get; init; }

    /// <summary>音频 URL Audio URL</summary>
    public required string Url { get; init; }

    /// <summary>是否暂停 Whether paused</summary>
    public required bool Pause { get; init; }

    /// <summary>
    /// 哈希码计算：仅基于 Identity 和 Pause，用于快速比较快照是否变化。
    /// Hash code calculation: based only on Identity and Pause for quick snapshot change detection.
    /// </summary>
    public override int GetHashCode()
        => HashCode.Combine(Identity, Pause);
}
