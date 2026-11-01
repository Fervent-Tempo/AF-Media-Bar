// 防止图表历史无界增长、缺失值被冒充零、相对标尺误用容量，以及短暂隐藏后错误沿用或丢失趋势。
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>趋势窗口、绘制标尺与短暂隐藏保留的输入边界。</summary>
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

    [TestMethod]
    public void SnapshotHistoryResumesBriefPauseAndLaterSamplesKeepOriginalTimes()
    {
        var history = new MetricSnapshotHistory();
        var sample = new SystemMetricsSnapshot(40, 50, 20, 200, 1024, 2048);
        history.Add(10, sample);
        history.Add(11, sample);
        history.Suspend(12);
        Assert.IsFalse(history.IsExpired(12 + MetricHistory.RetentionSeconds - 1));
        Assert.IsFalse(history.Resume(12 + MetricHistory.RetentionSeconds - 1));
        history.Add(40, sample);
        CollectionAssert.AreEqual(new[] { 10d, 11d, 40d }, history.Samples.Select(item => item.Time).ToArray());
    }

    [TestMethod]
    public void SnapshotHistoryExpiresFromFirstPauseAndRepeatedPauseDoesNotExtendIt()
    {
        var history = new MetricSnapshotHistory();
        history.Add(10, new SystemMetricsSnapshot(40, 50, 20, 200, 1024, 2048));
        history.Suspend(12);
        history.Suspend(20);
        Assert.IsFalse(history.IsExpired(12 + MetricHistory.RetentionSeconds - 1));
        Assert.IsTrue(history.IsExpired(12 + MetricHistory.RetentionSeconds));
        Assert.IsTrue(history.Resume(12 + MetricHistory.RetentionSeconds));
        Assert.AreEqual(0, history.Samples.Count);
    }

    [TestMethod]
    public void SnapshotHistoryTreatsClockRollbackAsExpired()
    {
        var history = new MetricSnapshotHistory();
        history.Add(10, new SystemMetricsSnapshot(40, 50, 20, 200, 1024, 2048));
        history.Suspend(100);
        Assert.IsTrue(history.IsExpired(90));
        Assert.IsTrue(history.Resume(90));
        Assert.AreEqual(0, history.Samples.Count);
    }

    [TestMethod]
    public void SnapshotHistoryStaysBoundedAndDropsSamplesOutsideTheTrendWindow()
    {
        var history = new MetricSnapshotHistory();
        var sample = new SystemMetricsSnapshot(40, 50, 20, 200, 1024, 2048);
        for (var index = 0; index < 500; index++) history.Add(index / 10d, sample);
        Assert.AreEqual(MetricHistory.MaximumSamples, history.Samples.Count);
        Assert.AreEqual(49.9, history.Samples[^1].Time, 0.001);
        history.Add(120, sample);
        Assert.AreEqual(1, history.Samples.Count, "新样本必须同时按容量与趋势窗口截断旧样本。");

        var resumed = new MetricSnapshotHistory();
        resumed.Add(9.9, sample);
        resumed.Add(50, sample);
        resumed.Suspend(55);
        Assert.IsFalse(resumed.Resume(75));
        Assert.AreEqual(50, resumed.Samples.Single().Time, 0.001, "恢复时窗口外的样本不能继续保留。");
    }
}
