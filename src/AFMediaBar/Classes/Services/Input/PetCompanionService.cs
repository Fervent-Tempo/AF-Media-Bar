// 应用容器拥有状态协调与按需频谱采样；最后一个呈现租约释放后停止计时。
using System.Windows;
using System.Windows.Threading;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Settings;
using AFMediaBar.Resources;

namespace AFMediaBar.Classes.Services;

/// <summary>将播放状态和共享频谱变成宠物的呈现输入。</summary>
public sealed class PetCompanionService : IDisposable
{
    private readonly PetVoiceService _voice;
    private readonly AudioMonitorService _audio;
    private readonly List<Func<string, Action?, bool>> _messages = [];
    private readonly IDisplayMonitorService _display;
    private int _phraseIndex;
    private long _reactionUntil;
    private string _reactionAnimation = "notifying";

    public string Speak()
    {
        _reactionAnimation = "happy";
        _reactionUntil = Environment.TickCount64 + 1800;
        Publish();
        var text = _snapshot.IsConnected && !string.IsNullOrWhiteSpace(_snapshot.Title)
            ? Translations.Format(_snapshot.IsPlaying ? "Widgets.Pet.Say.Playing" : "Widgets.Pet.Say.Paused", _snapshot.Title, _snapshot.Artist)
            : Translations.Get("Widgets.Pet.Say.Hello" + (_phraseIndex++ % 3));
        _voice.Play(text);
        return text;
    }

    public bool TryNotify(string title, string message, Action? activation = null)
    {
        if (_disposed || !_dispatcher.CheckAccess() || !SettingsManager.Current.PetComponent.NotificationsEnabled ||
            _display.IsForegroundWindowFullscreen()) return false;
        foreach (var present in _messages.ToArray())
        {
            try { if (!present(title + "\n" + message, activation)) continue; }
            catch (Exception error) { AppLogService.Current?.Warn("Pet", error.Message); continue; }
            _reactionAnimation = "notifying";
            _reactionUntil = Environment.TickCount64 + 1800;
            Publish();
            return true;
        }
        return false;
    }
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

    public PetCompanionService(AudioMonitorService audio, IDisplayMonitorService display, PetVoiceService voice)
    {
        _voice = voice;
        _audio = audio;
        _display = display;
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

    public IDisposable Subscribe(Action<string, double> view, Func<string, Action?, bool>? message = null)
    {
        _dispatcher.VerifyAccess();
        ObjectDisposedException.ThrowIf(_disposed, this);
        _views.Add(view);
        if (message is not null) _messages.Add(message);
        if (!_timer.IsEnabled) { _generation++; _timer.Start(); }
        Publish();
        return new Lease(this, view, message);
    }

    private void Publish()
    {
        var now = Environment.TickCount64;
        var animation = now < _reactionUntil ? _reactionAnimation : now < _happyUntil ? "happy" : _snapshot.IsPlaying ? "listening" :
            !_snapshot.IsConnected && now - _idleSince > 30000 ? "drowsy" : "idle";
        foreach (var view in _views.ToArray()) view(animation, now < _reactionUntil || now < _happyUntil ? 0 : _beat);
    }

    private async void OnTick(object? sender, EventArgs args)
    {
        if (_disposed || _pending) return;
        var generation = _generation;
        var settings = SettingsManager.Current.PetComponent.Normalize();
        _beat = _beat < 0.005 ? 0 : _beat * 0.68;
        if (!_voice.IsSpeaking && settings.MusicMotionEnabled && _snapshot.IsPlaying && !SystemParameters.HighContrast && MotionPolicy.ResolveCurrent().Mode == MotionMode.Full)
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

    private sealed class Lease(PetCompanionService owner, Action<string, double> view, Func<string, Action?, bool>? message) : IDisposable
    {
        private bool _released;
        public void Dispose()
        {
            if (_released) return;
            _released = true;
            owner._views.Remove(view);
            if (message is not null) owner._messages.Remove(message);
            if (owner._views.Count == 0) { owner._generation++; owner._timer.Stop(); owner._voice.Stop(); owner._beat = owner._average = 0; }
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
        _messages.Clear();
    }
}
