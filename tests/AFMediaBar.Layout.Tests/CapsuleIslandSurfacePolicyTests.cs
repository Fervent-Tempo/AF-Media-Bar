using System.Windows;
using System.Windows.Media;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Services.Appearance;
using AFMediaBar.Classes.Settings;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>
/// 灵动岛表面外观与位置归一化的纯逻辑测试：只算颜色与圆角，不建窗口、不读设置文件。
/// Pure-logic tests for the capsule island's surface appearance and position normalization: colours and corner radii only,
/// no windows and no settings file.
/// </summary>
[TestClass]
public sealed class CapsuleIslandSurfacePolicyTests
{
    /// <summary>深色主题中性底（iOS 纯黑，与外观策略中的常量一致）。/ Dark-theme neutral surface (the iOS pure black, matching the constant inside the policy).</summary>
    private static readonly Color DarkNeutral = Color.FromArgb(0xFF, 0x00, 0x00, 0x00);

    /// <summary>浅色主题中性底。/ Light-theme neutral surface.</summary>
    private static readonly Color LightNeutral = Color.FromArgb(0xFF, 0xF3, 0xF3, 0xF3);

    private static readonly Color Accent = Color.FromRgb(0x00, 0x78, 0xD4);

    [TestMethod]
    public void ResolveSurfaceColor_三种样式各自可区分()
    {
        var automatic = Resolve(PlayerSurfaceStyle.Automatic, 100, false, Accent);
        var themeTint = Resolve(PlayerSurfaceStyle.ThemeTint, 100, false, Accent);
        var solid = Resolve(PlayerSurfaceStyle.Solid, 100, false, Accent);

        // 深色主题下 Automatic 与 Solid 本来就同色（都是恒定深色底），三者的两两差异只在浅色主题下才全部成立。
        // In a dark theme Automatic and Solid are the same colour by design (both are the constant dark surface), so the
        // three-way distinction only holds in a light theme.
        Assert.AreNotEqual(automatic, themeTint, "混入强调色后必须与纯浅色中性底不同");
        Assert.AreNotEqual(solid, themeTint, "混入强调色后必须与纯深色底不同");
        Assert.AreNotEqual(automatic, solid, "浅色主题下的自动样式必须与恒定深色底不同");
    }

    [TestMethod]
    public void ResolveSurfaceColor_自动样式跟随主题而恒定样式不跟随()
    {
        var automaticDark = Resolve(PlayerSurfaceStyle.Automatic, 100, true, Accent);
        var automaticLight = Resolve(PlayerSurfaceStyle.Automatic, 100, false, Accent);
        var solidDark = Resolve(PlayerSurfaceStyle.Solid, 100, true, Accent);
        var solidLight = Resolve(PlayerSurfaceStyle.Solid, 100, false, Accent);

        // 浅色主题下自动样式取浅色中性底，深色主题下与恒定深色底同色。
        Assert.AreEqual(LightNeutral, automaticLight);
        Assert.AreEqual(DarkNeutral, automaticDark);
        Assert.AreEqual(DarkNeutral, solidDark);
        Assert.AreEqual(solidDark, solidLight, "恒定样式不随主题变化");
    }

    [TestMethod]
    public void ResolveSurfaceColor_主题色混合方向正确()
    {
        // 深色中性底 (0,0,0) 与蓝色强调色 (0x00,0x78,0xD4) 按 35% 混合 = (0x00,0x2A,0x4A)。
        var mixedDark = Resolve(PlayerSurfaceStyle.ThemeTint, 100, true, Accent);
        var mixedLight = Resolve(PlayerSurfaceStyle.ThemeTint, 100, false, Accent);

        Assert.AreEqual(0x00, mixedDark.R);
        Assert.AreEqual(0x2A, mixedDark.G);
        Assert.AreEqual(0x4A, mixedDark.B);
        Assert.AreEqual(0xFF, mixedDark.A);

        // 浅色中性底 (0xF3,0xF3,0xF3) 与同一强调色按同一比例混合 = (0x9E,0xC8,0xE8)。
        Assert.AreEqual(0x9E, mixedLight.R);
        Assert.AreEqual(0xC8, mixedLight.G);
        Assert.AreEqual(0xE8, mixedLight.B);
        Assert.AreNotEqual(mixedDark, mixedLight, "主题色必须同样跟随明暗主题");
        Assert.AreNotEqual(mixedDark, Accent, "混合结果不可能等于纯强调色");
        Assert.AreNotEqual(mixedDark, DarkNeutral, "混合结果不可能等于纯中性底");
    }

    [TestMethod]
    public void ResolveSurfaceColor_深色底为iOS纯黑()
    {
        // iOS 灵动岛的观感来自"纯黑不透明"表面，深色主题下 Automatic 与 Solid 都必须是 #FF000000。
        var automaticDark = Resolve(PlayerSurfaceStyle.Automatic, 100, true, Accent);
        var solid = Resolve(PlayerSurfaceStyle.Solid, 100, false, Accent);

        Assert.AreEqual(Color.FromArgb(0xFF, 0x00, 0x00, 0x00), automaticDark);
        Assert.AreEqual(Color.FromArgb(0xFF, 0x00, 0x00, 0x00), solid);
    }

    [TestMethod]
    public void ResolveSurfaceColor_不透明度换算为四舍五入的alpha()
    {
        Assert.AreEqual(0, Resolve(PlayerSurfaceStyle.Solid, 0, true, Accent).A);
        Assert.AreEqual(128, Resolve(PlayerSurfaceStyle.Solid, 50, true, Accent).A);
        Assert.AreEqual(255, Resolve(PlayerSurfaceStyle.Solid, 100, true, Accent).A);
    }

    [TestMethod]
    public void ResolveSurfaceColor_越界不透明度被夹紧()
    {
        // 负值夹到 0%，超过 100 的值夹到 100%：alpha 不可能溢出成回绕值。
        Assert.AreEqual(0, Resolve(PlayerSurfaceStyle.Automatic, -10, true, Accent).A);
        Assert.AreEqual(255, Resolve(PlayerSurfaceStyle.Automatic, 150, true, Accent).A);
    }

    [TestMethod]
    public void ResolveCornerRadius_正常值原样透传()
    {
        Assert.AreEqual(6, CapsuleIslandSurfacePolicy.ResolveCornerRadius(6, 220, 44), 1e-9);
        Assert.AreEqual(0, CapsuleIslandSurfacePolicy.ResolveCornerRadius(0, 220, 44), 1e-9);
    }

    [TestMethod]
    public void ResolveCornerRadius_超过短边一半时被夹到半高()
    {
        // 220×44 的胶囊：配置 24 会超出半高 22，夹到 22。
        Assert.AreEqual(22, CapsuleIslandSurfacePolicy.ResolveCornerRadius(24, 220, 44), 1e-9);
        Assert.AreEqual(80, CapsuleIslandSurfacePolicy.ResolveCornerRadius(100, 320, 160), 1e-9);
    }

    [TestMethod]
    public void ResolveCornerRadius_负值与非有限数按零处理()
    {
        Assert.AreEqual(0, CapsuleIslandSurfacePolicy.ResolveCornerRadius(-4, 220, 44), 1e-9);
        Assert.AreEqual(0, CapsuleIslandSurfacePolicy.ResolveCornerRadius(double.NaN, 220, 44), 1e-9);
        Assert.AreEqual(0, CapsuleIslandSurfacePolicy.ResolveCornerRadius(double.PositiveInfinity, 220, 44), 1e-9);
    }

    [TestMethod]
    public void ResolveCornerRadius_宽高非正时返回零()
    {
        Assert.AreEqual(0, CapsuleIslandSurfacePolicy.ResolveCornerRadius(6, 0, 44), 1e-9);
        Assert.AreEqual(0, CapsuleIslandSurfacePolicy.ResolveCornerRadius(6, 220, 0), 1e-9);
        Assert.AreEqual(0, CapsuleIslandSurfacePolicy.ResolveCornerRadius(6, -220, -44), 1e-9);
    }

    [TestMethod]
    public void GetNormalizedCenter_居中的窗口约等于半格()
    {
        var workArea = new Rect(0, 0, 1920, 1040);
        var centered = new Rect(900, 498, 120, 44);

        var center = DynamicIslandPositionCalculator.GetNormalizedCenter(workArea, centered);

        Assert.AreEqual(0.5, center.X, 1e-9);
        Assert.AreEqual(0.5, center.Y, 1e-9);
    }

    [TestMethod]
    public void GetNormalizedCenter_贴左上角的窗口接近原点()
    {
        var workArea = new Rect(0, 0, 1920, 1040);
        var atOrigin = new Rect(0, 0, 0, 0);

        var center = DynamicIslandPositionCalculator.GetNormalizedCenter(workArea, atOrigin);

        // 左上角处的退化窗口：中心点正好是工作区原点，归一化结果为 0。
        // The degenerate window at the top-left corner: its centre is exactly the work area's origin, so the result is zero.
        Assert.AreEqual(0, center.X, 1e-9);
        Assert.AreEqual(0, center.Y, 1e-9);

        // 窗宽为 0 时中心点仍在原点上，只是带上了工作区左上角可能不为零的偏移。
        // With zero window width the centre stays on the origin, only offset by a work area whose top-left is not zero.
        var offsetArea = new Rect(100, 50, 1920, 1040);
        var offsetCenter = DynamicIslandPositionCalculator.GetNormalizedCenter(offsetArea, new Rect(100, 50, 0, 0));
        Assert.AreEqual(0, offsetCenter.X, 1e-9);
        Assert.AreEqual(0, offsetCenter.Y, 1e-9);
    }

    [TestMethod]
    public void GetNormalizedCenter_工作区尺寸为零时返回半格()
    {
        var center = DynamicIslandPositionCalculator.GetNormalizedCenter(new Rect(0, 0, 0, 0), new Rect(10, 10, 220, 44));

        Assert.AreEqual(0.5, center.X, 1e-9);
        Assert.AreEqual(0.5, center.Y, 1e-9);
    }

    [TestMethod]
    public void GetNormalizedCenter_越界窗口被夹到零到一()
    {
        var workArea = new Rect(0, 0, 1920, 1040);

        var beyondRight = DynamicIslandPositionCalculator.GetNormalizedCenter(workArea, new Rect(4000, 3000, 220, 44));
        var beforeOrigin = DynamicIslandPositionCalculator.GetNormalizedCenter(workArea, new Rect(-800, -600, 220, 44));

        Assert.AreEqual(1, beyondRight.X, 1e-9);
        Assert.AreEqual(1, beyondRight.Y, 1e-9);
        Assert.AreEqual(0, beforeOrigin.X, 1e-9);
        Assert.AreEqual(0, beforeOrigin.Y, 1e-9);
    }

    private static Color Resolve(PlayerSurfaceStyle style, int opacityPercent, bool isDarkTheme, Color accentColor) =>
        CapsuleIslandSurfacePolicy.ResolveSurfaceColor(style, opacityPercent, isDarkTheme, accentColor);
}
