// 应用容器拥有状态协调与按需频谱采样；最后一个呈现租约释放后停止计时。
using System.Windows;
using System.Windows.Threading;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Settings;

namespace AFMediaBar.Classes.Services;

/// <summary>将播放状态和共享频谱变成宠物的呈现输入。</summary>
public sealed class PetCompanionService : IDisposable
{
    private readonly AudioMonitorService _audio;
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _timer;
    private readonly List<Action<string, double>> _views = [];
    private readonly float[] _bands = new float[16];
    private MediaSnapshot _snapshot = MediaSnapshot.Disconnected;
    private long _idleSince = Environment.TickCount64;
    private long _happyUntil;
    private bool _pending;
    private bool _disposed;
    private int _generation;
    private double _average;
    private double _beat;
    private long _lastBeat;

    public PetCompanionService(AudioMonitorService audio)
    {
        _audio = audio;
        _dispatcher = Application.Current.Dispatcher;
        _timer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher) { Interval = TimeSpan.FromMilliseconds(50) };
        _timer.Tick += OnTick;
    }

    public void UpdateSnapshot(MediaSnapshot snapshot)
    {
        if (!_dispatcher.CheckAccess()) { _dispatcher.BeginInvoke(() => UpdateSnapshot(snapshot)); return; }
        if (_disposed) return;
        if (_snapshot.IsConnected && snapshot.IsConnected && _snapshot.Title != snapshot.Title)
            _happyUntil = Environment.TickCount64 + 1800;
        if (_snapshot.IsConnected != snapshot.IsConnected) _idleSince = Environment.TickCount64;
        _snapshot = snapshot;
    }

    public IDisposable Subscribe(Action<string, double> view)
    {
        _dispatcher.VerifyAccess();
        ObjectDisposedException.ThrowIf(_disposed, this);
        _views.Add(view);
        if (!_timer.IsEnabled) { _generation++; _timer.Start(); }
        Publish();
        return new Lease(this, view);
    }

    private void Publish()
    {
        var now = Environment.TickCount64;
        var animation = now < _happyUntil ? "happy" : _snapshot.IsPlaying ? "listening" :
            !_snapshot.IsConnected && now - _idleSince > 30000 ? "drowsy" : "idle";
        foreach (var view in _views.ToArray()) view(animation, _beat);
    }

    private async void OnTick(object? sender, EventArgs args)
    {
        if (_disposed || _pending) return;
        var generation = _generation;
        var settings = SettingsManager.Current.PetComponent.Normalize();
        _beat *= 0.68;
        if (settings.MusicMotionEnabled && _snapshot.IsPlaying && !SystemParameters.HighContrast && MotionPolicy.ResolveCurrent().Mode == MotionMode.Full)
        {
            _pending = true;
            try
            {
                var sample = await _audio.GetSpectrumAsync(_bands, _bands.Length);
                if (_disposed || generation != _generation) return;
                if (sample.IsAvailable && _audio.IsCaptureCurrent(sample.CaptureVersion))
                {
                    var energy = (_bands[0] + _bands[1] + _bands[2]) / 3d;
                    _average += (energy - _average) * 0.12;
                    // ponytail: 能量突增检测不预测 BPM；需要稳定节拍网格时再引入节拍跟踪。
                    if (energy > Math.Max(0.025, _average * (1 + 0.65 * 100 / settings.MusicSensitivity)) && Environment.TickCount64 - _lastBeat > 180)
                    { _beat = Math.Clamp(energy * 3 * settings.MusicSensitivity / 100, 0, 1); _lastBeat = Environment.TickCount64; }
                }
                else { _beat = 0; _average = 0; }
            }
            catch (Exception error) { _beat = 0; AppLogService.Current?.Warn("Pet", error.Message); }
            finally { _pending = false; }
        }
        else { _beat = 0; _average = 0; }
        if (!_disposed && generation == _generation) Publish();
    }

    private sealed class Lease(PetCompanionService owner, Action<string, double> view) : IDisposable
    {
        private bool _released;
        public void Dispose()
        {
            if (_released) return;
            _released = true;
            owner._views.Remove(view);
            if (owner._views.Count == 0) { owner._generation++; owner._timer.Stop(); owner._beat = owner._average = 0; }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _generation++;
        _timer.Stop();
        _timer.Tick -= OnTick;
        _views.Clear();
    }
}
