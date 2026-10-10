using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using AFMediaBar.Classes.Interop;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services;
using AFMediaBar.Resources;
using Wpf.Ui.Controls;
using Button = System.Windows.Controls.Button;

namespace AFMediaBar.Views.Windows;

/// <summary>复用托盘音频面板外观和生命周期的任务栏紧凑菜单。 / Taskbar compact menu sharing the tray audio flyout appearance and lifecycle.</summary>
public partial class TaskbarCompactFlyoutWindow : FluentWindow, IDisposable
{
    private TaskbarCompactFlyoutMode _mode;
    private bool _isUpdating;
    private bool _isOutputDropDownOpen;
    private bool _isHiding;
    private bool _allowClose;
    private int _animationVersion;

    public event Action<QuickLaunchEntry>? QuickLaunchSelected;
    public event Action<AudioDeviceOption>? OutputDeviceSelected;
    public event Action<int>? OutputDeviceWheelRequested;
    public event Action<int>? VolumeWheelRequested;
    public event Action<int>? VolumeValueRequested;

    /// <summary>创建使用统一窗口材质的紧凑菜单。 / Creates a compact menu using the shared window material.</summary>
    public TaskbarCompactFlyoutWindow(WindowAppearanceService appearanceService)
    {
        InitializeComponent();
        appearanceService.Attach(this);
    }

    /// <summary>判断指定内容当前是否可见。 / Determines whether the requested content is currently visible.</summary>
    public bool IsShowing(TaskbarCompactFlyoutMode mode) => IsVisible && _mode == mode;

    /// <summary>按内容宽度显示快速启动列表。 / Shows the quick-launch list sized to its content.</summary>
    public void ShowQuickLaunch(IReadOnlyList<QuickLaunchEntry> entries, TrayIconBounds anchor)
    {
        _isUpdating = true;
        QuickLaunchList.ItemsSource = entries;
        QuickLaunchEmptyText.Visibility = entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        QuickLaunchStatusText.Visibility = Visibility.Collapsed;
        _isUpdating = false;
        var longest = entries.Count == 0 ? 12 : entries.Max(entry => entry.DisplayName.Length);
        Width = Math.Clamp(76 + longest * 8d, 180, 320);
        ShowMode(TaskbarCompactFlyoutMode.QuickLaunch, anchor);
    }

    /// <summary>显示与托盘面板同宽的输出设备选择器。 / Shows the output-device selector at the tray-panel width.</summary>
    public void ShowOutputDevices(IReadOnlyList<AudioDeviceOption> devices, AudioDeviceOption? selected, TrayIconBounds anchor)
    {
        _isUpdating = true;
        OutputDeviceComboBox.ItemsSource = devices;
        OutputDeviceComboBox.SelectedItem = selected;
        _isUpdating = false;
        Width = 320;
        ShowMode(TaskbarCompactFlyoutMode.OutputDevice, anchor);
    }

    /// <summary>
    /// 显示竖向媒体音量面板：来源名称、百分比与竖向滑块自上而下排列。
    /// Shows the vertical media-volume panel: source name, percentage, and vertical slider from top to bottom.
    /// </summary>
    public void ShowVolume(string sourceName, int? volume, TrayIconBounds anchor)
    {
        _isUpdating = true;
        // 文案在每次显示时按当前语言取：窗口长期存在，但可见内容每次都重新填，因此不需要订阅语言变化。
        // The text is read in the active language every time the menu is shown: the window lives on, but its visible content is
        // filled again on each presentation, so no language subscription is needed.
        VolumeSourceText.Text = string.IsNullOrWhiteSpace(sourceName) ? Translations.Get("Panel.Volume.CurrentMedia") : sourceName;
        VolumeSlider.IsEnabled = volume is not null;
        VolumeSlider.Value = volume ?? 0;
        VolumePercentText.Text = volume is int value ? $"{value}%" : Translations.Get("Panel.Volume.Unavailable");
        _isUpdating = false;
        Width = 132;
        ShowMode(TaskbarCompactFlyoutMode.Volume, anchor);
    }

    /// <summary>更新滚轮预览的输出设备。 / Updates the wheel-previewed output device.</summary>
    public void SetOutputDevicePreview(AudioDeviceOption device)
    {
        _isUpdating = true;
        OutputDeviceComboBox.SelectedItem = device;
        _isUpdating = false;
    }

    /// <summary>更新滚轮预览的媒体音量。 / Updates the wheel-previewed media volume.</summary>
    public void SetVolumePreview(int volume)
    {
        _isUpdating = true;
        VolumeSlider.Value = volume;
        VolumePercentText.Text = $"{volume}%";
        _isUpdating = false;
    }

    /// <summary>在快速启动菜单内显示失败状态。 / Shows a failure status in the quick-launch menu.</summary>
    public void ShowQuickLaunchStatus(string message)
    {
        QuickLaunchStatusText.Text = message;
        QuickLaunchStatusText.Visibility = Visibility.Visible;
    }

    /// <summary>使用统一退出动效隐藏菜单。 / Hides the menu with the shared exit motion.</summary>
    public void Dismiss()
    {
        if (IsVisible) BeginHideAnimation();
    }

    private void ShowMode(TaskbarCompactFlyoutMode mode, TrayIconBounds anchor)
    {
        _animationVersion++;
        _mode = mode;
        QuickLaunchPanel.Visibility = mode == TaskbarCompactFlyoutMode.QuickLaunch ? Visibility.Visible : Visibility.Collapsed;
        OutputDevicePanel.Visibility = mode == TaskbarCompactFlyoutMode.OutputDevice ? Visibility.Visible : Visibility.Collapsed;
        VolumePanel.Visibility = mode == TaskbarCompactFlyoutMode.Volume ? Visibility.Visible : Visibility.Collapsed;
        _isHiding = false;
        ApplyMotionEffects();
        FlyoutRoot.BeginAnimation(OpacityProperty, null);
        FlyoutScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        FlyoutScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        var motion = MotionPolicy.ResolveCurrent();
        FlyoutRoot.Opacity = motion.UseTransitions ? 0 : 1;
        FlyoutScale.ScaleX = FlyoutScale.ScaleY = motion.UseTransitions ? 0.98 : 1;
        if (!IsVisible) Show();
        Activate();
        UpdateLayout();
        PositionNear(anchor);
        if (!motion.UseTransitions) return;
        var ease = new PowerEase { Power = 3, EasingMode = EasingMode.EaseOut };
        FlyoutRoot.BeginAnimation(OpacityProperty, new DoubleAnimation(1, motion.PanelDuration) { EasingFunction = ease });
        FlyoutScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, motion.PanelDuration) { EasingFunction = ease });
        FlyoutScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, motion.PanelDuration) { EasingFunction = ease });
    }

    private void PositionNear(TrayIconBounds anchor)
    {
        var point = new NativeMethods.POINT { X = (anchor.Left + anchor.Right) / 2, Y = (anchor.Top + anchor.Bottom) / 2 };
        var monitor = NativeMethods.MonitorFromPoint(point, NativeMethods.MONITOR_DEFAULTTONEAREST);
        var info = new NativeMethods.MONITORINFOEX { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MONITORINFOEX>() };
        NativeMethods.GetMonitorInfo(monitor, ref info);
        var scale = NativeMethods.GetDpiForMonitor(monitor, NativeMethods.MonitorDpiType.MDT_EFFECTIVE_DPI, out var dpiX, out _) == 0 ? dpiX / 96d : 1d;
        var width = (int)Math.Ceiling(ActualWidth * scale);
        var height = (int)Math.Ceiling(ActualHeight * scale);
        var x = Math.Clamp((anchor.Left + anchor.Right - width) / 2, info.rcWork.Left, info.rcWork.Right - width);
        var y = anchor.Top - info.rcWork.Top >= height + 8 ? anchor.Top - height - 8 : anchor.Bottom + 8;
        y = Math.Clamp(y, info.rcWork.Top, info.rcWork.Bottom - height);
        FlyoutRoot.RenderTransformOrigin = y < anchor.Top ? new Point(0.5, 1) : new Point(0.5, 0);
        NativeMethods.SetWindowPos(new WindowInteropHelper(this).Handle, -1, x, y, 0, 0,
            NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
    }

    private void QuickLaunchItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: QuickLaunchEntry entry })
        {
            Dismiss();
            QuickLaunchSelected?.Invoke(entry);
        }
    }

    private void OutputDeviceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isUpdating && OutputDeviceComboBox.SelectedItem is AudioDeviceOption device)
        {
            Dismiss();
            OutputDeviceSelected?.Invoke(device);
        }
    }

    private void OutputDeviceComboBox_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        OutputDeviceWheelRequested?.Invoke(e.Delta);
        e.Handled = true;
    }

    /// <summary>
    /// 菜单任意位置的滚轮都按设备候选推进；菜单内滚轮因此不会冒泡为播放器手势。
    /// Wheel anywhere in the menu advances the device candidate, so an in-menu wheel never bubbles up to a player gesture.
    /// </summary>
    private void OutputDevicePanel_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Handled)
            return;

        OutputDeviceWheelRequested?.Invoke(e.Delta);
        e.Handled = true;
    }

    /// <summary>菜单任意位置的滚轮都按 2% 调节当前媒体音量。/ Wheel anywhere in the menu adjusts the current media volume by 2%.</summary>
    private void VolumePanel_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Handled)
            return;

        VolumeWheelRequested?.Invoke(e.Delta);
        e.Handled = true;
    }

    private void VolumeSlider_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        VolumeWheelRequested?.Invoke(e.Delta);
        e.Handled = true;
    }

    private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isUpdating) return;
        var value = Math.Clamp((int)Math.Round(e.NewValue), 0, 100);
        VolumePercentText.Text = $"{value}%";
        VolumeValueRequested?.Invoke(value);
    }

    private void OutputDeviceComboBox_DropDownOpened(object? sender, EventArgs e) => _isOutputDropDownOpen = true;
    private void OutputDeviceComboBox_DropDownClosed(object? sender, EventArgs e) => _isOutputDropDownOpen = false;

    private void Window_OnDeactivated(object? sender, EventArgs e) => Dispatcher.BeginInvoke(() =>
    {
        if (IsVisible && !_isOutputDropDownOpen && !OutputDeviceComboBox.IsDropDownOpen && !IsActive) BeginHideAnimation();
    }, DispatcherPriority.ContextIdle);

    private void Window_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { Dismiss(); e.Handled = true; }
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_allowClose && Application.Current?.Dispatcher.HasShutdownStarted != true)
        {
            e.Cancel = true;
            Dismiss();
        }
        base.OnClosing(e);
    }

    private void BeginHideAnimation()
    {
        if (_isHiding || !IsVisible) return;
        _isHiding = true;
        var version = ++_animationVersion;
        var motion = MotionPolicy.ResolveCurrent();
        if (!motion.UseTransitions) { Hide(); _isHiding = false; return; }
        var ease = new PowerEase { Power = 3, EasingMode = EasingMode.EaseInOut };
        FlyoutRoot.BeginAnimation(OpacityProperty, new DoubleAnimation(0, motion.ExitDuration) { EasingFunction = ease });
        FlyoutScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.985, motion.ExitDuration) { EasingFunction = ease });
        var close = new DoubleAnimation(0.985, motion.ExitDuration) { EasingFunction = ease, FillBehavior = FillBehavior.Stop };
        close.Completed += (_, _) =>
        {
            if (!IsVisible || version != _animationVersion) return;
            Hide();
            _isHiding = false;
            FlyoutRoot.Opacity = 1;
            FlyoutScale.ScaleX = FlyoutScale.ScaleY = 1;
        };
        FlyoutScale.BeginAnimation(ScaleTransform.ScaleYProperty, close, HandoffBehavior.SnapshotAndReplace);
    }

    private void ApplyMotionEffects()
    {
        if (MotionPolicy.ResolveCurrent().UseDecorativeEffects)
            FlyoutRoot.SetResourceReference(Border.EffectProperty, "AfFlyoutShadowEffect");
        else
            FlyoutRoot.Effect = null;
    }

    /// <summary>永久关闭窗口并解除回调。 / Permanently closes the window and releases callbacks.</summary>
    public void Dispose()
    {
        if (_allowClose) return;
        _allowClose = true;
        QuickLaunchSelected = null;
        OutputDeviceSelected = null;
        OutputDeviceWheelRequested = null;
        VolumeWheelRequested = null;
        VolumeValueRequested = null;
        Close();
    }
}

/// <summary>任务栏紧凑菜单当前内容。 / Current content shown by the taskbar compact menu.</summary>
public enum TaskbarCompactFlyoutMode
{
    QuickLaunch,
    OutputDevice,
    Volume
}
