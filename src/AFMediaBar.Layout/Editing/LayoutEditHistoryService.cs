using AFMediaBar.Layout.Models;

namespace AFMediaBar.Services;

/// <summary>
/// Layout-only bounded undo history. It has no persistence or UI dependencies.
/// The namespace remains compatible with existing callers while ownership moves to Layout.
/// </summary>
public sealed class LayoutEditHistoryService
{
    private const int MaximumSnapshotsPerProfile = 40;
    private readonly Dictionary<LayoutProfileKey, Stack<LayoutProfile>> _history = [];

    public void Record(LayoutProfile profile)
    {
        if (!_history.TryGetValue(profile.Key, out var snapshots))
        {
            snapshots = new Stack<LayoutProfile>();
            _history[profile.Key] = snapshots;
        }

        if (snapshots.TryPeek(out var latest) && latest == profile)
        {
            return;
        }

        snapshots.Push(profile);
        if (snapshots.Count <= MaximumSnapshotsPerProfile)
        {
            return;
        }

        var retained = snapshots.Take(MaximumSnapshotsPerProfile).Reverse().ToArray();
        snapshots.Clear();
        foreach (var snapshot in retained)
        {
            snapshots.Push(snapshot);
        }
    }

    public bool CanUndo(LayoutProfileKey key) =>
        _history.TryGetValue(key, out var snapshots) && snapshots.Count > 0;

    public bool TryUndo(LayoutProfileKey key, out LayoutProfile profile)
    {
        if (_history.TryGetValue(key, out var snapshots) && snapshots.TryPop(out profile!))
        {
            return true;
        }

        profile = null!;
        return false;
    }

    public void Clear(LayoutProfileKey key) => _history.Remove(key);
}
