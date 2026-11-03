using System.Diagnostics;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using AFMediaBar.Classes.Interop;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Services.Layout;
using AFMediaBar.Classes.Settings;
using AFMediaBar.Classes.Utils;
using Wpf.Ui.Controls;

namespace AFMediaBar.Views.Windows;

/// <summary>可复用、非激活的曲目切换通知窗口。 / Reusable, non-activating track-change notification window.</summary>
public partial class TrackChangeNotificationWindow : FluentWindow
{
    private const int ContentSizeAttemptCount = 3;
    private const double ContentSizeTolerance = 0.5;
    private readonly DispatcherTimer _hideTimer;
    private DisplayMonitorInfo? _currentMonitor;
    private TrackChangeNotificationPosition _currentPosition;
    private TrackChangeNotificationSettings _currentSettings;
    private bool _hasCompletedInitialLayout;
    private int _presentationVersion;
    private bool _isExiting;

    /// <summary>创建通知窗口并接入统一窗口外观。 / Creates the notification window and attaches unified window appearance.</summary>
    public TrackChangeNotificationWindow(WindowAppearanceService appearanceService)
    {
        WindowHelper.SetNoActivate(this);
        InitializeComponent();
        ApplyMotionEffects();
        Left = -10000;
        Top = -10000;
        appearanceService.AttachNonActivatingTransient(this);
        _hideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _hideTimer.Tick += HideTimer_Tick;
    }

    /// <summary>
    /// 更新并显示通知；首次显示会等待布局稳定，重复调用会复用 HWND 并重置停留时间。
    /// Updates and shows the notification; the first presentation waits for stable layout,
    /// while subsequent calls reuse its HWND and reset dwell time.
    /// </summary>
    public void ShowNotification(TrackChangeNotificationRequest request)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => ShowNotification(request));
            return;
        }

        var presentationVersion = ++_presentationVersion;
        _hideTimer.Stop();
        StopAnimations(preservePresentation: IsVisible);
        _isExiting = false;
        ApplyMotionEffects();
        _currentMonitor = request.Monitor;
        _currentSettings = request.Settings.Normalize();
        _currentPosition = _currentSettings.Position;
        NotificationContent.Settings = _currentSettings;
        ApplySnapshot(request.Snapshot);

        if (!IsVisible)
        {
            Opacity = 0;
            Show();
        }

        if (!_hasCompletedInitialLayout)
        {
            // FluentWindow 在第一次 Show 后才完成模板和窗口框架测量；首帧保持透明且位于屏外，
            // 等 SizeToContent 稳定后再定位。FluentWindow completes template/chrome measurement after
            // the first Show call; keep that frame transparent and off-screen until SizeToContent settles.
            Dispatcher.BeginInvoke(
                () => CompletePresentation(request, presentationVersion),
                DispatcherPriority.ContextIdle);
            return;
        }

        CompletePresentation(request, presentationVersion);
    }

    private void CompletePresentation(TrackChangeNotificationRequest request, int presentationVersion)
    {
        if (presentationVersion != _presentationVersion || !IsVisible)
            return;

        EnsureContentSizedWindow();
        PositionOnMonitor(request.Monitor, request.Settings.Position);
        _hasCompletedInitialLayout = true;
        BeginEntryAnimation(request.Settings.Position);

        _hideTimer.Interval = TimeSpan.FromMilliseconds(request.Settings.DurationMilliseconds);
        _hideTimer.Start();
    }

    /// <summary>
    /// 按“客户端尺寸等于内容期望尺寸”校验并修正窗口尺寸。
    /// 首次 Show 时 WPF 仍按窗口当时的标题栏和边框补偿 <c>SizeToContent</c>：窗口在 96 DPI 下比内容宽出
    /// 16 物理像素、高出 39 物理像素（<c>SM_CXSIZEFRAME</c> 与 <c>SM_CYCAPTION</c> 的合计），内容停在左上角，
    /// 右侧与下方留下空带；复用同一 HWND 的后续呈现不再补偿标题栏，尺寸因此正常。
    /// 这里在窗口框架稳定后重新施加一次 <c>SizeToContent</c>，让首次呈现与后续复用得到同一尺寸。
    /// Verifies and corrects the window size against the content's desired size. On the first Show, WPF still compensates
    /// <c>SizeToContent</c> for the caption and frame the window had at that moment: the window is 16 physical pixels wider
    /// and 39 physical pixels taller than its content at 96 DPI (the combined <c>SM_CXSIZEFRAME</c> and <c>SM_CYCAPTION</c>
    /// metrics), and the content stays anchored to the top-left with empty strips at the right and bottom. Later
    /// presentations reuse the HWND without that compensation and size correctly. Re-applying <c>SizeToContent</c> once the
    /// window frame has settled makes the first presentation match every later one.
    /// </summary>
    private void EnsureContentSizedWindow()
    {
        for (var attempt = 0; attempt < ContentSizeAttemptCount; attempt++)
        {
            UpdateLayout();
            var desired = AnimatedRoot.DesiredSize;
            var widthMatches = Math.Abs(ActualWidth - desired.Width) <= ContentSizeTolerance;
            var heightMatches = Math.Abs(ActualHeight - desired.Height) <= ContentSizeTolerance;
            if (desired.Width > 0 && desired.Height > 0 && widthMatches && heightMatches)
            {
                return;
            }

            var sizeToContent = SizeToContent;
            SizeToContent = System.Windows.SizeToContent.Manual;
            SizeToContent = sizeToContent;
        }

        Debug.WriteLine(
            $"[TrackChangeNotification] Content size did not settle: window {ActualWidth}x{ActualHeight}, " +
            $"content {AnimatedRoot.DesiredSize.Width}x{AnimatedRoot.DesiredSize.Height}");
    }

    /// <summary>补全当前可见通知的封面或文字，不重新显示窗口或重置计时。 / Enriches artwork or text for the visible notification without re-showing it or resetting its timer.</summary>
    public void UpdateSnapshot(MediaSnapshot snapshot)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => UpdateSnapshot(snapshot));
            return;
        }

        if (!IsVisible || _currentMonitor is null)
            return;

        ApplySnapshot(snapshot);
        if (!_hasCompletedInitialLayout)
            return;

        UpdateLayout();
        PositionOnMonitor(_currentMonitor, _currentPosition);
    }

    private void ApplySnapshot(MediaSnapshot snapshot) => NotificationContent.Snapshot = snapshot;

    /// <summary>立即隐藏通知并取消动画和计时。 / Immediately hides the notification and cancels animations and timing.</summary>
    public void HideImmediately()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(HideImmediately);
            return;
        }

        _presentationVersion++;
        _isExiting = false;
        _hideTimer.Stop();
        StopAnimations();
        if (IsVisible)
            Hide();
    }

    private void PositionOnMonitor(DisplayMonitorInfo monitor, TrackChangeNotificationPosition position)
    {
        var scaleX = Math.Max(1d / 96d, monitor.DpiX / 96d);
        var scaleY = Math.Max(1d / 96d, monitor.DpiY / 96d);
        var pixelSize = NotificationPlacementCalculator.ToPhysicalSize(
            new Size(Math.Max(1, ActualWidth), Math.Max(1, ActualHeight)),
            monitor.DpiX,
            monitor.DpiY);
        var point = NotificationPlacementCalculator.Calculate(
            monitor.WorkArea,
            pixelSize,
            position,
            16 * Math.Max(scaleX, scaleY),
            _currentSettings.HorizontalOffsetDip * scaleX,
            _currentSettings.VerticalOffsetDip * scaleY);
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
            return;

        NativeMethods.SetWindowPos(
            handle,
            -1,
            (int)Math.Round(point.X),
            (int)Math.Round(point.Y),
            0,
            0,
            NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
    }

    private void BeginEntryAnimation(TrackChangeNotificationPosition position)
    {
        var motion = MotionPolicy.ResolveCurrent();
        var startsAtTop = position is TrackChangeNotificationPosition.TopLeft or
            TrackChangeNotificationPosition.TopCenter or TrackChangeNotificationPosition.TopRight;
        if (!motion.UseTransitions)
        {
            Opacity = 1;
            EntryTransform.Y = 0;
            EntryScaleTransform.ScaleX = EntryScaleTransform.ScaleY = 1;
            return;
        }

        var duration = motion.StandardDuration;
        if (motion.Mode == MotionMode.Full)
        {
            if (Opacity <= 0)
            {
                EntryTransform.Y = startsAtTop ? -12 : 12;
                EntryScaleTransform.ScaleX = EntryScaleTransform.ScaleY = 0.98;
            }
            EntryTransform.BeginAnimation(TranslateTransform.YProperty, CreateTransition(EntryTransform.Y, 0, duration));
            EntryScaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, CreateTransition(EntryScaleTransform.ScaleX, 1, duration));
            EntryScaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, CreateTransition(EntryScaleTransform.ScaleY, 1, duration));
        }
        else
        {
            EntryTransform.Y = 0;
            EntryScaleTransform.ScaleX = EntryScaleTransform.ScaleY = 1;
        }
        // 整个 HWND 一起淡入，避免背景材质先于文字突然出现；中断时从当前透明度继续。
        BeginAnimation(OpacityProperty, CreateTransition(Opacity, 1, duration));
    }

    private static DoubleAnimation CreateTransition(double from, double to, TimeSpan duration) => new(from, to, new Duration(duration))
    {
        EasingFunction = new PowerEase { Power = 3, EasingMode = EasingMode.EaseOut }
    };

    private void HideTimer_Tick(object? sender, EventArgs e) => BeginExitAnimation();

    private void BeginExitAnimation()
    {
        if (!IsVisible || _isExiting) return;
        _isExiting = true;
        _hideTimer.Stop();
        var version = ++_presentationVersion;
        var motion = MotionPolicy.ResolveCurrent();
        if (!motion.UseTransitions)
        {
            HideImmediately();
            return;
        }

        var animation = CreateTransition(Opacity, 0, motion.ExitDuration);
        animation.Completed += (_, _) =>
        {
            if (version == _presentationVersion && IsVisible)
                HideImmediately();
        };
        BeginAnimation(OpacityProperty, animation);
    }

    private void Window_MouseEnter(object sender, MouseEventArgs e) => BeginExitAnimation();

    private void StopAnimations(bool preservePresentation = false)
    {
        var opacity = Opacity;
        var scaleX = EntryScaleTransform.ScaleX;
        var scaleY = EntryScaleTransform.ScaleY;
        var offset = EntryTransform.Y;
        BeginAnimation(OpacityProperty, null);
        AnimatedRoot.BeginAnimation(OpacityProperty, null);
        EntryScaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        EntryScaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        EntryTransform.BeginAnimation(TranslateTransform.YProperty, null);
        AnimatedRoot.Opacity = 1;
        Opacity = preservePresentation ? opacity : 0;
        EntryScaleTransform.ScaleX = preservePresentation ? scaleX : 1;
        EntryScaleTransform.ScaleY = preservePresentation ? scaleY : 1;
        EntryTransform.Y = preservePresentation ? offset : 0;
    }

    private void ApplyMotionEffects()
    {
        if (MotionPolicy.ResolveCurrent().UseDecorativeEffects)
            AnimatedRoot.SetResourceReference(Border.EffectProperty, "AfNotificationShadowEffect");
        else
            AnimatedRoot.Effect = null;
    }

    /// <summary>停止通知计时并释放窗口。 / Stops notification timing and releases the window.</summary>
    protected override void OnClosed(EventArgs e)
    {
        _presentationVersion++;
        _hideTimer.Stop();
        _hideTimer.Tick -= HideTimer_Tick;
        StopAnimations();
        base.OnClosed(e);
    }
}
