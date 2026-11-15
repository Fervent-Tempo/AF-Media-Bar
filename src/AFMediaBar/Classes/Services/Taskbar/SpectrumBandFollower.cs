namespace AFMediaBar.Classes.Services;

/// <summary>
/// 帧驱动样式的单频段跟随状态：显示值上升快、下降慢，并维护一个保持后下落的峰值。
/// Per-band follower state for frame-driven styles: the displayed level rises fast, falls slowly, and carries a held peak.
/// </summary>
public struct SpectrumBandFollower
{
    /// <summary>当前显示值（0–1）。 / Current displayed level (0–1).</summary>
    public float Level { get; private set; }

    /// <summary>回落中的峰值（0–1）。 / Falling peak level (0–1).</summary>
    public float Peak { get; private set; }

    /// <summary>峰值保持剩余时间（秒）。 / Remaining peak hold time in seconds.</summary>
    public double HoldSeconds { get; private set; }

    /// <summary>
    /// 按真实经过时间向目标推进一步。帧率无关，同一段真实时间无论跑多少帧结果一致。
    /// Advances one step towards the target using real elapsed time; frame-rate independent.
    /// </summary>
    public void Advance(float target, TimeSpan elapsed, TimeSpan fastDuration)
    {
        var goal = float.IsFinite(target) ? Math.Clamp(target, 0f, 1f) : 0f;
        if (!float.IsFinite(Level)) Level = 0;
        if (!float.IsFinite(Peak)) Peak = 0;
        if (elapsed <= TimeSpan.Zero)
        {
            if (Level == 0 && Peak == 0) Level = goal;
            return;
        }

        var seconds = elapsed.TotalSeconds;
        var rise = SpectrumPresentationPolicy.ResolveRiseTimeConstantSeconds(fastDuration);
        var fall = SpectrumPresentationPolicy.ResolveFallTimeConstantSeconds(fastDuration);
        var timeConstant = goal > Level ? rise : fall;
        if (timeConstant <= 0 || seconds <= 0)
            Level = goal;
        else
        {
            var factor = 1 - Math.Exp(-seconds / timeConstant);
            Level += (goal - Level) * (float)factor;
            if (Math.Abs(goal - Level) <= SpectrumPresentationPolicy.FollowerTolerance) Level = goal;
        }

        if (Level >= Peak)
        {
            Peak = Level;
            HoldSeconds = SpectrumPresentationPolicy.PeakHoldSeconds;
        }
        else
        {
            var remaining = seconds;
            if (HoldSeconds > 0)
            {
                var consumed = Math.Min(HoldSeconds, remaining);
                HoldSeconds -= consumed;
                remaining -= consumed;
            }
            if (remaining > 0)
            {
                Peak = Math.Max(Level, Peak - SpectrumPresentationPolicy.PeakFallPerSecond * (float)remaining);
                if (Peak - Level <= SpectrumPresentationPolicy.FollowerTolerance) Peak = Level;
            }
        }
    }

    /// <summary>直接落到目标；动效关闭、隐藏或清空时使用。 / Snaps to the target for disabled motion, hiding, or clearing.</summary>
    public void Snap(float target)
    {
        var goal = float.IsFinite(target) ? Math.Clamp(target, 0f, 1f) : 0f;
        Level = goal;
        Peak = goal;
        HoldSeconds = 0;
    }

    /// <summary>显示值和峰值是否都已落到目标附近。 / Whether both the level and the peak have settled near the target.</summary>
    public bool IsSettled(float target)
    {
        var goal = float.IsFinite(target) ? Math.Clamp(target, 0f, 1f) : 0f;
        return Math.Abs(Level - goal) <= SpectrumPresentationPolicy.FollowerTolerance &&
               Math.Abs(Peak - Level) <= SpectrumPresentationPolicy.FollowerTolerance;
    }
}

/// <summary>
/// 柱状图只需要的峰值状态：柱高继续由 WPF 合成线程动画，峰值帽按帧保持/下落。
/// Peak-only state for bars: the bar height stays on the WPF composition-thread animation while the peak cap holds and falls per frame.
/// </summary>
public struct SpectrumPeakTracker
{
    /// <summary>回落中的峰值（0–1）。 / Falling peak level (0–1).</summary>
    public float Peak { get; private set; }

    /// <summary>峰值保持剩余时间（秒）。 / Remaining peak hold time in seconds.</summary>
    public double HoldSeconds { get; private set; }

    /// <summary>采样到达时立即抬高新的峰值；不等待下一帧。 / Raises a new peak immediately when a sample arrives instead of waiting for the next frame.</summary>
    public void Observe(float level)
    {
        var goal = float.IsFinite(level) ? Math.Clamp(level, 0f, 1f) : 0f;
        if (goal >= Peak)
        {
            Peak = goal;
            HoldSeconds = SpectrumPresentationPolicy.PeakHoldSeconds;
        }
    }

    /// <summary>用当前采样电平推进峰值。 / Advances the peak from the current sampled level.</summary>
    public void Advance(float level, TimeSpan elapsed)
    {
        var goal = float.IsFinite(level) ? Math.Clamp(level, 0f, 1f) : 0f;
        if (!float.IsFinite(Peak)) Peak = 0;
        if (elapsed <= TimeSpan.Zero) return;
        var seconds = elapsed.TotalSeconds;
        if (goal >= Peak)
        {
            Peak = goal;
            HoldSeconds = SpectrumPresentationPolicy.PeakHoldSeconds;
        }
        else
        {
            var remaining = seconds;
            if (HoldSeconds > 0)
            {
                var consumed = Math.Min(HoldSeconds, remaining);
                HoldSeconds -= consumed;
                remaining -= consumed;
            }
            if (remaining > 0)
            {
                Peak = Math.Max(goal, Peak - SpectrumPresentationPolicy.PeakFallPerSecond * (float)remaining);
                if (Peak - goal <= SpectrumPresentationPolicy.FollowerTolerance) Peak = goal;
            }
        }
    }

    /// <summary>直接落到电平；隐藏/清空时使用。 / Snaps to the level for hiding or clearing.</summary>
    public void Snap(float level)
    {
        Peak = float.IsFinite(level) ? Math.Clamp(level, 0f, 1f) : 0f;
        HoldSeconds = 0;
    }

    /// <summary>峰值是否已经回到当前电平附近。 / Whether the peak has settled near the current level.</summary>
    public bool IsSettled(float level)
    {
        var goal = float.IsFinite(level) ? Math.Clamp(level, 0f, 1f) : 0f;
        return Peak - goal <= SpectrumPresentationPolicy.FollowerTolerance;
    }
}
