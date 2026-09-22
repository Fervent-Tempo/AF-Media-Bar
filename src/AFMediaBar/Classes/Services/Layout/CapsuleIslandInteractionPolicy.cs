using System;

namespace AFMediaBar.Classes.Services.Layout;

/// <summary>
/// 胶囊岛的交互判定：跑马灯是否需要滚动、指针位移是否越过拖拽阈值、指针离开时是否收起。
/// Capsule-island interaction decisions: whether the marquee has to scroll, whether a pointer displacement crosses the
/// drag threshold, and whether a pointer leave collapses the island.
///
/// 这三个判定原本写死在 <c>CapsuleIslandWindow</c> 里，且分别被"文本宽度取错"和"逐轴比较位移"两个缺陷影响；
/// 抽到这里是为了让它们可以被测试覆盖，窗口只负责取量与执行。
/// These three decisions used to be hard-coded in <c>CapsuleIslandWindow</c>, where two of them carried defects — the text
/// width was read from a stretched element and the displacement was compared per axis. They live here so tests can cover
/// them and the window only measures and acts.
/// </summary>
public static class CapsuleIslandInteractionPolicy
{
    /// <summary>
    /// 判定标题是否需要跑马灯：文本的**自然宽度**才与容器可视宽度比较。
    /// Decides whether the title has to scroll: the text's **natural width** is what the container's visible width is
    /// compared against.
    /// </summary>
    /// <param name="textWidth">文本自然宽度（DIP）/ Natural text width in DIP.</param>
    /// <param name="containerWidth">容器可视宽度（DIP）/ Container's visible width in DIP.</param>
    public static bool NeedsMarquee(double textWidth, double containerWidth)
    {
        // 亚像素级溢出（<= 1 DIP）来自取整而不是真的放不下，滚起来反而像在抖。
        // Sub-pixel overflow (<= 1 DIP) comes from rounding rather than from content that does not fit, and scrolling on it
        // reads as jitter.
        return double.IsFinite(textWidth) &&
               double.IsFinite(containerWidth) &&
               containerWidth > 0 &&
               textWidth > containerWidth + 1;
    }

    /// <summary>
    /// 判定指针位移是否越过拖拽阈值（按位移矢量的长度，而不是逐轴比较）。
    /// Decides whether a pointer displacement crosses the drag threshold, measured as the length of the displacement
    /// vector rather than per axis.
    /// </summary>
    /// <param name="deltaX">水平位移（DIP）/ Horizontal displacement in DIP.</param>
    /// <param name="deltaY">垂直位移（DIP）/ Vertical displacement in DIP.</param>
    /// <param name="thresholdDip">拖拽阈值（DIP）；非正数或非有限值表示不设阈值 / Drag threshold in DIP; non-positive or non-finite means no threshold.</param>
    public static bool ExceedsDragThreshold(double deltaX, double deltaY, double thresholdDip)
    {
        // 逐轴比较会让"两个分量都不大、但斜向已经推出很远"的移动不算拖拽；按矢量长度比较则一致。
        // Comparing per axis makes a move whose components are both small but whose diagonal is already long count as no
        // drag; comparing the vector length is consistent.
        var distance = Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
        if (!double.IsFinite(distance))
            return false;

        if (!double.IsFinite(thresholdDip) || thresholdDip <= 0)
            return distance > 0;

        return distance > thresholdDip;
    }

    /// <summary>
    /// 判定指针离开时是否收起：仅当已展开、未固定、未拖拽时收起。
    /// Decides whether a pointer leave collapses the island: only an expanded, unpinned, non-dragging island collapses.
    /// </summary>
    /// <param name="isExpanded">当前是否为卡片态 / Whether the card form is showing.</param>
    /// <param name="isPinned">是否被点击固定 / Whether a click pinned it open.</param>
    /// <param name="isPointerInside">指针是否仍在窗口内 / Whether the pointer is still inside the window.</param>
    /// <param name="isDragging">是否正在拖拽 / Whether a drag is in progress.</param>
    public static bool ShouldCollapse(bool isExpanded, bool isPinned, bool isPointerInside, bool isDragging) =>
        isExpanded && !isPinned && !isPointerInside && !isDragging;
}
