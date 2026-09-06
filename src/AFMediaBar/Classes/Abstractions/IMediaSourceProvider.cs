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

    void UpdateSessionSnapshot(MediaSnapshot snapshot);

    void Start();
}
