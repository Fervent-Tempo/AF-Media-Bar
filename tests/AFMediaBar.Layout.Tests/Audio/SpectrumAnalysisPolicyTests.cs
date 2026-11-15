// 验证频谱分析的采样率策略与 Blackman 窗；不启动真实音频采集。
using AFMediaBar.Classes.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests.Audio;

[TestClass]
public sealed class SpectrumAnalysisPolicyTests
{
    [TestMethod]
    [DataRow(0, 1024)]
    [DataRow(8000, 1024)]
    [DataRow(22050, 1024)]
    [DataRow(44100, 2048)]
    [DataRow(48000, 2048)]
    [DataRow(96000, 4096)]
    [DataRow(192000, 8192)]
    [DataRow(384000, 8192)]
    public void FftSizeFollowsSampleRateWithinBounds(int sampleRate, int expected)
    {
        var size = SpectrumAnalysisPolicy.ResolveFftSize(sampleRate);
        Assert.AreEqual(expected, size);
        Assert.IsTrue(size >= SpectrumAnalysisPolicy.MinimumFftSize);
        Assert.IsTrue(size <= SpectrumAnalysisPolicy.MaximumFftSize);
        Assert.AreEqual(0, size & (size - 1), "FFT 点数必须为 2 的幂。");
    }

    [TestMethod]
    public void BlackmanWindowHasExpectedShape()
    {
        var window = SpectrumAnalysisPolicy.CreateWindow(1024);

        Assert.AreEqual(1024, window.Length);
        Assert.AreEqual(0d, window[0], 1e-12);
        Assert.AreEqual(0d, window[^1], 1e-12);
        Assert.AreEqual(1d, window[512], 1e-3);
        Assert.IsTrue(window.All(double.IsFinite));
        for (var index = 1; index < window.Length / 2; index++)
            Assert.AreEqual(window[index], window[^(index + 1)], 1e-12, "Blackman 窗应左右对称。");
    }

    [TestMethod]
    public void InvalidWindowSizeIsEmpty()
    {
        Assert.AreEqual(0, SpectrumAnalysisPolicy.CreateWindow(0).Length);
        Assert.AreEqual(0, SpectrumAnalysisPolicy.CreateWindow(-1).Length);
        CollectionAssert.AreEqual(new[] { 1d }, SpectrumAnalysisPolicy.CreateWindow(1));
    }
}
