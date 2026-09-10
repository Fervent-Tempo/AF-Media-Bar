// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

namespace AFMediaBar.Classes.Utils;

/// <summary>
/// 提供线程安全、固定容量的最近最少使用缓存。
/// Provides a thread-safe, fixed-capacity least-recently-used cache.
/// </summary>
internal sealed class LruCache<TKey, TValue> where TKey : notnull
{
    private readonly int _capacity;
    private readonly Dictionary<TKey, LinkedListNode<CacheEntry>> _map;
    private readonly LinkedList<CacheEntry> _lruList = [];
    private readonly object _sync = new();

    private sealed class CacheEntry(TKey key, TValue value)
    {
        public TKey Key { get; } = key;
        public TValue Value { get; set; } = value;
    }

    public LruCache(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);

        _capacity = capacity;
        _map = new Dictionary<TKey, LinkedListNode<CacheEntry>>(capacity);
    }

    public bool TryGetValue(TKey key, out TValue? value)
    {
        lock (_sync)
        {
            if (_map.TryGetValue(key, out var node))
            {
                _lruList.Remove(node);
                _lruList.AddFirst(node);
                value = node.Value.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    public void Set(TKey key, TValue value)
    {
        lock (_sync)
        {
            if (_map.TryGetValue(key, out var existing))
            {
                existing.Value.Value = value;
                _lruList.Remove(existing);
                _lruList.AddFirst(existing);
                return;
            }

            var node = new LinkedListNode<CacheEntry>(new CacheEntry(key, value));
            _lruList.AddFirst(node);
            _map[key] = node;

            if (_map.Count <= _capacity)
                return;

            var leastRecent = _lruList.Last;
            if (leastRecent == null)
                return;

            _lruList.RemoveLast();
            _map.Remove(leastRecent.Value.Key);
        }
    }
}
