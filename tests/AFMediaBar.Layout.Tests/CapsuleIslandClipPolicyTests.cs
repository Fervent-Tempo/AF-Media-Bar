using System.Windows;
using AFMediaBar.Classes.Services.Layout;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>
/// 胶囊岛圆角裁剪的纯逻辑测试：内容必须裁进与命中判定同一个圆角形状，而尺寸不可用时**不能**把整座岛裁没。
/// Pure-logic tests for the capsule island's rounded clip: the content has to be clipped to the very shape the hit test uses, while an
/// unusable size must **never** erase the whole island.
/// </summary>
[TestClass]
public sealed class CapsuleIslandClipPolicyTests
{
    /// <summary>
    /// 只有有限且为正的宽高才给出裁剪矩形（左上角固定在原点，尺寸就是窗口尺寸）。
    /// A clip rectangle comes out only for a finite, positive size (anchored at the origin, sized like the window).
    /// </summary>
    [TestMethod]
    public void ResolveClipRect_只有有限正尺寸才给出裁剪矩形()
    {
        Assert.AreEqual(new Rect(0, 0, 226.8, 51.6), CapsuleIslandClipPolicy.ResolveClipRect(226.8, 51.6));
        Assert.AreEqual(new Rect(0, 0, 412, 258), CapsuleIslandClipPolicy.ResolveClipRect(412, 258));
    }

    /// <summary>
    /// 尺寸不可用（NaN / 非正 / 非有限）时返回 null：调用方据此保留上一次的裁剪值。
    /// 退化成"不裁剪"会让圆角外重新露出内容，写成空矩形则会把整座岛裁没——两者都不是这里想要的。
    /// An unusable size (NaN, non-positive, non-finite) answers null so the caller keeps the previous clip: degrading to "no clip" would
    /// show the content outside the corners again, while an empty rectangle would erase the whole island.
    /// </summary>
    [TestMethod]
    public void ResolveClipRect_尺寸不可用时返回null()
    {
        Assert.IsNull(CapsuleIslandClipPolicy.ResolveClipRect(double.NaN, 51.6));
        Assert.IsNull(CapsuleIslandClipPolicy.ResolveClipRect(226.8, double.NaN));
        Assert.IsNull(CapsuleIslandClipPolicy.ResolveClipRect(0, 51.6));
        Assert.IsNull(CapsuleIslandClipPolicy.ResolveClipRect(226.8, 0));
        Assert.IsNull(CapsuleIslandClipPolicy.ResolveClipRect(-5, 51.6));
        Assert.IsNull(CapsuleIslandClipPolicy.ResolveClipRect(226.8, double.PositiveInfinity));
    }

    /// <summary>
    /// 圆角半径：非法或非正值取 0（直角裁剪），合法值夹到短边的一半——与 <c>Border</c>/<c>Border.CornerRadius</c> 的口径一致，
    /// 超过一半的半径在几何裁剪里没有意义（药丸本来就是高度的一半）。
    /// The clip radius: an invalid or non-positive value becomes 0 (a square clip) and a valid one is clamped to half the shorter side,
    /// matching the <c>Border</c> basis; a radius past half is meaningless for the geometry (the pill's radius already is half its
    /// height).
    /// </summary>
    [TestMethod]
    public void ResolveClipRadius_非法半径归零并夹到短边一半()
    {
        Assert.AreEqual(0, CapsuleIslandClipPolicy.ResolveClipRadius(0, 226.8, 51.6));
        Assert.AreEqual(0, CapsuleIslandClipPolicy.ResolveClipRadius(-4, 226.8, 51.6));
        Assert.AreEqual(0, CapsuleIslandClipPolicy.ResolveClipRadius(double.NaN, 226.8, 51.6));
        Assert.AreEqual(0, CapsuleIslandClipPolicy.ResolveClipRadius(double.PositiveInfinity, 226.8, 51.6));
        // 药丸：半径 = 高度一半（本机 51.6 / 2 = 25.8）
        // The pill: its radius is half the height (25.8 of 51.6 on this machine).
        Assert.AreEqual(25.8, CapsuleIslandClipPolicy.ResolveClipRadius(25.8, 226.8, 51.6), 0.0001);
        // 超出短边一半的配置值夹到一半（卡片态同样如此）。
        // A configured value past half the shorter side is clamped to half (the card behaves the same way).
        Assert.AreEqual(129, CapsuleIslandClipPolicy.ResolveClipRadius(200, 412, 258), 0.0001);
        // 尺寸本身不可用时同样归零：裁剪矩形都还没有，半径先别自作主张。
        // An unusable size also answers zero: there is no clip rectangle yet, so the radius must not invent one.
        Assert.AreEqual(0, CapsuleIslandClipPolicy.ResolveClipRadius(24, double.NaN, 258));
        Assert.AreEqual(0, CapsuleIslandClipPolicy.ResolveClipRadius(24, 0, 258));
    }
}
