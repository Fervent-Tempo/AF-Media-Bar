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
    public void Resolve_UniformBlack_UsesLightText()
    {
        var decision = PlayerForegroundPolicy.Resolve(Repeated(Colors.Black));

        Assert.IsNotNull(decision);
        Assert.IsTrue(decision.Value.UsesLightText);
    }

    [TestMethod]
    public void Resolve_UniformWhite_UsesDarkText()
    {
        var decision = PlayerForegroundPolicy.Resolve(Repeated(Colors.White));

        Assert.IsNotNull(decision);
        Assert.IsFalse(decision.Value.UsesLightText);
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
    public void Resolve_SmallDarkOutlierSet_DoesNotDisturbWhiteBackground()
    {
        var samples = Enumerable.Repeat(Colors.White, 50)
            .Concat(Enumerable.Repeat(Colors.Black, 5))
            .ToArray();

        var decision = PlayerForegroundPolicy.Resolve(samples);

        Assert.IsNotNull(decision);
        Assert.IsFalse(decision.Value.UsesLightText);
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

        var keepLight = PlayerForegroundPolicy.Resolve(nearThreshold, new PlayerForegroundDecision(true));
        var keepDark = PlayerForegroundPolicy.Resolve(nearThreshold, new PlayerForegroundDecision(false));

        Assert.IsTrue(keepLight?.UsesLightText);
        Assert.IsFalse(keepDark?.UsesLightText);
    }

    [TestMethod]
    public void ResolvePresentation_HonorsHighContrastAndForcedModesBeforeAutomaticDecision()
    {
        var automatic = new PlayerForegroundDecision(true);

        var highContrast = PlayerForegroundPolicy.ResolvePresentation(
            PlayerForegroundMode.Automatic, highContrast: true, themeUsesLightText: false, automatic);
        var forcedLight = PlayerForegroundPolicy.ResolvePresentation(
            PlayerForegroundMode.LightText, highContrast: false, themeUsesLightText: false, automatic);
        var forcedDark = PlayerForegroundPolicy.ResolvePresentation(
            PlayerForegroundMode.DarkText, highContrast: false, themeUsesLightText: true, automatic);

        Assert.IsTrue(highContrast.UsesSystemColors);
        Assert.IsTrue(forcedLight.UsesLightText);
        Assert.IsFalse(forcedDark.UsesLightText);
    }

    [TestMethod]
    public void ResolvePresentation_UsesThemeOnlyWhenAutomaticSampleIsUnavailable()
    {
        var fallback = PlayerForegroundPolicy.ResolvePresentation(
            PlayerForegroundMode.Automatic, highContrast: false, themeUsesLightText: true, automaticDecision: null);
        var sampled = PlayerForegroundPolicy.ResolvePresentation(
            PlayerForegroundMode.Automatic, highContrast: false, themeUsesLightText: false,
            new PlayerForegroundDecision(true));

        Assert.IsTrue(fallback.UsesLightText);
        Assert.IsTrue(sampled.UsesLightText);
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
