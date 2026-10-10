// 验证媒体文字限宽与定位最小布局分离，使用原始元数据而非滚动窗口。
using AFMediaBar.Classes.Models.Layout;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Settings;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>长标题首选宽度、完整悬停布局和锚点保持回归。</summary>
[TestClass]
public sealed class MetadataTextWidthTests
{
    [TestMethod]
    public void LongTitlesAreCappedButShortTitlesAndHoverControlsRetainTheirSize()
    {
        Assert.AreEqual(240d, TaskbarExperiencePolicy.ResolveMetadataTextWidth(1800, 159));
        Assert.AreEqual(180d, TaskbarExperiencePolicy.ResolveMetadataTextWidth(180, 159));
        Assert.AreEqual(159d, TaskbarExperiencePolicy.ResolveMetadataTextWidth(60, 159));
        Assert.AreEqual(280d, TaskbarExperiencePolicy.ResolveMetadataTextWidth(1800, 280));
        Assert.AreEqual(360d, TaskbarExperiencePolicy.ResolvePrimaryLength(440, 280, 1000, TaskbarLengthMode.Fixed, 360));
    }

    [TestMethod]
    public void InlineTitleUsesTheCompleteOriginalTitleAndArtist()
    {
        var title = string.Concat(Enumerable.Repeat("视频 👩🏽‍💻 é ", 50));
        var combined = TaskbarExperiencePolicy.FormatMetadataTitle(title, "Artist", TaskbarContentLayout.CompactInline);
        Assert.AreEqual(title + " · Artist", combined);
        Assert.AreEqual(title, TaskbarExperiencePolicy.FormatMetadataTitle(title, "Artist", TaskbarContentLayout.AdaptiveStack));
        Assert.AreEqual("Artist", TaskbarExperiencePolicy.FormatMetadataTitle("", "Artist", TaskbarContentLayout.CompactInline));
    }

    [TestMethod]
    public void SmallerCurrentRegionKeepsItsAnchorUntilTheRealMinimumFails()
    {
        var first = TaskbarPlacementPolicy.Evaluate(new(), Snapshot(1, 20, 360), TaskbarBarPosition.Start, 1600, 0, 280, 300);
        var longTitle = TaskbarPlacementPolicy.Evaluate(first.State, Snapshot(2, 20, 320), TaskbarBarPosition.Start, 1600, 0, 280, 440);
        Assert.AreEqual(first.Position, longTitle.Position);
        Assert.AreEqual(300, longTitle.Width);
        Assert.IsFalse(longTitle.NotifyFallback);
        var noRoom = TaskbarPlacementPolicy.Evaluate(longTitle.State, Snapshot(3, 20, 280), TaskbarBarPosition.Start, 1600, 0, 280, 440);
        Assert.IsTrue(noRoom.NotifyFallback);
        Assert.IsTrue(noRoom.Position >= 1000);
    }

    private static TaskbarOccupancySnapshot Snapshot(long id, int start, int end) =>
        new(TaskbarProbeStatus.Success, 1, id, id, [new(start, end), new(1000, 1500)]);
}
