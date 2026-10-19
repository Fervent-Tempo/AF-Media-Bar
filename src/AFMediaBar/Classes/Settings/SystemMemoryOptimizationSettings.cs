// 自动优化只保存用户策略，不保存或推断操作系统授权。

namespace AFMediaBar.Classes.Settings;

/// <summary>系统内存清理的固定档位。</summary>
public enum SystemMemoryOptimizationLevel { Standard, Deep }

/// <summary>缺字段保持自动优化关闭，默认使用标准档位。</summary>
public sealed record SystemMemoryOptimizationSettings
{
    public const int MinimumIntervalMinutes = 15;
    public const int MaximumIntervalMinutes = 1440;
    public const int DefaultIntervalMinutes = 60;
    public const int MinimumThresholdPercent = 50;
    public const int MaximumThresholdPercent = 95;
    public const int DefaultThresholdPercent = 80;
    public bool TimerEnabled { get; init; }
    public int IntervalMinutes { get; init; } = DefaultIntervalMinutes;
    public bool ThresholdEnabled { get; init; }
    public int ThresholdPercent { get; init; } = DefaultThresholdPercent;
    public SystemMemoryOptimizationLevel Level { get; init; }
    public SystemMemoryOptimizationSettings Normalize() => this with
    {
        IntervalMinutes = Math.Clamp(IntervalMinutes, MinimumIntervalMinutes, MaximumIntervalMinutes),
        ThresholdPercent = Math.Clamp(ThresholdPercent, MinimumThresholdPercent, MaximumThresholdPercent),
        Level = Enum.IsDefined(Level) ? Level : SystemMemoryOptimizationLevel.Standard
    };
}
