using AFMediaBar.Models;

namespace AFMediaBar.Services;

/// <summary>
/// 为每个布局档案维护独立的有界撤销栈；不负责持久化，也不跨档案回放编辑。
/// Maintains a bounded undo stack per layout profile without persistence or cross-profile replay.
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

    public void Clear(LayoutProfileKey key)
    {
        _history.Remove(key);
    }
}
