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
    public Func<bool>? QuickLaunchActionsAllowed { get; set; }
    public event Action<QuickLaunchEntry>? QuickLaunchDirectRequested;
    private bool CanUseQuickLaunch => !_isConnected && IsBarPresentation && IsLoaded && IsVisible &&
        (QuickLaunchActionsAllowed?.Invoke() ?? true);

    internal void BindQuickLaunch(QuickLaunchService selection, Action invalidate)
    {
        _quickLaunchSession?.Dispose();
        _quickLaunchSession = new(this, selection, invalidate);
    }

    internal void ApplyQuickLaunchState(QuickLaunchSettings settings, QuickLaunchEntry? target, int wheelDelta)
    {
        _quickLaunchSettings = settings;
        _quickLaunchTarget = target;
        ApplyQuickLaunchEntries(settings.Entries ?? []);
        if (!_isConnected && target is not null && SongImageBorder.IsMouseOver) SetQuickLaunchPreview(target);
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
        if (!CanUseQuickLaunch || args.StylusDevice is not null) return;
        _quickLaunchClickTimer?.Stop();
        _noteClickCount = args.ClickCount;
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
            CancelQuickLaunchInteraction();
    }

    private void HandleQuickLaunchClick()
    {
        var count = _noteClickCount;
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
                _quickLaunchClickTimer = new DispatcherTimer(DispatcherPriority.Input, Dispatcher);
                _quickLaunchClickTimer.Tick += QuickLaunchSingleClick;
            }
            _quickLaunchClickTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(1, NativeMethods.GetDoubleClickTime()));
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
        var target = pressed ? 0.97 : 1;
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
