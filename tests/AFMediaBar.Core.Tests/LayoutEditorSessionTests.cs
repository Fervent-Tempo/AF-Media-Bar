using AFMediaBar.Layout.Editing;
using AFMediaBar.Layout.Defaults;
using AFMediaBar.Models;

namespace AFMediaBar.Core.Tests;

[TestClass]
public sealed class LayoutEditorSessionTests
{
    [TestMethod]
    public void ApplyUndoAndRedoPreserveDocumentHistory()
    {
        var original = LayoutDefaultTemplates.LoadDocument();
        var session = new LayoutEditorSession(original);
        var updated = original with { SchemaVersion = original.SchemaVersion + 1 };

        Assert.IsTrue(session.TryApply(_ => updated));
        Assert.AreSame(updated, session.Document);
        Assert.IsTrue(session.CanUndo);

        Assert.IsTrue(session.Undo());
        Assert.AreSame(original, session.Document);
        Assert.IsTrue(session.CanRedo);

        Assert.IsTrue(session.Redo());
        Assert.AreSame(updated, session.Document);
    }

    [TestMethod]
    public void SelectionAndPreviewAreIndependentFromDocumentMutation()
    {
        var document = LayoutDefaultTemplates.LoadDocument();
        var session = new LayoutEditorSession(document);
        var changed = 0;
        session.StateChanged += (_, _) => changed++;

        session.Select("container-1");
        session.SetPreview(LayoutGridRect.Unit(2, 3));

        Assert.AreEqual("container-1", session.SelectedInstanceId);
        Assert.AreEqual(LayoutGridRect.Unit(2, 3), session.PreviewBounds);
        Assert.IsNull(session.LastError);
        Assert.AreEqual(2, changed);
    }
}
