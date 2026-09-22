using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Settings;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>
/// 胶囊岛自动前景采样门控的纯逻辑测试：只有自动模式、表面真的透出背景、窗口可见且不在拖拽/形变中时才去采桌面像素。
/// Pure-logic tests for the capsule island's automatic-foreground sampling gate: desktop pixels are only sampled in automatic mode
/// while the surface really shows the background and the window is visible and not busy dragging or morphing.
///
/// 门控必须收紧，因为采样读的是**最终桌面合成结果**：表面不透明时采到的就是我们自己画的那块表面，决定会自反馈锁定在某个颜色上。
/// The gate has to be tight because the sampler reads the **final desktop composition**: with an opaque surface the samples are our
/// own paint, and the decision locks onto itself.
/// </summary>
[TestClass]
public sealed class CapsuleIslandForegroundPolicyTests
{
    /// <summary>
    /// 自动模式 + 表面透出背景（旧设置 Transparent，或不透明度低于 100%）→ 采样。
    /// Automatic mode plus a surface that really shows the background (the legacy Transparent setting, or an opacity below 100%)
    /// is what allows sampling.
    /// </summary>
    [TestMethod]
    public void ShouldSample_自动模式且表面透出背景时采样()
    {
        Assert.IsTrue(CapsuleIslandForegroundPolicy.ShouldSample(
            PlayerForegroundMode.Automatic, backgroundTransparent: false, backgroundOpacityPercent: 80,
            isVisible: true, isBusy: false));
        Assert.IsTrue(CapsuleIslandForegroundPolicy.ShouldSample(
            PlayerForegroundMode.Automatic, backgroundTransparent: false, backgroundOpacityPercent: 0,
            isVisible: true, isBusy: false));
        // Transparent 走的是 alpha=1 的表面，透明度设置此时无关紧要，必须照样采样。
        // Transparent uses an alpha-1 surface, where the opacity setting is irrelevant, and it has to sample all the same.
        Assert.IsTrue(CapsuleIslandForegroundPolicy.ShouldSample(
            PlayerForegroundMode.Automatic, backgroundTransparent: true, backgroundOpacityPercent: 100,
            isVisible: true, isBusy: false));
    }

    /// <summary>
    /// 不透明表面（不透明度 100%）不采样：采到的就是我们自己画的表面，决定会自反馈；强制浅色/深色文字与高对比度之外的
    /// 固定模式也不需要采样，前景本来就与背景无关。
    /// An opaque surface (100%) is never sampled, because the samples would be our own paint and the decision feeds back on itself;
    /// a forced light/dark mode needs no sampling either, since its foreground is independent of the background by definition.
    /// </summary>
    [TestMethod]
    public void ShouldSample_不透明表面与固定前景模式都不采样()
    {
        Assert.IsFalse(CapsuleIslandForegroundPolicy.ShouldSample(
            PlayerForegroundMode.Automatic, backgroundTransparent: false, backgroundOpacityPercent: 100,
            isVisible: true, isBusy: false));
        Assert.IsFalse(CapsuleIslandForegroundPolicy.ShouldSample(
            PlayerForegroundMode.LightText, backgroundTransparent: true, backgroundOpacityPercent: 0,
            isVisible: true, isBusy: false));
        Assert.IsFalse(CapsuleIslandForegroundPolicy.ShouldSample(
            PlayerForegroundMode.DarkText, backgroundTransparent: true, backgroundOpacityPercent: 0,
            isVisible: true, isBusy: false));
    }

    /// <summary>
    /// 窗口不可见、正在拖拽或正在形变时都不采样：那些时刻窗口位置/尺寸每帧都在变，采样矩形当场就过期，
    /// 而且拖拽中按下的鼠标键会把应用自己的点击路径搅进来。
    /// Nothing is sampled while the window is invisible, dragging, or morphing: the window's position and size change every frame
    /// in those moments, so the sample rectangle is stale the instant it is taken.
    /// </summary>
    [TestMethod]
    public void ShouldSample_不可见或忙碌时都不采样()
    {
        Assert.IsFalse(CapsuleIslandForegroundPolicy.ShouldSample(
            PlayerForegroundMode.Automatic, backgroundTransparent: false, backgroundOpacityPercent: 80,
            isVisible: false, isBusy: false));
        Assert.IsFalse(CapsuleIslandForegroundPolicy.ShouldSample(
            PlayerForegroundMode.Automatic, backgroundTransparent: false, backgroundOpacityPercent: 80,
            isVisible: true, isBusy: true));
    }
}
