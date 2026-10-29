// 防止图表历史无界增长、缺失值被冒充零以及相对标尺误用容量。
using AFMediaBar.Classes.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>趋势窗口和绘制标尺的输入边界。</summary>
[TestClass]
public sealed class MetricHistoryTests
{
    [TestMethod]
    public void HistoryIsBoundedAndExpiredPeakLeavesRelativeScale()
    {
        var history = new MetricHistory();
        for (var index = 0; index < 500; index++) history.Add(index / 10d, index);
        Assert.AreEqual(MetricHistory.MaximumSamples, history.Samples.Count);
        Assert.AreEqual(499d, history.GetMaximum(false));
        Assert.AreEqual(100d, history.GetMaximum(true));
        history.Add(120, 10);
        Assert.AreEqual(1, history.Samples.Count);
        Assert.AreEqual(10d, history.GetMaximum(false));
    }

    [TestMethod]
    public void InvalidReadingsRemainGapsAndClockResetDiscardsPreviousHistory()
    {
        var history = new MetricHistory();
        history.Add(10, null);
        history.Add(11, double.NaN);
        history.Add(12, -1);
        Assert.IsTrue(history.Samples.All(sample => sample.Value is null));
        Assert.AreEqual(1d, history.GetMaximum(false));
        history.Add(1, 0);
        Assert.AreEqual(1, history.Samples.Count);
        Assert.AreEqual(0d, history.Samples[0].Value);
        history.Clear();
        Assert.AreEqual(1d, history.GetMaximum(false));
    }
}
