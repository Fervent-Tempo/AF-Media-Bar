using AFMediaBar.Layout.Defaults;
using AFMediaBar.Layout.Editing;
using AFMediaBar.Layout.Models;
using AFMediaBar.Layout.Ports;

namespace AFMediaBar.Core.Tests;

[TestClass]
public sealed class LayoutEditorCommandProcessorTests
{
    [TestMethod]
    public void ResizeCommitsThroughConstraintPortAndSessionHistory()
    {
        var document = LayoutDefaultTemplates.LoadDocument();
        var session = new LayoutEditorSession(document);
        var target = document.Horizontal.Containers[0];
        var next = target.GridBounds! with { Width = target.GridBounds.Width + 1 };
        var processor = new LayoutEditorCommandProcessor(new StubConstraintEngine(next));

        Assert.IsTrue(processor.TryResize(session, target.InstanceId, LayoutEdge.Right, 1));
        Assert.IsTrue(session.CanUndo);
        Assert.AreEqual(next.Width, session.Document.Horizontal.Containers[0].GridBounds!.Width);
    }

    private sealed class StubConstraintEngine(LayoutGridRect updatedBounds) : ILayoutConstraintEngine
    {
        public LayoutMutationResult TrySetBounds(LayoutProfile profile, string instanceId, LayoutGridRect bounds) => Update(profile, instanceId, bounds);
        public LayoutMutationResult TryMove(LayoutProfile profile, string instanceId, int deltaX, int deltaY) => Update(profile, instanceId, updatedBounds);
        public LayoutMutationResult TryResize(LayoutProfile profile, string instanceId, LayoutEdge edge, int delta) => Update(profile, instanceId, updatedBounds);

        private static LayoutMutationResult Update(LayoutProfile profile, string instanceId, LayoutGridRect bounds)
        {
            var container = profile.Containers.First(item => item.InstanceId == instanceId);
            return new LayoutMutationResult(profile with { Containers = [container with { GridBounds = bounds }] }, []);
        }
    }
}
