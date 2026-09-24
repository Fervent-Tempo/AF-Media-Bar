using AFMediaBar.Classes.Models;

namespace AFMediaBar.Classes.Abstractions;

/// <summary>
/// 只读的 SMTC 会话来源快照：给不需要会话控制的消费者（如歌词页的"扫描正在播放"）一个最小依赖面，
/// 测试可以用假实现替代完整的 <see cref="MediaSessionService"/>。
/// A read-only snapshot of the SMTC session sources: consumers that need no session control (such as the lyrics page's
/// "scan playing players") depend on this minimal surface, and tests can substitute a fake for the full
/// MediaSessionService.
/// </summary>
public interface IMediaSessionSourceScanner
{
    /// <summary>当前已发现的会话选项（含来源标识与显示名）。/ The currently discovered session options, with source ids and display names.</summary>
    IReadOnlyList<MediaSessionOption> CurrentSessionOptions { get; }
}
