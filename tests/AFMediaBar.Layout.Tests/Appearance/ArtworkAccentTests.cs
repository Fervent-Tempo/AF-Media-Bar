// 验证“从封面取色”的入口：透明封面不产生颜色，换封面/清空时旧结果不残留，强调色只在 FromCover 模式使用封面色。
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Settings;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

[TestClass]
[DoNotParallelize]
public sealed class ArtworkAccentTests
{
    [TestMethod]
    public void OpaqueArtworkProducesAccentForBothThemes()
    {
        StaTest.Run(_ =>
        {
            var artwork = Bitmap([0, 0, 255, 255]);

            Assert.IsNotNull(ArtworkAccentExtractor.Extract(artwork, darkTheme: true));
            Assert.IsNotNull(ArtworkAccentExtractor.Extract(artwork, darkTheme: false));
            return Task.CompletedTask;
        });
    }

    [TestMethod]
    public void TransparentArtworkHasNoAccent()
    {
        StaTest.Run(_ =>
        {
            Assert.IsNull(ArtworkAccentExtractor.Extract(Bitmap([255, 0, 0, 0]), darkTheme: false));
            return Task.CompletedTask;
        });
    }

    [TestMethod]
    public void ClearingArtworkClearsPreviousAccent()
    {
        StaTest.Run(_ =>
        {
            var service = new CoverAccentColorService();
            var changes = 0;
            service.Changed += () => changes++;

            service.Update(Bitmap([0, 0, 255, 255]));
            Assert.IsNotNull(service.Resolve(darkTheme: false), "有封面时应产生封面色。");
            Assert.IsNotNull(service.Resolve(darkTheme: true), "两种主题都应预先算好。");

            service.Update(null);
            Assert.IsNull(service.Resolve(darkTheme: false), "封面清空后不得沿用上一首颜色。");
            Assert.IsNull(service.Resolve(darkTheme: true));
            Assert.AreEqual(2, changes, "换封面和清空各触发一次变化。");
            return Task.CompletedTask;
        });
    }

    [TestMethod]
    public void AccentPolicyUsesCoverOnlyInFromCoverMode()
    {
        var system = Colors.Blue;
        var cover = Colors.Red;

        Assert.AreEqual(cover, AccentColorPolicy.ResolveRequested(system, AccentColorMode.FromCover, null, cover));
        Assert.AreEqual(system, AccentColorPolicy.ResolveRequested(system, AccentColorMode.FromCover, null, null),
            "没有封面色时必须回退系统色。");
        Assert.AreEqual(Colors.Green,
            AccentColorPolicy.ResolveRequested(system, AccentColorMode.Custom, "#008000", cover),
            "自定义模式优先使用用户颜色，不受封面色影响。");
    }

    /// <summary>
    /// 封面主色可能整张图都是同一个色调，压在相近的背景上会让进度条、频谱看不见。
    /// 从封面取色时必须把强调色拉到最低对比度之上，而系统色与自选色不能被擅自改动。
    /// A cover's dominant colour can fill the whole image and sit too close to the surface to keep the progress bar and the
    /// spectrum readable. The artwork accent has to be lifted to the minimum contrast; system and custom accents must not be touched.
    /// </summary>
    [TestMethod]
    public void ArtworkAccentGetsContrastGuardButOtherSourcesDoNot()
    {
        var darkCover = Color.FromRgb(0x1C, 0x22, 0x30);
        var lifted = AccentColorPolicy.Build(darkCover, darkTheme: true, highContrast: false, fromArtwork: true).Accent;
        Assert.AreNotEqual(darkCover, lifted, "深色主题下过暗的封面主色必须被提亮。");
        Assert.IsTrue(lifted.R + lifted.G + lifted.B > darkCover.R + darkCover.G + darkCover.B, "调整方向必须朝背景的反方向。");
        Assert.IsTrue(
            AccentColorPolicy.ContrastRatio(lifted, AccentColorPolicy.DarkSurface) >= AccentColorPolicy.MinimumSurfaceContrast - 0.001,
            "提亮后必须达到最低对比度。");

        var lightCover = Color.FromRgb(0xF2, 0xEF, 0xD8);
        var deepened = AccentColorPolicy.Build(lightCover, darkTheme: false, highContrast: false, fromArtwork: true).Accent;
        Assert.IsTrue(deepened.R + deepened.G + deepened.B < lightCover.R + lightCover.G + lightCover.B, "浅色主题下过亮的封面主色必须被压暗。");
        Assert.IsTrue(
            AccentColorPolicy.ContrastRatio(deepened, AccentColorPolicy.LightSurface) >= AccentColorPolicy.MinimumSurfaceContrast - 0.001,
            "压暗后必须达到最低对比度。");

        Assert.AreEqual(darkCover,
            AccentColorPolicy.Build(darkCover, darkTheme: true, highContrast: false).Accent,
            "系统强调色与自选色不能被对比度兜底改动。");
    }

    [TestMethod]
    public void ReadableArtworkAccentKeepsItsExactColour()
    {
        var cover = Color.FromRgb(0x37, 0x8A, 0xDD);
        Assert.IsTrue(AccentColorPolicy.ContrastRatio(cover, AccentColorPolicy.DarkSurface) >= AccentColorPolicy.MinimumSurfaceContrast,
            "前提：这个封面色在深色背景上本来就够清楚。");
        Assert.AreEqual(cover,
            AccentColorPolicy.Build(cover, darkTheme: true, highContrast: false, fromArtwork: true).Accent,
            "达标时不做任何改动，配色忠实度优先。");
    }

    private static BitmapSource Bitmap(byte[] bgra)
    {
        var bitmap = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, bgra, 4);
        bitmap.Freeze();
        return bitmap;
    }
}
