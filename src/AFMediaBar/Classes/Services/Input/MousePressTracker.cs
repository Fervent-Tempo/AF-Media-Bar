// 记录一次按压中的滚轮与松键，按事件时间消费；不持有平台资源，不依赖两个输入线程的先后顺序。
namespace AFMediaBar.Classes.Services;

internal sealed class MousePressTracker
{
    private const int Capacity = 16;
    private readonly List<Press> _presses = [];
    private Press? _active;
    private uint? _lastTransition;

    internal uint? StartedBefore(uint timestamp) =>
        _presses.LastOrDefault(press => IsAtOrBefore(press.StartedAt, timestamp))?.StartedAt;

    internal void SetDown(bool down, uint timestamp)
    {
        if (_lastTransition is uint last && !IsAtOrBefore(last, timestamp)) return;
        _lastTransition = timestamp;
        if (down)
        {
            if (_active is not null) return;
            _active = new(timestamp);
            _presses.Add(_active);
            if (_presses.Count > Capacity) _presses.RemoveAt(0);
        }
        else if (_active is { } active)
        {
            active.ReleasedAt = timestamp;
            _active = null;
        }
    }

    internal void Wheel(uint timestamp)
    {
        // 迟到的本地滚轮也只记入当时的按压，不能污染下一次真实点击。
        var press = _presses.LastOrDefault(item => IsAtOrBefore(item.StartedAt, timestamp) &&
            (item.ReleasedAt is not uint released || IsAtOrBefore(timestamp, released)));
        if (press is not null) press.HadWheel = true;
    }

    internal bool Consume(uint clickTime, uint? localPressStart = null)
    {
        var press = _presses.LastOrDefault(item => IsAtOrBefore(item.StartedAt, clickTime));
        if (press is null || press.ReleasedAt is not uint released || !IsAtOrBefore(released, clickTime) ||
            unchecked(clickTime - released) > 750 || press.Consumed || !press.HadWheel ||
            (localPressStart is uint floor && !IsAtOrBefore(floor, press.StartedAt))) return false;
        press.Consumed = true;
        return true;
    }

    internal void Clear() { _active = null; _lastTransition = null; _presses.Clear(); }
    private static bool IsAtOrBefore(uint first, uint second) => unchecked((int)(second - first)) >= 0;
    private sealed class Press(uint startedAt)
    {
        internal uint StartedAt { get; } = startedAt;
        internal uint? ReleasedAt { get; set; }
        internal bool HadWheel { get; set; }
        internal bool Consumed { get; set; }
    }
}
