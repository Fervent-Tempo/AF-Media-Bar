// 同一浓度必须生成同一个窗口遮罩和内容表面；不再按材质模式分叉。
// The same concentration must produce one window overlay and one content surface regardless of the material mode.
using System.Windows;
using System.Windows.Media;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Settings;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Wpf.Ui.Appearance;

namespace AFMediaBar.Layout.Tests;

[TestClass]
[DoNotParallelize]
public sealed class MaterialMaskResourceTests
{
    [TestMethod]
    public void SameConcentrationUsesSameMaskForMicaAndAcrylic()
    {
        StaTest.Run(_ =>
        {
            if (SystemParameters.HighContrast)
            {
                Assert.Inconclusive("High contrast intentionally publishes system brushes.");
                return Task.CompletedTask;
            }

            var resources = new ResourceDictionary();
            var renderer = new ApplicationAppearanceResources(resources);
            var accent = new AccentPalette(
                Colors.RoyalBlue, Colors.CornflowerBlue, Colors.MediumBlue, Colors.LightSkyBlue, Colors.White);
            var settings = AppearanceSettings.Default with
            {
                BackdropMode = ApplicationBackdropMode.Mica,
                BackdropTintOpacityPercent = 60
            };
            renderer.Apply(settings, ApplicationTheme.Light, accent);
            var overlay = (SolidColorBrush)resources["AfWindowMaterialOverlayBrush"];
            Assert.AreEqual(153, overlay.Color.A, "60% concentration must produce a 153 alpha overlay.");
            var micaCard = (SolidColorBrush)resources["AfSettingsCardBrush"];
            Assert.AreSame(micaCard, resources["AfMaterialCardBrush"], "Settings and popup cards must share one surface brush.");

            renderer.Apply(settings with { BackdropMode = ApplicationBackdropMode.Acrylic }, ApplicationTheme.Light, accent);
            Assert.AreEqual(micaCard.Color.A, ((SolidColorBrush)resources["AfSettingsCardBrush"]).Color.A,
                "The same concentration must not produce different card masks for Mica and Acrylic.");

            renderer.Apply(settings with { BackdropTintOpacityPercent = 100 }, ApplicationTheme.Light, accent);
            Assert.AreEqual(byte.MaxValue, ((SolidColorBrush)resources["AfWindowMaterialOverlayBrush"]).Color.A,
                "100% concentration must produce an opaque overlay.");
            return Task.CompletedTask;
        });
    }
}
