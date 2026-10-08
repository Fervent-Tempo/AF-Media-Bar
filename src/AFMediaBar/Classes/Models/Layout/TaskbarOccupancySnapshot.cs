// 任务栏探测与布局决策的数据契约，不持有窗口、探测器或计时器。
namespace AFMediaBar.Classes.Models.Layout;

/// <summary>一次探测的状态；成功的空集合与尚未完成或失败分别处理。</summary>
public enum TaskbarProbeStatus { Pending, Success, Failed }

/// <summary>当前环境代的一次不可变探测结果，时间使用单调秒数。</summary>
public sealed record TaskbarOccupancySnapshot(
    TaskbarProbeStatus Status, long Generation, long PublicationId,
    double CompletedSeconds, IReadOnlyList<TaskbarPrimaryRange> Ranges, bool IsTrustedGeometry = false, long LastFailurePublicationId = 0);

/// <summary>单个宿主的已应用锚点和探测确认状态；宿主拥有并在实际应用后替换。</summary>
public sealed record TaskbarPlacementState
{
    public long Generation { get; init; } = -1;
    public long LastPublicationId { get; init; }
    public long LastFailurePublicationId { get; init; }
    public bool IsContentHidden { get; init; }
    public bool NeedsSpaceRecovery { get; init; }
    public bool MinimumNeedsValidation { get; init; }
    public bool HasHome { get; init; }
    public long HomeAnchorTwice { get; init; }
    public long AppliedAnchorTwice { get; init; }
    public TaskbarPrimaryRange Range { get; init; }
    public int Budget { get; init; }
    public int Minimum { get; init; }
    public bool IsVisible { get; init; }
    public bool FallbackNotified { get; init; }
    public long? CandidateAnchorTwice { get; init; }
    public bool CandidateIsHome { get; init; }
    public double FirstConfirmationSeconds { get; init; }
    public double LastConfirmationSeconds { get; init; }
    public int ConfirmationCount { get; init; }
    public int ConfirmationBudget { get; init; }
}

/// <summary>一次可预览的布局结果；只有宿主应用后才能提交 State 或发出通知。</summary>
public sealed record TaskbarPlacementDecision(
    TaskbarPlacementState State, int Position, int Width, bool NotifyFallback);

/// <summary>媒体栏确实进入空间不足回退后发布的呈现结果。</summary>
public sealed class TaskbarPlacementFallbackEventArgs(bool isHidden) : EventArgs
{
    public bool IsHidden { get; } = isHidden;
}
