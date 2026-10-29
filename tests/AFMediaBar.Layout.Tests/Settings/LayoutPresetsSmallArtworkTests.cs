// 检查小封面的预设尺寸；最终占位与相邻组件位置由真实控件布局验证。
using AFMediaBar.Classes.Models.Layout;
using AFMediaBar.Classes.Services.Layout;
using AFMediaBar.Classes.Settings;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

[TestClass]
public sealed class LayoutPresetsSmallArtworkTests
{
    [TestMethod]
    public void SmallArtworkShrinksThePresetCoverBeforeRuntimeArrangement()
    {
        var normal = LayoutPresets.GetLayout(WindowMode.Taskbar, LayoutOrientation.Horizontal);
        var small = LayoutPresets.GetLayout(WindowMode.Taskbar, LayoutOrientation.Horizontal, smallArtwork: true);
        var normalArtwork = Artwork(normal);
        var smallArtwork = Artwork(small);

        Assert.AreEqual(Math.Round(normalArtwork.Bounds.Width * 0.8), smallArtwork.Bounds.Width, 0.001);
        Assert.AreEqual(Math.Round(normalArtwork.Bounds.Height * 0.8), smallArtwork.Bounds.Height, 0.001);
        Assert.AreEqual(normalArtwork.Bounds.X + (normalArtwork.Bounds.Width - smallArtwork.Bounds.Width) / 2,
            smallArtwork.Bounds.X, 0.001);
        Assert.AreEqual(normalArtwork.Bounds.Y + (normalArtwork.Bounds.Height - smallArtwork.Bounds.Height) / 2,
            smallArtwork.Bounds.Y, 0.001);

        // 预设只调整封面，运行时再按实际尺寸排列相邻组件。
        CollectionAssert.AreEqual(BoundsExceptArtwork(normal), BoundsExceptArtwork(small));
    }

    [TestMethod]
    public void VerticalLayoutShrinksTheCoverByTheSameRatio()
    {
        var normal = Artwork(LayoutPresets.GetLayout(WindowMode.Taskbar, LayoutOrientation.Vertical));
        var small = Artwork(LayoutPresets.GetLayout(WindowMode.Taskbar, LayoutOrientation.Vertical, smallArtwork: true));

        Assert.AreEqual(Math.Round(normal.Bounds.Width * 0.8), small.Bounds.Width, 0.001);
        Assert.AreEqual(Math.Round(normal.Bounds.Height * 0.8), small.Bounds.Height, 0.001);
    }

    [TestMethod]
    public void SmallArtworkDefaultsOffAndSurvivesNormalize()
    {
        // 旧设置缺字段时保持关闭，升级不改变任何人的现有观感。
        Assert.IsFalse(new AppSettings().TaskbarExperience.SmallArtwork);
        var settings = new AppSettings().TaskbarExperience with { SmallArtwork = true };
        Assert.IsTrue(settings.Normalize().SmallArtwork);
    }

    private static ComponentConfig Artwork(LayoutSchema layout) =>
        layout.Components.Single(component => string.Equals(component.Id, LayoutComponentIds.Artwork, StringComparison.Ordinal));

    private static List<ComponentBounds> BoundsExceptArtwork(LayoutSchema layout) =>
        layout.Components
            .Where(component => !string.Equals(component.Id, LayoutComponentIds.Artwork, StringComparison.Ordinal))
            .Select(component => component.Bounds)
            .ToList();
}
