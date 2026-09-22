using System.Windows;
using AFMediaBar.Classes.Models.Layout;

namespace AFMediaBar.Classes.Services.Layout;

/// <summary>
/// 胶囊岛（Capsule Island）形态常量：胶囊/卡片目标尺寸、圆角半径与边缘吸附参数，单位一律 DIP。
/// Capsule-island shape constants: capsule/card target sizes, corner radii, and edge-snap parameters, all in DIP.
/// </summary>
public static class CapsuleIslandMetrics
{
    /// <summary>胶囊态基准尺寸（缩放系数 1.0）= iOS 药丸形。/ Baseline collapsed capsule size at scale 1.0 — the iOS pill.</summary>
    /// <remarks>
    /// 宽度 180 = 原 176 + <see cref="CapsuleLeadInsetDip"/>：内容左侧要留出封面与药丸左弧之间的间距，
    /// 而这 4 DIP 如果从标题那一列里扣，刚好溢出的歌名会被挤进跑马灯（或在其引导期内被右缘切掉一点字）。
    /// 药丸本身加宽同样的量，标题可用宽度因此分毫未减，间距不是从歌名那里拿的。
    /// Width 180 = the original 176 plus <see cref="CapsuleLeadInsetDip"/>: the content needs a gap between the cover and the
    /// pill's left arc, and taking those 4 DIP out of the title column would push a just-overflowing title into the marquee (or
    /// clip a sliver of it during the lead-in). Widening the pill by the same amount keeps every DIP of the title's width, so the
    /// spacing is not taken from the song title.
    /// </remarks>
    public static readonly Size CapsuleSize = new(180, 40);

    /// <summary>
    /// 胶囊内容与药丸左弧之间的左内缩（基准 DIP，随缩放系数走）。
    /// Left inset between the capsule content and the pill's left arc, in baseline DIP, scaled with the island.
    /// </summary>
    /// <remarks>
    /// 药丸两端是完全圆头（半径 = 高度一半），封面若与左弧相切会读成"贴着边"；留一点间距后封面才在药丸里坐得住。
    /// <see cref="CapsuleSize"/> 的宽度已包含这一项，因此加它不会挤窄标题。
    /// The pill's ends are fully rounded (radius = half the height), and a cover tangent to the left arc reads as "stuck to the
    /// edge"; a little breathing room makes it sit inside the pill. <see cref="CapsuleSize"/>'s width already carries this term,
    /// so adding it never narrows the title.
    /// </remarks>
    public const double CapsuleLeadInsetDip = 4;

    /// <summary>
    /// 卡片态基准尺寸（缩放系数 1.0；118 大封面 + 标题/作者 + 当前歌词 + 进度 + 控制，上下各 12 DIP 边距）。
    /// 高度必须容得下这四行：212 时进度条与传输控制会被根部的 ClipToBounds 裁掉，200 是靠行距与底部对齐挤出来的余量。
    /// Baseline expanded card size at scale 1.0 (118 artwork + title/artist + current lyric + seek + controls, 12 DIP of
    /// margin top and bottom). The height has to hold all four rows: at 212 the seek row and the transport controls were
    /// clipped by the root's ClipToBounds, and 200 is the slack the row spacing and bottom alignment buy.
    /// </summary>
    public static readonly Size CardSize = new(320, 200);

    /// <summary>
    /// 胶囊态基准圆角半径（= 高度一半 20，形成完全圆头），只说明系数 1.0 时的取值；
    /// 实际半径一律由 <see cref="ResolveCapsuleCornerRadius"/> 按缩放后高度的一半解析，**不受设置里的圆角值影响**。
    /// Baseline capsule corner radius (half of the 40-DIP height, fully rounded ends), describing the scale-1.0 value only;
    /// the actual radius always comes from <see cref="ResolveCapsuleCornerRadius"/> as half the scaled height and is
    /// **never** taken from the configured corner radius.
    /// </summary>
    public const double CapsuleCornerRadius = 20;

    /// <summary>卡片态基准圆角半径（iOS 观感）。/ Baseline card corner radius (the iOS look).</summary>
    public const double CardCornerRadius = 24;

    /// <summary>
    /// 缩放系数 → 该形态尺寸：系数非有限或 ≤0 时按 1.0（基准尺寸）。
    /// Scale factor to one form's size; a non-finite or non-positive factor counts as 1.0 (the baseline size).
    /// </summary>
    /// <param name="scale">来自 <see cref="CapsuleIslandScalePolicy.ResolveScale"/> 的系数。/ Factor from <see cref="CapsuleIslandScalePolicy.ResolveScale"/>.</param>
    public static Size ResolveCapsuleSize(double scale)
    {
        var s = Normalize(scale);
        return new Size(CapsuleSize.Width * s, CapsuleSize.Height * s);
    }

    /// <summary>缩放系数 → 卡片态尺寸（与 <see cref="ResolveCapsuleSize"/> 同一套系数规则）。/ Scale factor to the card size, with the same factor rules as <see cref="ResolveCapsuleSize"/>.</summary>
    public static Size ResolveCardSize(double scale)
    {
        var s = Normalize(scale);
        return new Size(CardSize.Width * s, CardSize.Height * s);
    }

    /// <summary>缩放系数 → 胶囊态圆角 = 缩放后高度的一半（"两端半圆"这条不变量）。/ Scale factor to the capsule radius = half of the scaled height (the fully-rounded-ends invariant).</summary>
    public static double ResolveCapsuleCornerRadius(double scale) => ResolveCapsuleSize(scale).Height / 2;

    /// <summary>把系数夹到合法范围：NaN/Infinity/非正数一律按 1.0。/ Normalises a factor: NaN, Infinity, and non-positive values all become 1.0.</summary>
    private static double Normalize(double scale) => double.IsFinite(scale) && scale > 0 ? scale : 1.0;

    /// <summary>拖拽结束后判定贴边的距离阈值（DIP）。/ Edge-snap detection threshold in DIP.</summary>
    public const double EdgeSnapThresholdDip = 24;

    /// <summary>贴边吸附后与屏幕工作区边缘的间隙（DIP）。/ Gap to the work-area edge after snapping, in DIP.</summary>
    public const double EdgeGapDip = 8;
}

/// <summary>
/// 圆角矩形命中测试：用于 WM_NCHITTEST 把圆角外的透明像素判定为穿透（HTTRANSPARENT）。
/// Rounded-rectangle hit test used by WM_NCHITTEST to pass clicks outside the rounded corners through.
/// </summary>
public static class RoundedRectHitTest
{
    /// <summary>
    /// 判断点 (x, y) 是否落在宽 width、高 height、圆角 radius 的圆角矩形内（含边界）。
    /// Returns whether point (x, y) lies inside the rounded rectangle of size width×height with corner radius, boundary inclusive.
    /// </summary>
    public static bool Contains(double width, double height, double radius, double x, double y)
    {
        if (x < 0 || y < 0 || x > width || y > height)
            return false;

        // 以距该点最近的"圆角矩形内核"点为圆心做圆形判定；内核点即把坐标夹进 [radius, size-radius]。
        // Treat the nearest point of the rounded rect's core (x/y clamped into [radius, size-radius]) as the circle center.
        var closestX = Math.Clamp(x, radius, width - radius);
        var closestY = Math.Clamp(y, radius, height - radius);
        var dx = x - closestX;
        var dy = y - closestY;
        return dx * dx + dy * dy <= radius * radius;
    }
}

/// <summary>
/// 胶囊态歌名跑马灯的位移计算：按速度线性累积并在周期末尾回绕。
/// Marquee offset arithmetic for the capsule title: accumulates linearly and wraps at the cycle end.
/// </summary>
public static class MarqueeOffsetCalculator
{
    /// <summary>
    /// 推进一个位移帧。
    /// Advances the offset by one frame.
    /// </summary>
    /// <param name="offset">当前位移（DIP）。/ Current offset in DIP.</param>
    /// <param name="speedDipPerSecond">滚动速度（DIP/秒）。/ Scroll speed in DIP per second.</param>
    /// <param name="elapsedMilliseconds">本帧毫秒数。/ Elapsed milliseconds for this frame.</param>
    /// <param name="cycleLength">周期长度（DIP）；非正数时不产生位移。/ Cycle length in DIP; non-positive values disable scrolling.</param>
    /// <returns>回绕到 [0, cycleLength) 的新位移。/ The new offset wrapped into [0, cycleLength).</returns>
    public static double Advance(double offset, double speedDipPerSecond, double elapsedMilliseconds, double cycleLength)
    {
        if (cycleLength <= 0)
            return 0;

        var next = offset + speedDipPerSecond * elapsedMilliseconds / 1000.0;
        return next - Math.Floor(next / cycleLength) * cycleLength;
    }
}

/// <summary>
/// 胶囊岛边缘吸附判定：拖拽结束后判断窗口是否贴到工作区某条边。
/// Capsule-island edge-snap detection: decides which work-area edge the window has been dropped against.
/// </summary>
public static class IslandEdgeSnap
{
    /// <summary>
    /// 判定窗口（左上角 topLeft、尺寸 size）是否贴近工作区 workArea 的某条边；距离不超过 thresholdDip 时返回该边，否则 null。
    /// Returns the work-area edge the window touches within thresholdDip, or null when it is not near any edge.
    /// </summary>
    public static DynamicIslandEdge? FindEdge(Rect workArea, Point topLeft, Size size, double thresholdDip)
    {
        var candidates = new (DynamicIslandEdge Edge, double Distance)[]
        {
            (DynamicIslandEdge.Top, Math.Abs(topLeft.Y - workArea.Top)),
            (DynamicIslandEdge.Right, Math.Abs(workArea.Right - (topLeft.X + size.Width))),
            (DynamicIslandEdge.Bottom, Math.Abs(workArea.Bottom - (topLeft.Y + size.Height))),
            (DynamicIslandEdge.Left, Math.Abs(topLeft.X - workArea.Left)),
        };

        var best = candidates.OrderBy(c => c.Distance).First();
        return best.Distance <= thresholdDip ? best.Edge : null;
    }
}
