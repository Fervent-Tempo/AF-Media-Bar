using AFMediaBar.Classes.Models;

namespace AFMediaBar.Classes.Abstractions;

/// <summary>
/// 可插拔媒体来源扩展：负责提供来源专属快照，并接收当前 SMTC 状态用于仲裁。
/// Pluggable media-source provider: supplies source-specific snapshots and receives current SMTC state for arbitration.
/// </summary>
public interface IMediaSourceProvider : IDisposable
{
    event Action<IMediaSourceProvider, MediaSnapshot?>? SnapshotChanged;

    /// <summary>
    /// 判断此提供者是否负责指定的 SMTC 来源。
    /// Determines whether this provider handles the specified SMTC source.
    /// </summary>
    bool CanHandle(string sourceId);

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
