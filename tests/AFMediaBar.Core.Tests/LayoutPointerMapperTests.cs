using System.Windows;
using AFMediaBar.LayoutEditor.Wpf.Input;

namespace AFMediaBar.Core.Tests;

[TestClass]
public sealed class LayoutPointerMapperTests
{
    [TestMethod]
    public void ConvertsCanvasPointToPaddedGridCell()
    {
        var cell = LayoutPointerMapper.ToCell(new Point(52, 28), 8, 6);

        Assert.AreEqual(0, cell.X);
        Assert.AreEqual(-3, cell.Y);
    }
}
