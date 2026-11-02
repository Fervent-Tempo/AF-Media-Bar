// 每个媒体栏拥有一个越界呈现窗口；短时隐藏保留浏览器，关闭或宿主卸载时释放全部资源与订阅。
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Settings;
using static AFMediaBar.Classes.Interop.NativeMethods;

namespace AFMediaBar.Components;

internal sealed class PetOverflowHost : IDisposable
{
    private readonly FrameworkElement _anchor;
    private readonly Window _owner;
    private readonly Window _window;
    private readonly PetView _pet = new();
    private HwndSource? _source;
    private Rect? _lastBounds;
    private PetComponentSettings _settings = new();
    private bool _vertical;
    private bool _onLeft;
    private bool _visible;
    private bool _disposed;
    private bool _positioning;

    internal PetOverflowHost(FrameworkElement anchor, Window owner, PetWebEnvironment? environment)
    {
        _anchor = anchor;
        _owner = owner;
        _pet.BindEnvironment(environment);
        var surface = new InputSurface { Child = _pet };
        surface.MouseEnter += (_, _) => _pet.SetHovered(true);
        surface.MouseLeave += (_, _) => _pet.SetHovered(false);
        Point? press = null;
        surface.MouseDown += (_, args) =>
        {
            if (args.ChangedButton is MouseButton.Left or MouseButton.Right) _pet.Press();
            if (args.ChangedButton == MouseButton.Left) press = args.GetPosition(surface);
            args.Handled = true;
        };
        surface.MouseUp += (_, args) =>
        {
            if (args.ChangedButton == MouseButton.Left && press is { } point && (args.GetPosition(surface) - point).Length < SystemParameters.MinimumHorizontalDragDistance) Activated?.Invoke();
            press = null;
            args.Handled = true;
        };
        surface.MouseWheel += (_, args) => args.Handled = true;
        _window = new Window
        {
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            ShowActivated = false,
            ShowInTaskbar = false,
            Focusable = false,
            MinWidth = 0,
            MinHeight = 0,
            Width = 1,
            Height = 1,
            Topmost = true,
            Owner = owner,
            Content = surface,
            UseLayoutRounding = true
        };
        _window.SourceInitialized += OnSourceInitialized;
        _window.Closed += OnClosed;
        _anchor.LayoutUpdated += OnLayoutUpdated;
        _owner.LocationChanged += OnOwnerChanged;
        _owner.SizeChanged += OnOwnerSizeChanged;
        _owner.IsVisibleChanged += OnOwnerVisibilityChanged;
        _owner.Closed += OnOwnerClosed;
    }

    internal event Action? Activated;
    internal PetView View => _pet;
    internal bool IsVisible => _visible && !_disposed;

    internal void Show(PetComponentSettings settings, bool vertical, bool onLeft)
    {
        if (_disposed) return;
        _settings = settings;
        _vertical = vertical;
        _onLeft = onLeft;
        _pet.Configure(settings);
        _window.Width = _window.Height = settings.SizeDip;
        _visible = true;
        if (!_window.IsVisible)
        {
            _window.Opacity = 0;
            _window.Show();
        }
        Position();
        _pet.SetPresentation(true, true);
        _window.Opacity = 1;
    }

    internal void Hide()
    {
        if (_disposed || !_visible) return;
        _visible = false;
        _pet.SetPresentation(false, true);
        _window.Hide();
        _lastBounds = null;
    }

    private void Position()
    {
        if (!_visible || _disposed || _positioning || !_anchor.IsVisible || _source is null ||
            PresentationSource.FromVisual(_anchor) is null) return;
        _positioning = true;
        try
        {
            var origin = _anchor.PointToScreen(new Point());
            var end = _anchor.PointToScreen(new Point(_anchor.ActualWidth, _anchor.ActualHeight));
            var monitor = MonitorFromPoint(new POINT { X = (int)origin.X, Y = (int)origin.Y }, MONITOR_DEFAULTTONEAREST);
            var info = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
            if (!GetMonitorInfo(monitor, ref info)) return;
            var scale = VisualTreeHelper.GetDpi(_anchor).DpiScaleX;
            var screen = new Rect(info.rcMonitor.Left, info.rcMonitor.Top,
                info.rcMonitor.Right - info.rcMonitor.Left, info.rcMonitor.Bottom - info.rcMonitor.Top);
            var bounds = PetPlacementPolicy.Arrange(new Rect(origin, end), screen,
                _settings.SizeDip * scale, _vertical, _onLeft,
                _owner is AFMediaBar.Views.Windows.TaskbarWindow
                    ? new Rect(info.rcWork.Left, info.rcWork.Top, info.rcWork.Right - info.rcWork.Left, info.rcWork.Bottom - info.rcWork.Top) : null);
            if (_lastBounds == bounds) return;
            if (SetWindowPos(_source.Handle, -1, (int)Math.Round(bounds.X), (int)Math.Round(bounds.Y),
                (int)Math.Ceiling(bounds.Width), (int)Math.Ceiling(bounds.Height), SWP_NOACTIVATE)) _lastBounds = bounds;
        }
        finally { _positioning = false; }
    }

    private void OnSourceInitialized(object? sender, EventArgs args)
    {
        var handle = new WindowInteropHelper(_window).Handle;
        SetWindowLong(handle, GWL_EXSTYLE, GetWindowLong(handle, GWL_EXSTYLE) | WS_EX_NOACTIVATE | 0x80);
        _source = HwndSource.FromHwnd(handle);
        _source?.AddHook(WindowMessage);
    }

    private nint WindowMessage(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == 0x21) { handled = true; return 3; } // WM_MOUSEACTIVATE / MA_NOACTIVATE
        return 0;
    }
    private void OnLayoutUpdated(object? sender, EventArgs args) => Position();
    private void OnOwnerChanged(object? sender, EventArgs args) => Position();
    private void OnOwnerSizeChanged(object sender, SizeChangedEventArgs args) => Position();
    private void OnOwnerVisibilityChanged(object sender, DependencyPropertyChangedEventArgs args) { if (!_owner.IsVisible) Hide(); }
    private void OnOwnerClosed(object? sender, EventArgs args) => Dispose();
    private void OnClosed(object? sender, EventArgs args) => Release();

    private void Release()
    {
        if (_disposed) return;
        _disposed = true;
        _visible = false;
        _anchor.LayoutUpdated -= OnLayoutUpdated;
        _owner.LocationChanged -= OnOwnerChanged;
        _owner.SizeChanged -= OnOwnerSizeChanged;
        _owner.IsVisibleChanged -= OnOwnerVisibilityChanged;
        _owner.Closed -= OnOwnerClosed;
        _source?.RemoveHook(WindowMessage);
        _source = null;
        _pet.Dispose();
        _window.SourceInitialized -= OnSourceInitialized;
        _window.Closed -= OnClosed;
    }

    public void Dispose()
    {
        if (_disposed) return;
        Release();
        _window.Close();
    }

    // 覆盖 stage 内旋转、歪身和抬升的姿态包络；透明像素仍由分层窗口放行。
    internal static bool ContainsVisualPoint(Point point) =>
        (point - new Point(32, 32)).LengthSquared <= 32 * 32;

    // 透明窗口的空白角落不参与 WPF 命中，避免扩大到整个方形视口的点击区域。
    private sealed class InputSurface : Border
    {
        protected override HitTestResult? HitTestCore(PointHitTestParameters parameters)
        {
            var size = Math.Min(ActualWidth, ActualHeight);
            if (size <= 0) return null;
            var p = parameters.HitPoint;
            return ContainsVisualPoint(new Point(p.X * 64 / size, p.Y * 64 / size)) ? new PointHitTestResult(this, p) : null;
        }
    }
}
