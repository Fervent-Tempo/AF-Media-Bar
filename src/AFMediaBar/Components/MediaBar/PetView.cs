// 拥有一个宠物浏览器及其异步代际；卸载销毁、短时挂起保留，失败时呈现 WPF 回退。
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Settings;

namespace AFMediaBar.Components;

/// <summary>透明网页宠物及初始化、故障时的矢量回退。</summary>
public sealed class PetView : Grid, IDisposable
{
    private readonly PetControl _fallback = new();
    private readonly Func<IPetWebRenderer>? _createRenderer;
    private PetWebEnvironment? _environment;
    private IPetWebRenderer? _renderer;
    private PetComponentSettings _settings = new();
    private bool _active;
    private bool _failed;
    private bool _ready;
    private bool _disposed;
    private bool _hovered;
    private int _generation;
    private long _blinkSequence;
    private long _pressSequence;
    private Point? _pointer;
    private Vector _gaze;
    private PetWebState? _lastState;
    private string? _contextAnimation;
    private double _beat;

    internal void SetContext(string animation, double beat)
    {
        _contextAnimation = animation;
        _beat = beat;
        SendState();
    }

    public PetView() : this(null) { }
    internal PetView(Func<IPetWebRenderer>? createRenderer)
    {
        _createRenderer = createRenderer;
        IsHitTestVisible = false;
        Focusable = false;
        Children.Add(_fallback);
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        LayoutUpdated += (_, _) => UpdateGaze();
    }

    internal bool IsWebReady => _ready;
    internal bool HasBrowser => _renderer is not null;
    internal Task Initialization { get; private set; } = Task.CompletedTask;

    internal void BindEnvironment(PetWebEnvironment? environment) => _environment = environment;

    internal void Configure(PetComponentSettings settings)
    {
        if (_disposed) return;
        var normalized = settings.Normalize();
        if (_settings == normalized) return;
        _settings = normalized;
        _fallback.ApplySettings(_settings);
        InvalidateMeasure();
        SendState();
    }

    internal void SetPresentation(bool active, bool componentVisible)
    {
        if (_disposed) return;
        _active = active;
        if (!componentVisible)
        {
            ReleaseRenderer();
            _failed = false;
        }
        else if (active) EnsureRenderer();
        if (!active)
        {
            _pointer = null;
            _hovered = false;
            _fallback.Suspend();
        }
        SendState();
        _renderer?.SetActive(active);
    }

    internal void Present(PetInputSnapshot snapshot)
    {
        if (!_active || _disposed) return;
        _pointer = snapshot.ScreenPosition;
        if (snapshot.Blink) _blinkSequence++;
        UpdateGaze();
        if (!_ready) _fallback.Present(snapshot);
        SendState();
    }

    internal void SetHovered(bool hovered)
    {
        if (_disposed) return;
        _hovered = _active && hovered;
        SendState();
    }

    internal void Press()
    {
        if (!_active || _disposed) return;
        _pressSequence++;
        SendState();
    }

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        SystemParameters.StaticPropertyChanged += OnSystemChanged;
        if (_active) EnsureRenderer();
    }

    private void OnUnloaded(object sender, RoutedEventArgs args)
    {
        SystemParameters.StaticPropertyChanged -= OnSystemChanged;
        _active = false;
        _pointer = null;
        ReleaseRenderer();
        _fallback.Suspend();
    }

    private void OnSystemChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(() => OnSystemChanged(sender, args)); return; }
        if (_disposed) return;
        _fallback.ApplySettings(_settings);
        SendState();
    }

    private void UpdateGaze()
    {
        if (!_active || _pointer is not { } pointer || !IsVisible || ActualWidth <= 0 ||
            PresentationSource.FromVisual(this) is null) return;
        var center = PointToScreen(new Point(ActualWidth / 2, ActualHeight / 2));
        var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(this);
        var direction = new Vector((pointer.X - center.X) / (400 * dpi.DpiScaleX),
            (pointer.Y - center.Y) / (400 * dpi.DpiScaleY));
        if (direction.Length > 1) direction.Normalize();
        _gaze = new Vector(direction.X * 4, direction.Y * 3);
        SendState();
    }

    private void SendState()
    {
        if (_disposed || _renderer is null) return;
        var colors = PetVisualDefinition.Colors(_settings);
        var state = new PetWebState(_gaze.X, _gaze.Y, _hovered, _blinkSequence, _pressSequence,
            colors.Body, colors.Eyes, colors.HighContrast, _settings.IdleAnimationsEnabled,
            MotionPolicy.ResolveCurrent().Mode, _active, _settings.Animation == "auto" && _settings.FollowPlayback ? _contextAnimation ?? "idle" : _settings.Animation, _beat);
        if (state == _lastState) return;
        _lastState = state;
        _renderer.Present(state);
    }

    private void EnsureRenderer()
    {
        if (_disposed || _failed || _renderer is not null || !IsLoaded ||
            (_environment is null && _createRenderer is null)) return;
        try
        {
            var renderer = _createRenderer?.Invoke() ?? new PetWebRenderer(_environment!);
            _renderer = renderer;
            var generation = ++_generation;
            renderer.Ready += OnReady;
            renderer.Failed += OnFailed;
            PetGraphicsRecovery.Requested += OnGraphicsRecovery;
            renderer.View.Opacity = 0;
            Children.Add(renderer.View);
            SendState();
            Initialization = InitializeRendererAsync(renderer, generation);
        }
        catch (Exception error) { Fail(error); }
    }

    private async Task InitializeRendererAsync(IPetWebRenderer renderer, int generation)
    {
        try
        {
            await renderer.InitializeAsync();
            if (_disposed || generation != _generation || !ReferenceEquals(renderer, _renderer)) return;
            renderer.SetActive(_active);
        }
        catch (Exception error)
        {
            if (!_disposed && generation == _generation && ReferenceEquals(renderer, _renderer)) Fail(error);
        }
    }

    private void OnReady(object? sender, EventArgs args)
    {
        if (_disposed || !ReferenceEquals(sender, _renderer)) return;
        _ready = true;
        _fallback.Suspend();
        _fallback.Visibility = Visibility.Hidden;
        _renderer!.View.Opacity = 1;
    }

    private void OnFailed(object? sender, EventArgs args)
    {
        if (ReferenceEquals(sender, _renderer)) Fail(null);
    }

    private bool OnGraphicsRecovery()
    {
        if (_disposed || _renderer is null || Dispatcher.HasShutdownStarted) return false;
        var generation = _generation;
        Dispatcher.BeginInvoke(() =>
        {
            if (!_disposed && generation == _generation) Fail(null);
        }, DispatcherPriority.Normal);
        return true;
    }

    private void Fail(Exception? error)
    {
        _failed = true;
        AppLogService.Current?.Warn("Pet", $"WebView2 宠物退回 WPF: {error?.Message ?? "浏览器或图形故障"}");
        ReleaseRenderer();
        if (_active && _pointer is { } pointer) _fallback.Present(new(pointer, false));
    }

    private void ReleaseRenderer()
    {
        ++_generation;
        _ready = false;
        _lastState = null;
        PetGraphicsRecovery.Requested -= OnGraphicsRecovery;
        if (_renderer is { } renderer)
        {
            _renderer = null;
            renderer.Ready -= OnReady;
            renderer.Failed -= OnFailed;
            try { Children.Remove(renderer.View); }
            finally { renderer.Dispose(); }
        }
        _fallback.Visibility = Visibility.Visible;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _active = false;
        SystemParameters.StaticPropertyChanged -= OnSystemChanged;
        ReleaseRenderer();
        _fallback.Suspend();
        _environment = null;
    }

    protected override Size MeasureOverride(Size constraint)
    {
        base.MeasureOverride(constraint);
        return new Size(Math.Min(_settings.SizeDip, constraint.Width), Math.Min(_settings.SizeDip, constraint.Height));
    }
}
