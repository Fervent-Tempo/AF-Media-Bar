using AFMediaBar.Classes.Models;

namespace AFMediaBar.Classes.Abstractions;

/// <summary>
/// 播放器可选能力：为 AF 提供真实队列、跳转、随机和状态变化。曲库、账号、音频和播放调度仍归外部播放器。
/// Optional player capabilities: real queue, jump, shuffle, and state changes. Library, account, audio, and scheduling stay with the player.
///
/// 消费者只订阅事件和读取快照，不负责释放该服务；生命周期由组合根持有。
/// Consumers only subscribe and read snapshots; the composition root owns the lifetime.
/// </summary>
public interface IPlayerCompanion : IDisposable
{
    /// <summary>用于界面标题/提示的播放器显示名；不包含账号信息。</summary>
    string DisplayName { get; }

    /// <summary>当前连接支持的能力；队列读取成功后才是稳定的最终值。</summary>
    PlayerCompanionCapabilities Capabilities { get; }

    /// <summary>最近一次发布的不可变快照；可在任意线程读取。</summary>
    PlayerCompanionSnapshot Current { get; }

    /// <summary>连接状态、队列、当前曲目或能力变化时在 UI 线程触发。</summary>
    event EventHandler<PlayerCompanionSnapshot>? Changed;

    /// <summary>判断当前媒体快照是否属于该播放器；不依赖正在浏览的页面。</summary>
    bool MatchesSource(string? sourceId, string? sourceName);

    /// <summary>建立或复用连接；首次使用才联网，失败时返回稳定错误码。</summary>
    Task<PlayerCompanionResult> EnsureConnectedAsync(CancellationToken cancellationToken = default);

    /// <summary>重新读取播放器与队列完整快照；已连接时用于版本冲突或超时后的校准。</summary>
    Task<PlayerCompanionResult> RefreshAsync(CancellationToken cancellationToken = default);

    /// <summary>跳转到真实队列中的歌曲；expectedQueueRevision 来自界面展示的快照。</summary>
    Task<PlayerCompanionCommandResult> JumpAsync(string trackId, long expectedQueueRevision, CancellationToken cancellationToken = default);

    /// <summary>把随机状态设置为目标值；使用“设为开启/关闭”而不是“切换”，避免重试反转状态。</summary>
    Task<PlayerCompanionCommandResult> SetShuffleAsync(bool enabled, long expectedQueueRevision, CancellationToken cancellationToken = default);
}
