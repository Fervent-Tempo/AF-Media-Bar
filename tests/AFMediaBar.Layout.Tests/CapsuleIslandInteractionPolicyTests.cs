using System;
using AFMediaBar.Classes.Services.Layout;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>
/// 胶囊岛交互判定的纯逻辑测试：跑马灯是否需要滚动、是否越过拖拽阈值、指针离开时是否收起。
/// Pure-logic tests for capsule-island interaction decisions: whether the marquee has to scroll, whether a pointer move
/// crosses the drag threshold, and whether a pointer leave collapses the island.
/// </summary>
[TestClass]
public sealed class CapsuleIslandInteractionPolicyTests
{
    /// <summary>
    /// 标题自然宽度严格超出容器可视宽度才滚动：这是"跑马灯永不触发"的回归线——窗口曾把被 Star 列拉伸后的
    /// ActualWidth 当作文本自然宽度，于是溢出判定恒为假。
    /// The title scrolls only once its natural width strictly exceeds the container's visible width: this is the regression
    /// line for the marquee that never triggered, because the window treated the ActualWidth of a star-stretched TextBlock
    /// as the text's natural width and the overflow test was therefore always false.
    /// </summary>
    [TestMethod]
    public void NeedsMarquee_只有文本自然宽度超出容器时才滚动()
    {
        Assert.IsTrue(CapsuleIslandInteractionPolicy.NeedsMarquee(180, 120));
        Assert.IsTrue(CapsuleIslandInteractionPolicy.NeedsMarquee(122, 120));
        // 恰好超出容差（1 DIP）时仍不滚动，超过它才开始滚动。
        // Exactly the tolerance (1 DIP) of overflow still does not scroll; past it, it does.
        Assert.IsFalse(CapsuleIslandInteractionPolicy.NeedsMarquee(121, 120));
        // 相等或更窄时不滚动，偏移必须能停在零位。
        // Equality or a narrower text does not scroll, which is what keeps the offset at zero.
        Assert.IsFalse(CapsuleIslandInteractionPolicy.NeedsMarquee(120, 120));
        Assert.IsFalse(CapsuleIslandInteractionPolicy.NeedsMarquee(80, 120));
    }

    /// <summary>
    /// 亚像素误差不触发滚动：半像素的溢出来自取整而不是真的放不下，滚动起来反而更像抖动。
    /// Sub-pixel overflow does not scroll: half a pixel of overflow comes from rounding rather than from content that does
    /// not fit, and scrolling on it reads as jitter.
    /// </summary>
    [TestMethod]
    public void NeedsMarquee_亚像素溢出不算溢出()
    {
        Assert.IsFalse(CapsuleIslandInteractionPolicy.NeedsMarquee(120.4, 120));
        Assert.IsTrue(CapsuleIslandInteractionPolicy.NeedsMarquee(121.2, 120));
    }

    /// <summary>
    /// 容器或文本尚无有效尺寸（尚未布局、非有限值）时不滚动，避免在首次布局前先滚一帧。
    /// No scrolling while the container or the text has no valid size yet (not laid out, non-finite), so nothing scrolls for
    /// one frame before the first layout pass.
    /// </summary>
    [TestMethod]
    public void NeedsMarquee_无有效尺寸时不滚动()
    {
        Assert.IsFalse(CapsuleIslandInteractionPolicy.NeedsMarquee(0, 0));
        Assert.IsFalse(CapsuleIslandInteractionPolicy.NeedsMarquee(200, 0));
        Assert.IsFalse(CapsuleIslandInteractionPolicy.NeedsMarquee(0, 120));
        Assert.IsFalse(CapsuleIslandInteractionPolicy.NeedsMarquee(-5, 120));
        Assert.IsFalse(CapsuleIslandInteractionPolicy.NeedsMarquee(double.NaN, 120));
        Assert.IsFalse(CapsuleIslandInteractionPolicy.NeedsMarquee(200, double.NaN));
        Assert.IsFalse(CapsuleIslandInteractionPolicy.NeedsMarquee(double.PositiveInfinity, 120));
    }

    /// <summary>
    /// 拖拽阈值按位移矢量的长度判定：位移只是倾斜但两个分量都不大时仍不算拖拽，而斜向稍微一推就超过阈值时才算。
    /// The drag threshold measures the length of the displacement vector: a mostly diagonal move whose components are both
    /// small is not a drag, while a slightly larger diagonal push is.
    /// </summary>
    [TestMethod]
    public void ExceedsDragThreshold_按位移长度判定()
    {
        Assert.IsFalse(CapsuleIslandInteractionPolicy.ExceedsDragThreshold(0, 0, 4));
        Assert.IsFalse(CapsuleIslandInteractionPolicy.ExceedsDragThreshold(3, 0, 4));
        // 两个分量都在阈值内，但斜向长度已超过：斜推也必须算拖拽。
        // Both components sit inside the threshold while the diagonal length exceeds it: a diagonal push is a drag too.
        Assert.IsTrue(CapsuleIslandInteractionPolicy.ExceedsDragThreshold(3, 3, 4));
        // 恰好在阈值上不算越界，与既有"小于阈值不拖拽"的语义一致。
        // Exactly at the threshold is not past it, matching the existing "below the threshold means no drag" semantics.
        Assert.IsFalse(CapsuleIslandInteractionPolicy.ExceedsDragThreshold(4, 0, 4));
        Assert.IsTrue(CapsuleIslandInteractionPolicy.ExceedsDragThreshold(4, 0.1, 4));
        Assert.IsTrue(CapsuleIslandInteractionPolicy.ExceedsDragThreshold(-10, 0, 4));
        Assert.IsTrue(CapsuleIslandInteractionPolicy.ExceedsDragThreshold(0, -10, 4));
    }

    /// <summary>阈值为零或负数（或非有限值）时视为不设阈值，任何有意义的位移都算拖拽。/ A zero, negative, or non-finite threshold means "no threshold", so any real displacement is a drag.</summary>
    [TestMethod]
    public void ExceedsDragThreshold_无有效阈值时任何位移都算拖拽()
    {
        Assert.IsTrue(CapsuleIslandInteractionPolicy.ExceedsDragThreshold(0.001, 0, 0));
        Assert.IsTrue(CapsuleIslandInteractionPolicy.ExceedsDragThreshold(0.001, 0, -1));
        Assert.IsTrue(CapsuleIslandInteractionPolicy.ExceedsDragThreshold(0.001, 0, double.NaN));
        Assert.IsFalse(CapsuleIslandInteractionPolicy.ExceedsDragThreshold(0, 0, 0));
    }

    /// <summary>
    /// 指针离开只在"已展开、未固定、未拖拽"时收起；固定态（点击钉住）下指针离开不收起。
    /// A pointer leave collapses only while the island is expanded, unpinned, and not being dragged; a pinned island stays
    /// expanded when the pointer leaves.
    /// </summary>
    [TestMethod]
    public void ShouldCollapse_指针离开时只在未固定且未拖拽时收起()
    {
        Assert.IsTrue(CapsuleIslandInteractionPolicy.ShouldCollapse(isExpanded: true, isPinned: false, isPointerInside: false, isDragging: false));
        Assert.IsFalse(CapsuleIslandInteractionPolicy.ShouldCollapse(isExpanded: true, isPinned: true, isPointerInside: false, isDragging: false));
        Assert.IsFalse(CapsuleIslandInteractionPolicy.ShouldCollapse(isExpanded: true, isPinned: false, isPointerInside: true, isDragging: false));
        Assert.IsFalse(CapsuleIslandInteractionPolicy.ShouldCollapse(isExpanded: true, isPinned: false, isPointerInside: false, isDragging: true));
        Assert.IsFalse(CapsuleIslandInteractionPolicy.ShouldCollapse(isExpanded: true, isPinned: true, isPointerInside: true, isDragging: true));
    }

    /// <summary>已经是胶囊态时任何组合都不再收起（避免反复启动收起动画）。/ While the island is already a capsule no combination collapses it, which keeps the collapse animation from restarting.</summary>
    [TestMethod]
    public void ShouldCollapse_收起态永不重复收起()
    {
        foreach (var pinned in new[] { false, true })
        {
            foreach (var inside in new[] { false, true })
            {
                foreach (var dragging in new[] { false, true })
                {
                    Assert.IsFalse(CapsuleIslandInteractionPolicy.ShouldCollapse(
                        isExpanded: false,
                        isPinned: pinned,
                        isPointerInside: inside,
                        isDragging: dragging));
                }
            }
        }
    }

    /// <summary>固定与拖拽各自单独就足以阻止收起，两条都成立时同样不收起。/ Pinning and dragging each independently block the collapse, and both together do as well.</summary>
    [TestMethod]
    public void ShouldCollapse_固定与拖拽互相独立地阻止收起()
    {
        Assert.IsFalse(CapsuleIslandInteractionPolicy.ShouldCollapse(true, false, true, true));
        Assert.IsFalse(CapsuleIslandInteractionPolicy.ShouldCollapse(true, true, false, true));
        Assert.IsTrue(CapsuleIslandInteractionPolicy.ShouldCollapse(true, false, false, false));
    }
}
