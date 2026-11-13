// 播放队列浮层：只负责窗口定位、材质/动效和输入意图，队列状态与命令由 PlayerQueueViewModel 协调。
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using AFMediaBar.Classes.Interop;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services;
using AFMediaBar.ViewModels.Windows;
using Wpf.Ui.Controls;

namespace AFMediaBar.Views.Windows;

/// <summary>锚定在任务栏队列按钮附近的播放队列浮层；外部点击、Esc 或命令关闭。</summary>
public partial class PlayerQueuePanelWindow : FluentWindow, IDisposable
{
    private const double NormalWidth = 380;
    private const double NormalHeight = 520;
    private const double EdgeMargin = 8;

    private readonly PlayerQueueViewModel _viewModel;
    private readonly AnchoredWindowPresentation _windowPresentation;
    private readonly WindowOutsideClickDismissal _outsideClick;
    private bool _isClosing;
    private bool _isDisposed;
    private int _animationVersion;

    /// <summary>最近一次由外部点击触发的关闭时间戳；宿主用它避免“先关后立即重开”。</summary>
    public long LastOutsideDismissedAt { get; private set; }

    /// <summary>是否已在退出流程中；宿主据此避免对同一浮层重复开关。</summary>
    public bool IsClosing => _isClosing;

    public PlayerQueuePanelWindow(
        PlayerQueueViewModel viewModel,
        WindowAppearanceService appearanceService,
        NativeMouseInputMonitor mouseInput)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _windowPresentation = new(this);
        DataContext = viewModel;
        _outsideClick = new(this, mouseInput, () =>
        {
            LastOutsideDismissedAt = Stopwatch.GetTimestamp();
            RequestClose();
        });
        appearanceService.Attach(this);
        _viewModel.CloseRequested += OnCloseRequested;
        Closed += OnClosed;
        ApplyMotionEffects();
    }

    /// <summary>在队列按钮锚点附近显示；面板已关闭/正在关闭时忽略。</summary>
    public void ShowNear(TrayIconBounds anchor)
    {
        if (_isDisposed || _isClosing)
            return;

        _ = ShowNearAsync(anchor);
    }

    private async Task ShowNearAsync(TrayIconBounds anchor)
    {
        try
        {
            ApplyMotionEffects();
            PanelRoot.Opacity = 0;
            PanelScale.ScaleX = PanelScale.ScaleY = 0.98;
            var physicalAnchor = AnchoredWindowPresentation.GetPhysicalAnchor(anchor);
            if (!await _windowPresentation.ShowAsync(physicalAnchor, () => TryPositionNear(anchor)))
            {
                CloseImmediately();
                return;
            }

            _outsideClick.Start();
            BeginOpenAnimation();
            if (ShowActivated)
                Activate();
            _ = ActivateViewModelAsync();
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"[PlayerQueuePanel] Presentation failed: {exception}");
            RequestClose();
        }
    }

    private async Task ActivateViewModelAsync()
    {
        try
        {
            await _viewModel.ActivateAsync();
            if (_isClosing || _isDisposed)
                return;

            // 激活期间列表可能经历初始快照和刷新两次重建；只在最终列表落地后滚动一次当前项。
            await Dispatcher.InvokeAsync(() =>
            {
                if (_isClosing || _viewModel.CurrentItem is not { } current)
                    return;
                QueueList.UpdateLayout();
                QueueList.ScrollIntoView(current);
            }, DispatcherPriority.Loaded);
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"[PlayerQueuePanel] Queue activation failed: {exception}");
        }
    }

    private bool TryPositionNear(TrayIconBounds anchor)
    {
        var point = new NativeMethods.POINT { X = (anchor.Left + anchor.Right) / 2, Y = (anchor.Top + anchor.Bottom) / 2 };
        var monitor = NativeMethods.MonitorFromPoint(point, NativeMethods.MONITOR_DEFAULTTONEAREST);
        var info = new NativeMethods.MONITORINFOEX
        {
            cbSize = Marshal.SizeOf<NativeMethods.MONITORINFOEX>()
        };
        if (!NativeMethods.GetMonitorInfo(monitor, ref info))
            return false;

        var scale = NativeMethods.GetDpiForMonitor(monitor, NativeMethods.MonitorDpiType.MDT_EFFECTIVE_DPI, out var dpiX, out _) == 0
            ? Math.Max(1, dpiX) / 96d
            : 1d;

        var workWidth = Math.Max(1, (info.rcWork.Right - info.rcWork.Left) / scale - EdgeMargin * 2);
        var workHeight = Math.Max(1, (info.rcWork.Bottom - info.rcWork.Top) / scale - EdgeMargin * 2);
        MaxWidth = workWidth;
        MaxHeight = workHeight;
        Width = Math.Min(NormalWidth, workWidth);
        Height = Math.Min(NormalHeight, workHeight);
        UpdateLayout();

        var widthPx = (int)Math.Ceiling(ActualWidth * scale);
        var heightPx = (int)Math.Ceiling(ActualHeight * scale);
        var margin = (int)EdgeMargin;
        var x = Math.Clamp((anchor.Left + anchor.Right - widthPx) / 2,
            info.rcWork.Left, Math.Max(info.rcWork.Left, info.rcWork.Right - widthPx));
        var y = anchor.Top - info.rcWork.Top >= heightPx + margin * 2
            ? anchor.Top - heightPx - margin
            : anchor.Bottom + margin;
        y = Math.Clamp(y, info.rcWork.Top, Math.Max(info.rcWork.Top, info.rcWork.Bottom - heightPx));
        PanelRoot.RenderTransformOrigin = y < anchor.Top ? new Point(0.5, 1) : new Point(0.5, 0);

        return NativeMethods.SetWindowPos(new WindowInteropHelper(this).Handle, -1, x, y, 0, 0,
            NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
    }

    private void BeginOpenAnimation()
    {
        var motion = MotionPolicy.ResolveCurrent();
        PanelRoot.BeginAnimation(OpacityProperty, null);
        PanelScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        PanelScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        if (!motion.UseTransitions)
        {
            PanelRoot.Opacity = 1;
            PanelScale.ScaleX = PanelScale.ScaleY = 1;
            return;
        }

        var ease = new PowerEase { Power = 3, EasingMode = EasingMode.EaseOut };
        PanelRoot.BeginAnimation(OpacityProperty, new DoubleAnimation(1, motion.PanelDuration) { EasingFunction = ease });
        PanelScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, motion.PanelDuration) { EasingFunction = ease });
        PanelScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, motion.PanelDuration) { EasingFunction = ease });
    }

    /// <summary>按现有动效策略关闭；重复调用安全，关闭后立即停止视图模型更新。</summary>
    public void RequestClose()
    {
        if (_isClosing || _isDisposed)
            return;

        _isClosing = true;
        _outsideClick.Stop();
        _viewModel.Deactivate();
        var motion = MotionPolicy.ResolveCurrent();
        if (!IsVisible || _windowPresentation.IsPreparing || !motion.UseTransitions)
        {
            Close();
            return;
        }

        var version = ++_animationVersion;
        var ease = new PowerEase { Power = 3, EasingMode = EasingMode.EaseInOut };
        PanelRoot.BeginAnimation(OpacityProperty, new DoubleAnimation(0, motion.ExitDuration) { EasingFunction = ease });
        PanelScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.985, motion.ExitDuration) { EasingFunction = ease });
        var close = new DoubleAnimation(0.985, motion.ExitDuration) { EasingFunction = ease, FillBehavior = FillBehavior.Stop };
        close.Completed += (_, _) =>
        {
            if (!_isDisposed && version == _animationVersion)
                Close();
        };
        PanelScale.BeginAnimation(ScaleTransform.ScaleYProperty, close, HandoffBehavior.SnapshotAndReplace);
    }

    private void CloseImmediately()
    {
        if (_isDisposed)
            return;
        _isClosing = true;
        _outsideClick.Stop();
        _viewModel.Deactivate();
        Close();
    }

    private void OnCloseRequested(object? sender, EventArgs e) => RequestClose();

    private void QueueList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
            return;

        var item = FindQueueItem(e.OriginalSource as DependencyObject);
        if (item is null || !_viewModel.JumpCommand.CanExecute(item))
            return;

        e.Handled = true;
        _viewModel.JumpCommand.Execute(item);
    }

    private void QueueList_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            RequestClose();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter && QueueList.SelectedItem is PlayerQueueItemViewModel item &&
            _viewModel.JumpCommand.CanExecute(item))
        {
            e.Handled = true;
            _viewModel.JumpCommand.Execute(item);
        }
    }

    private static PlayerQueueItemViewModel? FindQueueItem(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is ListBoxItem { DataContext: PlayerQueueItemViewModel item })
                return item;
            source = source is Visual
                ? VisualTreeHelper.GetParent(source)
                : LogicalTreeHelper.GetParent(source);
        }

        return null;
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
            return;
        RequestClose();
        e.Handled = true;
    }

    private void ApplyMotionEffects()
    {
        if (MotionPolicy.ResolveCurrent().UseDecorativeEffects)
            PanelChrome.SetResourceReference(Border.EffectProperty, "AfPanelShadowEffect");
        else
            PanelChrome.Effect = null;
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        if (_isDisposed)
            return;

        _isDisposed = true;
        _outsideClick.Dispose();
        _windowPresentation.Dispose();
        _viewModel.CloseRequested -= OnCloseRequested;
        _viewModel.Dispose();
        Closed -= OnClosed;
        PanelChrome.Effect = null;
    }

    public void Dispose()
    {
        if (_isDisposed)
            return;

        _isClosing = true;
        _outsideClick.Stop();
        _viewModel.Deactivate();
        if (IsVisible)
            Close();
        OnClosed(this, EventArgs.Empty);
    }
}
