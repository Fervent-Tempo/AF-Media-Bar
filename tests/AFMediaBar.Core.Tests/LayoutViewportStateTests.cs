using System.Windows;
using AFMediaBar.LayoutEditor.Wpf.Input;

namespace AFMediaBar.Core.Tests;

[TestClass]
public sealed class LayoutViewportStateTests
{
    [TestMethod]
    public void ZoomAroundKeepsViewportCenterStable()
    {
        var state = new LayoutViewportState();
        state.Set(new Point(20, 10), 1);

        state.ZoomAround(new Point(100, 80), 120);

        Assert.AreEqual(1.15, state.Scale, 0.0001);
        Assert.AreEqual(8, state.Translate.X, 0.0001);
        Assert.AreEqual(-0.5, state.Translate.Y, 0.0001);
    }
}
