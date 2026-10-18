// 独立完整层入口的呈现与跨窗口指针衔接；任务栏宿主持有，隐藏停表，关闭释放订阅。
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Settings;
using AFMediaBar.Classes.Utils;
using AFMediaBar.Resources;
using static AFMediaBar.Classes.Interop.NativeMethods;

namespace AFMediaBar.Views.Windows;

/// <summary>只覆盖横杠按钮的小窗口；不接管媒体栏或完整层的生命周期。</summary>
public sealed class ExternalFullPanelHandleWindow : Window, IDisposable
{
    private readonly NativeMouseInputMonitor _input;
    private readonly Border _line;
    private readonly DispatcherTimer _pointerTimer;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private Rect _barBounds;
    private Rect _handleBounds;
    private Rect _transitBounds;
    private FullPanelOpenRequestEventArgs? _request;
    private Func<bool>? _canRemainVisible;
    private FullPanelHandleTrigger _trigger;
    private TimeSpan? _hoverSince;
    private TimeSpan? _outsideSince;
    private bool _taskbarSuspended;
    private bool _isPressed;
    private bool _disposed;

    public event EventHandler<FullPanelOpenRequestEventArgs>? OpenRequested;

    public ExternalFullPanelHandleWindow(NativeMouseInputMonitor input)
    {
        _input = input;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        UseLayoutRounding = true;
        WindowHelper.SetNoActivate(this);
        _line = new Border { CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        // 极低 alpha 让整个按钮矩形可命中；窗口尺寸不包含媒体栏与过渡区域。
        var surface = new Border { Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)), Child = _line, Cursor = Cursors.Hand };
        surface.MouseLeftButtonUp += Surface_MouseLeftButtonUp;
        surface.MouseLeftButtonDown += Surface_MouseLeftButtonDown;
        Content = surface;
        _pointerTimer = new DispatcherTimer(DispatcherPriority.Input, Dispatcher) { Interval = TimeSpan.FromMilliseconds(50) };
        _pointerTimer.Tick += PointerTimer_Tick;
    }

    public void Reveal(TrayIconBounds bar, Rect handleBounds, Rect transitBounds, FullPanelOpenRequestEventArgs request,
        bool vertical, Brush brush, FullPanelHandleTrigger trigger, Func<bool> canRemainVisible)
    {
        if (_disposed) return;
        _barBounds = new Rect(bar.Left, bar.Top, bar.Right - bar.Left, bar.Bottom - bar.Top);
        if (IsVisible)
        {
            _taskbarSuspended = false;
            _outsideSince = null;
            return;
        }
        _handleBounds = handleBounds;
        _transitBounds = transitBounds;
        _request = request;
        _canRemainVisible = canRemainVisible;
        _trigger = trigger;
        Title = Translations.Get("Experimental.FullPanel.Title");
        _taskbarSuspended = false;
        _hoverSince = null;
        _outsideSince = null;
        Width = vertical ? 8 : 44;
        Height = vertical ? 44 : 8;
        _line.Width = vertical ? 4 : 28;
        _line.Height = vertical ? 28 : 4;
        _line.Background = brush;
        var handle = new WindowInteropHelper(this).EnsureHandle();
        Position(handle);
        Show();
        // Show 可能完成目标显示器的 DPI 切换，再断言物理边界，避免首次显示使用主屏缩放。
        Position(handle);
        _pointerTimer.Start();
    }

    private void Position(IntPtr handle)
    {
        if (!SetWindowPos(handle, -1, (int)Math.Round(_handleBounds.X), (int)Math.Round(_handleBounds.Y),
                (int)Math.Round(_handleBounds.Width), (int)Math.Round(_handleBounds.Height), SWP_NOACTIVATE))
            throw new Win32Exception();
    }

    /// <summary>只保留横杠和过渡区域，收起中的原媒体栏不再延长入口寿命。</summary>
    public void SuspendTaskbar() => _taskbarSuspended = true;

    public void Dismiss()
    {
        _pointerTimer.Stop();
        _request = null;
        _canRemainVisible = null;
        _hoverSince = null;
        _outsideSince = null;
        _isPressed = false;
        if (!_disposed && IsVisible) Hide();
    }

    private void PointerTimer_Tick(object? sender, EventArgs e)
    {
        try
        {
            if (_disposed || _canRemainVisible?.Invoke() != true || !_input.TryGetPointerPosition(out var point))
            {
                Dismiss();
                return;
            }
            var now = _clock.Elapsed;
            var overHandle = _handleBounds.Contains(point) && IsMouseOver;
            var overArea = overHandle || _transitBounds.Contains(point) || (!_taskbarSuspended && _barBounds.Contains(point));
            if (!overArea)
            {
                _hoverSince = null;
                _outsideSince ??= now;
                if (now - _outsideSince.Value >= TimeSpan.FromMilliseconds(150)) Dismiss();
                return;
            }
            _outsideSince = null;
            if (!overHandle || _input.IsLeftButtonDown || _input.IsRightButtonDown)
            {
                _hoverSince = null;
                return;
            }
            _hoverSince ??= now;
            if (_trigger == FullPanelHandleTrigger.Hover && now - _hoverSince.Value >= TimeSpan.FromMilliseconds(500))
                RequestOpen();
        }
        catch (Exception exception)
        {
            Dismiss();
            AppLogService.Current?.Warn("FullPanel", "外侧入口指针检查失败", exception);
        }
    }

    private void Surface_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        var pressed = _isPressed;
        _isPressed = false;
        if (pressed && IsMouseOver && !_input.ConsumeSuppressedClick()) RequestOpen();
    }

    private void Surface_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _isPressed = true;
        e.Handled = true;
    }

    private void RequestOpen()
    {
        if (_disposed || !IsVisible || _request is not { } request || _canRemainVisible?.Invoke() != true) return;
        Dismiss();
        OpenRequested?.Invoke(this, request);
    }

    public void Dispose()
    {
        if (_disposed) return;
        Dismiss();
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        Dismiss();
        _disposed = true;
        _pointerTimer.Tick -= PointerTimer_Tick;
        ((Border)Content).MouseLeftButtonUp -= Surface_MouseLeftButtonUp;
        ((Border)Content).MouseLeftButtonDown -= Surface_MouseLeftButtonDown;
        OpenRequested = null;
        base.OnClosed(e);
    }
}
