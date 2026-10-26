// 独立完整面板入口的呈现、背景采样及跨窗口指针衔接；所属宿主持有，隐藏取消采样，关闭释放订阅。
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Services.Layout;
using AFMediaBar.Classes.Settings;
using AFMediaBar.Classes.Utils;
using AFMediaBar.Components;
using AFMediaBar.Resources;
using static AFMediaBar.Classes.Interop.NativeMethods;

namespace AFMediaBar.Views.Windows;

/// <summary>只覆盖横杠按钮的小窗口；不接管媒体栏或完整层的生命周期。</summary>
public sealed class ExternalFullPanelHandleWindow : Window, IDisposable
{
    internal const double HitLengthDip = FullPanelPlacementPolicy.HitLengthDip;
    internal const double HitThicknessDip = FullPanelPlacementPolicy.HitThicknessDip;
    private static readonly TimeSpan PointerLeaveGrace = TimeSpan.FromMilliseconds(500);
    private readonly NativeMouseInputMonitor _input;
    private readonly Border _line;
    private readonly Border _outline;
    private readonly ColorTransitionBrush _lineColor = new();
    private readonly ColorTransitionBrush _outlineColor = new();
    private readonly Grid _marks;
    private readonly Border _visualHost;
    private readonly TranslateTransform _markTranslation = new();
    private readonly ScreenBackgroundSampler _sampler;
    private readonly AdaptiveForegroundSamplingSession _contrastSampling;
    private readonly DispatcherTimer _pointerTimer;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private Rect _barBounds;
    private Rect _handleBounds;
    private Rect _hitBounds;
    private Rect _transitBounds;
    private FullPanelOpenRequestEventArgs? _request;
    private Func<bool>? _canRemainVisible;
    private Func<bool>? _isBarPointerActive;
    private FullPanelHandleTrigger _trigger;
    private TimeSpan _hoverDelay;
    private TimeSpan? _hoverSince;
    private TimeSpan? _outsideSince;
    private TimeSpan _nextContrastRefresh;
    private CancellationTokenSource? _initialContrastCancellation;
    private PlayerForegroundDecision? _foregroundDecision;
    private int _revealVersion;
    private bool _contrastReady;
    private int _animationVersion;
    private bool _isHiding;
    private bool _isAnimating;
    private bool _canResumeDismissal;
    private bool _vertical;
    private bool _taskbarOnLeadingEdge;
    private bool _taskbarSuspended;
    private bool _isPressed;
    private bool _disposed;

    public event EventHandler<FullPanelOpenRequestEventArgs>? OpenRequested;

    public ExternalFullPanelHandleWindow(NativeMouseInputMonitor input, ScreenBackgroundSampler sampler)
    {
        _input = input;
        _sampler = sampler;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        UseLayoutRounding = true;
        WindowHelper.SetNoActivate(this);
        _line = new Border { CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        _outline = new Border { CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        _contrastSampling = new AdaptiveForegroundSamplingSession(sampler, Dispatcher, GetContrastBounds, ApplyContrast);
        // 极低 alpha 让整个按钮矩形可命中；窗口尺寸不包含媒体栏与过渡区域。
        _marks = new Grid { Opacity = 0, RenderTransform = _markTranslation };
        _marks.Children.Add(_outline);
        _marks.Children.Add(_line);
        _visualHost = new Border { Child = _marks, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, ClipToBounds = true };
        var layout = new Grid();
        layout.Children.Add(_visualHost);
        var surface = new Border { Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)), Child = layout, Cursor = Cursors.Hand, ClipToBounds = true };
        surface.MouseLeftButtonUp += Surface_MouseLeftButtonUp;
        surface.MouseLeftButtonDown += Surface_MouseLeftButtonDown;
        surface.MouseEnter += Surface_MouseEnter;
        surface.MouseLeave += (_, _) => _hoverSince = null;
        surface.PreviewMouseDown += (_, _) => _hoverSince = null;
        Content = surface;
        _pointerTimer = new DispatcherTimer(DispatcherPriority.Input, Dispatcher) { Interval = TimeSpan.FromMilliseconds(50) };
        _pointerTimer.Tick += PointerTimer_Tick;
    }

    public void Reveal(TrayIconBounds bar, Rect handleBounds, Rect hitBounds, Rect transitBounds, FullPanelOpenRequestEventArgs request,
        bool vertical, bool taskbarOnLeadingEdge, FullPanelHandleTrigger trigger, int hoverDelayMilliseconds, Func<bool> canRemainVisible,
        Func<bool>? isBarPointerActive = null)
    {
        if (_disposed) return;
        _barBounds = new Rect(bar.Left, bar.Top, bar.Right - bar.Left, bar.Bottom - bar.Top);
        if (IsVisible && (_handleBounds != handleBounds || _hitBounds != hitBounds || _vertical != vertical || _taskbarOnLeadingEdge != taskbarOnLeadingEdge))
            Dismiss(immediate: true);
        _request = request;
        _canRemainVisible = canRemainVisible;
        _isBarPointerActive = isBarPointerActive;
        var hoverDelay = TimeSpan.FromMilliseconds(hoverDelayMilliseconds);
        if (_trigger != trigger || _hoverDelay != hoverDelay)
        {
            _hoverSince = null;
            _isPressed = false;
        }
        _trigger = trigger;
        _hoverDelay = hoverDelay;
        if (IsVisible)
        {
            _taskbarSuspended = false;
            _pointerTimer.Start();
            ResumePresentation();
            return;
        }
        _handleBounds = handleBounds;
        _hitBounds = hitBounds;
        _transitBounds = transitBounds;
        _vertical = vertical;
        _taskbarOnLeadingEdge = taskbarOnLeadingEdge;
        Title = Translations.Get("DisplayModes.Rest.Row.Entry.Title");
        _taskbarSuspended = false;
        _hoverSince = null;
        _outsideSince = null;
        Width = vertical ? HitThicknessDip : HitLengthDip;
        Height = vertical ? HitLengthDip : HitThicknessDip;
        _line.Width = vertical ? 4 : 28;
        _line.Height = vertical ? 28 : 4;
        _outline.Width = vertical ? 6 : 30;
        _outline.Height = vertical ? 30 : 6;
        _contrastReady = false;
        SetPresentation(0, 0, 0);
        var handle = new WindowInteropHelper(this).EnsureHandle();
        Position(handle);
        Show();
        // Show 可能完成目标显示器的 DPI 切换，再断言物理边界，避免首次显示使用主屏缩放。
        Position(handle);
        // 命中窗口向桌面侧扩展；视觉框保留原来的物理位置、取色区域和滑动距离。
        var dpi = VisualTreeHelper.GetDpi(this);
        _visualHost.Width = handleBounds.Width / dpi.DpiScaleX;
        _visualHost.Height = handleBounds.Height / dpi.DpiScaleY;
        _visualHost.Margin = new Thickness((handleBounds.Left - hitBounds.Left) / dpi.DpiScaleX,
            (handleBounds.Top - hitBounds.Top) / dpi.DpiScaleY, 0, 0);
        var motion = MotionPolicy.ResolveCurrent();
        var hiddenOffset = motion.Mode == MotionMode.Full ? GetHiddenTranslation() : new Vector();
        SetPresentation(0, hiddenOffset.X, hiddenOffset.Y);
        _pointerTimer.Start();
        if (SystemParameters.HighContrast)
        {
            ApplyContrast(null);
            CompleteInitialContrast();
        }
        else if (GetContrastBounds() is { } bounds)
        {
            var cancellation = new CancellationTokenSource();
            _initialContrastCancellation = cancellation;
            _ = PrepareContrastAsync(bounds, ++_revealVersion, cancellation);
        }
    }

    private async Task PrepareContrastAsync(Int32Rect bounds, int version, CancellationTokenSource cancellation)
    {
        try
        {
            IReadOnlyList<Color> samples = [];
            try { samples = await _sampler.SampleAsync(bounds, cancellation.Token); }
            catch (Exception exception) { Debug.WriteLine($"[FullPanelHandle] Initial background sampling failed: {exception}"); }
            if (_disposed || cancellation.IsCancellationRequested || version != _revealVersion ||
                !IsVisible || _request is null || _canRemainVisible?.Invoke() != true) return;
            // 首次取色完成前不绘制横杠，避免系统回退色先黑后白。
            ApplyContrast(PlayerForegroundPolicy.Resolve(samples, _foregroundDecision));
            CompleteInitialContrast();
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"[FullPanelHandle] Initial presentation failed: {exception}");
            if (!_disposed && version == _revealVersion) Dismiss();
        }
        finally
        {
            if (ReferenceEquals(_initialContrastCancellation, cancellation)) _initialContrastCancellation = null;
            cancellation.Dispose();
        }
    }

    private void CompleteInitialContrast()
    {
        _contrastReady = true;
        _nextContrastRefresh = _clock.Elapsed + TimeSpan.FromMilliseconds(250);
        AnimatePresentation(visible: true);
    }

    private void AnimatePresentation(bool visible)
    {
        _isHiding = !visible;
        var version = ++_animationVersion;
        var motion = MotionPolicy.ResolveCurrent();
        var opacity = _marks.Opacity;
        var offsetX = _markTranslation.X;
        var offsetY = _markTranslation.Y;
        var hiddenOffset = motion.Mode == MotionMode.Full && !visible ? GetHiddenTranslation() : new Vector();
        // 先读取当前动画值，再移除旧时钟；反向过渡从当前画面接续，不跳回起点。
        SetPresentation(visible ? 1 : 0, hiddenOffset.X, hiddenOffset.Y);
        if (!motion.UseTransitions || !visible && opacity <= 0)
        {
            if (!visible) CompleteDismiss();
            return;
        }

        var duration = visible ? motion.PositionDuration : motion.StandardDuration;
        var ease = new PowerEase { Power = 3, EasingMode = EasingMode.EaseOut };
        var fade = new DoubleAnimation(opacity, visible ? 1 : 0, duration) { EasingFunction = ease, FillBehavior = FillBehavior.Stop };
        // 旧退场即使已排入完成队列，也不能隐藏后来恢复的入口。
        fade.Completed += (_, _) =>
        {
            if (_disposed || version != _animationVersion || _isHiding == visible) return;
            if (visible) SetPresentation(1, 0, 0);
            else CompleteDismiss();
        };
        _isAnimating = true;
        // 滑动会经过按钮边缘的取色区域，过渡期间暂停采样，避免采到横杠自身。
        _contrastSampling.Invalidate(clearDecision: false);
        _marks.BeginAnimation(OpacityProperty, fade, HandoffBehavior.SnapshotAndReplace);
        if (motion.Mode == MotionMode.Full)
        {
            _markTranslation.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(offsetX, hiddenOffset.X, duration)
                { EasingFunction = ease, FillBehavior = FillBehavior.Stop }, HandoffBehavior.SnapshotAndReplace);
            _markTranslation.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(offsetY, hiddenOffset.Y, duration)
                { EasingFunction = ease, FillBehavior = FillBehavior.Stop }, HandoffBehavior.SnapshotAndReplace);
        }
    }

    private Vector GetHiddenTranslation()
    {
        // 起点落在靠媒体栏的一侧边缘，上方/左侧为负方向，下方/右侧为正方向。
        var direction = _taskbarOnLeadingEdge ? -1 : 1;
        return _vertical ? new Vector(direction * _visualHost.Width / 2, 0) : new Vector(0, direction * _visualHost.Height / 2);
    }

    private void SetPresentation(double opacity, double offsetX, double offsetY)
    {
        _marks.BeginAnimation(OpacityProperty, null);
        _markTranslation.BeginAnimation(TranslateTransform.XProperty, null);
        _markTranslation.BeginAnimation(TranslateTransform.YProperty, null);
        _isAnimating = false;
        _marks.Opacity = opacity;
        _markTranslation.X = offsetX;
        _markTranslation.Y = offsetY;
    }

    private Int32Rect? GetContrastBounds()
    {
        if (_disposed || !IsVisible) return null;
        // 只取按钮透明边缘的背景，避开横杠及轮廓本身，避免颜色自反馈。
        var scale = (_vertical ? _handleBounds.Width : _handleBounds.Height) / 8;
        return _vertical
            ? new Int32Rect((int)_handleBounds.Left, (int)Math.Round(_handleBounds.Top + 8 * scale),
                Math.Max(1, (int)Math.Floor(scale)), Math.Max(1, (int)Math.Round(28 * scale)))
            : new Int32Rect((int)Math.Round(_handleBounds.Left + 8 * scale), (int)_handleBounds.Top,
                Math.Max(1, (int)Math.Round(28 * scale)), Math.Max(1, (int)Math.Floor(scale)));
    }

    private void ApplyContrast(PlayerForegroundDecision? decision)
    {
        if (_disposed) return;
        if (decision is not null) _foregroundDecision = decision;
        var brushes = HandleIndicatorAppearance.Resolve(_foregroundDecision);
        var duration = ColorTransitionBrush.ResolveDuration(_contrastReady && !_isHiding && IsVisible && decision is not null);
        _line.Background = _lineColor.Apply(brushes.Line, duration);
        _outline.Background = _outlineColor.Apply(brushes.Outline, duration);
    }

    private void RefreshContrast()
    {
        if (!_contrastReady || _isAnimating || _clock.Elapsed < _nextContrastRefresh) return;
        _nextContrastRefresh = _clock.Elapsed + TimeSpan.FromMilliseconds(250);
        if (SystemParameters.HighContrast) _contrastSampling.Invalidate(clearDecision: true);
        else _contrastSampling.RequestRefresh();
    }

    private void Position(IntPtr handle)
    {
        if (!SetWindowPos(handle, -1, (int)Math.Round(_hitBounds.X), (int)Math.Round(_hitBounds.Y),
                (int)Math.Round(_hitBounds.Width), (int)Math.Round(_hitBounds.Height), SWP_NOACTIVATE))
            throw new Win32Exception();
    }

    /// <summary>只保留横杠和过渡区域，收起中的原媒体栏不再延长入口寿命。</summary>
    public void SuspendTaskbar() => _taskbarSuspended = true;

    /// <summary>普通收起播放退场；宿主失效、拖动或释放时可立即取消全部过渡。</summary>
    public void Dismiss(bool immediate = false)
    {
        if (_disposed) return;
        StopInteraction();
        if (immediate || !IsVisible) CompleteDismiss();
        else if (!_isHiding) AnimatePresentation(visible: false);
    }

    private void StopInteraction()
    {
        _lineColor.Finish();
        _outlineColor.Finish();
        _pointerTimer.Stop();
        _revealVersion++;
        _initialContrastCancellation?.Cancel();
        _initialContrastCancellation = null;
        _contrastSampling.Invalidate(clearDecision: false);
        _canResumeDismissal = false;
        _request = null;
        _canRemainVisible = null;
        _isBarPointerActive = null;
        _hoverSince = null;
        _outsideSince = null;
        _isPressed = false;
    }

    private void CompleteDismiss()
    {
        StopInteraction();
        _animationVersion++;
        _isHiding = false;
        _contrastReady = false;
        SetPresentation(0, 0, 0);
        if (IsVisible) Hide();
    }

    private void BeginPointerLeaveDismissal()
    {
        if (_isHiding) return;
        _lineColor.Finish();
        _outlineColor.Finish();
        _canResumeDismissal = true;
        _isPressed = false;
        _contrastSampling.Invalidate(clearDecision: false);
        AnimatePresentation(visible: false);
    }

    private void ResumePresentation()
    {
        _outsideSince = null;
        _canResumeDismissal = false;
        if (_isHiding && _contrastReady) AnimatePresentation(visible: true);
    }

    private void PointerTimer_Tick(object? sender, EventArgs e)
    {
        if (!_pointerTimer.IsEnabled) return;
        try
        {
            if (_disposed || _canRemainVisible?.Invoke() != true || !_input.TryGetPointerPosition(out var point))
            {
                Dismiss(immediate: true);
                return;
            }
            var now = _clock.Elapsed;
            if (!_isHiding) RefreshContrast();
            var overHandle = _hitBounds.Contains(point) && IsMouseOver;
            var overArea = overHandle || _transitBounds.Contains(point) ||
                (!_taskbarSuspended && _barBounds.Contains(point) && _isBarPointerActive?.Invoke() != false);
            if (!overArea)
            {
                _hoverSince = null;
                _outsideSince ??= now;
                if (now - _outsideSince.Value >= PointerLeaveGrace) BeginPointerLeaveDismissal();
                return;
            }
            _outsideSince = null;
            if (_canResumeDismissal) ResumePresentation();
            RefreshContrast();
            if (!_contrastReady || !overHandle || _input.IsLeftButtonDown || _input.IsRightButtonDown)
            {
                _hoverSince = null;
                return;
            }
            _hoverSince ??= now;
            if (_trigger == FullPanelHandleTrigger.Hover && now - _hoverSince.Value >= _hoverDelay)
                RequestOpen();
        }
        catch (Exception exception)
        {
            Dismiss(immediate: true);
            AppLogService.Current?.Warn("FullPanel", "外侧入口指针检查失败", exception);
        }
    }

    private void Surface_MouseEnter(object sender, MouseEventArgs e)
    {
        if (!_disposed && _canResumeDismissal && _request is not null && _pointerTimer.IsEnabled) ResumePresentation();
    }

    private void Surface_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        var pressed = _isPressed;
        _isPressed = false;
        var suppressed = _input.ConsumeSuppressedClick();
        if (_trigger == FullPanelHandleTrigger.Click && pressed && IsMouseOver && !suppressed) RequestOpen();
    }

    private void Surface_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _hoverSince = null;
        _isPressed = _trigger == FullPanelHandleTrigger.Click;
        e.Handled = true;
    }

    private void RequestOpen()
    {
        if (_disposed || !IsVisible || !_contrastReady || _request is not { } request || _canRemainVisible?.Invoke() != true) return;
        Dismiss();
        OpenRequested?.Invoke(this, request);
    }

    public void Dispose()
    {
        if (_disposed) return;
        Dismiss(immediate: true);
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        Dismiss(immediate: true);
        _disposed = true;
        _contrastSampling.Dispose();
        _pointerTimer.Tick -= PointerTimer_Tick;
        ((Border)Content).MouseLeftButtonUp -= Surface_MouseLeftButtonUp;
        ((Border)Content).MouseLeftButtonDown -= Surface_MouseLeftButtonDown;
        ((Border)Content).MouseEnter -= Surface_MouseEnter;
        OpenRequested = null;
        base.OnClosed(e);
    }
}
