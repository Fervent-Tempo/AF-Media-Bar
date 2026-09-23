namespace AFMediaBar.Classes.Services;

/// <summary>
/// 频谱电平映射：把 FFT 频段幅度换算成 0–1 的显示电平。
///
/// 采集链路（WASAPI 回环）的电平 = 音乐 × 播放器内音量 × 系统设备主音量，因此绝对幅度完全取决于用户把音量开到多大；
/// 旧实现用固定常数把绝对幅度直接映射成电平，音量低时整个频谱会一直停在最小柱高（实测：设备主音量 20% 时频段幅度
/// 比公式需要的低约 30dB）。这里改成以"参考峰值"为基准的相对 dB 窗口：参考峰值瞬时抬起、缓慢衰减，
/// 频段只要落在参考峰值下方 40dB 内就能显示出来，显示强弱因此与系统音量基本无关。
/// Spectrum level mapping: converts FFT band magnitudes into 0–1 display levels.
///
/// The capture path (WASAPI loopback) carries music × player volume × endpoint master volume, so absolute magnitudes depend entirely on
/// how loud the user listens. The old implementation mapped raw magnitudes through a fixed constant, which pinned the whole spectrum to its
/// minimum bar height at low volume (measured: at a 20% master volume the band magnitudes sat about 30 dB below what the formula needed).
/// This maps magnitudes into a relative dB window around a reference peak instead: the reference jumps to a signal peak instantly and decays
/// slowly, and any band within 40 dB below it stays visible, so display strength no longer follows the system volume.
/// </summary>
public static class SpectrumLevelPolicy
{
    /// <summary>参考峰值的衰减速度（dB/秒）。只有衰减、没有增长：增长永远由当前的信号峰值立即触发，因此打击乐不会被拖慢。
    /// Decay speed of the reference peak (dB/s). It only decays; growth is always triggered instantly by the current signal peak, so percussion is not slowed down.</summary>
    public const double ReferenceDecayDbPerSecond = 12;

    /// <summary>参考峰值的下限（线性幅度）；低于它的信号整体视为静音。实测本机回环静音为数字 0，因此该下限不会抬高噪声。
    /// Lower bound of the reference peak (linear magnitude); signals below it are treated as silence. The loopback measured exact digital zero when muted, so this floor cannot lift noise.</summary>
    public const double MinimumReferenceMagnitude = 1e-7;

    /// <summary>相对参考峰值的可见动态范围（dB）：比峰值低 40dB 以上的频段显示为 0。40dB 让高频段在低音量下也能看到，同时保留频谱自身的强弱形状。
    /// Visible dynamic range below the reference peak (dB): bands more than 40 dB under it read as zero. Forty decibels keeps high bands visible at low volume while preserving the spectrum's own shape.</summary>
    public const double RelativeRangeDb = 40;

    /// <summary>
    /// 推进参考峰值：取"本次信号峰值"与"按真实时间衰减后的旧参考"的较大者，并夹到下限。
    /// Advances the reference peak: the larger of this signal's peak and the time-decayed previous reference, clamped to the floor.
    /// </summary>
    /// <param name="reference">上一次的参考峰值。/ Previous reference peak.</param>
    /// <param name="peakMagnitude">本次采样的最大频段幅度。/ Largest band magnitude of this sample.</param>
    /// <param name="elapsed">距上一次推进的真实时间。/ Real time since the previous advance.</param>
    public static double UpdateReference(double reference, double peakMagnitude, TimeSpan elapsed)
    {
        if (!double.IsFinite(reference) || reference < 0)
            reference = 0;
        if (!double.IsFinite(peakMagnitude) || peakMagnitude < 0)
            peakMagnitude = 0;

        var decayed = elapsed > TimeSpan.Zero && reference > 0
            ? reference * Math.Pow(10, -ReferenceDecayDbPerSecond * elapsed.TotalSeconds / 20)
            : reference;

        var updated = Math.Max(peakMagnitude, decayed);
        return updated < MinimumReferenceMagnitude ? 0 : updated;
    }

    /// <summary>
    /// 把一个频段幅度映射成显示电平：相对参考峰值低于 <see cref="RelativeRangeDb"/> 的频段为 0，等于参考峰值的频段为 1。
    /// Maps one band magnitude to a display level: bands more than <see cref="RelativeRangeDb"/> below the reference peak read zero, and
    /// the band at the reference peak reads one.
    /// </summary>
    /// <param name="magnitude">频段幅度。/ Band magnitude.</param>
    /// <param name="reference">参考峰值；为 0 表示静音。<see cref="UpdateReference"/> 的结果。/ Reference peak; zero means silence, as returned by <see cref="UpdateReference"/>.</param>
    public static float ToNormalizedLevel(double magnitude, double reference)
    {
        // 这一段同时兜住 NaN、负值与两边的 0：任一不合法都直接落到"静音"。
        // This guard also covers NaN, negatives, and a zero on either side: anything invalid simply reads as silence.
        if (!(magnitude > 0) || !(reference > 0))
            return 0f;

        var relativeDb = 20 * Math.Log10(magnitude / reference);
        return (float)Math.Clamp((relativeDb + RelativeRangeDb) / RelativeRangeDb, 0, 1);
    }
}
