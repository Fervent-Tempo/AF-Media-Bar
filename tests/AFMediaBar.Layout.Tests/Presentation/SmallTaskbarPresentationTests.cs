// 验证实际预算、原始逐字帧与一次性提示，不依赖 Windows 任务栏。
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Services.Lyrics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

[TestClass]
public sealed class SmallTaskbarPresentationTests
{
    [TestMethod]
    public void BothAxesKeepPrimaryReadableAndRestoreSecondaryWithMoreRoom()
    {
        var horizontal = SmallTaskbarPresentationPolicy.Resolve(28, false, 18, 15, 2);
        Assert.IsTrue(horizontal.HideSecondary);
        Assert.AreEqual(1d, horizontal.FontScale);
        Assert.AreEqual(1d, horizontal.PaddingDip);
        Assert.IsFalse(SmallTaskbarPresentationPolicy.Resolve(48, false, 18, 15, 2).HideSecondary);
        Assert.IsTrue(SmallTaskbarPresentationPolicy.Resolve(36, true, 22.5, 17.5, 2).HideSecondary);
        Assert.IsFalse(SmallTaskbarPresentationPolicy.Resolve(64, true, 22.5, 17.5, 2).HideSecondary);
        var extreme = SmallTaskbarPresentationPolicy.Resolve(16, true, 22.5, 17.5, 2);
        Assert.IsTrue(extreme.FontScale < 1);
        Assert.IsTrue(22.5 * extreme.FontScale + extreme.PaddingDip * 2 <= 16);
    }

    [TestMethod]
    public void HiddenSecondaryDoesNotChangeRawTextTimingOrScan()
    {
        var raw = new LyricsPresentationFrame(true, "track", 2, "中文 Mixed 🎵", "A long following line",
            "Translation", "Following translation", true, .3, .42, .55, true);
        var projected = SmallTaskbarPresentationPolicy.Project(raw, true);
        Assert.AreEqual(raw.Current, projected.Current);
        Assert.AreEqual(raw.TrackId, projected.TrackId);
        Assert.AreEqual(raw.WordScanProgress, projected.WordScanProgress);
        Assert.AreEqual(raw.ScrollProgress, projected.ScrollProgress);
        Assert.AreEqual(raw.LineProgress, projected.LineProgress);
        Assert.AreEqual(string.Empty, projected.Next);
        Assert.AreEqual(string.Empty, projected.CurrentTranslation);
        Assert.AreEqual("Translation", raw.CurrentTranslation);
        Assert.AreSame(raw, SmallTaskbarPresentationPolicy.Project(raw, false));
    }
}
