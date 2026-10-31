// 双列教程卡片的布局：展开一张只影响它所在的列，其它列的高度与位置都不动。
using System.Windows;
using System.Windows.Controls;
using AFMediaBar.Components;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

[TestClass]
[DoNotParallelize]
public sealed class ColumnFlowPanelTests
{
    [TestMethod]
    public void KeepsEachChildAtItsOwnHeight() => SettingsMotionTests.RunSta(() =>
    {
        var panel = new ColumnFlowPanel { Columns = 2 };
        var cards = AddCards(panel, 60, 60, 300, 60);
        Layout(panel, 400);

        Assert.AreEqual(60, cards[0].ActualHeight);
        Assert.AreEqual(60, cards[1].ActualHeight);
        Assert.AreEqual(300, cards[2].ActualHeight);
        Assert.AreEqual(60, cards[3].ActualHeight);
        // 两列各自堆叠：左 60+300，右 60+60。
        Assert.AreEqual(360, panel.ActualHeight);
    });

    [TestMethod]
    public void ExpandingOneCardLeavesTheOtherColumnInPlace() => SettingsMotionTests.RunSta(() =>
    {
        var panel = new ColumnFlowPanel { Columns = 2 };
        var cards = AddCards(panel, 60, 60, 60, 60);
        Layout(panel, 400);
        var neighbourTop = Top(cards[1], panel);

        ((Border)cards[2].Child).Height = 300;
        panel.InvalidateMeasure();
        Layout(panel, 400);

        Assert.AreEqual(60, cards[1].ActualHeight, "另一列的卡片不应被撑高。");
        Assert.AreEqual(neighbourTop, Top(cards[1], panel), "另一列的卡片不应被推走。");
        Assert.AreEqual(300, cards[2].ActualHeight);
        Assert.AreEqual(360, panel.ActualHeight);
    });

    [TestMethod]
    public void SingleColumnKeepsOrder() => SettingsMotionTests.RunSta(() =>
    {
        var panel = new ColumnFlowPanel { Columns = 1 };
        var cards = AddCards(panel, 60, 60, 60);
        Layout(panel, 400);

        for (var i = 0; i < cards.Length; i++)
        {
            Assert.AreEqual(60, cards[i].ActualHeight);
            Assert.AreEqual(i * 60, Top(cards[i], panel), $"第 {i + 1} 张应紧接上一张。");
        }
        Assert.AreEqual(180, panel.ActualHeight);
    });

    private static Border[] AddCards(ColumnFlowPanel panel, params double[] heights) =>
        heights.Select(height =>
        {
            var card = new Border { Child = new Border { Height = height } };
            panel.Children.Add(card);
            return card;
        }).ToArray();

    private static void Layout(ColumnFlowPanel panel, double width)
    {
        panel.Measure(new Size(width, double.PositiveInfinity));
        panel.Arrange(new Rect(new Point(0, 0), panel.DesiredSize));
        panel.UpdateLayout();
    }

    private static double Top(UIElement element, UIElement relativeTo) =>
        element.TranslatePoint(new Point(0, 0), relativeTo).Y;
}
