// 仅处理小音符输入判定与呈现；单击计时器由控件持有，卸载和宿主取消时立即停止。
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using AFMediaBar.Classes.Interop;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Settings;
using AFMediaBar.Resources;

namespace AFMediaBar.Components;

public partial class TaskBarMediaControl
{
    private DispatcherTimer? _quickLaunchClickTimer;
    private QuickLaunchControlSession? _quickLaunchSession;
    private QuickLaunchSettings _quickLaunchSettings = QuickLaunchSettings.Default;
    private QuickLaunchEntry? _quickLaunchTarget;
    private int _noteClickCount;
    private bool _notePressed;
    private Point _notePressPoint;
    private long _noteDownTick;
    private CancellationTokenSource _noteRequests = new();
    private bool _noteRequestsDisposed;
    internal CancellationToken QuickLaunchRequestToken
    {
        get
        {
            if (_noteRequestsDisposed) return new CancellationToken(canceled: true);
            if (_noteRequests.IsCancellationRequested) { _noteRequests.Dispose(); _noteRequests = new(); }
            return _noteRequests.Token;
        }
    }
    internal void InvalidateQuickLaunchRequests() { if (!_noteRequestsDisposed) _noteRequests.Cancel(); }
    private void DisposeQuickLaunchRequests()
    {
        if (_noteRequestsDisposed) return;
        _noteRequestsDisposed = true;
        _noteRequests.Cancel();
        _noteRequests.Dispose();
    }
    public Func<bool>? QuickLaunchActionsAllowed { get; set; }
    public event Action<QuickLaunchEntry>? QuickLaunchDirectRequested;
    private bool CanUseQuickLaunch => !_isConnected && !_isHostVisibilitySuspended && IsBarPresentation && IsLoaded && IsVisible &&
        (QuickLaunchActionsAllowed?.Invoke() ?? true);

    internal void BindQuickLaunch(QuickLaunchService selection, Action invalidate, QuickLaunchIconService? icons)
    {
        _quickLaunchSession?.Dispose();
        _quickLaunchSession = new(this, selection, invalidate, icons);
    }

    internal void ApplyQuickLaunchState(QuickLaunchSettings settings, QuickLaunchEntry? target, int wheelDelta)
    {
        if (_quickLaunchTarget != target)
        {
            CancelQuickLaunchInteraction();
            InvalidateQuickLaunchRequests();
        }
        _quickLaunchSettings = settings;
        _quickLaunchTarget = target;
        ApplyQuickLaunchEntries(settings.Entries ?? []);
        if (!_isConnected && target is not null && SongImageBorder.IsMouseOver) SetQuickLaunchPreview(target);
        if (!_isConnected && target is null)
        {
            _quickLaunchTooltip.Content = WheelTooltipPolicy.BuildQuickLaunchPreview(null);
            ApplyArtworkTooltipOwner();
        }
    }

    public void CancelQuickLaunchInteraction()
    {
        _quickLaunchClickTimer?.Stop();
        _noteClickCount = 0;
        var pressed = _notePressed;
        _notePressed = false;
        if (pressed) SetNotePressed(false, immediate: true);
    }

    private void SongImageBorder_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs args)
    {
        if (!CanUseQuickLaunch) return;
        _quickLaunchClickTimer?.Stop();
        _noteClickCount = args.ClickCount;
        _noteDownTick = Environment.TickCount64;
        _notePressPoint = args.GetPosition(SongImageBorder);
        _notePressed = true;
        SetNotePressed(true);
    }

    private void SongImageBorder_PreviewMouseMove(object sender, MouseEventArgs args)
    {
        if (!_notePressed || args.LeftButton != MouseButtonState.Pressed) return;
        var point = args.GetPosition(SongImageBorder);
        if (Math.Abs(point.X - _notePressPoint.X) > SystemParameters.MinimumHorizontalDragDistance ||
            Math.Abs(point.Y - _notePressPoint.Y) > SystemParameters.MinimumVerticalDragDistance)
            SuspendQuickLaunchPresentation();
    }

    private void HandleQuickLaunchClick()
        => CompleteQuickLaunchClick(_noteClickCount, _noteDownTick);

    internal void CompleteQuickLaunchClick(int count, long downTick)
    {
        _notePressed = false;
        SetNotePressed(false);
        if (!CanUseQuickLaunch) { CancelQuickLaunchInteraction(); return; }
        if (_quickLaunchSettings.InteractionMode == QuickLaunchInteractionMode.ListLaunch || _quickLaunchEntries.Count == 0)
        {
            QuickLaunchMenuRequested?.Invoke(this, EventArgs.Empty);
            return;
        }
        if (count == 2)
        {
            _quickLaunchClickTimer?.Stop();
            if (_quickLaunchTarget is { } target) QuickLaunchDirectRequested?.Invoke(target);
        }
        else if (count == 1)
        {
            if (_quickLaunchClickTimer is null)
            {
                // 先处理已排队的第二次按下，避免 UI 繁忙时先弹出单击菜单。
                _quickLaunchClickTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher);
                _quickLaunchClickTimer.Tick += QuickLaunchSingleClick;
            }
            var remaining = NativeMethods.GetDoubleClickTime() - (Environment.TickCount64 - downTick);
            _quickLaunchClickTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(1, remaining));
            _quickLaunchClickTimer.Start();
        }
    }

    private void QuickLaunchSingleClick(object? sender, EventArgs args)
    {
        _quickLaunchClickTimer?.Stop();
        if (CanUseQuickLaunch && _quickLaunchSettings.InteractionMode == QuickLaunchInteractionMode.SelectThenLaunch)
            QuickLaunchMenuRequested?.Invoke(this, EventArgs.Empty);
    }

    private void SetNotePressed(bool pressed, bool immediate = false)
    {
        var target = pressed && CurrentMotion.Mode == MotionMode.Full ? 0.97 : 1;
        var duration = immediate || CurrentMotion.Mode != MotionMode.Full ? TimeSpan.Zero : CurrentMotion.FastDuration;
        foreach (var property in new[] { System.Windows.Media.ScaleTransform.ScaleXProperty, System.Windows.Media.ScaleTransform.ScaleYProperty })
        {
            var current = (double)QuickLaunchPressScale.GetValue(property);
            QuickLaunchPressScale.BeginAnimation(property, null);
            QuickLaunchPressScale.SetValue(property, target);
            if (duration > TimeSpan.Zero) QuickLaunchPressScale.BeginAnimation(property,
                new DoubleAnimation(current, target, duration) { EasingFunction = CreateEaseOut(), FillBehavior = FillBehavior.Stop });
        }
    }
}
