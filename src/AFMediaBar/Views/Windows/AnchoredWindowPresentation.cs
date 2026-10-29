// 窗口持有首次布局的可见性门禁；隐藏或关闭使排队的呈现失效，关闭时解除订阅。
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using AFMediaBar.Classes.Interop;
using AFMediaBar.Classes.Models;

namespace AFMediaBar.Views.Windows;

/// <summary>在目标显示器完成窗口布局和定位后，才恢复整窗可见性。</summary>
internal sealed class AnchoredWindowPresentation : IDisposable
{
    private readonly Window _window;
    private int _version;
    private bool _disposed;
    private bool _showActivated;
    private bool _hitTestVisible;
    private bool _hasPresented;

    public bool IsPreparing { get; private set; }

    public AnchoredWindowPresentation(Window window)
    {
        _window = window;
        window.IsVisibleChanged += OnVisibilityChanged;
        window.Closed += OnClosed;
    }

    public async Task<bool> ShowAsync(Point physicalAnchor, Action position)
    {
        if (_disposed || _window.Dispatcher.HasShutdownStarted) return false;
        Cancel();
        var version = _version;
        _showActivated = _window.ShowActivated;
        _hitTestVisible = _window.IsHitTestVisible;
        IsPreparing = true;
        _window.Opacity = 0;
        _window.ShowActivated = false;
        _window.IsHitTestVisible = false;
        try
        {
            var point = new NativeMethods.POINT { X = (int)physicalAnchor.X, Y = (int)physicalAnchor.Y };
            var monitor = NativeMethods.MonitorFromPoint(point, NativeMethods.MONITOR_DEFAULTTONEAREST);
            var scale = NativeMethods.GetDpiForMonitor(monitor, NativeMethods.MonitorDpiType.MDT_EFFECTIVE_DPI, out var dpiX, out var dpiY) == 0
                ? new Point(Math.Max(1, dpiX) / 96d, Math.Max(1, dpiY) / 96d) : new Point(1, 1);
            _window.WindowStartupLocation = WindowStartupLocation.Manual;
            _window.Left = physicalAnchor.X / scale.X;
            _window.Top = physicalAnchor.Y / scale.Y;
            var handle = new WindowInteropHelper(_window).EnsureHandle();
            if (!IsCurrent(version)) return false;
            NativeMethods.SetWindowPos(handle, 0, point.X, point.Y, 0, 0,
                NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOZORDER);
            if (!_window.IsVisible) _window.Show();

            // EnsureHandle 尚未挂接 RootVisual；必须等首次 Show 的模板和窗口框架布局完成。
            await _window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Loaded);
            if (!IsCurrent(version) || !_window.IsVisible) return false;
            if (!_hasPresented)
            {
                var sizeToContent = _window.SizeToContent;
                _window.SizeToContent = SizeToContent.Manual;
                _window.SizeToContent = sizeToContent;
            }
            _window.UpdateLayout();
            position();
            if (!IsCurrent(version) || !_window.IsVisible) return false;
            _hasPresented = true;
            RestoreInput();
            _window.Opacity = 1;
            return true;
        }
        finally
        {
            if (IsCurrent(version) && IsPreparing)
            {
                RestoreInput();
                _window.Hide();
            }
        }
    }

    public void Cancel()
    {
        _version++;
        if (IsPreparing) RestoreInput();
    }

    private bool IsCurrent(int version) => !_disposed && version == _version && !_window.Dispatcher.HasShutdownStarted;

    private void RestoreInput()
    {
        IsPreparing = false;
        _window.ShowActivated = _showActivated;
        _window.IsHitTestVisible = _hitTestVisible;
    }

    private void OnVisibilityChanged(object sender, DependencyPropertyChangedEventArgs args)
    {
        if (!_window.IsVisible) Cancel();
    }

    private void OnClosed(object? sender, EventArgs args) => Dispose();

    public void Dispose()
    {
        if (_disposed) return;
        Cancel();
        _disposed = true;
        _window.IsVisibleChanged -= OnVisibilityChanged;
        _window.Closed -= OnClosed;
    }

    internal static Point GetPhysicalAnchor(TrayIconBounds? bounds)
    {
        if (bounds is { } icon) return new Point((icon.Left + icon.Right) / 2d, (icon.Top + icon.Bottom) / 2d);
        NativeMethods.GetCursorPos(out var point);
        var monitor = NativeMethods.MonitorFromPoint(point, NativeMethods.MONITOR_DEFAULTTONEAREST);
        var info = new NativeMethods.MONITORINFOEX { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MONITORINFOEX>() };
        return NativeMethods.GetMonitorInfo(monitor, ref info)
            ? new Point((info.rcWork.Left + info.rcWork.Right) / 2d, (info.rcWork.Top + info.rcWork.Bottom) / 2d)
            : new Point(point.X, point.Y);
    }
}
