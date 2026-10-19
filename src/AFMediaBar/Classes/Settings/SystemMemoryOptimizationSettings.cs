// 自动优化只保存用户策略，不保存或推断操作系统授权。
using AFMediaBar.Classes.Services;

namespace AFMediaBar.Classes.Settings;

/// <summary>缺字段保持自动优化关闭，默认使用标准档位。</summary>
public sealed record SystemMemoryOptimizationSettings
{
    public bool TimerEnabled { get; init; }
    public int IntervalMinutes { get; init; } = 60;
    public bool ThresholdEnabled { get; init; }
    public int ThresholdPercent { get; init; } = 80;
    public SystemMemoryOptimizationLevel Level { get; init; }
    public SystemMemoryOptimizationSettings Normalize() => this with
    {
        IntervalMinutes = Math.Clamp(IntervalMinutes, 15, 1440),
        ThresholdPercent = Math.Clamp(ThresholdPercent, 50, 95),
        Level = Enum.IsDefined(Level) ? Level : SystemMemoryOptimizationLevel.Standard
    };
}
