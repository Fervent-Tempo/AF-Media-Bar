using AFMediaBar.Classes.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Linq;

namespace AFMediaBar.Layout.Tests;

/// <summary>
/// 频谱电平策略：参考峰值的攻击/衰减/下限，以及"相对参考峰值"的 dB 映射与音量无关性。
/// Spectrum level policy: reference-peak attack/decay/floor, the relative-dB mapping, and volume independence.
/// </summary>
[TestClass]
public sealed class SpectrumLevelPolicyTests
{
    [TestMethod]
    public void ReferenceJumpsToTheSignalPeakImmediately()
    {
        Assert.AreEqual(
            1e-3,
            SpectrumLevelPolicy.UpdateReference(0, 1e-3, TimeSpan.FromSeconds(1)),
            1e-12);
        // 削减瞬间也不受"衰减"牵制：峰值永远先抬起来。
        // Even during a decay the peak still raises it first: attack is never slowed by the decay term.
        Assert.AreEqual(
            2e-3,
            SpectrumLevelPolicy.UpdateReference(1e-3, 2e-3, TimeSpan.FromSeconds(1)),
            1e-12);
    }

    [TestMethod]
    public void ReferenceDecaysTwelveDecibelsPerSecond()
    {
        var decayed = SpectrumLevelPolicy.UpdateReference(1e-3, 0, TimeSpan.FromSeconds(1));
        var expected = 1e-3 * Math.Pow(10, -SpectrumLevelPolicy.ReferenceDecayDbPerSecond / 20.0);
        Assert.AreEqual(expected, decayed, expected * 1e-6);
        Assert.AreEqual(1e-3, SpectrumLevelPolicy.UpdateReference(1e-3, 0, TimeSpan.Zero), 1e-12);
    }

    [TestMethod]
    public void ReferenceFallsToZeroBelowTheFloorAndSurvivesBadInput()
    {
        Assert.AreEqual(0, SpectrumLevelPolicy.UpdateReference(1e-8, 0, TimeSpan.FromSeconds(1)));
        Assert.AreEqual(0, SpectrumLevelPolicy.UpdateReference(double.NaN, double.NaN, TimeSpan.FromSeconds(1)));
        Assert.AreEqual(0, SpectrumLevelPolicy.UpdateReference(-1, -1, TimeSpan.FromSeconds(1)));
    }

    [TestMethod]
    public void LevelIsRelativeToTheReferencePeak()
    {
        var reference = 1e-3;
        Assert.AreEqual(1f, SpectrumLevelPolicy.ToNormalizedLevel(reference, reference), 1e-6);
        // 恰好低 20dB → 半格；低 40dB → 0；更低仍然是 0。
        // Exactly 20 dB under reads half; 40 dB under reads zero; anything lower stays zero.
        Assert.AreEqual(0.5f, SpectrumLevelPolicy.ToNormalizedLevel(reference / 10, reference), 1e-6);
        Assert.AreEqual(0f, SpectrumLevelPolicy.ToNormalizedLevel(reference / 100, reference), 1e-6);
        Assert.AreEqual(0f, SpectrumLevelPolicy.ToNormalizedLevel(reference / 1000, reference), 1e-6);
        // 高于参考峰值（理论上不会发生）仍夹在 1。
        // Above the reference (which should not happen) it still clamps to one.
        Assert.AreEqual(1f, SpectrumLevelPolicy.ToNormalizedLevel(reference * 10, reference), 1e-6);
    }

    [TestMethod]
    public void SilenceAndInvalidInputsReadZero()
    {
        Assert.AreEqual(0f, SpectrumLevelPolicy.ToNormalizedLevel(0, 0));
        Assert.AreEqual(0f, SpectrumLevelPolicy.ToNormalizedLevel(0, 1e-3));
        Assert.AreEqual(0f, SpectrumLevelPolicy.ToNormalizedLevel(1e-3, 0));
        Assert.AreEqual(0f, SpectrumLevelPolicy.ToNormalizedLevel(double.NaN, 1e-3));
        Assert.AreEqual(0f, SpectrumLevelPolicy.ToNormalizedLevel(1e-3, double.NaN));
    }

    [TestMethod]
    public void DisplayedShapeIsIndependentOfTheListeningVolume()
    {
        // 同一组频段整体降低 30dB（相当于用户把音量拧小）：AGC 后相对形状必须一致。
        // The same bands scaled 30 dB down (as if the user turned the volume down): the relative shape after AGC must be identical.
        double[] loud = [1e-3, 5e-4, 2.5e-4, 1e-4];
        var scale = Math.Pow(10, -30 / 20.0);
        double[] quiet = [.. loud.Select(value => value * scale)];

        var loudLevels = MapWithFreshReference(loud);
        var quietLevels = MapWithFreshReference(quiet);

        for (var index = 0; index < loudLevels.Length; index++)
        {
            Assert.AreEqual(loudLevels[index], quietLevels[index], 1e-6);
        }

        Assert.AreEqual(1f, loudLevels[0], 1e-6);
        // 最低频段相对峰值 -20dB → 半格：形状既没有被压平，也没有被拉满。
        // The lowest band sits 20 dB under the peak, which reads half: the shape is neither flattened nor saturated.
        Assert.AreEqual(0.5f, loudLevels[^1], 1e-6);
    }

    private static float[] MapWithFreshReference(double[] magnitudes)
    {
        var reference = SpectrumLevelPolicy.UpdateReference(0, magnitudes.Max(), TimeSpan.Zero);
        var levels = new float[magnitudes.Length];
        for (var index = 0; index < magnitudes.Length; index++)
        {
            levels[index] = SpectrumLevelPolicy.ToNormalizedLevel(magnitudes[index], reference);
        }

        return levels;
    }
}
