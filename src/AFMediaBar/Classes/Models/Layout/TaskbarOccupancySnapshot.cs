// 任务栏探测与布局决策的数据契约，不持有窗口、探测器或计时器。
namespace AFMediaBar.Classes.Models.Layout;

/// <summary>一次探测的状态；成功的空集合与尚未完成或失败分别处理。</summary>
public enum TaskbarProbeStatus { Pending, Success, Failed }

/// <summary>真实空间压力，与探测等待和动画中间帧分别处理。</summary>
public enum TaskbarSpacePressure { Normal, Constrained, Hidden }

/// <summary>当前环境代的一次不可变探测结果，时间使用单调秒数。</summary>
public sealed record TaskbarOccupancySnapshot(
    TaskbarProbeStatus Status, long Generation, long PublicationId,
    double CompletedSeconds, IReadOnlyList<TaskbarPrimaryRange> Ranges, bool IsTrustedGeometry = false, long LastFailurePublicationId = 0);

/// <summary>单个宿主已接受的目标和安全区确认；实际动画帧由宿主另行持有。</summary>
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
    public IReadOnlyList<TaskbarPrimaryRange> SafeRanges { get; init; } = [];
    public IReadOnlyList<TaskbarRangeObservation> Observations { get; init; } = [];
    public bool IsDragSeed { get; init; }
}

/// <summary>一份新发布的成功占用区观察，用于确认持续空闲的交集。</summary>
public sealed record TaskbarRangeObservation(double Seconds, IReadOnlyList<TaskbarPrimaryRange> Ranges);

/// <summary>一次可预览的布局结果；只有宿主应用后才能提交 State 或发出通知。</summary>
public sealed record TaskbarPlacementDecision(
    TaskbarPlacementState State, int Position, int Width, bool NotifyFallback);

/// <summary>应用布局后发布的真实空间观察；空内容不参与空间提示。</summary>
public sealed class TaskbarPlacementFallbackEventArgs(TaskbarSpacePressure pressure, bool ample,
    long generation, long publicationId, bool hasContent = true, long lastFailurePublicationId = 0) : EventArgs
{
    public long LastFailurePublicationId { get; } = lastFailurePublicationId;
    public bool HasContent { get; } = hasContent;
    public TaskbarSpacePressure Pressure { get; } = pressure;
    public bool HasAmpleSpace { get; } = ample;
    public long Generation { get; } = generation;
    public long PublicationId { get; } = publicationId;
}
