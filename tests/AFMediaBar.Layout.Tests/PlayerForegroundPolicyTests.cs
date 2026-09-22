using System.Windows.Media;
using System.Windows;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Settings;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

[TestClass]
public sealed class PlayerForegroundPolicyTests
{
    [TestMethod]
    public void TaskbarHoverPaletteFollowsTheResolvedForegroundForBothThemes()
    {
        var lightText = TaskbarHoverPalettePolicy.Resolve(Colors.White);
        var darkText = TaskbarHoverPalettePolicy.Resolve(Color.FromRgb(0x1C, 0x1C, 0x1C));

        Assert.AreEqual(Colors.White, lightText.Foreground);
        Assert.AreEqual(Colors.White, lightText.Surface);
        Assert.AreEqual(Color.FromRgb(0x1C, 0x1C, 0x1C), darkText.Foreground);
        Assert.AreEqual(Color.FromRgb(0x1C, 0x1C, 0x1C), darkText.Surface);
        Assert.AreEqual(darkText.Foreground.R, darkText.ButtonHover.R);
        Assert.AreEqual(darkText.Foreground.G, darkText.ButtonHover.G);
        Assert.AreEqual(darkText.Foreground.B, darkText.ButtonHover.B);
        Assert.AreEqual((byte)0x20, darkText.ButtonHover.A);
        Assert.AreEqual(lightText.SurfaceOpacity, darkText.SurfaceOpacity);
    }

    [TestMethod]
    public void Resolve_UniformBlack_UsesLightTextWithoutShadow()
    {
        var decision = PlayerForegroundPolicy.Resolve(Repeated(Colors.Black));

        Assert.IsNotNull(decision);
        Assert.IsTrue(decision.Value.UsesLightText);
        Assert.IsFalse(decision.Value.NeedsContrastShadow);
    }

    [TestMethod]
    public void Resolve_UniformWhite_UsesDarkTextWithoutShadow()
    {
        var decision = PlayerForegroundPolicy.Resolve(Repeated(Colors.White));

        Assert.IsNotNull(decision);
        Assert.IsFalse(decision.Value.UsesLightText);
        Assert.IsFalse(decision.Value.NeedsContrastShadow);
    }

    [TestMethod]
    public void Resolve_ColorsAroundThreshold_SelectExpectedForeground()
    {
        var darkBackground = PlayerForegroundPolicy.Resolve(Repeated(Color.FromRgb(0x70, 0x70, 0x70)));
        var lightBackground = PlayerForegroundPolicy.Resolve(Repeated(Color.FromRgb(0x88, 0x88, 0x88)));

        Assert.IsTrue(darkBackground?.UsesLightText);
        Assert.IsFalse(lightBackground?.UsesLightText);
    }

    [TestMethod]
    public void Resolve_MixedDarkAndLightBackground_RequestsShadow()
    {
        var samples = Enumerable.Repeat(Colors.Black, 30)
            .Concat(Enumerable.Repeat(Colors.White, 30))
            .ToArray();

        var decision = PlayerForegroundPolicy.Resolve(samples);

        Assert.IsNotNull(decision);
        Assert.IsTrue(decision.Value.NeedsContrastShadow);
    }

    [TestMethod]
    public void Resolve_SmallDarkOutlierSet_DoesNotDisturbWhiteBackground()
    {
        var samples = Enumerable.Repeat(Colors.White, 50)
            .Concat(Enumerable.Repeat(Colors.Black, 5))
            .ToArray();

        var decision = PlayerForegroundPolicy.Resolve(samples);

        Assert.IsNotNull(decision);
        Assert.IsFalse(decision.Value.UsesLightText);
        Assert.IsFalse(decision.Value.NeedsContrastShadow);
    }

    [TestMethod]
    public void Resolve_ContrastShadowKeepsItsStateInsideTheHysteresisBand()
    {
        // #6E6E6E 的对比度约 5.1：高于进入阈值 4.5，低于退出阈值 6.5，因此两个方向都必须保留上一次决定。
        // #6E6E6E yields roughly 5.1 contrast: above the 4.5 entry threshold and below the 6.5 exit threshold, so both
        // directions must keep the previous decision instead of flapping.
        var samples = Repeated(Color.FromRgb(0x6E, 0x6E, 0x6E));

        var keepShadow = PlayerForegroundPolicy.Resolve(samples, new PlayerForegroundDecision(true, true));
        var keepClear = PlayerForegroundPolicy.Resolve(samples, new PlayerForegroundDecision(true, false));

        Assert.IsTrue(keepShadow?.NeedsContrastShadow);
        Assert.IsFalse(keepClear?.NeedsContrastShadow);
    }

    [TestMethod]
    public void Resolve_LowContrastBackgroundAlwaysRequestsShadow()
    {
        // #7A7A7A 的对比度在两种文字色分支下都低于 4.5，因此无论上一次是什么决定都应开启阴影。
        // #7A7A7A stays below 4.5 contrast in both text-color branches, so the shadow turns on regardless of the
        // previous decision.
        var samples = Repeated(Color.FromRgb(0x7A, 0x7A, 0x7A));

        var fromClear = PlayerForegroundPolicy.Resolve(samples, new PlayerForegroundDecision(true, false));
        var fromShadow = PlayerForegroundPolicy.Resolve(samples, new PlayerForegroundDecision(true, true));

        Assert.IsTrue(fromClear?.NeedsContrastShadow);
        Assert.IsTrue(fromShadow?.NeedsContrastShadow);
    }

    [TestMethod]
    public void Resolve_HighContrastBackgroundClearsShadowOnlyAboveTheExitBand()
    {
        var samples = Repeated(Color.FromRgb(0x20, 0x20, 0x20));

        var fromShadow = PlayerForegroundPolicy.Resolve(samples, new PlayerForegroundDecision(true, true));
        var fromClear = PlayerForegroundPolicy.Resolve(samples, new PlayerForegroundDecision(true, false));

        Assert.IsFalse(fromShadow?.NeedsContrastShadow);
        Assert.IsFalse(fromClear?.NeedsContrastShadow);
    }

    [TestMethod]
    public void Resolve_EmptyOrTransparentSamples_ReturnsNoDecision()
    {
        Assert.IsNull(PlayerForegroundPolicy.Resolve([]));
        Assert.IsNull(PlayerForegroundPolicy.Resolve([Colors.Transparent]));
    }

    [TestMethod]
    public void Resolve_NearThreshold_PreservesPreviousChoice()
    {
        var nearThreshold = Repeated(Color.FromRgb(0x7B, 0x7B, 0x7B));

        var keepLight = PlayerForegroundPolicy.Resolve(nearThreshold, new PlayerForegroundDecision(true, false));
        var keepDark = PlayerForegroundPolicy.Resolve(nearThreshold, new PlayerForegroundDecision(false, false));

        Assert.IsTrue(keepLight?.UsesLightText);
        Assert.IsFalse(keepDark?.UsesLightText);
    }

    [TestMethod]
    public void ResolvePresentation_HonorsHighContrastAndForcedModesBeforeAutomaticDecision()
    {
        var automatic = new PlayerForegroundDecision(true, true);

        var highContrast = PlayerForegroundPolicy.ResolvePresentation(
            PlayerForegroundMode.Automatic, highContrast: true, themeUsesLightText: false, automatic);
        var forcedLight = PlayerForegroundPolicy.ResolvePresentation(
            PlayerForegroundMode.LightText, highContrast: false, themeUsesLightText: false, automatic);
        var forcedDark = PlayerForegroundPolicy.ResolvePresentation(
            PlayerForegroundMode.DarkText, highContrast: false, themeUsesLightText: true, automatic);

        Assert.IsTrue(highContrast.UsesSystemColors);
        Assert.IsFalse(highContrast.NeedsContrastShadow);
        Assert.IsTrue(forcedLight.UsesLightText);
        Assert.IsFalse(forcedLight.NeedsContrastShadow);
        Assert.IsFalse(forcedDark.UsesLightText);
        Assert.IsFalse(forcedDark.NeedsContrastShadow);
    }

    [TestMethod]
    public void ResolvePresentation_UsesThemeOnlyWhenAutomaticSampleIsUnavailable()
    {
        var fallback = PlayerForegroundPolicy.ResolvePresentation(
            PlayerForegroundMode.Automatic, highContrast: false, themeUsesLightText: true, automaticDecision: null);
        var sampled = PlayerForegroundPolicy.ResolvePresentation(
            PlayerForegroundMode.Automatic, highContrast: false, themeUsesLightText: false,
            new PlayerForegroundDecision(true, true));

        Assert.IsTrue(fallback.UsesLightText);
        Assert.IsFalse(fallback.NeedsContrastShadow);
        Assert.IsTrue(sampled.UsesLightText);
        Assert.IsTrue(sampled.NeedsContrastShadow);
    }

    [TestMethod]
    public void SamplingGeneration_RejectsDisposedOrStaleResults()
    {
        Assert.IsTrue(PlayerForegroundPolicy.IsCurrent(disposed: false, resultGeneration: 4, currentGeneration: 4));
        Assert.IsFalse(PlayerForegroundPolicy.IsCurrent(disposed: true, resultGeneration: 4, currentGeneration: 4));
        Assert.IsFalse(PlayerForegroundPolicy.IsCurrent(disposed: false, resultGeneration: 3, currentGeneration: 4));
    }

    /// <summary>
    /// 频谱必须与媒体文字共用同一个自动前景决定，只有不透明度按各自的视觉重量取值：
    /// 色相被改写就会出现「文字已转深、频谱还是白的」这种半跟随状态。
    /// The spectrum must share the media text's automatic foreground decision and differ only in the alpha that matches its
    /// own visual weight: rewriting the hue would leave the half-followed state where the text has gone dark while the
    /// spectrum is still white.
    /// </summary>
    [TestMethod]
    public void ToSpectrumForeground_KeepsTheTextHueAndAppliesTheSpectrumAlpha()
    {
        foreach (var textForeground in new[] { Colors.White, Color.FromRgb(0x1C, 0x1C, 0x1C), Color.FromRgb(0x3A, 0x7B, 0xD5) })
        {
            var spectrum = PlayerForegroundPolicy.ToSpectrumForeground(textForeground);
            Assert.AreEqual(PlayerForegroundPolicy.SpectrumForegroundAlpha, spectrum.A);
            Assert.AreEqual(textForeground.R, spectrum.R);
            Assert.AreEqual(textForeground.G, spectrum.G);
            Assert.AreEqual(textForeground.B, spectrum.B);
        }

        // 深色文字必须真的变成深色频谱，否则浅色背景上仍是旧的白柱。
        // Dark text must really produce a dark spectrum, otherwise the old white bars survive on light backgrounds.
        Assert.AreEqual(
            Color.FromArgb(PlayerForegroundPolicy.SpectrumForegroundAlpha, 0x1C, 0x1C, 0x1C),
            PlayerForegroundPolicy.ToSpectrumForeground(Color.FromRgb(0x1C, 0x1C, 0x1C)));
    }

    [TestMethod]
    public async Task ScreenSampler_InvalidBounds_ReturnsNoSamples()
    {
        var sampler = new ScreenBackgroundSampler();

        var samples = await sampler.SampleAsync(Int32Rect.Empty, CancellationToken.None);

        Assert.AreEqual(0, samples.Count);
    }

    private static Color[] Repeated(Color color) => Enumerable.Repeat(color, 40).ToArray();
}
