using AFMediaBar.Classes.Models;

namespace AFMediaBar.Classes.Abstractions;

/// <summary>
/// 可插拔媒体来源扩展：负责提供来源专属快照，并接收当前 SMTC 状态用于仲裁。
/// Pluggable media-source provider: supplies source-specific snapshots and receives current SMTC state for arbitration.
/// 需要脱离 SMTC 独立参与来源选择时，实现 <see cref="IIndependentMediaSourceProvider"/> 并提供纯策略。
/// Implement <see cref="IIndependentMediaSourceProvider"/> to participate in selection without an SMTC session.
/// </summary>
public interface IMediaSourceProvider : IDisposable
{
    event Action<IMediaSourceProvider, MediaSnapshot?>? SnapshotChanged;

    /// <summary>
    /// 判断此提供者是否负责指定的 SMTC 来源。
    /// Determines whether this provider handles the specified SMTC source.
    /// </summary>
    bool CanHandle(string sourceId);

    /// <summary>由本提供器取词，SMTC 通道仅在提供器不可用时兜底，避免重复请求。</summary>
    bool ProvidesLyrics => false;

    /// <summary>
    /// 更新当前 SMTC 会话快照。
    /// Updates the current SMTC session snapshot.
    /// </summary>
    /// <param name="snapshot">统一媒体快照 / Unified media snapshot.</param>
    void UpdateSessionSnapshot(MediaSnapshot snapshot);

    /// <summary>
    /// 启动媒体来源监听。
    /// Starts media-source monitoring.
    /// </summary>
    void Start();
}
