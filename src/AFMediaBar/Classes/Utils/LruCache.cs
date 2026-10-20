// 线程安全的有界最近最少使用缓存。

namespace AFMediaBar.Classes.Utils;

/// <summary>
/// 提供线程安全、固定容量（以及可选固定总代价）的最近最少使用缓存。
/// Provides a thread-safe least-recently-used cache with a fixed capacity and, optionally, a fixed total cost.
///
/// 为什么要有"总代价"这一档：条数与内存并不是一回事。歌词缓存里一条纯文本歌词和一条逐字歌词差着几十倍，
/// 只按条数封顶时，64 条逐字歌词可能比 64 条纯文本歌词多占一个数量级——而缓存要防的正是这个。
/// A total cost exists because entry count and memory are not the same thing. Inside the lyric cache a plain-text entry and a syllable-synced one differ by
/// tens of times, so an entry-count cap alone lets 64 syllable-synced entries take an order of magnitude more memory than 64 plain-text ones — and that is
/// exactly what the cache is supposed to prevent.
/// </summary>
/// <typeparam name="TKey">键类型。/ Key type.</typeparam>
/// <typeparam name="TValue">值类型。/ Value type.</typeparam>
public sealed class LruCache<TKey, TValue> where TKey : notnull
{
    private readonly int _capacity;
    private readonly Func<TValue, long>? _costSelector;
    private readonly long _maximumCost;
    private readonly TimeProvider _timeProvider;
    private readonly Dictionary<TKey, LinkedListNode<CacheEntry>> _map;
    private readonly LinkedList<CacheEntry> _lruList = [];
    private readonly object _sync = new();
    private long _currentCost;

    private sealed class CacheEntry(TKey key, TValue value, long cost)
    {
        public TKey Key { get; } = key;
        public TValue Value { get; set; } = value;
        public long Cost { get; set; } = cost;
        public long WrittenAt { get; set; }
        public TimeSpan? Lifetime { get; set; }
    }

    /// <summary>创建只按条数封顶的缓存。/ Creates a cache capped by entry count alone.</summary>
    /// <param name="capacity">最大条数。/ Maximum number of entries.</param>
    public LruCache(int capacity)
        : this(capacity, costSelector: null, maximumCost: 0)
    {
    }

    /// <summary>
    /// 创建同时按条数与总代价封顶的缓存。
    /// Creates a cache capped by both entry count and total cost.
    /// </summary>
    /// <param name="capacity">最大条数。/ Maximum number of entries.</param>
    /// <param name="costSelector">取单条代价的函数；为空时只按条数封顶。/ Cost selector for one entry, or null to cap by entry count alone.</param>
    /// <param name="maximumCost">总代价上限；0 表示不限制，负值无效。</param>
    /// <param name="timeProvider">过期检查使用的单调时钟；省略时使用系统时钟。</param>
    public LruCache(int capacity, Func<TValue, long>? costSelector, long maximumCost, TimeProvider? timeProvider = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumCost);

        _capacity = capacity;
        _costSelector = costSelector;
        _maximumCost = costSelector is null ? 0 : maximumCost;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _map = new Dictionary<TKey, LinkedListNode<CacheEntry>>(capacity);
    }

    /// <summary>当前条数。/ The current number of entries.</summary>
    public int Count
    {
        get
        {
            lock (_sync)
            {
                return _map.Count;
            }
        }
    }

    /// <summary>当前总代价；没有配置代价函数时恒为 0。/ The current total cost, always 0 while no cost selector is configured.</summary>
    public long CurrentCost
    {
        get
        {
            lock (_sync)
            {
                return _currentCost;
            }
        }
    }

    /// <summary>读取一个键并把它的新鲜度提到最新；未命中时返回 <see langword="false"/>。/ Reads one key and promotes it to newest; returns <see langword="false"/> on a miss.</summary>
    /// <param name="key">键。/ Key.</param>
    /// <param name="value">命中的值；未命中时为默认值。/ The value on a hit, or the default value on a miss.</param>
    /// <returns>是否命中。/ Whether the key was present.</returns>
    public bool TryGetValue(TKey key, out TValue? value)
    {
        lock (_sync)
        {
            if (key is null)
            {
                value = default;
                return false;
            }

            if (_map.TryGetValue(key, out var node))
            {
                if (node.Value.Lifetime is { } lifetime &&
                    _timeProvider.GetElapsedTime(node.Value.WrittenAt) >= lifetime)
                {
                    Remove(node);
                    value = default;
                    return false;
                }
                _lruList.Remove(node);
                _lruList.AddFirst(node);
                value = node.Value.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    /// <summary>
    /// 写入一个键值：已存在的键只更新值并把新鲜度提到最新，之后按条数与总代价淘汰最久未使用的条目。
    /// Writes one entry: an existing key only updates its value and promotes it, after which the least recently used entries are evicted until both the entry
    /// count and the total cost are within their limits.
    /// </summary>
    /// <param name="key">键。/ Key.</param>
    /// <param name="value">值。/ Value.</param>
    /// <param name="lifetime">可选有效期，从写入时计算；读取不会延长。必须为正值。</param>
    /// <exception cref="ArgumentOutOfRangeException">代价为负或有效期不为正。</exception>
    /// <exception cref="OverflowException">总代价超过 Int64 范围；保留原缓存状态。</exception>
    public void Set(TKey key, TValue value, TimeSpan? lifetime = null)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (lifetime is { } duration && duration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(lifetime));
        // Estimation may walk an entire lyric document; keep caller work outside the cache lock.
        var cost = CostOf(value);
        ArgumentOutOfRangeException.ThrowIfNegative(cost);
        lock (_sync)
        {
            if (_map.TryGetValue(key, out var existing))
            {
                var updatedCost = checked(_currentCost - existing.Value.Cost + cost);
                var writtenAt = _timeProvider.GetTimestamp();
                existing.Value.Value = value;
                existing.Value.Cost = cost;
                existing.Value.WrittenAt = writtenAt;
                existing.Value.Lifetime = lifetime;
                _currentCost = updatedCost;
                _lruList.Remove(existing);
                _lruList.AddFirst(existing);
                TrimToLimits();
                return;
            }

            var totalCost = checked(_currentCost + cost);
            var node = new LinkedListNode<CacheEntry>(new CacheEntry(key, value, cost)
            {
                WrittenAt = _timeProvider.GetTimestamp(),
                Lifetime = lifetime
            });
            _map.Add(key, node);
            _lruList.AddFirst(node);
            _currentCost = totalCost;

            TrimToLimits();
        }
    }

    /// <summary>
    /// 清空所有条目，用于"设置变了，缓存结果不再适用"的场景。
    /// Clears every entry, for the case where a settings change made the cached results no longer applicable.
    /// </summary>
    public void Clear()
    {
        lock (_sync)
        {
            _map.Clear();
            _lruList.Clear();
            _currentCost = 0;
        }
    }

    private long CostOf(TValue value) => _costSelector?.Invoke(value) ?? 0;

    private void Remove(LinkedListNode<CacheEntry> node)
    {
        _map.Remove(node.Value.Key);
        _lruList.Remove(node);
        _currentCost -= node.Value.Cost;
    }

    /// <summary>
    /// 淘汰最久未使用的条目，直到条数与总代价都在上限内。
    /// Evicts the least recently used entries until both the entry count and the total cost are within their limits.
    ///
    /// 关键的一条：**永远留下刚写入的那一条**（`_map.Count > 1`）。单条本身就超过总代价上限时（例如一首超长的逐字歌词），
    /// 若把它自己淘汰掉，缓存就会变成"每次取都未命中再取一次"的抖动——那比多留一份内存更糟。
    /// One rule matters here: the entry just written is **always kept** (`_map.Count > 1`). When a single entry exceeds the cost ceiling on its own — a very
    /// long syllable-synced lyric, for instance — evicting it would turn the cache into a thrash that misses and refetches on every lookup, which is worse
    /// than keeping one entry over budget.
    /// </summary>
    private void TrimToLimits()
    {
        while (_map.Count > _capacity ||
               (_maximumCost > 0 && _currentCost > _maximumCost && _map.Count > 1))
        {
            var leastRecent = _lruList.Last;
            if (leastRecent is null)
            {
                return;
            }

            Remove(leastRecent);
        }
    }
}
