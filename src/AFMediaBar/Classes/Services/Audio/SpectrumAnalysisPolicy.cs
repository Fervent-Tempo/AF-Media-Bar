namespace AFMediaBar.Classes.Services;

/// <summary>
/// 频谱分析的采样率相关策略：FFT 点数、频段能量积分与频率倾斜。
///
/// 背景：固定 512 点 FFT 在 96 kHz 设备上的 bin 宽是 187.5 Hz，而 20 段频谱的最前 5 段（45–206 Hz）
/// 全都落在同一个 bin 里，于是最左边几列高度永远一样。参考 Chromium Web Audio 的
/// <c>RealtimeAnalyser</c>（点数可配、按 1/fftSize 归一化、再做 dB 映射与时间平滑）与常见
/// 频谱分析仪的"粉噪坡度"显示，这里把三件事做成可单测的纯策略：
/// ① FFT 点数按采样率选，保证 bin 宽足够细；② 频段值用分数 bin 线性插值按能量积分（RMS），
/// 频段比一个 bin 还窄时也不会与邻段共用同一个值；③ 按倍频程补偿高频的能量滚降，让显示更均衡。
/// Sample-rate aware policies for spectrum analysis: FFT size, band energy integration, and frequency tilt.
///
/// Background: a fixed 512-point FFT on a 96 kHz device has 187.5 Hz bins, while the first five of twenty bands
/// (45–206 Hz) all fall into one bin, which is why the leftmost columns never differ. Following Chromium's Web Audio
/// <c>RealtimeAnalyser</c> (configurable size, 1/fftSize normalization, dB mapping, temporal smoothing) and the
/// pink-noise slope of common spectrum analyzers, this turns three things into pure, unit-testable policies:
/// (1) the FFT size follows the sample rate so bins stay narrow enough; (2) each band integrates energy with fractional-bin
/// linear interpolation (RMS), so a band narrower than one bin no longer shares a value with its neighbour; (3) high
/// frequencies are compensated by an octave slope so the display looks balanced.
/// </summary>
public static class SpectrumAnalysisPolicy
{
    /// <summary>FFT 点数下限：再小的话低频段就分不开了。/ Lower bound of the FFT size; below it the low bands stop separating.</summary>
    public const int MinimumFftSize = 1_024;

    /// <summary>FFT 点数上限：再大对 20 段显示的收益很小，只是白烧 CPU。/ Upper bound of the FFT size; beyond it a twenty-band display gains almost nothing while burning CPU.</summary>
    public const int MaximumFftSize = 4_096;

    /// <summary>目标 bin 宽（Hz）：点数按"采样率 ÷ 它"向上取到 2 的幂，低频段因此始终窄于一个 bin 的宽度。
    /// Target bin width in hertz: the size is the power of two above "sample rate ÷ this", so the lowest bands always stay narrower than one bin.</summary>
    public const double TargetBinWidthHz = 40;

    /// <summary>倾斜的参考频率（Hz）：该频率处不增不减。/ Tilt reference frequency in hertz: no gain or cut at this frequency.</summary>
    public const double TiltReferenceHz = 1_000;

    /// <summary>每倍频程的倾斜量（dB，幅度域）。音乐的频谱大致每倍频程下降 3–6 dB，补偿 +3 dB/oct 后高低频观感更均衡。
    /// Tilt per octave in decibels, amplitude domain. Music falls about 3–6 dB per octave, and compensating +3 dB/octave makes the display look balanced.</summary>
    public const double TiltDecibelsPerOctave = 3;

    /// <summary>
    /// 按采样率选出 FFT 点数：结果总是 2 的幂，且在 [<see cref="MinimumFftSize"/>, <see cref="MaximumFftSize"/>] 内。
    /// Selects the FFT size for a sample rate: always a power of two within [<see cref="MinimumFftSize"/>, <see cref="MaximumFftSize"/>].
    /// </summary>
    /// <param name="sampleRate">设备混音格式的采样率（Hz）。/ Sample rate of the device mix format in hertz.</param>
    public static int ResolveFftSize(int sampleRate)
    {
        if (sampleRate <= 0)
        {
            return MinimumFftSize;
        }

        var desired = (int)Math.Ceiling(sampleRate / TargetBinWidthHz);
        var size = MinimumFftSize;
        while (size < desired && size < MaximumFftSize)
        {
            size <<= 1;
        }

        return Math.Min(size, MaximumFftSize);
    }

    /// <summary>
    /// 返回频率对应的功率倾斜增益（作用于功率谱，等于幅度倾斜的平方）。
    /// Returns the power tilt gain for a frequency (applied to the power spectrum; it is the square of the amplitude tilt).
    /// +3 dB/octave amplitude means the power scales with 10^(3·log2(f/f0)/10).
    /// </summary>
    /// <param name="frequencyHz">频段中心频率（Hz）。/ Band center frequency in hertz.</param>
    public static double ResolveTiltPowerGain(double frequencyHz)
    {
        if (!double.IsFinite(frequencyHz) || frequencyHz <= 0)
        {
            return 1;
        }

        return Math.Pow(10, TiltDecibelsPerOctave * Math.Log2(frequencyHz / TiltReferenceHz) / 10);
    }

    /// <summary>
    /// 在功率谱上按频率区间积分并返回平均功率。
    ///
    /// 功率谱的第 <c>i</c> 个 bin 的中心频率是 <c>(i + 0.5) · binWidth</c>；区间内按等距采样并在相邻 bin 之间线性插值，
    /// 因此"比一个 bin 还窄"的频段也会得到沿频率连续变化的取值，而不是整段照抄同一个 bin。
    /// Integrates the power spectrum over a frequency interval and returns the mean power.
    ///
    /// Bin <c>i</c>'s center sits at <c>(i + 0.5) · binWidth</c>; the interval is sampled at even steps with linear interpolation
    /// between neighbouring bins, so a band narrower than one bin still reads a value that varies continuously with frequency
    /// instead of copying one bin wholesale.
    /// </summary>
    /// <param name="powerSpectrum">各 bin 的功率（幅度平方）。/ Power per bin (magnitude squared).</param>
    /// <param name="binWidthHz">bin 宽（Hz）。/ Bin width in hertz.</param>
    /// <param name="lowHz">区间下界（Hz）。/ Interval lower bound in hertz.</param>
    /// <param name="highHz">区间上界（Hz）。/ Interval upper bound in hertz.</param>
    public static double IntegrateBandPower(
        ReadOnlySpan<double> powerSpectrum,
        double binWidthHz,
        double lowHz,
        double highHz)
    {
        if (powerSpectrum.Length == 0 || !(binWidthHz > 0) || !double.IsFinite(lowHz) || !double.IsFinite(highHz))
        {
            return 0;
        }

        // 有效频率范围只到最后一个 bin 的中心；再往上没有数据可插值。
        // The usable frequency range ends at the last bin's center; there is nothing to interpolate above it.
        var firstCenter = binWidthHz * 0.5;
        var lastCenter = (powerSpectrum.Length - 0.5) * binWidthHz;
        var low = Math.Clamp(lowHz, firstCenter, lastCenter);
        var high = Math.Clamp(highHz, firstCenter, lastCenter);
        if (high <= low)
        {
            return InterpolatePower(powerSpectrum, binWidthHz, low);
        }

        // 采样间隔不大于半个 bin：比这更稀会漏掉 bin 之间的线性形状。
        // The sampling step never exceeds half a bin; anything coarser would miss the linear shape between bins.
        var samples = Math.Max(2, (int)Math.Ceiling((high - low) / (binWidthHz * 0.5)) + 1);
        var sum = 0d;
        for (var index = 0; index < samples; index++)
        {
            var frequency = low + (high - low) * index / (samples - 1);
            sum += InterpolatePower(powerSpectrum, binWidthHz, frequency);
        }

        return sum / samples;
    }

    /// <summary>在给定频率处对功率谱做线性插值（索引按 bin 中心对齐）。/ Linearly interpolates the power spectrum at a frequency (indices aligned to bin centers).</summary>
    private static double InterpolatePower(ReadOnlySpan<double> powerSpectrum, double binWidthHz, double frequency)
    {
        var position = frequency / binWidthHz - 0.5;
        var lower = (int)Math.Floor(position);
        if (lower < 0)
        {
            return powerSpectrum[0];
        }

        if (lower >= powerSpectrum.Length - 1)
        {
            return powerSpectrum[^1];
        }

        var fraction = position - lower;
        var first = powerSpectrum[lower];
        var second = powerSpectrum[lower + 1];
        return first + (second - first) * fraction;
    }
}
