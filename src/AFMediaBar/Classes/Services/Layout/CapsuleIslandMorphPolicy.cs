using System;
using AFMediaBar.Classes.Models.Layout;

namespace AFMediaBar.Classes.Services.Layout;

/// <summary>
/// 胶囊岛形变（胶囊 ↔ 卡片）的**锚定**与**内容缩放**：岛中心不动、四面伸展，贴到屏幕某条边时那一边改为钉住。
/// Anchoring and content scaling for the island's morph (capsule to card): the island stays centred and grows outwards on every free
/// side, while a side that is docked to a screen edge is pinned instead.
///
/// 为什么锚定要与贴边绑定：岛贴在屏顶时上方只剩几个 DIP，"向上生长"会被工作区夹回来、末帧再跳一下；
/// 因此贴边的那一轴由贴边侧钉住、另一轴围绕焦点居中生长，整段动画里只有一次位置求解、没有中途跳变。
/// Why anchoring is tied to docking: with the island on the screen's top edge there are only a few DIP above it, so "growing upwards" would be
/// clamped back by the work area and jump on the final frame. A docked axis is therefore pinned on the docked side and the other axis grows
/// centred on the focus point, which solves the position only once and leaves no mid-animation jump.
///
/// 与 <c>MediaBarSizeAnimationCalculator</c> 的关系：尺寸、圆角与位置三者每帧都由同一个**缓动后**的进度算出，
/// 所以边界与内容永远同步；本类只提供纯函数，窗口侧不保存第二套算术。
/// How it relates to <c>MediaBarSizeAnimationCalculator</c>: size, corner radius, and position are all resolved from one **eased** progress
/// per frame, so the frame and its content can never disagree; this class holds only the pure functions and the window keeps no second copy
/// of the arithmetic.
/// </summary>
public static class CapsuleIslandMorphPolicy
{
    /// <summary>
    /// 形变曲线的**缓入缓出**（三次）：与任务栏那次缓出不同，岛屿的形变必须"中段最快"。
    ///
    /// 缓出（<c>1-(1-t)³</c>）在时间刚过三分之一时就跑完了三分之二的位移，后面一路爬行；而卡片的淡入受"两边不许半透明重叠"的约束
    /// 只能发生在后半段，等它可见时内容已经长到八九成，于是"从小长到大"完全看不见、读起来就是"一下就送到了"。
    /// 缓入缓出把大部分位移挪到中段，与淡入的窗口重合，生长才真正可见。
    /// The morph curve's **ease-in-out** (cubic): unlike the taskbar's ease-out, the island's morph has to be fastest in the middle.
    ///
    /// An ease-out (1-(1-t)³) has already covered two thirds of the distance one third of the way in and then crawls; the card's fade-in, held to the
    /// second half by the "never half transparent on top of each other" rule, only becomes visible once the content is nearly grown — so
    /// "grows out of the pill" is invisible and reads as the target being snapped into place. Ease-in-out moves most of the travel into the middle,
    /// which is exactly where the fade-in lives, and the growth finally shows.
    /// </summary>
    /// <param name="timeFraction">线性时间比例（0–1）。/ Linear time fraction, 0–1.</param>
    public static double EaseInOut(double timeFraction)
    {
        var clamped = Math.Clamp(double.IsFinite(timeFraction) ? timeFraction : 0, 0, 1);
        return clamped < 0.5
            ? 4 * clamped * clamped * clamped
            : 1 - Math.Pow(-2 * clamped + 2, 3) / 2;
    }

    /// <summary>
    /// 一帧的位置锚定结果。
    /// One frame's anchoring result.
    /// </summary>
    /// <param name="Left">窗口左上角 X（DIP）。/ Window left in DIP.</param>
    /// <param name="Top">窗口左上角 Y（DIP）。/ Window top in DIP.</param>
    public readonly record struct AnchorResult(double Left, double Top);

    /// <summary>
    /// 把"动画走到哪了"换算成**卡片占比**：展开时就是缓动后的进度，收拢时取补数。
    ///
    /// 这是尺寸、圆角、位置与内容缩放唯一的进度入口。历史上这一层缺失时，尺寸插值被写成"永远从胶囊长到卡片"，
    /// 于是收拢的第一帧就把窗口写成胶囊宽、接着又被后续帧长回卡片，岛再也回不到紧凑形态——把方向收进一个可测的函数就不会再犯。
    /// Converts "how far the animation has come" into the **progress towards the target form**: the eased progress while expanding, its complement
    /// while collapsing.
    ///
    /// This is the single entry point for the size, radius, position, and content-scale curves. While it was missing, the size interpolation read
    /// "always grow from the capsule to the card", so a collapse wrote the capsule's width on its very first frame, then grew back towards the card
    /// on the following ones, and the island could never return to its compact form — folding the direction into one testable function prevents that.
    /// </summary>
    /// <param name="isExpanded">目标形态（true = 展开到卡片）。/ The target form (true while expanding into the card).</param>
    /// <param name="eased">缓动后的行进度（0 = 动画起点，1 = 动画终点）。/ The eased row progress (zero is the animation's start, one its end).</param>
    public static double CardShare(bool isExpanded, double eased)
    {
        var clamped = Math.Clamp(double.IsFinite(eased) ? eased : 0, 0, 1);
        return isExpanded ? clamped : 1 - clamped;
    }

    /// <summary>
    /// 按当前进度解析窗口位置：中心停在 <paramref name="focusX"/>/<paramref name="focusY"/>，贴边轴改为钉在贴边侧。
    /// Resolves the window position at the current progress: its centre rests on the focus point, while a docked axis is pinned on the docked
    /// side instead.
    /// </summary>
    /// <param name="CardShare">卡片占比（0 = 胶囊，1 = 卡片）；收拢态必须传 <see cref="CardShare"/> 的结果，不能直接传缓动进度。/ the share of the card (zero is the capsule, one the card); a collapse has to pass the output of <see cref="CardShare"/> rather than the eased progress itself.</param>
    /// <param name="capsuleSize">胶囊尺寸（进度 0 的窗口尺寸）。/ Capsule size, the window size at progress zero.</param>
    /// <param name="cardSize">卡片尺寸（进度 1 的窗口尺寸）。/ Card size, the window size at progress one.</param>
    /// <param name="focusX">焦点 X（展开那一刻的岛屿中心，工作区坐标）。/ Focus X, the island's centre when the expansion started, in work-area coordinates.</param>
    /// <param name="focusY">焦点 Y（展开那一刻的岛屿中心，工作区坐标）。/ Focus Y, the island's centre when the expansion started, in work-area coordinates.</param>
    /// <param name="dockedEdge">贴边的那条边；null 表示不贴边（四面都自由生长）。/ The docked edge, or null when nothing is docked and every side grows freely.</param>
    /// <param name="workArea">工作区（DIP），窗口最终夹进它。/ The work area in DIP the window is finally clamped into.</param>
    public static AnchorResult ResolveAnchor(
        double CardShare,
        System.Windows.Size capsuleSize,
        System.Windows.Size cardSize,
        double focusX,
        double focusY,
        DynamicIslandEdge? dockedEdge,
        System.Windows.Rect workArea)
    {
        var eased = Math.Clamp(double.IsFinite(CardShare) ? CardShare : 0, 0, 1);
        var width = Lerp(capsuleSize.Width, cardSize.Width, eased);
        var height = Lerp(capsuleSize.Height, cardSize.Height, eased);

        var left = eased <= 0
            ? focusX - capsuleSize.Width / 2
            : focusX - width / 2;
        var top = eased <= 0
            ? focusY - capsuleSize.Height / 2
            : focusY - height / 2;

        switch (dockedEdge)
        {
            // 贴顶：上边钉住（那一面不伸展），横向围绕焦点居中生长。
            // Docked to the top: the top edge is pinned (that side does not grow) and the width grows centred on the focus point.
            case DynamicIslandEdge.Top:
                left = focusX - width / 2;
                top = workArea.Top + EdgeGapDip;
                break;

            // 贴底：下边钉住。
            // Docked to the bottom: the bottom edge is pinned.
            case DynamicIslandEdge.Bottom:
                left = focusX - width / 2;
                top = workArea.Bottom - EdgeGapDip - height;
                break;

            // 贴左：左边钉住，纵向围绕焦点居中生长。
            // Docked to the left: the left edge is pinned and the height grows centred on the focus point.
            case DynamicIslandEdge.Left:
                left = workArea.Left + EdgeGapDip;
                top = focusY - height / 2;
                break;

            // 贴右：右边钉住。
            // Docked to the right: the right edge is pinned.
            case DynamicIslandEdge.Right:
                left = workArea.Right - EdgeGapDip - width;
                top = focusY - height / 2;
                break;
        }

        // 最后统一夹进工作区：不贴边时焦点可能靠近屏幕边角，夹取让它整条动画都留在可见区内。
        // Finally clamp into the work area: an undocked focus point can sit near a corner, and clamping keeps the whole animation on screen.
        var clampedLeft = Clamp(left, workArea.Left, workArea.Right - width);
        var clampedTop = Clamp(top, workArea.Top, workArea.Bottom - height);
        return new AnchorResult(clampedLeft, clampedTop);
    }

    /// <summary>
    /// 解析当前进度下的岛屿圆角：从"胶囊的半个高度"（药丸）**平滑**收到卡片的圆角，并按当前窗口短边夹紧。
    ///
    /// **中途绝不能出现直角。** 曾经这里做过"药丸 → 直角 → 卡片圆角"的三段曲线，想表达"先摊平再收圆"，真机上那一段直角读起来
    /// 是一瞬间变成方形，用户直接报成了缺陷。圆角现在只做一件事：在两端之间平滑插值。
    /// Resolves the surface's corner radius at the current progress: it settles **smoothly** from the capsule's half height (the pill) onto the
    /// card's radius, clamped to the current window's shorter side.
    ///
    /// **It must never become a right angle on the way.** This used to run a three-stage "pill, then a right angle, then the card's radius" curve in
    /// an attempt to express "flatten first and round off afterwards"; on the real machine that right-angle stretch read as a split second of
    /// squareness and the user reported it as a defect. The radius now does exactly one thing: interpolate smoothly between its two ends.
    /// </summary>
    /// <param name="CardShare">卡片占比（0 = 胶囊，1 = 卡片）。/ the share of the card (zero is the capsule, one the card).</param>
    /// <param name="capsuleSize">胶囊尺寸。/ Capsule size.</param>
    /// <param name="cardSize">卡片尺寸。/ Card size.</param>
    /// <param name="cardCornerRadius">设置里的卡片圆角（非法或非正时按 0）。/ The card's configured corner radius, treated as zero when invalid or non-positive.</param>
    public static double ResolveCornerRadius(
        double CardShare,
        System.Windows.Size capsuleSize,
        System.Windows.Size cardSize,
        double cardCornerRadius)
    {
        var eased = Math.Clamp(double.IsFinite(CardShare) ? CardShare : 0, 0, 1);
        var width = Lerp(capsuleSize.Width, cardSize.Width, eased);
        var height = Lerp(capsuleSize.Height, cardSize.Height, eased);
        var shortSide = Math.Min(width, height);
        var limit = double.IsFinite(shortSide) && shortSide > 0 ? shortSide / 2 : 0;

        var pillRadius = capsuleSize.Height / 2;
        var cardRadius = double.IsFinite(cardCornerRadius) && cardCornerRadius > 0 ? cardCornerRadius : 0;

        // 两端都必须被当前短边夹紧：动画中途窗口还很小（药丸高度只有 40），照抄卡片圆角会变成两端圆角的胶囊。
        // Both ends are clamped by the current shorter side: mid-animation the window is still small (the pill is about forty tall), and copying the
        // card's radius would make a two-capped pill.
        return Math.Clamp(Lerp(pillRadius, cardRadius, eased), 0, limit);
    }

    /// <summary>
    /// 解析卡片内容在当前进度下要缩放到多少：1 表示内容按最终尺寸铺满窗口，小于 1 表示它正从小长出来。
    ///
    /// 取"当前窗口尺寸 ÷ 卡片目标尺寸"的**两轴较小者**，不按哪一轴贴边分情况：贴边的端点本来就是胶囊尺寸，
    /// 两轴比例已经自然地把"那一面不伸展"算了进去（贴顶时高度终点只是胶囊高，比值即 0.2），
    /// 而终点两轴都是 1——内容因此无论贴不贴边都恰好铺满，不会缩成比窗口小一圈的一块。
    /// How far the card's content is scaled at the current progress: one means it fills the window at its final size, and less than one means it is
    /// still growing out of the pill.
    ///
    /// It is the **smaller of the two ratios** between the current window size and the card's target size, with no per-axis special case for
    /// docking: a docked endpoint already *is* the capsule's size, so the ratios account for "that side never grows" on their own (top-docked, the
    /// height only ever reaches the capsule's, which is the 0.2 ratio), and at the end both are one — so the content fills the window exactly
    /// whether or not anything is docked, instead of ending up smaller than its frame.
    /// </summary>
    /// <param name="CardShare">卡片占比（0 = 胶囊，1 = 卡片）。/ the share of the card (zero is the capsule, one the card).</param>
    /// <param name="capsuleSize">胶囊尺寸。/ Capsule size.</param>
    /// <param name="cardSize">卡片尺寸。/ Card size.</param>
    public static double ResolveContentScale(double CardShare, System.Windows.Size capsuleSize, System.Windows.Size cardSize)
    {
        var eased = Math.Clamp(double.IsFinite(CardShare) ? CardShare : 0, 0, 1);
        if (cardSize.Width <= 0 || cardSize.Height <= 0)
            return 0;

        // 进度 0 不是"缩放 0"：内容此刻仍然要按胶囊窗口显示，起点就是"胶囊 ÷ 卡片"的较小轴（否则药丸里的卡片内容会整块消失）。
        // Progress zero is not "scale zero": the content is still shown inside the pill window, and its starting point is the smaller of the
        // capsule-to-card ratios (otherwise the card's content would simply vanish inside the pill).
        if (eased <= 0)
            return Math.Min(capsuleSize.Width / cardSize.Width, capsuleSize.Height / cardSize.Height);

        var width = Lerp(capsuleSize.Width, cardSize.Width, eased);
        var height = Lerp(capsuleSize.Height, cardSize.Height, eased);
        return Math.Min(width / cardSize.Width, height / cardSize.Height);
    }

    /// <summary>
    /// 内容不透明度在第几成**行进度**处开始变化、又到哪里结束：前半段只有胶囊渐隐（卡片一直全透明），后半段才轮到卡片渐显。
    /// Where the content opacity starts changing along the **row progress**, and where it stops: the first half fades the capsule out alone (the card
    /// stays fully transparent), and only the second half brings the card in.
    ///
    /// 为什么必须错开：两边同时交叉淡化时，中间有整段时间两边都是半透明，药丸文字与卡片标题叠在同一格像素上、读起来是一团灰，
    /// 这正是用户报的"生硬"里最直观的一半。错开之后任一时刻只有一边在可见范围内，过渡读起来是"旧的让位、新的登场"。
    /// Why they have to be staggered: a symmetric cross-fade keeps both sides half transparent for a whole stretch in the middle, with the pill's text
    /// and the card's title painted onto the same pixels and reading as a grey smear — the most visible half of the "stiff" morph the user reported.
    /// Staggered, only one side is ever inside the visible range, and the transition reads as the old making way for the new.
    /// </summary>
    public const double CapsuleFadeEnd = 0.45;

    /// <summary>卡片开始渐显的行进度；它与 <see cref="CapsuleFadeEnd"/> 对齐，因此两边完全不重叠，而此刻缓动后的几何才走到约 36%，内容仍有可见的生长空间。/ The row progress at which the card starts fading in; it lines up with <see cref="CapsuleFadeEnd"/>, so the two never overlap while the eased geometry is only about 36% of the way, leaving visible room for the content to grow.</summary>
    public const double CardFadeStart = 0.45;

    /// <summary>
    /// 按**行进度**解析两个视图的不透明度：胶囊在前半段渐隐到 0，卡片在后半段渐显到 1，两边从不半透明地重叠。
    /// Resolves both views' opacity from the **row progress**: the capsule fades out over the first half and the card fades in over the second, so the
    /// two are never half transparent on top of each other.
    /// </summary>
    /// <param name="isExpanded">目标形态（true = 展开到卡片）。/ The target form, true while expanding into the card.</param>
    /// <param name="rowProgress">未缓动的行进度（0–1）。/ The un-eased row progress, 0–1.</param>
    public static (double Card, double Capsule) ResolveViewOpacities(bool isExpanded, double rowProgress)
    {
        var row = Math.Clamp(double.IsFinite(rowProgress) ? rowProgress : 0, 0, 1);

        // 收拢是展开的逆过程：把行进度翻过来，两条曲线因此共用一套常量，不会出现"展开好看、收起糊成一片"。
        // A collapse is the expansion reversed: the row progress is flipped, which lets both share one set of constants instead of expanding smoothly
        // and collapsing into a smear.
        var towardsCard = isExpanded ? row : 1 - row;
        var capsule = Clamp01((CapsuleFadeEnd - towardsCard) / CapsuleFadeEnd);
        var card = Clamp01((towardsCard - CardFadeStart) / (1 - CardFadeStart));
        return (card, capsule);
    }

    /// <summary>贴边时窗口距离工作区边缘留出的空隙（DIP），与窗口落位用的是同一个常量。/ The gap kept between the window and the work-area edge while docked, the same constant the landing uses.</summary>
    private static double EdgeGapDip => CapsuleIslandMetrics.EdgeGapDip;

    private static double Clamp01(double value) => Math.Clamp(value, 0, 1);

    private static double Lerp(double start, double target, double progress) => start + (target - start) * progress;

    private static double Clamp(double value, double minimum, double maximum) =>
        maximum < minimum ? minimum : Math.Clamp(value, minimum, maximum);
}
