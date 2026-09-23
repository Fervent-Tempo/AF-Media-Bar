using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using AFMediaBar.Classes.Utils;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>
/// 事件源上溯：鼠标事件落在文本的 <see cref="Run"/> 上时，源是 <c>ContentElement</c> 而不是 <c>Visual</c>，
/// 直接调用 <c>VisualTreeHelper.GetParent</c> 会抛"Run 不是 Visual 或 Visual3D"并把进程带崩。
/// 上溯助手必须同时处理可视元素与内容元素，并且对没有父级的节点返回 null 而不是抛异常。
/// Event-source walking: when a mouse event lands on a text <see cref="Run"/>, the source is a <c>ContentElement</c> rather than a
/// <c>Visual</c>, and calling <c>VisualTreeHelper.GetParent</c> directly throws "Run is not a Visual or Visual3D", taking the process
/// down. The walker has to handle visual and content elements alike, and return null for parentless nodes instead of throwing.
/// </summary>
[TestClass]
public sealed class VisualTreeWalkTests
{
    [TestMethod]
    public void WalksFromAnInlineRunToItsVisualAncestors()
    {
        RunOnSta(() =>
        {
            var border = new Border { Tag = "MediaAction" };
            var text = new TextBlock();
            var run = new Run("歌词");
            text.Inlines.Add(run);
            border.Child = text;

            // 从 Run 出发必须能走到承载它的 TextBlock 与更外层的可视祖先（旧实现会在这里抛异常）。
            // Walking from a Run has to reach the TextBlock that hosts it and the visual ancestors beyond (the old implementation
            // threw right here).
            var parent = VisualTreeWalk.GetParent(run);
            Assert.AreSame(text, parent);
            Assert.AreSame(border, VisualTreeWalk.GetParent(parent!));

            // 原有可视树路径不受影响。
            // The plain visual path is unchanged.
            Assert.AreSame(border, VisualTreeWalk.GetParent(text));
        });
    }

    [TestMethod]
    public void ReturnsNullForParentlessNodesInsteadOfThrowing()
    {
        RunOnSta(() =>
        {
            Assert.IsNull(VisualTreeWalk.GetParent(new Run("孤")));
            Assert.IsNull(VisualTreeWalk.GetParent(new DependencyObject()));
            Assert.IsNull(VisualTreeWalk.GetParent(null));
        });
    }

    private static void RunOnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.IsNull(failure, failure?.ToString());
    }
}
