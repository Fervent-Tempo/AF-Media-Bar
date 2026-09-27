using AFMediaBar.Classes.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>
/// 播放时间短文本的测试：m:ss / h:mm:ss 切换与非法值兜底（完整层与悬停层气泡共用）。
/// Tests for the short playback-time text: the m:ss / h:mm:ss switch and the invalid-value fallback (shared by the full
/// panel and the hover bubble).
/// </summary>
[TestClass]
public sealed class PlaybackTimeTextTests
{
    [TestMethod]
    public void FormatsMinutesAndSeconds()
    {
        Assert.AreEqual("0:00", PlaybackTimeText.Format(0));
        Assert.AreEqual("1:05", PlaybackTimeText.Format(65));
        Assert.AreEqual("59:59", PlaybackTimeText.Format(3599));
    }

    [TestMethod]
    public void SwitchesToHoursAtOneHour()
    {
        Assert.AreEqual("1:00:00", PlaybackTimeText.Format(3600));
        Assert.AreEqual("1:00:05", PlaybackTimeText.Format(3605));
    }

    [TestMethod]
    public void InvalidValuesCountAsZero()
    {
        Assert.AreEqual("0:00", PlaybackTimeText.Format(-5));
        Assert.AreEqual("0:00", PlaybackTimeText.Format(double.NaN));
        Assert.AreEqual("0:00", PlaybackTimeText.Format(double.PositiveInfinity));
    }
}
