namespace AFMediaBar.Classes.Services.Layout;

/// <summary>灵动岛弹簧的位置和每秒速度。/ Island spring position and velocity per second.</summary>
public readonly record struct IslandSpringFrame(double Value, double Velocity);

/// <summary>
/// 解析求解欠阻尼弹簧；不依赖帧率、计时器或窗口。
/// Analytically solves an underdamped spring independently of frame rate, timers, or windows.
/// </summary>
public static class DynamicIslandMotion
{
    private const double DecayRate = 20;
    private const double OscillationFrequency = 18;
    private const double SpringStrength = DecayRate * DecayRate + OscillationFrequency * OscillationFrequency;
    private const double SettledTolerance = 0.0005;

    /// <summary>
    /// 从现有位置和速度推进，改变目标不会重置速度。无效时间不推进，无效状态恢复为有限值。
    /// Advances the existing position and velocity without resetting momentum on retargeting.
    /// Invalid elapsed time does not advance; invalid state is recovered to finite values.
    /// </summary>
    public static IslandSpringFrame Advance(IslandSpringFrame frame, double target, double elapsedSeconds)
    {
        if (!double.IsFinite(target))
            target = double.IsFinite(frame.Value) ? frame.Value : 0;

        var value = double.IsFinite(frame.Value) ? frame.Value : target;
        var velocity = double.IsFinite(frame.Velocity) ? frame.Velocity : 0;
        if (!double.IsFinite(elapsedSeconds) || elapsedSeconds <= 0)
            return new IslandSpringFrame(value, velocity);

        var decay = Math.Exp(-DecayRate * elapsedSeconds);
        if (decay == 0)
            return new IslandSpringFrame(target, 0);

        var displacement = value - target;
        var (sine, cosine) = Math.SinCos(OscillationFrequency * elapsedSeconds);
        var nextValue = target + decay * (
            displacement * cosine + (velocity + DecayRate * displacement) / OscillationFrequency * sine);
        var nextVelocity = decay * (
            velocity * cosine - (DecayRate * velocity + SpringStrength * displacement) / OscillationFrequency * sine);

        // 极端有限输入也可能溢出；不把 NaN 或无穷传播给 WPF。
        // Even extreme finite inputs can overflow; never propagate NaN or infinity into WPF.
        if (!double.IsFinite(nextValue) || !double.IsFinite(nextVelocity))
            return new IslandSpringFrame(target, 0);

        // 不在这里吸附到终点，以保留不同帧划分和反向切换的连续性。
        // Do not snap here: preserve continuity across frame partitions and reversals.
        return new IslandSpringFrame(nextValue, nextVelocity);
    }

    /// <summary>
    /// 用单调衰减的弹簧能量判断停止，避免把高速经过目标误判为完成。
    /// Uses monotonically decaying spring energy so crossing the target at speed never counts as settled.
    /// </summary>
    public static bool IsSettled(IslandSpringFrame frame, double target)
    {
        if (!double.IsFinite(frame.Value) || !double.IsFinite(frame.Velocity) || !double.IsFinite(target))
            return false;

        var displacement = frame.Value - target;
        return displacement * displacement + frame.Velocity * frame.Velocity / SpringStrength
            <= SettledTolerance * SettledTolerance;
    }
}
