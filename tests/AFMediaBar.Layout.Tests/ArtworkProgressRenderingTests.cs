// 在独立STA上检查封面进度绘图的几何复用、尺寸更新和可变主题画刷，不加载真实媒体。
using System.Reflection;
using System.Windows;
using System.Windows.Media;
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
                return visual.Drawing.Children.OfType<GeometryDrawing>().ToArray();
            }

            var first = Draw();
            control.Progress = 0.5;
            var advanced = Draw();
            Assert.AreSame(first[0].Geometry, advanced[0].Geometry, "进度变化不应重建完整轨道。");
            Assert.AreNotSame(first[1].Geometry, advanced[1].Geometry);
            Assert.IsTrue(advanced[1].Geometry.IsFrozen);
            var repeated = Draw();
            Assert.AreSame(advanced[1].Geometry, repeated[1].Geometry, "相同进度的重绘应复用路径。");

            brush.Color = Colors.Red;
            var recolored = Draw();
            Assert.AreSame(advanced[1].Geometry, recolored[1].Geometry);
            Assert.AreEqual(Colors.Red, ((SolidColorBrush)recolored[2].Pen.Brush).Color);
            Assert.IsFalse(brush.IsFrozen, "缓存不能冻结调用方的主题画刷。");

            control.Measure(new Size(80, 40));
            control.Arrange(new Rect(0, 0, 80, 40));
            var resized = Draw();
            Assert.AreNotSame(advanced[0].Geometry, resized[0].Geometry);
            Assert.AreNotEqual(advanced[0].Geometry.Bounds, resized[0].Geometry.Bounds);
            control.Progress = 2;
            var complete = Draw();
            Assert.AreSame(complete[0].Geometry, complete[1].Geometry, "完整进度可直接复用轨道。");
            control.Progress = double.NaN;
            Assert.AreEqual(1, Draw().Length);
            return Task.CompletedTask;
        });
    }
}
