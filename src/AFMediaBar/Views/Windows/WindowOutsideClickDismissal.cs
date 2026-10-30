// 窗口持有外部点击关闭订阅；仅可见期间监听共享输入，关闭或隐藏后拒绝排队的旧点击。
using System.Windows.Interop;
using System.Diagnostics;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using AFMediaBar.Classes.Services;

namespace AFMediaBar.Views.Windows;

/// <summary>不依赖窗口激活状态，按点击时的 HWND 所属关系识别面板及其 Popup。</summary>
internal sealed class WindowOutsideClickDismissal : IDisposable
{
    private readonly Window _window;
    private readonly NativeMouseInputMonitor _input;
    private readonly Action _dismiss;
    private EventHandler<NativeMousePressEventArgs>? _handler;
    private readonly HashSet<Popup> _popups = [];
    private readonly List<PopupScope> _popupScopes = [];
    private DispatcherOperation? _cleanup;
    private int _generation;
    private bool _disposed;

    private sealed class PopupScope(Popup popup, nint handle)
    {
        internal Popup Popup { get; } = popup;
        internal nint Handle { get; } = handle;
        internal long OpenedAt { get; } = Stopwatch.GetTimestamp();
        internal long ClosedAt { get; set; } = long.MaxValue;
    }

    internal WindowOutsideClickDismissal(Window window, NativeMouseInputMonitor input, Action dismiss)
    {
        _window = window;
        _input = input;
        _dismiss = dismiss;
        window.IsVisibleChanged += OnVisibilityChanged;
        window.Closed += OnClosed;
    }

    internal void Start()
    {
        if (_disposed || _handler is not null || !_window.IsVisible) return;
        var handle = new WindowInteropHelper(_window).Handle;
        if (handle == nint.Zero) return;
        var generation = ++_generation;
        _handler = (_, args) =>
        {
            if (_disposed || generation != _generation || !_window.IsVisible || args.WindowChain.Contains(handle) ||
                _popupScopes.Any(scope => args.WindowChain.Contains(scope.Handle) &&
                    args.CapturedAt >= scope.OpenedAt && args.CapturedAt <= scope.ClosedAt)) return;
            Stop();
            _dismiss();
        };
        _input.ButtonPressed += _handler;
        foreach (var popup in _popups.Where(popup => popup.IsOpen)) TrackPopup(popup);
    }

    internal void RegisterPopup(Popup popup)
    {
        if (_disposed || !_popups.Add(popup)) return;
        popup.Opened += OnPopupOpened;
        popup.Closed += OnPopupClosed;
        if (popup.IsOpen && _handler is not null) TrackPopup(popup);
    }

    private void OnPopupOpened(object? sender, EventArgs args)
    {
        if (_handler is not null && sender is Popup popup) TrackPopup(popup);
    }

    private void TrackPopup(Popup popup)
    {
        if (popup.Child is not null && PresentationSource.FromVisual(popup.Child) is HwndSource source && source.Handle != nint.Zero)
            _popupScopes.Add(new PopupScope(popup, source.Handle));
    }

    private void OnPopupClosed(object? sender, EventArgs args)
    {
        if (_disposed || _handler is null || sender is not Popup popup) return;
        var now = Stopwatch.GetTimestamp();
        foreach (var scope in _popupScopes.Where(scope => scope.Popup == popup && scope.ClosedAt == long.MaxValue)) scope.ClosedAt = now;
        if (_cleanup?.Status == DispatcherOperationStatus.Pending) return;
        var generation = _generation;
        // 先处理 Input 队列中已经按下的点击，再回收关闭的 Popup 身份。
        _cleanup = _window.Dispatcher.BeginInvoke(() =>
        {
            _cleanup = null;
            if (!_disposed && generation == _generation) _popupScopes.RemoveAll(scope => scope.ClosedAt != long.MaxValue);
        }, DispatcherPriority.ContextIdle);
    }

    internal void Stop()
    {
        _generation++;
        _cleanup?.Abort();
        _cleanup = null;
        _popupScopes.Clear();
        if (_handler is null) return;
        _input.ButtonPressed -= _handler;
        _handler = null;
    }

    private void OnVisibilityChanged(object sender, DependencyPropertyChangedEventArgs args)
    {
        if (!_window.IsVisible) Stop();
    }

    private void OnClosed(object? sender, EventArgs args) => Dispose();

    public void Dispose()
    {
        if (_disposed) return;
        Stop();
        _disposed = true;
        _window.IsVisibleChanged -= OnVisibilityChanged;
        _window.Closed -= OnClosed;
        foreach (var popup in _popups)
        {
            popup.Opened -= OnPopupOpened;
            popup.Closed -= OnPopupClosed;
        }
        _popups.Clear();
    }
}
