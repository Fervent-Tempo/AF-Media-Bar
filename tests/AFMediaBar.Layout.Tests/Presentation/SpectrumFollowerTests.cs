// 验证频谱跟随器的起落、峰值保持与主题策略；不依赖 WPF 视觉树。
using AFMediaBar.Classes.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests.Presentation;

[TestClass]
public sealed class SpectrumFollowerTests
{
    private static readonly TimeSpan Fast = TimeSpan.FromMilliseconds(120);

    [TestMethod]
    public void RiseIsFasterThanFall()
    {
        var rising = new SpectrumBandFollower();
        rising.Advance(1, TimeSpan.FromMilliseconds(16), Fast);
        var fall = new SpectrumBandFollower();
        fall.Snap(1);
        fall.Advance(0, TimeSpan.FromMilliseconds(16), Fast);

        Assert.IsTrue(rising.Level > 1 - fall.Level, "同样时间内的上升位移应大于下降位移。");
    }

    [TestMethod]
    public void PeakHoldsThenFalls()
    {
        var follower = new SpectrumBandFollower();
        follower.Advance(1, TimeSpan.FromSeconds(1), Fast);
        follower.Advance(0, TimeSpan.FromMilliseconds(100), Fast);
        Assert.AreEqual(1, follower.Peak, 0.0001, "保持期内峰值不应下落。");

        follower.Advance(0, TimeSpan.FromMilliseconds(500), Fast);
        Assert.IsTrue(follower.Peak < 1, "保持期结束后峰值应开始下落。");
        Assert.IsTrue(follower.Peak >= follower.Level);
    }

    [TestMethod]
    public void SnapAndSettleReachTarget()
    {
        var follower = new SpectrumBandFollower();
        follower.Snap(0.7f);
        Assert.AreEqual(0.7f, follower.Level, 0.0001);
        Assert.AreEqual(0.7f, follower.Peak, 0.0001);
        Assert.IsTrue(follower.IsSettled(0.7f));

        for (var index = 0; index < 120; index++)
            follower.Advance(0, TimeSpan.FromMilliseconds(16), Fast);
        Assert.IsTrue(follower.Level <= SpectrumPresentationPolicy.FollowerTolerance);
        Assert.IsTrue(follower.IsSettled(0));
    }

    [TestMethod]
    public void PeakTrackerObservesAndFalls()
    {
        var tracker = new SpectrumPeakTracker();
        tracker.Observe(0.8f);
        Assert.AreEqual(0.8f, tracker.Peak, 0.0001);
        Assert.IsFalse(tracker.IsSettled(0.1f));

        tracker.Advance(0.1f, TimeSpan.FromMilliseconds(100));
        Assert.AreEqual(0.8f, tracker.Peak, 0.0001);
        tracker.Advance(0.1f, TimeSpan.FromMilliseconds(800));
        Assert.IsTrue(tracker.Peak < 0.8f);
        Assert.IsTrue(tracker.Peak >= 0.1f);
    }

    [TestMethod]
    public void ThemePolicyFollowsContrastAndDecorativeTier()
    {
        var normal = SpectrumVisualThemePolicy.Resolve(highContrast: false, useDecorativeEffects: true);
        Assert.IsTrue(normal.UseMasks);
        Assert.IsTrue(normal.ShowPeakMarkers);
        Assert.IsTrue(normal.ResolveBarOpacity(0) < 1);
        Assert.IsTrue(normal.ResolveBarOpacity(1) == 1);

        var highContrast = SpectrumVisualThemePolicy.Resolve(highContrast: true, useDecorativeEffects: false);
        Assert.IsFalse(highContrast.UseMasks);
        Assert.IsFalse(highContrast.ShowPeakMarkers);
        Assert.AreEqual(1, highContrast.ResolveBarOpacity(0));
        Assert.AreEqual(0, highContrast.UnlitPixelOpacity);

        var lowPerformance = SpectrumVisualThemePolicy.Resolve(highContrast: false, useDecorativeEffects: false);
        Assert.IsFalse(lowPerformance.UseMasks);
        Assert.IsFalse(lowPerformance.ShowPeakMarkers);
        Assert.AreEqual(1, lowPerformance.ResolveBarOpacity(0.3f));
    }
}
