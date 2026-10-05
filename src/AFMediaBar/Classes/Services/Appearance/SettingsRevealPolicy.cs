using System;

namespace AFMediaBar.Classes.Services;

/// <summary>
/// 单个设置块的入场揭示参数。/ Reveal parameters for a single settings block.
/// </summary>
/// <param name="ShouldAnimate">是否执行揭示；即时动效级别下为 false，调用方不得写入任何动画值。/ Whether to reveal; false under instant motion, and callers must then write no animated value.</param>
/// <param name="Delay">相对页面加载的起始延迟。/ Start delay relative to page load.</param>
/// <param name="Duration">透明度与位移共用的时长。/ Duration shared by the opacity and the offset.</param>
/// <param name="OffsetY">起始纵向位移（DIP）；减少动效时为 0，只保留透明度。/ Starting vertical offset in DIP; zero under reduced motion, which keeps opacity only.</param>
public readonly record struct SettingsReveal(bool ShouldAnimate, TimeSpan Delay, TimeSpan Duration, double OffsetY);

/// <summary>
/// 设置页入场揭示策略：把动效级别和块序号解析为可直接执行的动画参数。
/// 纯逻辑，不访问 WPF 元素、Dispatcher 或设置文件，因此可以直接单元测试。
/// Resolves the settings-page entrance reveal from the motion level and the block index.
/// Pure logic: it touches no WPF element, dispatcher, or settings file, so it is unit-testable.
///
/// 为什么限制错峰块数：错峰是装饰性的，长列表末尾若继续累加延迟，
/// 用户会先看到一段静止的页面再看到内容出现，那正是“卡顿”的来源。
/// 上限由时间预算反推而来：块时长取 PositionDuration（220 ms），
/// 因此最大起始延迟不超过 80 ms，让整页在 300 ms 内完成揭示。
/// Why the stagger is capped: stagger is decorative, so letting it accumulate down a long list
/// shows the user a frozen page before content appears, which reads as lag rather than polish.
/// The cap is derived from the time budget: a block lasts PositionDuration (220 ms), so the largest start
/// delay stays at or under 80 ms for the whole reveal to finish inside 300 ms.
/// </summary>
public static class SettingsRevealPolicy
{
    /// <summary>参与错峰的最大块数；其后的块在同一个延迟上一起开始。/ Maximum staggered blocks; later blocks start together at the capped delay.</summary>
    public const int MaxStaggeredBlocks = 5;

    /// <summary>相邻块的错峰步长；缩短等待，使较长的单块揭示也不拖慢整页。/ Stagger step between adjacent blocks, shortened so a longer reveal does not delay the whole page.</summary>
    public static readonly TimeSpan StaggerStep = TimeSpan.FromMilliseconds(20);

    /// <summary>
    /// 起始纵向位移（DIP）。位移只用于给出方向感，因此保持小于一个行高。
    /// Starting vertical offset in DIP. It exists only to give direction, so it stays under one line height.
    /// </summary>
    public const double EntranceOffsetDip = 8d;

    /// <summary>
    /// 按块序号和动效级别解析揭示参数；无效序号按 0 处理，不抛异常。
    /// Resolves the reveal for a block index and motion profile; an invalid index is treated as 0 and never throws.
    /// </summary>
    /// <param name="blockIndex">块在页面根容器中的序号，负数按 0 处理。/ Block index within the page root; negatives are treated as 0.</param>
    /// <param name="motion">当前环境解析出的动效参数。/ Motion parameters resolved for the current environment.</param>
    public static SettingsReveal Resolve(int blockIndex, MotionProfile motion)
    {
        if (motion.Mode == MotionMode.Instant)
            return default;

        // 减少动效时放弃位移和错峰，只保留帮助理解状态变化的透明度过渡。
        // Reduced motion drops the offset and the stagger, keeping only the opacity fade that aids comprehension.
        if (motion.Mode == MotionMode.Reduced)
            return new SettingsReveal(ShouldAnimate: true, TimeSpan.Zero, motion.StandardDuration, OffsetY: 0d);

        var normalizedIndex = Math.Clamp(blockIndex, 0, MaxStaggeredBlocks - 1);
        return new SettingsReveal(
            ShouldAnimate: true,
            StaggerStep * normalizedIndex,
            motion.PositionDuration,
            EntranceOffsetDip);
    }
}
