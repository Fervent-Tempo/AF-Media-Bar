// 拥有单个呈现宿主的计时器、采样缓冲和代际；借用共享音频服务，释放后丢弃在途结果。
using System.Windows.Threading;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Settings;

namespace AFMediaBar.Components;

internal sealed class SpectrumPresentationSession : IDisposable
{
    private readonly AudioMonitorService _monitor;
    private readonly Func<SpectrumComponentSettings> _settings;
    private readonly Func<bool> _canPublish;
    private readonly Action<float[], int> _publish;
    private readonly Action<Exception> _reportFailure;
    private readonly DispatcherTimer _timer;
    private readonly float[] _worker = new float[SpectrumComponentSettings.MaximumBandCount];
    private readonly float[] _frame = new float[SpectrumComponentSettings.MaximumBandCount];
    private bool _pending;
    private bool _hasFrame;
    private bool _disposed;
    private int _generation;

    internal SpectrumPresentationSession(AudioMonitorService monitor, Dispatcher dispatcher,
        Func<SpectrumComponentSettings> settings, Func<bool> canPublish,
        Action<float[], int> publish, Action<Exception> reportFailure)
    {
        _monitor = monitor;
        _settings = settings;
        _canPublish = canPublish;
        _publish = publish;
        _reportFailure = reportFailure;
        _timer = new DispatcherTimer(DispatcherPriority.Normal, dispatcher)
        { Interval = TimeSpan.FromMilliseconds(50) };
        _timer.Tick += Tick;
    }

    internal TimeSpan Interval { get => _timer.Interval; set => _timer.Interval = value; }
    internal bool IsEnabled => _timer.IsEnabled;
    internal void Invalidate() => _generation++;

    internal void Start()
    {
        if (_disposed || _timer.IsEnabled) return;
        _generation++;
        _timer.Start();
    }

    internal void Stop()
    {
        _generation++;
        _timer.Stop();
    }

    internal void Clear()
    {
        if (_disposed || !_hasFrame) return;
        Array.Clear(_frame);
        _publish(_frame, _settings().Normalize().BandCount);
        _hasFrame = false;
    }

    private async void Tick(object? sender, EventArgs args)
    {
        if (_disposed || _pending || !_canPublish()) return;
        var generation = _generation;
        var count = _settings().Normalize().BandCount;
        _pending = true;
        try
        {
            var sample = await _monitor.GetSpectrumAsync(_worker, count);
            if (_disposed || generation != _generation || !_canPublish()) return;
            // 宿主旧请求不能发布；当前宿主失去有效采集时则必须清除已经显示的旧频谱。
            if (!sample.IsAvailable || !_monitor.IsCaptureCurrent(sample.CaptureVersion))
            {
                Clear();
                return;
            }
            Array.Copy(_worker, _frame, count);
            _hasFrame = true;
            _publish(_frame, count);
        }
        catch (Exception exception)
        {
            if (!_disposed && generation == _generation)
            {
                _reportFailure(exception);
                if (_canPublish()) Clear();
            }
        }
        finally { _pending = false; }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        _timer.Tick -= Tick;
    }
}
