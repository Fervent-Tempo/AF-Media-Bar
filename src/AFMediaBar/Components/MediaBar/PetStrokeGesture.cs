namespace AFMediaBar.Components;

// 仅识别局部轻抚；按键、离开和菜单由宿主清空，冷却跨清空保留。
internal sealed class PetStrokeGesture
{
    private double? _last;
    private double _distance;
    private int _direction, _reversals;
    private long _start, _cooldown;
    internal void Reset() { _last = null; _distance = 0; _direction = _reversals = 0; }
    internal bool Move(double x, double width, long now)
    {
        if (now < _cooldown) return false;
        if (_last is null || now - _start > 800) { Reset(); _last = x; _start = now; return false; }
        var delta = x - _last.Value;
        if (Math.Abs(delta) < Math.Max(1, width * 0.04)) return false;
        _last = x;
        var direction = Math.Sign(delta);
        if (_direction != 0 && direction != _direction) _reversals++;
        _direction = direction;
        _distance += Math.Abs(delta);
        if (_reversals < 2 || _distance < width / 2) return false;
        Reset(); _cooldown = now + 2200;
        return true;
    }
}
