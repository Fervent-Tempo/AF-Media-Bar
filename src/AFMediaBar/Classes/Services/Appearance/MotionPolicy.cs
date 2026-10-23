using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace AFMediaBar.Classes.Services;

/// <summary>
/// 动画级别。/ Animation level.
/// </summary>
public enum MotionMode
{
    /// <summary>完整使用装饰性和连续动效。/ Use the full decorative and continuous motion set.</summary>
    Full,

    /// <summary>保留帮助理解状态的轻量过渡。/ Keep lightweight transitions that aid comprehension.</summary>
    Reduced,

    /// <summary>保持状态变化即时，仅保留必要的透明度更新。/ Make state changes immediate, retaining only essential opacity updates.</summary>
    Instant
}

/// <summary>
/// 当前环境可用的动效参数。/ Motion parameters available in the current environment.
/// </summary>
public readonly record struct MotionProfile(
    MotionMode Mode,
    TimeSpan FastDuration,
    TimeSpan StandardDuration,
    TimeSpan PanelDuration,
    TimeSpan PositionDuration,
    TimeSpan ExitDuration,
    bool UseDecorativeEffects,
    bool UseContinuousMotion)
{
    /// <summary>是否允许使用位置和缩放动画。/ Whether position and scale animations are allowed.</summary>
    public bool UseTransitions => Mode != MotionMode.Instant;

    /// <summary>自动深浅色的过渡时长，与位置、悬停和连接动画分开。</summary>
    public TimeSpan ForegroundColorDuration => Mode == MotionMode.Full ? TimeSpan.FromMilliseconds(220) : StandardDuration;

    /// <summary>媒体连接状态专用过渡时长，不影响常规位置与尺寸动画。/ Duration for media connection changes, independent of normal placement motion.</summary>
    public TimeSpan RestConnectionDuration => Mode switch
    {
        MotionMode.Full => TimeSpan.FromMilliseconds(450),
        MotionMode.Reduced => TimeSpan.FromMilliseconds(160),
        _ => TimeSpan.Zero
    };
}

/// <summary>
/// 根据系统动画、高对比度和渲染能力计算统一动效策略。
/// Resolves a unified motion policy from system animation, high-contrast, and rendering capability.
/// </summary>
public static class MotionPolicy
{
    /// <summary>媒体连接状态共用的柔和缓入缓出曲线。/ Shared gentle easing for media connection transitions.</summary>
    public static SineEase CreateRestConnectionEase() => new() { EasingMode = EasingMode.EaseInOut };

    private static readonly MotionProfile FullProfile = new(
        MotionMode.Full,
        TimeSpan.FromMilliseconds(120),
        TimeSpan.FromMilliseconds(180),
        TimeSpan.FromMilliseconds(180),
        TimeSpan.FromMilliseconds(220),
        TimeSpan.FromMilliseconds(120),
        UseDecorativeEffects: true,
        UseContinuousMotion: true);

    private static readonly MotionProfile ReducedProfile = new(
        MotionMode.Reduced,
        TimeSpan.FromMilliseconds(80),
        TimeSpan.FromMilliseconds(120),
        TimeSpan.FromMilliseconds(120),
        TimeSpan.FromMilliseconds(160),
        TimeSpan.FromMilliseconds(80),
        UseDecorativeEffects: false,
        UseContinuousMotion: false);

    private static readonly MotionProfile InstantProfile = new(
        MotionMode.Instant,
        TimeSpan.Zero,
        TimeSpan.Zero,
        TimeSpan.Zero,
        TimeSpan.Zero,
        TimeSpan.Zero,
        UseDecorativeEffects: false,
        UseContinuousMotion: false);

    /// <summary>
    /// 按外部环境解析动效级别；不访问窗口或设置文件，便于纯逻辑测试。
    /// Resolves motion from explicit environment flags without touching windows or settings, keeping the policy testable.
    /// </summary>
    /// <param name="clientAreaAnimation">Windows 是否允许客户区动画。/ Whether Windows allows client-area animations.</param>
    /// <param name="highContrast">是否启用高对比度。/ Whether high contrast is enabled.</param>
    /// <param name="lowPerformance">是否处于软件渲染或低性能路径。/ Whether software rendering or a low-performance path is active.</param>
    public static MotionProfile Resolve(bool clientAreaAnimation, bool highContrast, bool lowPerformance)
    {
        if (!clientAreaAnimation)
            return InstantProfile;

        if (highContrast || lowPerformance)
            return ReducedProfile;

        return FullProfile;
    }

    /// <summary>
    /// 读取当前桌面环境的动效策略。/ Reads the motion policy for the current desktop environment.
    /// </summary>
    public static MotionProfile ResolveCurrent()
    {
        var renderingTier = RenderCapability.Tier >> 16;
        return Resolve(
            SystemParameters.ClientAreaAnimation,
            SystemParameters.HighContrast,
            renderingTier <= 0);
    }
}
