// 应用容器拥有状态协调与按需频谱采样；最后一个呈现租约释放后停止计时。
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AFMediaBar.Classes.Utils;
using AFMediaBar.Classes.Services.Audio;
using System.Windows.Threading;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Settings;
using AFMediaBar.Resources;

namespace AFMediaBar.Classes.Services;

/// <summary>将播放状态和共享频谱变成宠物的呈现输入。</summary>
public sealed class PetCompanionService : IDisposable
{
    private readonly PetVoiceService _voice;
    private readonly ApplicationAudioControlCoordinator? _controls;
    private readonly AudioDeviceService? _devices;
    private readonly ApplicationVolumeService? _volumes;
    public MediaSnapshot Snapshot => _snapshot;
    public async Task<ApplicationVolumeSnapshot?> ReadVolumeAsync()
    {
        var snapshot = _snapshot;
        return _volumes is null ? null : await Task.Run(() => _volumes.GetCurrentMediaVolume(snapshot.SourceId, snapshot.SourceName));
    }
    public async Task ToggleMuteAsync()
    {
        var source = _snapshot.SourceId;
        var volume = await ReadVolumeAsync();
        if (!_disposed && source == _snapshot.SourceId && volume is not null && _controls is not null)
            await _controls.SetMuteAsync(volume.ProcessName, !volume.IsMuted, CancellationToken.None);
    }
    private ImageSource? _artwork;
    private int _artworkVersion, _deviceVersion;
    private string? _artworkColor, _deviceName;
    public string? ArtworkColor => _artworkColor;
        private string _musicAnimation = "listening", _candidate = "listening";
    private long _candidateSince, _statusAt;
    private (string Key, string Text)? _pendingStatus;
    public event Action? ArtworkColorChanged;
    private readonly List<Func<string, string, bool>> _statusViews = [];
    public IDisposable SubscribeStatus(Func<string, string, bool> show)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _statusViews.Add(show);
        return new StatusLease(_statusViews, show);
    }
    private sealed class StatusLease(List<Func<string, string, bool>> list, Func<string, string, bool> show) : IDisposable
    { public void Dispose() => list.Remove(show); }
    private void AudioCompleted(ApplicationAudioControlResult result)
    {
        if (result.Snapshot is not { } snapshot) return;
        _dispatcher.BeginInvoke(() => { if (_controls is not null && !_controls.IsCurrent(snapshot.ProcessName, result.Version)) return; ShowStatus(snapshot.ProcessName, Translations.Format(
            snapshot.IsMuted ? "Widgets.Pet.Status.Muted" : "Widgets.Pet.Status.Volume", snapshot.DisplayName, snapshot.VolumePercent)); });
    }
    private void ShowStatus(string key, string text)
    {
        if (_disposed || _statusViews.Count == 0 || !SettingsManager.Current.PetComponent.AudioStatusEnabled || _display.IsForegroundWindowFullscreen()) return;
        var now = Environment.TickCount64;
        if (now - _statusAt < 200) { _pendingStatus = (key, text); return; }
        _pendingStatus = null;
        foreach (var show in _statusViews.ToArray())
            if (show(key, text)) { _statusAt = now; _reactionAnimation = "notifying"; _reactionUntil = now + 700; break; }
    }
    private async void DevicesChanged(object? sender, EventArgs args)
    {
        var version = Interlocked.Increment(ref _deviceVersion);
        try
        {
            var device = (await _devices!.GetDevicesAsync(AudioDeviceDirection.Output)).FirstOrDefault(d => d.IsDefault);
            var name = device?.DisplayName;
            var id = device?.PolicyId;
            _ = _dispatcher.BeginInvoke(() =>
            {
                if (_disposed || version != _deviceVersion) return;
                var previous = _deviceName;
                _deviceName = id;
                if (previous is not null && previous != id && name is not null) ShowStatus("device", Translations.Format("Widgets.Pet.Status.Device", name));
            });
        }
        catch (Exception error) { AppLogService.Current?.Warn("Pet", error.Message); }
    }
    private async void UpdateArtwork(ImageSource? artwork)
    {
        if (ReferenceEquals(_artwork, artwork)) return;
        _artwork = artwork;
        var version = ++_artworkVersion;
        _artworkColor = null;
        ArtworkColorChanged?.Invoke();
        if (artwork is not BitmapSource bitmap || !bitmap.IsFrozen) return;
        try
        {
            var color = await Task.Run(() =>
            {
                var scale = Math.Min(1, 64d / Math.Max(bitmap.PixelWidth, bitmap.PixelHeight));
                var sample = new FormatConvertedBitmap(new TransformedBitmap(bitmap, new ScaleTransform(scale, scale)), PixelFormats.Bgra32, null, 0);
                var bytes = new byte[sample.PixelWidth * sample.PixelHeight * 4];
                sample.CopyPixels(bytes, sample.PixelWidth * 4, 0);
                var colors = DominantColorCalculator.Calculate(bytes, sample.PixelWidth, sample.PixelHeight, 1);
                return colors.Count == 0 ? null : ColorHex.Format(colors[0]);
            });
            if (!_disposed && version == _artworkVersion) { _artworkColor = color; ArtworkColorChanged?.Invoke(); }
        }
        catch (Exception error) { AppLogService.Current?.Warn("Pet", error.Message); }
    }
    private readonly AudioMonitorService _audio;
    private readonly List<Func<string, Action?, bool>> _messages = [];
    private readonly IDisplayMonitorService _display;
    private readonly Dictionary<string, Queue<int>> _phrases = [];
    private readonly Dictionary<string, int> _lastPhrase = [];

    internal string NextPhrase()
    {
        var group = !_snapshot.IsConnected ? "Idle" : _snapshot.IsPlaying ? "Playing" : "Paused";
        if (!_phrases.TryGetValue(group, out var deck) || deck.Count == 0)
        {
            var order = Enumerable.Range(0, 12).ToArray();
            Random.Shared.Shuffle(order);
            if (_lastPhrase.TryGetValue(group, out var last) && order[0] == last)
                (order[0], order[1]) = (order[1], order[0]);
            _phrases[group] = deck = new Queue<int>(order);
        }
        var pick = deck.Dequeue();
        _lastPhrase[group] = pick;
        var title = string.IsNullOrWhiteSpace(_snapshot.Title) ? Translations.Get("Widgets.Pet.ThisTrack") : _snapshot.Title;
        return Translations.Format($"Widgets.Pet.Line.{group}.{pick}", title);
    }

    private long _reactionUntil;
    private string _reactionAnimation = "notifying";

    public string Speak()
    {
        _reactionAnimation = "happy";
        _reactionUntil = Environment.TickCount64 + 1800;
        Publish();
        var text = NextPhrase();
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

    public PetCompanionService(AudioMonitorService audio, IDisplayMonitorService display, PetVoiceService voice, ApplicationAudioControlCoordinator? controls = null, AudioDeviceService? devices = null, ApplicationVolumeService? volumes = null)
    {
        _dispatcher = Application.Current.Dispatcher;
        _voice = voice;
        _controls = controls;
        _volumes = volumes;
        _devices = devices;
        if (controls is not null) controls.Completed += AudioCompleted;
        if (devices is not null) { devices.Changed += DevicesChanged; DevicesChanged(null, EventArgs.Empty); }
        _audio = audio;
        _display = display;
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
        if (_snapshot.IsPlaying != snapshot.IsPlaying)
        { _reactionAnimation = snapshot.IsPlaying ? "waking" : "drowsy"; _reactionUntil = Environment.TickCount64 + 700; }
        _snapshot = snapshot;
        UpdateArtwork(snapshot.Artwork);
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
        if (_pendingStatus is { } status && now - _statusAt >= 200) { _pendingStatus = null; ShowStatus(status.Key, status.Text); }
        var desired = _average < 0.025 ? "humming" : _average > 0.12 ? "excited" : "listening";
        if (_candidate != desired) { _candidate = desired; _candidateSince = now; }
        if (now - _candidateSince >= 1000) _musicAnimation = _candidate;
        var animation = now < _reactionUntil ? _reactionAnimation : now < _happyUntil ? "happy" : _snapshot.IsPlaying ? _musicAnimation :
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
            if (owner._views.Count == 0) { owner._generation++; owner._timer.Stop(); owner._voice.Stop(); owner._pendingStatus = null; owner._beat = owner._average = 0; }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _artworkVersion++;
        if (_controls is not null) _controls.Completed -= AudioCompleted;
        if (_devices is not null) _devices.Changed -= DevicesChanged;
        _statusViews.Clear();
        _generation++;
        _timer.Stop();
        _timer.Tick -= OnTick;
        _views.Clear();
        _messages.Clear();
    }
}
