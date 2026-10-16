// 设置列表的移动意图及快照校验，不持有界面或写入设置。
namespace AFMediaBar.Classes.Models;

/// <summary>一次移动请求；原始条目快照用于拒绝列表更新后的旧拖动。</summary>
public sealed class SettingsReorderRequest(object item, int targetIndex, IEnumerable<object> originalItems)
{
    public object Item { get; } = item;
    public int TargetIndex { get; } = targetIndex;
    private readonly object[] _originalItems = originalItems.ToArray();

    /// <summary>按稳定标识核对当前顺序，并返回有效的源、目标索引；原位请求不执行保存。</summary>
    public bool TryResolve<T, TKey>(IReadOnlyList<T> current, Func<T, TKey> key, out int sourceIndex, out int targetIndex, int minimumIndex = 0)
        where T : class
    {
        sourceIndex = -1;
        targetIndex = TargetIndex;
        if (Item is not T item || current.Count != _originalItems.Length || targetIndex < minimumIndex || targetIndex >= current.Count)
            return false;
        var comparer = EqualityComparer<TKey>.Default;
        for (var index = 0; index < current.Count; index++)
        {
            if (_originalItems[index] is not T original || !comparer.Equals(key(original), key(current[index]))) return false;
            if (comparer.Equals(key(item), key(current[index]))) sourceIndex = index;
        }
        return sourceIndex >= minimumIndex && sourceIndex != targetIndex;
    }
}
