using System.Windows;
using AFMediaBar.LayoutEditor.Wpf.Input;
using AFMediaBar.Services;

namespace AFMediaBar.Core.Tests;

[TestClass]
public sealed class LayoutEditorInteractionStateTests
{
    [TestMethod]
    public void ClearPlacementResetsGestureStateWithoutTouchingViewportState()
    {
        var state = new LayoutEditorInteractionState
        {
            PlacementTool = LayoutPlacementTool.Container(LayoutContainerKind.Static),
            IsDrawing = true,
            DragMoved = true,
            DrawStart = new Point(12, 16),
            DrawCandidate = new LayoutGridRect(1, 2, 3, 4),
            IsPanning = true
        };

        state.ClearPlacement();

        Assert.IsNull(state.PlacementTool);
        Assert.IsFalse(state.IsDrawing);
        Assert.IsFalse(state.DragMoved);
        Assert.IsNull(state.DrawCandidate);
        Assert.IsTrue(state.IsPanning);
    }
}
