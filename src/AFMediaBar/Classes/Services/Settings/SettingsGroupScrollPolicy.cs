using System;
using System.Collections.Generic;

namespace AFMediaBar.Classes.Services;

/// <summary>
/// 设置页分组导航的滚动策略：把分组的顶端偏移、视口高度和当前滚动量解析成“当前分组”“跳转目标”和
/// “滚动进度”。纯逻辑，不访问 WPF 元素、Dispatcher 或设置文件，因此可以直接单元测试。
/// Scroll policy for settings group navigation: resolves group top offsets, the viewport height, and the
/// current scroll offset into the active group, a jump target, and a scroll progress. Pure logic: it touches
/// no WPF element, dispatcher, or settings file, so it is unit-testable.
///
/// 为什么激活判定要留一个余量：分组标题正好贴在视口顶端时，人眼仍把它读作“上一个分组的结尾”。
/// 让激活点在视口顶端之下一点，标签切换才会与视觉分组边界对齐。
/// Why activation keeps a margin: a group header sitting exactly on the viewport edge still reads as the tail
/// of the previous group, so the activation point sits slightly below the top edge and the tab change lines
/// up with the boundary the user can see.
/// </summary>
public static class SettingsGroupScrollPolicy
{
    /// <summary>激活判定相对视口顶端的下移余量（DIP）。/ How far below the viewport top the active group is decided, in DIP.</summary>
    public const double ActivationMarginDip = 12d;

    /// <summary>判定“已滚动到底部”的容差（DIP）；亚像素布局会留下小于 1 DIP 的残差。/ Tolerance for "scrolled to the bottom" in DIP; sub-pixel layout leaves a residual under one DIP.</summary>
    public const double BottomToleranceDip = 1d;

    /// <summary>
    /// 解析当前激活的分组序号；空集合返回 -1，滚动到底部时固定为最后一个分组。
    /// Resolves the active group index. An empty collection returns -1, and the bottom of the scroll range
    /// always resolves to the last group so its tab can be reached and highlighted.
    /// </summary>
    /// <param name="groupTops">各分组顶端相对滚动内容起点的偏移，必须按升序排列。/ Group top offsets relative to the scroll content, in ascending order.</param>
    /// <param name="verticalOffset">当前滚动偏移。/ Current scroll offset.</param>
    /// <param name="atBottom">是否已滚动到底部。/ Whether the scroll range is at its bottom.</param>
    public static int ResolveActiveIndex(IReadOnlyList<double> groupTops, double verticalOffset, bool atBottom)
    {
        if (groupTops is null || groupTops.Count == 0)
        {
            return -1;
        }

        if (atBottom)
        {
            return groupTops.Count - 1;
        }

        var activation = (double.IsFinite(verticalOffset) ? Math.Max(0d, verticalOffset) : 0d) + ActivationMarginDip;
        var active = 0;
        for (var index = 0; index < groupTops.Count; index++)
        {
            if (groupTops[index] <= activation)
            {
                active = index;
                continue;
            }

            break;
        }

        return active;
    }

    /// <summary>判断滚动范围是否已经到达底部。/ Determines whether the scroll range has reached its bottom.</summary>
    /// <param name="verticalOffset">当前滚动偏移。/ Current scroll offset.</param>
    /// <param name="viewportHeight">视口高度。/ Viewport height.</param>
    /// <param name="extentHeight">内容高度。/ Content extent height.</param>
    public static bool IsAtBottom(double verticalOffset, double viewportHeight, double extentHeight)
    {
        var maximum = Math.Max(0d, extentHeight - viewportHeight);
        if (maximum <= 0d)
        {
            return true;
        }

        return verticalOffset >= maximum - BottomToleranceDip;
    }

    /// <summary>
    /// 解析“把某个分组带到视口顶端”所需的目标滚动偏移，并夹取到合法范围。
    /// Resolves the scroll offset that brings a group to the top of the viewport, clamped to the legal range.
    /// </summary>
    /// <param name="groupTop">目标分组顶端偏移。/ Top offset of the target group.</param>
    /// <param name="topInset">分组顶端到视口顶端之间要保留的间距，用于让分组标题不贴着窗口边缘。/ Inset kept between the group top and the viewport top so the header does not touch the window edge.</param>
    /// <param name="viewportHeight">视口高度。/ Viewport height.</param>
    /// <param name="extentHeight">内容高度。/ Content extent height.</param>
    public static double ResolveJumpOffset(double groupTop, double topInset, double viewportHeight, double extentHeight)
    {
        if (!double.IsFinite(groupTop))
        {
            return 0d;
        }

        var maximum = Math.Max(0d, extentHeight - viewportHeight);
        var target = groupTop - Math.Max(0d, topInset);
        return Math.Clamp(target, 0d, maximum);
    }

    /// <summary>
    /// 解析滚动进度比例（0–1），供页头进度线使用。没有可滚动距离时返回 0，而不是 1，
    /// 因为“没有内容可滚”应该读作静止而不是读完。
    /// Resolves the scroll progress ratio (0-1) for the page-header progress line. Returns 0 rather than 1
    /// when there is nothing to scroll, because "nothing to scroll" should read as idle, not as finished.
    /// </summary>
    /// <param name="verticalOffset">当前滚动偏移。/ Current scroll offset.</param>
    /// <param name="viewportHeight">视口高度。/ Viewport height.</param>
    /// <param name="extentHeight">内容高度。/ Content extent height.</param>
    public static double ResolveProgress(double verticalOffset, double viewportHeight, double extentHeight)
    {
        var maximum = extentHeight - viewportHeight;
        if (!double.IsFinite(maximum) || maximum <= 0d)
        {
            return 0d;
        }

        var offset = double.IsFinite(verticalOffset) ? verticalOffset : 0d;
        return Math.Clamp(offset / maximum, 0d, 1d);
    }
}
