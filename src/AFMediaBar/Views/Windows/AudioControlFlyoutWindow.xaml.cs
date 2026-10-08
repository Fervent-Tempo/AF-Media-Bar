using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Media;
using System.Windows.Threading;
using AFMediaBar.Classes.Interop;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services;
using AFMediaBar.ViewModels.Windows;
using Wpf.Ui.Controls;

namespace AFMediaBar.Views.Windows;

/// <summary>锚定自有托盘图标的紧凑音频面板。 / Compact audio flyout anchored to the app's own tray icon.</summary>
public partial class AudioControlFlyoutWindow : FluentWindow, IDisposable
{
    private bool _isOutputDeviceDropDownOpen;
    private bool _isHiding;
    private bool _isToggling;
    private bool _isDisposed;
    private int _animationVersion;

    public AudioControlViewModel ViewModel { get; }

    /// <summary>
    /// 创建由 MainWindow 持有并释放的可复用音频浮窗，注册统一窗口外观管理。
    /// </summary>
    public AudioControlFlyoutWindow(AudioControlViewModel viewModel, WindowAppearanceService appearanceService)
    {
        ViewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
        ApplyMotionEffects();
        appearanceService.Attach(this);
    }

    /// <summary>
    /// 在托盘锚点附近切换浮窗；显示前完成一次异步音频状态刷新。
    /// Toggles the flyout near the tray anchor and refreshes audio state asynchronously before showing it.
    /// </summary>
    public async Task ToggleAsync(TrayIconBounds? bounds)
    {
        if (_isDisposed || _isToggling || Dispatcher.HasShutdownStarted)
            return;

        // 门禁覆盖刷新和 HWND 创建；同步完成的刷新也必须先退出托盘原生消息回调。
        _isToggling = true;
        try
        {
            await Dispatcher.Yield(DispatcherPriority.Background);
            if (_isDisposed || Dispatcher.HasShutdownStarted)
                return;

            if (IsVisible && !_isHiding)
            {
                BeginHideAnimation();
                return;
            }

            await ViewModel.RefreshAsync();
            if (_isDisposed || Dispatcher.HasShutdownStarted)
                return;

            _animationVersion++;
            _isHiding = false;
            ApplyMotionEffects();
            FlyoutRoot.BeginAnimation(OpacityProperty, null);
            FlyoutScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            FlyoutScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            var motion = MotionPolicy.ResolveCurrent();
            FlyoutRoot.Opacity = motion.UseTransitions ? 0 : 1;
            FlyoutScale.ScaleX = motion.UseTransitions ? 0.98 : 1;
            FlyoutScale.ScaleY = motion.UseTransitions ? 0.98 : 1;
            if (!IsVisible)
                Show();
            Activate();
            UpdateLayout();
            PositionNear(bounds);
            if (motion.UseTransitions)
            {
                var ease = new PowerEase { Power = 3, EasingMode = EasingMode.EaseOut };
                FlyoutRoot.BeginAnimation(OpacityProperty, new DoubleAnimation(1, motion.PanelDuration) { EasingFunction = ease });
                FlyoutScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, motion.PanelDuration) { EasingFunction = ease });
                FlyoutScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, motion.PanelDuration) { EasingFunction = ease });
            }
        }
        finally
        {
            _isToggling = false;
        }
    }

    private void PositionNear(TrayIconBounds? bounds)
    {
        if (bounds is not { } icon)
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            return;
        }

        var monitorPoint = new NativeMethods.POINT { X = (icon.Left + icon.Right) / 2, Y = (icon.Top + icon.Bottom) / 2 };
        var monitor = NativeMethods.MonitorFromPoint(monitorPoint, NativeMethods.MONITOR_DEFAULTTONEAREST);
        var info = new NativeMethods.MONITORINFOEX { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MONITORINFOEX>() };
        NativeMethods.GetMonitorInfo(monitor, ref info);
        var dpi = NativeMethods.GetDpiForMonitor(monitor, NativeMethods.MonitorDpiType.MDT_EFFECTIVE_DPI, out var dpiX, out _) == 0 ? dpiX / 96d : 1d;
        var width = (int)Math.Ceiling(ActualWidth * dpi);
        var height = (int)Math.Ceiling(ActualHeight * dpi);
        var x = Math.Clamp(icon.Right - width, info.rcWork.Left, info.rcWork.Right - width);
        var spaceAbove = icon.Top - info.rcWork.Top;
        var y = spaceAbove >= height + 8 ? icon.Top - height - 8 : icon.Bottom + 8;
        y = Math.Clamp(y, info.rcWork.Top, info.rcWork.Bottom - height);
        FlyoutRoot.RenderTransformOrigin = y < icon.Top
            ? new Point(0.85, 1)
            : new Point(0.85, 0);
        var handle = new WindowInteropHelper(this).Handle;
        NativeMethods.SetWindowPos(handle, -1, x, y, 0, 0,
            NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
    }

    private void OutputDevice_OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        ViewModel.PreviewOutputDeviceWheel(e.Delta);
        e.Handled = true;
    }

    private void ApplicationVolumeSlider_OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is Slider { DataContext: ApplicationVolumeItemViewModel application })
        {
            application.SetApplyImmediately(false);
            var notches = Math.Max(1, Math.Abs(e.Delta) / Mouse.MouseWheelDeltaForOneLine);
            application.AdjustVolume((e.Delta > 0 ? 1 : -1) * notches * 2);
            e.Handled = true;
        }
    }

    private void ApplicationVolumeSlider_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is Slider { DataContext: ApplicationVolumeItemViewModel application })
        {
            application.SetApplyImmediately(true);
        }
    }

    private void ApplicationVolumeSlider_OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is Slider { DataContext: ApplicationVolumeItemViewModel application })
        {
            application.SetApplyImmediately(false);
        }
    }

    private void ApplicationVolumeSlider_OnLostMouseCapture(object sender, MouseEventArgs e)
    {
        if (sender is Slider { DataContext: ApplicationVolumeItemViewModel application })
        {
            application.SetApplyImmediately(false);
        }
    }

    private void ApplicationVolumeSlider_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is Slider { DataContext: ApplicationVolumeItemViewModel application })
        {
            application.SetApplyImmediately(true);
        }
    }

    private void OutputDevice_OnDropDownOpened(object sender, EventArgs e) =>
        _isOutputDeviceDropDownOpen = true;

    private void OutputDevice_OnDropDownClosed(object sender, EventArgs e) =>
        _isOutputDeviceDropDownOpen = false;

    private void Window_OnDeactivated(object? sender, EventArgs e)
    {
        // ComboBox 的下拉列表使用独立 Popup；等 Popup 状态稳定后再判断是否真的点击到了窗口外。
        // The ComboBox list uses a separate Popup; wait for its state to settle before treating deactivation as an outside click.
        Dispatcher.BeginInvoke(() =>
        {
            if (!_isDisposed && IsVisible && !_isOutputDeviceDropDownOpen && !OutputDeviceComboBox.IsDropDownOpen && !IsActive)
            {
                BeginHideAnimation();
            }
        }, DispatcherPriority.ContextIdle);
    }

    /// <summary>处理面板关闭请求并保留托盘复用实例。/ Handles closing while retaining the tray flyout instance.</summary>
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_isDisposed && Application.Current?.Dispatcher.HasShutdownStarted != true)
        {
            e.Cancel = true;
            BeginHideAnimation();
        }
        base.OnClosing(e);
    }

    private void BeginHideAnimation()
    {
        if (_isDisposed || _isHiding || !IsVisible)
            return;

        _isHiding = true;
        var version = ++_animationVersion;
        var motion = MotionPolicy.ResolveCurrent();
        if (!motion.UseTransitions)
        {
            Hide();
            _isHiding = false;
            return;
        }

        var ease = new PowerEase { Power = 3, EasingMode = EasingMode.EaseInOut };
        FlyoutRoot.BeginAnimation(OpacityProperty, new DoubleAnimation(0, motion.ExitDuration) { EasingFunction = ease });
        FlyoutScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.985, motion.ExitDuration) { EasingFunction = ease });
        var closeAnimation = new DoubleAnimation(0.985, motion.ExitDuration)
        {
            EasingFunction = ease,
            FillBehavior = FillBehavior.Stop
        };
        closeAnimation.Completed += (_, _) =>
        {
            if (_isDisposed || !IsVisible || version != _animationVersion)
                return;
            Hide();
            _isHiding = false;
            FlyoutRoot.Opacity = 1;
            FlyoutScale.ScaleX = 1;
            FlyoutScale.ScaleY = 1;
        };
        FlyoutScale.BeginAnimation(ScaleTransform.ScaleYProperty, closeAnimation, HandoffBehavior.SnapshotAndReplace);
    }

    private void ApplyMotionEffects()
    {
        if (MotionPolicy.ResolveCurrent().UseDecorativeEffects)
            FlyoutRoot.SetResourceReference(Border.EffectProperty, "AfFlyoutShadowEffect");
        else
            FlyoutRoot.Effect = null;
    }

    /// <summary>永久关闭浮窗，取消动画并阻止待完成的显示请求重新打开窗口。</summary>
    public void Dispose()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;
        _animationVersion++;
        OutputDeviceComboBox.IsDropDownOpen = false;
        FlyoutRoot.BeginAnimation(OpacityProperty, null);
        FlyoutScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        FlyoutScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        Close();
    }
}
