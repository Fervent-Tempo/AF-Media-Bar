using AFMediaBar.Models;
using AFMediaBar.Services;

namespace AFMediaBar.Core.Tests;

[TestClass]
public sealed class FontAndFormattingTests
{
    [TestMethod]
    [DataRow(100, FontSettings.MinWeight)]
    [DataRow(FontSettings.DefaultWeight, FontSettings.DefaultWeight)]
    [DataRow(1200, FontSettings.MaxWeight)]
    public void NormalizeWeight_ClampsToSupportedRange(int value, int expected)
    {
        Assert.AreEqual(expected, FontSettings.NormalizeWeight(value));
    }

    [TestMethod]
    public void ResolveText_PutsLatinFontBeforeCjkFallbacks()
    {
        var resolved = FontSettings.ResolveText(
            LatinFontPreset.Arial,
            CjkFontPreset.MicrosoftYaHei);

        Assert.StartsWith("Arial, Microsoft YaHei UI", resolved, StringComparison.Ordinal);
        Assert.Contains("Microsoft JhengHei", resolved, StringComparison.Ordinal);
    }

    [TestMethod]
    public void MetricFormatter_FormatsLargeProcessMemoryInGigabytes()
    {
        var sample = new SystemMetricsSnapshot(40, 12, 8, 1536);

        Assert.AreEqual("APP 1.5G", MetricTextFormatter.Format(sample, MetricKind.ProcessMemory));
    }

    [TestMethod]
    [DataRow(0, 0)]
    [DataRow(120, 1)]
    [DataRow(-240, 2)]
    [DataRow(121, 2)]
    public void WheelInput_NormalizesStepMagnitude(int delta, int expected)
    {
        Assert.AreEqual(expected, WheelInput.GetStepCount(delta));
    }

    [TestMethod]
    public void WheelInput_MovesCircularlyInBothDirections()
    {
        Assert.AreEqual(0, WheelInput.MoveCircular(2, 1, 3));
        Assert.AreEqual(2, WheelInput.MoveCircular(0, -1, 3));
    }
}
