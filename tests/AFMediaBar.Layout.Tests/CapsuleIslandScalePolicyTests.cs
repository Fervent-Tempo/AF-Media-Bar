using System.Windows;
using AFMediaBar.Classes.Services.Layout;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>
/// 灵动岛随屏幕等比例缩放的纯逻辑测试：工作区 → 缩放系数，以及胶囊/卡片尺寸与圆角在系数下的取值。
/// Pure-logic tests for the island's screen-proportional scaling: work area to scale factor, plus the capsule/card sizes
/// and corner radii that factor produces.
/// </summary>
[TestClass]
public sealed class CapsuleIslandScalePolicyTests
{
    /// <summary>本机实测工作区（2560×1440 显示器、扣除任务栏后 1392 高）。/ The work area measured on this machine.</summary>
    private const double MeasuredScale = 1392.0 / 1080.0;

    [TestMethod]
    public void ResolveScale_基准工作区为一点零()
    {
        Assert.AreEqual(1.0, CapsuleIslandScalePolicy.ResolveScale(new Rect(0, 0, 1920, 1080)), 1e-9);
    }

    [TestMethod]
    public void ResolveScale_按较短边的比例取值()
    {
        // 2560/1920 = 1.3333、1392/1080 = 1.2889，取较小的那个：宽度不是瓶颈时高度说了算。
        // 2560/1920 = 1.3333 and 1392/1080 = 1.2889; the smaller one wins, so height governs when width is not the bottleneck.
        Assert.AreEqual(MeasuredScale, CapsuleIslandScalePolicy.ResolveScale(new Rect(0, 0, 2560, 1392)), 1e-9);
    }

    [TestMethod]
    public void ResolveScale_超过上限时夹到一点七五()
    {
        Assert.AreEqual(1.75, CapsuleIslandScalePolicy.ResolveScale(new Rect(0, 0, 3840, 2160)), 1e-9);
    }

    [TestMethod]
    public void ResolveScale_低于下限时夹到零点八五()
    {
        // 1366/1920 = 0.7115，低于 0.85 下限。
        Assert.AreEqual(0.85, CapsuleIslandScalePolicy.ResolveScale(new Rect(0, 0, 1366, 768)), 1e-9);
        Assert.AreEqual(0.85, CapsuleIslandScalePolicy.ResolveScale(new Rect(0, 0, 1024, 600)), 1e-9);
    }

    [TestMethod]
    public void ResolveScale_工作区非正或非有限时回退到一点零()
    {
        // Rect 本身拒绝负宽高，因此"非正尺寸"只可能是宽或高恰为 0（退化工作区）。
        // Rect itself rejects negative dimensions, so a "non-positive" work area can only be one with a zero width or height.
        Assert.AreEqual(1.0, CapsuleIslandScalePolicy.ResolveScale(new Rect(0, 0, 0, 0)), 1e-9);
        Assert.AreEqual(1.0, CapsuleIslandScalePolicy.ResolveScale(new Rect(0, 0, 0, 1080)), 1e-9);
        Assert.AreEqual(1.0, CapsuleIslandScalePolicy.ResolveScale(new Rect(0, 0, 1920, 0)), 1e-9);

        // NaN/Infinity 能构造成 Rect，但在策略里必须按"非有限"回退到 1.0，而不是把 NaN 传下去。
        // NaN/Infinity do build a Rect, but the policy has to treat them as non-finite and fall back to 1.0 rather than
        // propagating a NaN.
        Assert.AreEqual(1.0, CapsuleIslandScalePolicy.ResolveScale(new Rect(0, 0, double.NaN, 1080)), 1e-9);
        Assert.AreEqual(1.0, CapsuleIslandScalePolicy.ResolveScale(new Rect(0, 0, 1920, double.NaN)), 1e-9);
        Assert.AreEqual(1.0, CapsuleIslandScalePolicy.ResolveScale(new Rect(0, 0, double.PositiveInfinity, 1080)), 1e-9);
        Assert.AreEqual(1.0, CapsuleIslandScalePolicy.ResolveScale(new Rect(0, 0, 1920, double.PositiveInfinity)), 1e-9);
    }

    [TestMethod]
    public void ResolveCapsuleSize_按系数缩放基准尺寸()
    {
        var baseline = CapsuleIslandMetrics.ResolveCapsuleSize(1.0);
        // 药丸基准宽度 = 原 176 + 左内缩：加宽的那几 DIP 专门用来给封面留出与左弧的间距，不从标题宽度里扣。
        // The pill's baseline width is the original 176 plus the leading inset: those few DIP exist to give the cover its gap
        // against the left arc and are never taken out of the title's width.
        Assert.AreEqual(176 + CapsuleIslandMetrics.CapsuleLeadInsetDip, baseline.Width, 1e-9);
        Assert.AreEqual(40, baseline.Height, 1e-9);

        var scaled = CapsuleIslandMetrics.ResolveCapsuleSize(MeasuredScale);
        Assert.AreEqual(CapsuleIslandMetrics.CapsuleSize.Width * MeasuredScale, scaled.Width, 1e-9);
        Assert.AreEqual(40 * MeasuredScale, scaled.Height, 1e-9);
    }

    [TestMethod]
    public void ResolveCardSize_按系数缩放基准尺寸()
    {
        var baseline = CapsuleIslandMetrics.ResolveCardSize(1.0);
        Assert.AreEqual(320, baseline.Width, 1e-9);
        Assert.AreEqual(200, baseline.Height, 1e-9);

        var scaled = CapsuleIslandMetrics.ResolveCardSize(MeasuredScale);
        Assert.AreEqual(320 * MeasuredScale, scaled.Width, 1e-9);
        Assert.AreEqual(200 * MeasuredScale, scaled.Height, 1e-9);
    }

    [TestMethod]
    public void ResolveCapsuleCornerRadius_恒为缩放后高度的一半()
    {
        foreach (var scale in new[] { 0.85, 1.0, MeasuredScale, 1.75 })
        {
            var height = CapsuleIslandMetrics.ResolveCapsuleSize(scale).Height;
            var radius = CapsuleIslandMetrics.ResolveCapsuleCornerRadius(scale);

            // "两端半圆"这条不变量：半径必须恰好是高度的一半，缩放只改变绝对尺寸。
            // The "fully rounded ends" invariant: the radius is exactly half the height; scaling only changes absolute size.
            Assert.AreEqual(height / 2, radius, 1e-9, $"scale={scale}");
        }
    }

    [TestMethod]
    public void ResolveSize_系数非法时按一点零处理()
    {
        Assert.AreEqual(CapsuleIslandMetrics.ResolveCapsuleSize(1.0), CapsuleIslandMetrics.ResolveCapsuleSize(double.NaN));
        Assert.AreEqual(CapsuleIslandMetrics.ResolveCapsuleSize(1.0), CapsuleIslandMetrics.ResolveCapsuleSize(0));
        Assert.AreEqual(CapsuleIslandMetrics.ResolveCapsuleSize(1.0), CapsuleIslandMetrics.ResolveCapsuleSize(-2));
        Assert.AreEqual(CapsuleIslandMetrics.ResolveCapsuleSize(1.0), CapsuleIslandMetrics.ResolveCapsuleSize(double.PositiveInfinity));
        Assert.AreEqual(CapsuleIslandMetrics.ResolveCardSize(1.0), CapsuleIslandMetrics.ResolveCardSize(double.NaN));
        Assert.AreEqual(20, CapsuleIslandMetrics.ResolveCapsuleCornerRadius(double.NaN), 1e-9);
    }

    [TestMethod]
    public void 基准常量与解析结果一致()
    {
        // 静态基准字段必须仍等于系数 1.0 的解析结果，既有调用方（吸附判定等）才不会被缩放改写。
        // The static baseline fields must still equal the scale-1.0 results so existing callers (edge snap and friends)
        // are not rewritten by the scaling.
        Assert.AreEqual(CapsuleIslandMetrics.ResolveCapsuleSize(1.0), CapsuleIslandMetrics.CapsuleSize);
        Assert.AreEqual(CapsuleIslandMetrics.ResolveCardSize(1.0), CapsuleIslandMetrics.CardSize);
        Assert.AreEqual(20, CapsuleIslandMetrics.CapsuleCornerRadius, 1e-9);
        Assert.AreEqual(24, CapsuleIslandMetrics.CardCornerRadius, 1e-9);
    }
}
