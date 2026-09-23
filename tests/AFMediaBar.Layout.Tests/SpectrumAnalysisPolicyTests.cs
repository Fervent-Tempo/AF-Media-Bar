using AFMediaBar.Classes.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>
/// 频谱分析策略：FFT 点数随采样率自适应、频段功率积分（分数 bin 插值）与 +3 dB/oct 倾斜。
/// Spectrum analysis policies: adaptive FFT size, band power integration with fractional-bin interpolation, and the +3 dB/octave tilt.
/// </summary>
[TestClass]
public sealed class SpectrumAnalysisPolicyTests
{
    [TestMethod]
    public void FftSizeFollowsTheSampleRateAndStaysAPowerOfTwo()
    {
        // 目标 bin 宽 40 Hz：96 kHz 的 512 点（187.5 Hz/bin）正是"前几段挤在同一个 bin"的根因，所以必须是 4096。
        // The target bin width is 40 Hz: 512 points at 96 kHz (187.5 Hz per bin) is exactly why the first bands shared one bin, so the
        // result has to be 4096 there.
        Assert.AreEqual(4_096, SpectrumAnalysisPolicy.ResolveFftSize(96_000));
        Assert.AreEqual(2_048, SpectrumAnalysisPolicy.ResolveFftSize(48_000));
        Assert.AreEqual(2_048, SpectrumAnalysisPolicy.ResolveFftSize(44_100));
        Assert.AreEqual(1_024, SpectrumAnalysisPolicy.ResolveFftSize(22_050));
        // 极低采样率与非法值都落到下限；超高采样率夹在上限。
        // Very low rates and invalid values fall to the minimum; very high rates clamp to the maximum.
        Assert.AreEqual(SpectrumAnalysisPolicy.MinimumFftSize, SpectrumAnalysisPolicy.ResolveFftSize(8_000));
        Assert.AreEqual(SpectrumAnalysisPolicy.MinimumFftSize, SpectrumAnalysisPolicy.ResolveFftSize(0));
        Assert.AreEqual(SpectrumAnalysisPolicy.MaximumFftSize, SpectrumAnalysisPolicy.ResolveFftSize(192_000));
    }

    [TestMethod]
    public void TiltCompensatesThreeDecibelsPerOctave()
    {
        Assert.AreEqual(1, SpectrumAnalysisPolicy.ResolveTiltPowerGain(1_000), 1e-9);
        // +3 dB/oct 幅度 = 功率每倍频程 ×10^0.3。
        // +3 dB per octave in amplitude means the power scales by 10^0.3 per octave.
        Assert.AreEqual(Math.Pow(10, 0.3), SpectrumAnalysisPolicy.ResolveTiltPowerGain(2_000), 1e-9);
        Assert.AreEqual(Math.Pow(10, 0.6), SpectrumAnalysisPolicy.ResolveTiltPowerGain(4_000), 1e-9);
        Assert.AreEqual(Math.Pow(10, -0.3), SpectrumAnalysisPolicy.ResolveTiltPowerGain(500), 1e-9);
        Assert.AreEqual(1, SpectrumAnalysisPolicy.ResolveTiltPowerGain(0), 1e-9);
        Assert.AreEqual(1, SpectrumAnalysisPolicy.ResolveTiltPowerGain(double.NaN), 1e-9);
    }

    [TestMethod]
    public void AFlatSpectrumIntegratesToItsOwnValue()
    {
        double[] flat = [0.25, 0.25, 0.25, 0.25];
        Assert.AreEqual(
            0.25,
            SpectrumAnalysisPolicy.IntegrateBandPower(flat, binWidthHz: 100, lowHz: 60, highHz: 340),
            1e-9);
    }

    [TestMethod]
    public void EmptyOrInvalidInputsReadZero()
    {
        Assert.AreEqual(0, SpectrumAnalysisPolicy.IntegrateBandPower([], 100, 0, 1_000));
        Assert.AreEqual(0, SpectrumAnalysisPolicy.IntegrateBandPower([1.0], 0, 0, 1_000));
        Assert.AreEqual(0, SpectrumAnalysisPolicy.IntegrateBandPower([1.0], 100, double.NaN, 1_000));
        Assert.AreEqual(0, SpectrumAnalysisPolicy.IntegrateBandPower([1.0], 100, 0, double.PositiveInfinity));
    }

    [TestMethod]
    public void AdjacentBandsNarrowerThanOneBinStillReadDifferentValues()
    {
        // bin 宽 100 Hz、中心 50/150/250…；单个尖峰落在 150 Hz 的 bin 上。
        // Bin width 100 Hz with centers at 50/150/250…; a single spike sits on the 150 Hz bin.
        double[] power = [0, 1, 0, 0, 0];
        var first = SpectrumAnalysisPolicy.IntegrateBandPower(power, 100, 40, 60);
        var second = SpectrumAnalysisPolicy.IntegrateBandPower(power, 100, 60, 80);
        var third = SpectrumAnalysisPolicy.IntegrateBandPower(power, 100, 80, 100);

        Assert.IsTrue(second > first, "沿同一段斜率更靠近尖峰的频段必须更高。");
        Assert.IsTrue(third > second, "沿同一段斜率继续靠近尖峰的频段必须继续变高。");
        Assert.AreEqual(0.05, first, 1e-9);
        Assert.AreEqual(0.2, second, 1e-9);
        Assert.AreEqual(0.4, third, 1e-9);
    }

    [TestMethod]
    public void ABinCenterReadsItsOwnPowerAndRangesClampToTheValidSpectrum()
    {
        double[] power = [0, 1, 0, 0.5];
        // 区间退化到 bin 中心时直接读该 bin。
        // A degenerate interval reads its bin directly.
        Assert.AreEqual(1, SpectrumAnalysisPolicy.IntegrateBandPower(power, 100, 150, 150), 1e-9);
        // 超出最后一个 bin 中心的区间夹到有效范围，读的是最后一个 bin。
        // An interval past the last bin center clamps into range and reads the last bin.
        Assert.AreEqual(0.5, SpectrumAnalysisPolicy.IntegrateBandPower(power, 100, 400, 500), 1e-9);
        // 低于第一个 bin 中心的区间读第一个 bin。
        // An interval below the first bin center reads the first bin.
        Assert.AreEqual(0, SpectrumAnalysisPolicy.IntegrateBandPower(power, 100, 1, 10), 1e-9);
    }
}
