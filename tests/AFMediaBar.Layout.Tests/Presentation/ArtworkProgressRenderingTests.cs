// 在独立STA上检查封面进度绘图的几何复用、尺寸更新和可变主题画刷，不加载真实媒体。
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using AFMediaBar.Classes.Settings;
using AFMediaBar.Components;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>封面进度重绘缓存的资源复用与失效回归。</summary>
[TestClass]
public sealed class ArtworkProgressRenderingTests
{
    [TestMethod]
    public void RedrawReusesFrozenPathsAndRefreshesSizeProgressAndStroke()
    {
        StaTest.Run(_ =>
        {
            var brush = new SolidColorBrush(Colors.Blue);
            var control = new ArtworkProgress { Progress = 0.25, Stroke = brush };
            control.Measure(new Size(48, 48));
            control.Arrange(new Rect(0, 0, 48, 48));
            var render = typeof(ArtworkProgress).GetMethod("OnRender", BindingFlags.Instance | BindingFlags.NonPublic)!
                .CreateDelegate<Action<DrawingContext>>(control);
            GeometryDrawing[] Draw()
            {
                var visual = new DrawingVisual();
                using (var context = visual.RenderOpen()) render(context);
                // 没有进度时控件不画任何东西，DrawingVisual.Drawing 会保持为空。
                return visual.Drawing?.Children.OfType<GeometryDrawing>().ToArray() ?? [];
            }

            var first = Draw();
            Assert.AreEqual(2, first.Length, "进度段只有深色衬底与强调色两层，未播放部分不画底轨。");
            Assert.AreSame(first[0].Geometry, first[1].Geometry, "衬底与强调色必须共用同一条进度路径。");

            control.Progress = 0.5;
            var advanced = Draw();
            Assert.AreNotSame(first[1].Geometry, advanced[1].Geometry);
            Assert.IsTrue(advanced[1].Geometry.IsFrozen);
            var repeated = Draw();
            Assert.AreSame(advanced[1].Geometry, repeated[1].Geometry, "相同进度的重绘应复用路径。");

            brush.Color = Colors.Red;
            var recolored = Draw();
            Assert.AreSame(advanced[1].Geometry, recolored[1].Geometry);
            Assert.AreEqual(Colors.Red, ((SolidColorBrush)recolored[1].Pen.Brush).Color);
            Assert.IsFalse(brush.IsFrozen, "缓存不能冻结调用方的主题画刷。");

            control.Measure(new Size(80, 40));
            control.Arrange(new Rect(0, 0, 80, 40));
            var resized = Draw();
            Assert.AreNotSame(advanced[1].Geometry, resized[1].Geometry);
            Assert.AreNotEqual(advanced[1].Geometry.Bounds, resized[1].Geometry.Bounds);

            control.Progress = 2;
            var complete = Draw();
            Assert.AreSame(complete[0].Geometry, complete[1].Geometry, "完整进度可直接复用整条路径。");
            // 线宽 2px、只缩进半个线宽：整条路径必须停在 1px 处，外沿才贴得住封面边缘。
            Assert.AreEqual(1, complete[0].Geometry.Bounds.Left, 0.001, "进度线外沿必须贴住封面边缘。");
            Assert.AreEqual(control.RenderSize.Width - 2, complete[0].Geometry.Bounds.Width, 0.001);

            control.Progress = double.NaN;
            Assert.AreEqual(0, Draw().Length, "没有进度时什么都不画，不留底轨。");
            return Task.CompletedTask;
        });
    }

    [TestMethod]
    public void FillStyleTintsThePlayedPartAndLeavesAWaterline()
    {
        StaTest.Run(_ =>
        {
            var control = new ArtworkProgress
            {
                Progress = 0.5,
                ProgressStyle = ArtworkProgressStyle.Fill,
                Stroke = Brushes.Blue,
                CornerRadius = 5.5
            };
            control.Measure(new Size(48, 48));
            control.Arrange(new Rect(0, 0, 48, 48));
            var render = typeof(ArtworkProgress).GetMethod("OnRender", BindingFlags.Instance | BindingFlags.NonPublic)!
                .CreateDelegate<Action<DrawingContext>>(control);

            var visual = new DrawingVisual();
            using (var context = visual.RenderOpen()) render(context);
            var drawings = Flatten(visual.Drawing).ToArray();
            Assert.AreEqual(3, drawings.Length, "注水由两层水波和一条水位线组成。");
            Assert.IsTrue(drawings[0].Geometry.Bounds.Bottom >= 47.9, "水波填充要盖到封面底部。");
            Assert.IsTrue(drawings[1].Geometry.Bounds.Bottom >= 47.9, "水波填充要盖到封面底部。");
            Assert.AreEqual(1.2, drawings[2].Pen?.Thickness ?? 0, 0.001, "水位线是细描边。");
            Assert.AreEqual(24, drawings[2].Geometry.Bounds.Top, 2.0, "水位线应在封面一半高度附近起伏。");
            return Task.CompletedTask;
        });
    }

    private static IEnumerable<GeometryDrawing> Flatten(Drawing? drawing)
    {
        switch (drawing)
        {
            case GeometryDrawing geometry:
                yield return geometry;
                break;
            case DrawingGroup group:
                foreach (var child in group.Children)
                    foreach (var nested in Flatten(child))
                        yield return nested;
                break;
        }
    }
}
