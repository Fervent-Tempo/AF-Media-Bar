// 验证竖排混排度量与频段聚合；不访问真实任务栏、音频设备或网络。
using AFMediaBar.Classes.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

[TestClass]
public sealed class VerticalTaskbarPresentationTests
{
    [TestMethod]
    public void MixedTextKeepsLatinRunsAndEmojiGraphemesIntact()
    {
        var measured = new List<string>();
        var length = VerticalTaskbarPresentationPolicy.MeasureText("中文ABC 123👨‍👩‍👧‍👦", 18, 0, text =>
        {
            measured.Add(text);
            return 50;
        });
        CollectionAssert.AreEqual(new[] { "ABC 123" }, measured);
        Assert.AreEqual(108, length);
    }

    [TestMethod]
    public void CompactSpectrumPreservesPeaksAcrossAllSourceBands()
    {
        var count = VerticalTaskbarPresentationPolicy.SpectrumBandCount(48, 24);
        Assert.IsTrue(count < 24);
        var values = VerticalTaskbarPresentationPolicy.AggregateSpectrum([0, 0.8f, 0.2f, float.NaN, 0.9f, 0.1f], 2);
        CollectionAssert.AreEqual(new[] { 0.8f, 0.9f }, values);
        var outline = SpectrumPresentationPolicy.CreateWaveformOutline(values, 100, 24);
        Assert.IsTrue(outline.All(point => point.X <= 6 && point.Y >= 0 && point.Y <= 24));
    }
}
