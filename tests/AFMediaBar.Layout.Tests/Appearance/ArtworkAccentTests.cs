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

    private static BitmapSource Bitmap(byte[] bgra)
    {
        var bitmap = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, bgra, 4);
        bitmap.Freeze();
        return bitmap;
    }
}
