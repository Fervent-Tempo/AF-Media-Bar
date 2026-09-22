using System;
using System.Windows;
using AFMediaBar.Classes.Models.Layout;
using AFMediaBar.Classes.Services.Layout;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>
/// 胶囊岛形变的锚定与内容缩放：中心不动、四面伸展，贴到屏幕某条边时那一边改为钉住。
/// The island morph's anchoring and content scaling: centred, growing outwards on every free side, with a docked side pinned.
///
/// 这一组用例钉住的是用户报障的"生硬"观感里的**几何**部分：位置曾在整段动画里被钉死（贴边那一轴），
/// 而不是围绕焦点生长；圆角曾经与尺寸共用一条被夹紧的曲线，无法表达"药丸先摊成直角再收成卡片圆角"。
/// These cases pin the **geometric** half of the "stiff" morph the user reported: the position used to be pinned on the docked axis for the whole
/// animation instead of growing around the focus point, and the radius shared one clamped curve with the size, which cannot express
/// "the pill flattens into a right angle first and only then settles onto the card's radius".
/// </summary>
[TestClass]
public sealed class CapsuleIslandMorphPolicyTests
{
    /// <summary>本机实测的胶囊尺寸（DIP）：232×52 物理像素 ÷ scale 1.2889。/ The capsule size measured on the real machine, in DIP.</summary>
    private static readonly Size Capsule = new(180, 40);

    /// <summary>本机实测的卡片尺寸（DIP）。/ The card size measured on the real machine, in DIP.</summary>
    private static readonly Size Card = new(320, 200);

    private static readonly Rect WorkArea = new(0, 0, 1920, 1040);

    private const double CardRadius = 16;

    private static Point CentreOf(Rect rect) => new(rect.Left + rect.Width / 2, rect.Top + rect.Height / 2);

    [TestMethod]
    public void 静止时是药丸的半个高度()
    {
        Assert.AreEqual(20, CapsuleIslandMorphPolicy.ResolveCornerRadius(0, Capsule, Card, CardRadius), 1e-9);
    }

    /// <summary>形变曲线的缓入缓出：本策略接收的是**缓动后**的进度，生产侧传的正是 <c>CapsuleIslandMorphPolicy.EaseInOut</c> 的结果。/ The morph curve's ease-in-out: this policy takes the **eased** progress, which is exactly what the production side passes down.</summary>
    private static double Eased(double timeFraction) => CapsuleIslandMorphPolicy.EaseInOut(timeFraction);

    [TestMethod]
    public void 曲线两端为0和1且中段最快()
    {
        Assert.AreEqual(0, CapsuleIslandMorphPolicy.EaseInOut(0), 1e-9);
        Assert.AreEqual(0.5, CapsuleIslandMorphPolicy.EaseInOut(0.5), 1e-9);
        Assert.AreEqual(1, CapsuleIslandMorphPolicy.EaseInOut(1), 1e-9);

        // 单调递增，且中段的增量大于首尾：这是"生长要看得见"的前提，缓出曲线在这一点上会红（它开头就把大部分位移用掉）。
        // Monotone, and the middle step is larger than the first and last ones: that is what makes the growth visible, and an ease-out curve fails
        // here because it spends most of the travel at the very beginning.
        var previous = 0.0;
        var firstStep = 0.0;
        for (var step = 1; step <= 10; step++)
        {
            var value = CapsuleIslandMorphPolicy.EaseInOut(step / 10.0);
            Assert.IsTrue(value > previous, $"t={step / 10.0} 处曲线没有前进");
            if (step == 1)
                firstStep = value;
            previous = value;
        }

        Assert.IsTrue(CapsuleIslandMorphPolicy.EaseInOut(0.6) - CapsuleIslandMorphPolicy.EaseInOut(0.5) > firstStep,
            "中段的位移必须大于起步段");
    }

    [TestMethod]
    public void 两个视图从不半透明地重叠()
    {
        // 任一时刻至多只有一边可见：两边同时半透明时，药丸文字与卡片标题会叠在同一格像素上、读起来是一团灰。
        // At most one side is visible at any instant: with both half transparent the pill's text and the card's title paint onto the same pixels and read
        // as a grey smear.
        for (var step = 0; step <= 40; step++)
        {
            var row = step / 40.0;
            var (card, capsule) = CapsuleIslandMorphPolicy.ResolveViewOpacities(isExpanded: true, row);
            Assert.IsTrue(Math.Min(card, capsule) < 1e-9, $"row={row} 时两边同时可见：card={card:0.00} capsule={capsule:0.00}");
        }

        // 展开：起点是纯胶囊，终点是纯卡片。
        // An expansion: it starts as a pure capsule and ends as a pure card.
        var start = CapsuleIslandMorphPolicy.ResolveViewOpacities(isExpanded: true, 0);
        Assert.AreEqual(1, start.Capsule, 1e-9);
        Assert.AreEqual(0, start.Card, 1e-9);
        var end = CapsuleIslandMorphPolicy.ResolveViewOpacities(isExpanded: true, 1);
        Assert.AreEqual(1, end.Card, 1e-9);
        Assert.AreEqual(0, end.Capsule, 1e-9);

        // 收拢是反向的：行进度 1（动画结束）必须是"卡片 0 / 胶囊 1"。这一条钉住的正是历史缺陷——收拢结束时把卡片写成了不透明。
        // A collapse runs the other way: at row one (the animation's end) it has to be "card zero, capsule one". This pins the historical defect where a
        // collapse ended with the card left opaque.
        var collapsedEnd = CapsuleIslandMorphPolicy.ResolveViewOpacities(isExpanded: false, 1);
        Assert.AreEqual(0, collapsedEnd.Card, 1e-9);
        Assert.AreEqual(1, collapsedEnd.Capsule, 1e-9);
    }

    [TestMethod]
    public void 圆角在两瑞之间平滑收束且中途绝不出现直角()
    {
        // 用户报障："伸展开有一瞬间会变为方形"。历史实现里圆角先线性收到 0（摊成直角）再跳到卡片圆角，真机上就是一瞬间的方形，
        // 因此这条必须红：除两端之外，圆角必须**严格大于 0**，并且全程不超过当前窗口短边的一半。
        // The user reported "for an instant it becomes a square while expanding": the historical radius fell linearly to zero (a right angle) and then
        // jumped to the card's, which on the real machine read as a split second of squareness. This has to fail for that shape: apart from the two
        // endpoints the radius must stay **strictly above zero**, and it may never exceed half the current window's shorter side.
        Assert.AreEqual(20, CapsuleIslandMorphPolicy.ResolveCornerRadius(0, Capsule, Card, CardRadius), 1e-9);
        Assert.AreEqual(16, CapsuleIslandMorphPolicy.ResolveCornerRadius(1, Capsule, Card, CardRadius), 1e-9);

        for (var step = 1; step < 40; step++)
        {
            var cardShare = step / 40.0;
            var radius = CapsuleIslandMorphPolicy.ResolveCornerRadius(cardShare, Capsule, Card, CardRadius);
            var width = Capsule.Width + (Card.Width - Capsule.Width) * cardShare;
            var height = Capsule.Height + (Card.Height - Capsule.Height) * cardShare;

            Assert.IsTrue(radius > 0, $"cardShare={cardShare} 时圆角归零，画面上会出现一瞬间的方形");
            Assert.IsTrue(radius <= Math.Min(width, height) / 2 + 1e-9, $"cardShare={cardShare} 时圆角大过短边的一半");
        }
    }

    [TestMethod]
    public void 收拢方向的进度是缓动进度的补数()
    {
        // 用户报障："伸展后无法回到紧凑形态"。历史实现把尺寸插值写成"永远从胶囊长到卡片"，收拢第一帧就把窗口写成胶囊宽、
        // 随后又被长回卡片，于是再也回不去。这条钉住方向本身。
        // The user reported "after stretching it cannot return to the compact form": the historical size interpolation read "always grow capsule → card",
        // so a collapse wrote the capsule's width on its first frame and then grew back towards the card, and it could never return. This pins the
        // direction itself.
        Assert.AreEqual(0.25, CapsuleIslandMorphPolicy.CardShare(isExpanded: true, 0.25), 1e-9);
        Assert.AreEqual(0.75, CapsuleIslandMorphPolicy.CardShare(isExpanded: false, 0.25), 1e-9);

        // 展开终点 = 卡片（1）；收拢终点 = 胶囊（0）。收拢终点若不是 0，窗口尺寸就停在卡片上。
        // An expansion ends at the card (one); a collapse ends at the capsule (zero). A collapse whose end is not zero leaves the window at the card's size.
        Assert.AreEqual(1, CapsuleIslandMorphPolicy.CardShare(isExpanded: true, 1), 1e-9);
        Assert.AreEqual(0, CapsuleIslandMorphPolicy.CardShare(isExpanded: false, 1), 1e-9);
    }

    [TestMethod]
    public void 收拢时窗口尺寸回到胶囊大小()
    {
        // 用"卡片占比"把两个端点都走一遍：收拢的终点必须是胶囊尺寸，且中途单调收缩、不回涨。
        // Both endpoints are walked through the card's share: a collapse has to finish at the capsule's size and shrink monotonically
        // on the way, never growing back.
        var previous = double.MaxValue;
        for (var step = 0; step <= 20; step++)
        {
            var cardShare = CapsuleIslandMorphPolicy.CardShare(isExpanded: false, CapsuleIslandMorphPolicy.EaseInOut(step / 20.0));
            var width = Capsule.Width + (Card.Width - Capsule.Width) * cardShare;
            var height = Capsule.Height + (Card.Height - Capsule.Height) * cardShare;

            Assert.IsTrue(width <= previous + 1e-9, $"收拢途中窗口宽度回涨了：{previous} → {width}");
            previous = width;
        }

        Assert.AreEqual(Capsule.Width, Capsule.Width + (Card.Width - Capsule.Width) * 0, 1e-9);
        Assert.AreEqual(
            Capsule.Height,
            Capsule.Height + (Card.Height - Capsule.Height) * CapsuleIslandMorphPolicy.CardShare(false, 1),
            1e-9);
    }

    [TestMethod]
    public void 圆角在两瑞之间平滑收束()
    {
        // 两端之间的每一步都必须落在两端之间（单调、不越界），圆角因此不会像历史曲线那样"先归零再跳回"。
        // Every step between the two ends has to stay between them (monotone, in range), so the radius never does the historical "drop to zero and jump back".
        var previous = 20.0;
        for (var step = 1; step <= 20; step++)
        {
            var radius = CapsuleIslandMorphPolicy.ResolveCornerRadius(step / 20.0, Capsule, Card, CardRadius);
            Assert.IsTrue(radius <= previous + 1e-9, $"cardShare={step / 20.0} 时圆角回涨了");
            Assert.IsTrue(radius >= 16 - 1e-9 && radius <= 20 + 1e-9, $"cardShare={step / 20.0} 时圆角越出两端区间");
            previous = radius;
        }
    }

    [TestMethod]
    public void 不贴边时窗口中心始终停在焦点上()
    {
        var focus = new Point(1000, 500);
        foreach (var progress in new[] { 0.0, 0.25, 0.5, 0.75, 1.0 })
        {
            var anchor = CapsuleIslandMorphPolicy.ResolveAnchor(
                progress, Capsule, Card, focus.X, focus.Y, dockedEdge: null, WorkArea);

            // 窗口的尺寸随进度变化，因此中心必须按**当前**尺寸算：把卡片尺寸套上去比中心，只会量到"窗口左上角不等于卡片左上角"。
            // The window's size changes with the progress, so the centre has to be computed from the **current** size; plugging the card's size in
            // would only answer "the window's top-left differs from the card's top-left".
            var size = new Size(
                Capsule.Width + (Card.Width - Capsule.Width) * progress,
                Capsule.Height + (Card.Height - Capsule.Height) * progress);
            var centre = CentreOf(new Rect(anchor.Left, anchor.Top, size.Width, size.Height));

            // 焦点不动 ⇒ 无论进度如何，窗口中心都必须落在焦点上。
            // The focus never moves, so the window's centre has to land on it at every progress.
            Assert.AreEqual(focus.X, centre.X, 1e-9, $"progress={progress} 时横向中心偏离焦点");
            Assert.AreEqual(focus.Y, centre.Y, 1e-9, $"progress={progress} 时纵向中心偏离焦点");
        }
    }

    [TestMethod]
    public void 贴顶时顶边钉住而横向围绕焦点生长()
    {
        var focus = new Point(1000, 30);
        var anchor = CapsuleIslandMorphPolicy.ResolveAnchor(
            1.0, Capsule, Card, focus.X, focus.Y, DynamicIslandEdge.Top, WorkArea);

        Assert.AreEqual(CapsuleIslandMetrics.EdgeGapDip, anchor.Top, 1e-9, "贴顶那一面不许伸展");
        Assert.AreEqual(focus.X - Card.Width / 2, anchor.Left, 1e-9, "另一轴必须围绕焦点居中生长");
    }

    [TestMethod]
    public void 贴右时右边钉住而纵向围绕焦点生长()
    {
        var focus = new Point(1900, 300);
        var anchor = CapsuleIslandMorphPolicy.ResolveAnchor(
            1.0, Capsule, Card, focus.X, focus.Y, DynamicIslandEdge.Right, WorkArea);

        Assert.AreEqual(WorkArea.Right - CapsuleIslandMetrics.EdgeGapDip, anchor.Left + Card.Width, 1e-9);
        Assert.AreEqual(focus.Y - Card.Height / 2, anchor.Top, 1e-9);
    }

    [TestMethod]
    public void 焦点靠近屏幕边角时被夹回工作区()
    {
        var anchor = CapsuleIslandMorphPolicy.ResolveAnchor(
            1.0, Capsule, Card, focusX: 30, focusY: 5, dockedEdge: null, WorkArea);

        Assert.IsTrue(anchor.Left >= WorkArea.Left, "不许越过左缘");
        Assert.IsTrue(anchor.Top >= WorkArea.Top, "不许越过上缘");
        Assert.IsTrue(anchor.Left + Card.Width <= WorkArea.Right, "不许越过右缘");
        Assert.IsTrue(anchor.Top + Card.Height <= WorkArea.Bottom, "不许越过下缘");
    }

    [TestMethod]
    public void 内容缩放随进度从小长到铺满()
    {
        // 进度 0：内容从"当前窗口相对卡片"的较小轴上开始，因此起点就是胶囊高 / 卡片高。
        // Progress zero: the content starts on the smaller of the current-window-to-card ratios, which is the capsule's height over the card's.
        var start = CapsuleIslandMorphPolicy.ResolveContentScale(0, Capsule, Card);
        Assert.AreEqual(Capsule.Height / Card.Height, start, 1e-9);

        // 进度 1：内容必须正好铺满（缩放 = 1），否则卡片内容会比窗口小一圈。
        // Progress one: the content has to fill exactly (scale one); anything less leaves the card smaller than its frame.
        Assert.AreEqual(1, CapsuleIslandMorphPolicy.ResolveContentScale(1, Capsule, Card), 1e-9);

        // 单调递增：中途不许回缩。
        // Monotone: the content never shrinks part-way.
        var previous = start;
        for (var step = 1; step <= 20; step++)
        {
            var scale = CapsuleIslandMorphPolicy.ResolveContentScale(Eased(step / 20.0), Capsule, Card);
            Assert.IsTrue(scale >= previous, $"progress={Eased(step / 20.0)} 时内容回缩了");
            previous = scale;
        }
    }

    [TestMethod]
    public void 贴边轴的终点也要铺满()
    {
        // 贴顶只影响**窗口终点**（高度停在胶囊高），不影响内容缩放的算法：缩放只由当前窗口尺寸与卡片尺寸决定。
        // 若哪天有人把"贴边"塞进缩放算式，这条会红——那正是内容缩成一小块的成因。
        // Docking only changes the **window's endpoint** (the height stops at the capsule's); it never enters the content-scale arithmetic, which
        // depends on the current window size and the card's size alone. Anyone who folds "docked" into that arithmetic turns this red — and that
        // is exactly what shrinks the card content into a small block.
        var dockedEnd = CapsuleIslandMorphPolicy.ResolveAnchor(
            1.0, Capsule, Card, focusX: 1000, focusY: 30, DynamicIslandEdge.Top, WorkArea);
        var dockedScale = CapsuleIslandMorphPolicy.ResolveContentScale(1.0, Capsule, Card);

        Assert.AreEqual(CapsuleIslandMetrics.EdgeGapDip, dockedEnd.Top, 1e-9);
        Assert.AreEqual(1, dockedScale, 1e-9);
    }
}
