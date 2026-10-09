// 合并后台歌词转换请求，限制待处理任务和结果缓存；不持有窗口或向 UI 直接写入结果。
using System.Diagnostics;
using AFMediaBar.Classes.Settings;
using AFMediaBar.Classes.Utils;

namespace AFMediaBar.Classes.Services.Lyrics;

internal sealed class LyricsChineseConversionCache(
    Func<string, LyricsChineseConversionMode, string> convert,
    int capacity = 256,
    int pendingCapacity = 32)
{
    private readonly object _gate = new();
    private readonly LruCache<(string Text, LyricsChineseConversionMode Mode), string> _results = new(capacity);
    private readonly Queue<(string Text, LyricsChineseConversionMode Mode)> _queue = new();
    private readonly HashSet<(string Text, LyricsChineseConversionMode Mode)> _pending = new();
    private Task? _worker;

    public event Action? Updated;

    public string GetOrRequest(string text, LyricsChineseConversionMode mode)
    {
        lock (_gate)
        {
            var key = (text, mode);
            if (_results.TryGetValue(key, out var result))
                return result!;
            if (!_pending.Add(key))
                return text;

            // 队列满时淘汰尚未开始的旧请求，优先处理最新曲目和方向。
            if (_queue.Count >= pendingCapacity)
                _pending.Remove(_queue.Dequeue());
            _queue.Enqueue(key);
            _worker ??= Task.Run(ProcessQueue);
            return text;
        }
    }

    internal Task WaitForIdleAsync()
    {
        lock (_gate)
            return _worker ?? Task.CompletedTask;
    }

    private void ProcessQueue()
    {
        while (true)
        {
            (string Text, LyricsChineseConversionMode Mode) key;
            lock (_gate)
            {
                if (_queue.Count == 0)
                {
                    _worker = null;
                    return;
                }
                key = _queue.Dequeue();
            }

            string result;
            try
            {
                result = convert(key.Text, key.Mode);
            }
            catch (Exception ex)
            {
                // 同一失败文本保留原文，避免每帧重试并阻断后续请求。
                Debug.WriteLine($"[Lyrics] Chinese conversion failed: {ex}");
                result = key.Text;
            }

            lock (_gate)
            {
                _results.Set(key, result);
                _pending.Remove(key);
            }
            try
            {
                Updated?.Invoke();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Lyrics] Chinese conversion notification failed: {ex}");
            }
        }
    }
}
