using AFMediaBar.Layout.Models;

namespace AFMediaBar.Layout.Editing;

/// <summary>
/// Owns editor state independently from WPF controls. Pointer mapping and
/// constraint calculation remain outside this state holder.
/// </summary>
public sealed class LayoutEditorSession
{
    private readonly Stack<LayoutDocument> _undo = new();
    private readonly Stack<LayoutDocument> _redo = new();

    public LayoutEditorSession(LayoutDocument document, LayoutProfileKey profileKey = LayoutProfileKey.Horizontal)
    {
        Document = document ?? throw new ArgumentNullException(nameof(document));
        ProfileKey = profileKey;
    }

    public LayoutDocument Document { get; private set; }

    public LayoutProfileKey ProfileKey { get; private set; }

    public string? SelectedInstanceId { get; private set; }

    public LayoutGridRect? PreviewBounds { get; private set; }

    public string? LastError { get; private set; }

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    public event EventHandler? StateChanged;

    public void SelectProfile(LayoutProfileKey profileKey)
    {
        ProfileKey = profileKey;
        SelectedInstanceId = null;
        PreviewBounds = null;
        LastError = null;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Select(string? instanceId)
    {
        SelectedInstanceId = string.IsNullOrWhiteSpace(instanceId) ? null : instanceId;
        LastError = null;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetPreview(LayoutGridRect? bounds, string? error = null)
    {
        PreviewBounds = bounds;
        LastError = error;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public bool TryApply(Func<LayoutDocument, LayoutDocument?> mutation)
    {
        ArgumentNullException.ThrowIfNull(mutation);

        var updated = mutation(Document);
        if (updated is null || ReferenceEquals(updated, Document))
        {
            LastError = "The layout edit was rejected.";
            StateChanged?.Invoke(this, EventArgs.Empty);
            return false;
        }

        _undo.Push(Document);
        _redo.Clear();
        Document = updated;
        LastError = null;
        PreviewBounds = null;
        StateChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public bool Undo()
    {
        if (_undo.Count == 0)
        {
            return false;
        }

        _redo.Push(Document);
        Document = _undo.Pop();
        LastError = null;
        PreviewBounds = null;
        StateChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public bool Redo()
    {
        if (_redo.Count == 0)
        {
            return false;
        }

        _undo.Push(Document);
        Document = _redo.Pop();
        LastError = null;
        PreviewBounds = null;
        StateChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }
}
