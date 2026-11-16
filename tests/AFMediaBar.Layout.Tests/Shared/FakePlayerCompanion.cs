using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Models;

namespace AFMediaBar.Layout.Tests;

/// <summary>不连接网络的 companion 替身；只用于窗口构造和不需要真实队列的宿主回归。</summary>
internal sealed class FakePlayerCompanion : IPlayerCompanion
{
    public string DisplayName => "Nons";
    public PlayerCompanionCapabilities Capabilities => Current.Capabilities;
    public PlayerCompanionSnapshot Current { get; private set; } = PlayerCompanionSnapshot.Empty;
    public event EventHandler<PlayerCompanionSnapshot>? Changed;
    public bool MatchesSource(string? sourceId, string? sourceName) => false;

    /// <summary>测试需要模拟状态变化时使用；生产 companion 由 API 事件驱动。</summary>
    public void SetCurrent(PlayerCompanionSnapshot snapshot)
    {
        Current = snapshot;
        Changed?.Invoke(this, snapshot);
    }

    public Task<PlayerCompanionResult> EnsureConnectedAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(PlayerCompanionResult.Ok);

    public Task<PlayerCompanionResult> RefreshAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(PlayerCompanionResult.Ok);

    public Task<PlayerCompanionCommandResult> JumpAsync(string trackId, long expectedQueueRevision,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(PlayerCompanionCommandResult.Accept(expectedQueueRevision));

    public Task<PlayerCompanionCommandResult> SetShuffleAsync(bool enabled, long expectedQueueRevision,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(PlayerCompanionCommandResult.Accept(expectedQueueRevision));

    public void Dispose()
    {
        Changed = null;
    }
}
