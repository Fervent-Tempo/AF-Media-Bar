using System;
using System.Windows;

namespace AFMediaBar.Classes.Services.Layout;

/// <summary>
/// 灵动岛随屏幕等比例缩放的系数策略：以 1920×1080 为基准，按工作区较短边求比例并夹到 [0.85, 1.75]。
/// Capsule-island screen-proportional scale policy: a 1920×1080 baseline, the shorter work-area side's ratio, clamped into
/// [0.85, 1.75].
///
/// 取**较短边**的比例而不是面积或宽度：一边先成为瓶颈时，按面积算出来的系数会把另一边的内容压出屏幕。
/// The **shorter** side's ratio rather than area or width: once one side becomes the bottleneck, an area-derived factor
/// squeezes the content out of the other side.
/// </summary>
public static class CapsuleIslandScalePolicy
{
    /// <summary>缩放基准工作区宽度（DIP）。/ Baseline work-area width for scaling, in DIP.</summary>
    public const double BaselineWidth = 1920;

    /// <summary>缩放基准工作区高度（DIP）。/ Baseline work-area height for scaling, in DIP.</summary>
    public const double BaselineHeight = 1080;

    /// <summary>缩放系数下限：小屏上岛也要留得下封面槽与标题。/ Lower scale bound so the capsule still fits its slot and title on small screens.</summary>
    public const double MinimumScale = 0.85;

    /// <summary>缩放系数上限：4K 上不再继续放大，避免岛占掉过多桌面。/ Upper scale bound so the island does not eat the desktop on 4K.</summary>
    public const double MaximumScale = 1.75;

    /// <summary>
    /// 工作区 → 灵动岛缩放系数：按 1920×1080 基准取较小边比，夹到 [0.85, 1.75]；
    /// 工作区宽/高非正或非有限时返回 1.0。
    /// Work area to island scale factor: the smaller of the two side ratios against the 1920×1080 baseline, clamped into
    /// [0.85, 1.75]; a non-positive or non-finite work area returns 1.0.
    /// </summary>
    /// <param name="workArea">灵动岛所在显示器的工作区（DIP）。/ Work area of the island's monitor, in DIP.</param>
    public static double ResolveScale(Rect workArea)
    {
        if (!double.IsFinite(workArea.Width) || !double.IsFinite(workArea.Height) ||
            workArea.Width <= 0 || workArea.Height <= 0)
        {
            return 1.0;
        }

        var ratio = Math.Min(workArea.Width / BaselineWidth, workArea.Height / BaselineHeight);
        return Math.Clamp(ratio, MinimumScale, MaximumScale);
    }
}
