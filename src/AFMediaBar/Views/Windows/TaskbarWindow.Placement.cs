// 协调单个横向任务栏的已应用布局状态；窗口拥有状态，关闭后不发布通知。
// 区间与恢复算法归纯策略，探测与托盘资源仍由注入服务及隐藏宿主持有。
using System.Diagnostics;
using System.Windows.Controls;
using System.Windows.Threading;
using AFMediaBar.Classes.Models.Layout;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Settings;
using static AFMediaBar.Classes.Interop.NativeMethods;

namespace AFMediaBar.Views.Windows;

/// <summary>横向任务栏锚点、长度及实际应用的协调。</summary>
public partial class TaskbarWindow
{
    private TaskbarPlacementState _placementState = new();
    private PlacementContext? _placementContext;
    private TaskbarPlacementDecision? _pendingPlacementDecision;
    private PlacementContext? _pendingPlacementContext;
    private TaskbarOccupancySnapshot? _pendingPlacementSnapshot;
    private TaskbarPlacementState? _dragPlacementSeed;
    private PlacementContext? _dragPlacementContext;
    private readonly TaskbarReturnTransition _returnTransition = new();
    private RECT? _renderedHorizontalRect;
    private bool _skipPlacementTransition;
    private double _returnDpi;
    private int _returnCross;
    private int _returnThickness;
    private TaskbarPrimaryRange _returnRange;

    /// <summary>仅在几何和命中区域已经应用后通知隐藏宿主。</summary>
    public event EventHandler<TaskbarPlacementFallbackEventArgs>? PlacementFallbackRequested;
    /// <summary>开始移动前关闭依赖封面位置的浮层。</summary>
    public event EventHandler? PlacementTransitionStarted;

    private void StopLayoutTransition()
    {
        _sizeAnimationTimer?.Stop();
        _returnTransition.Stop();
        MediaControl.ApplyPlacementMotionSuspension(false);
    }

    private void ClosePlacementPopups()
    {
        MediaControl.ApplyPlacementMotionSuspension(true);
        _compactFlyout.Dismiss();
        PlayerMenu.IsOpen = false;
        PlacementTransitionStarted?.Invoke(this, EventArgs.Empty);
    }

    private readonly record struct PlacementContext(nint Handle, int Length, double Dpi,
        TaskbarBarPosition Alignment, int Padding, bool Avoid);

    private PlacementContext GetPlacementContext(RECT rect, double dpi) => new(
        _lastTaskbarHandle, rect.Right - rect.Left, dpi, SettingsManager.Current.Position,
        SettingsManager.Current.TaskbarBarManualPadding, SettingsManager.Current.TaskbarBarAvoidIcons);

    private void QueueOccupancyRefresh()
    {
        if (_isClosing || _isEnvironmentSuspended || IsBackgroundPruned || IsTaskbarPresentationSuspended ||
            _appliedOrientation != LayoutOrientation.Horizontal || !SettingsManager.Current.TaskbarBarAvoidIcons || MediaControl.IsRestLayerEmpty) return;
        _occupancyRefresh.Changed(Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency);
        _occupancyRefreshTimer.Start();
    }

    private void StopOccupancyRefresh()
    {
        _occupancyRefreshTimer?.Stop();
        _occupancyRefresh.Reset();
    }

    private void OccupancyRefreshTimer_Tick(object? sender, EventArgs e)
    {
        var seconds = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
        if (_isClosing || _isEnvironmentSuspended || IsBackgroundPruned || IsTaskbarPresentationSuspended ||
            _isDragging || _appliedOrientation != LayoutOrientation.Horizontal || MediaControl.IsRestLayerEmpty ||
            !SettingsManager.Current.TaskbarBarAvoidIcons || !_occupancyRefresh.IsActive(seconds))
        {
            StopOccupancyRefresh();
            return;
        }
        if (!_occupancyRefresh.TryBegin(seconds) || DateTime.UtcNow < _skipOccupiedAreaProbeUntilUtc ||
            _hostActions.IsEnvironmentRecovering) return;
        if (_taskBarService.TryGetTaskbarRect(_lastTaskbarHandle, out var rect))
            _occupiedAreaService.GetSnapshot(_lastTaskbarHandle, rect, LayoutOrientation.Horizontal, _lastDpiScale, EdgePadding, requestRefresh: true);
    }

    private TaskbarPlacementDecision PreviewHorizontalPlacement(RECT rect, double dpi, double desiredDip) =>
        PreviewHorizontalPlacement(rect, dpi, desiredDip, out _);

    private TaskbarPlacementDecision PreviewHorizontalPlacement(RECT rect, double dpi, double desiredDip,
        out TaskbarOccupancySnapshot snapshot)
    {
        var context = GetPlacementContext(rect, dpi);
        var state = _dragPlacementContext == context && _dragPlacementSeed is { } seed ? seed
            : _placementContext == context ? _placementState : new TaskbarPlacementState();
        var minimum = MediaControl.IsRestLayerEmpty ? 0 : (int)Math.Ceiling(MediaControl.GetMinimumPrimaryLength(dpi) * dpi);
        if (!context.Avoid)
            snapshot = new(TaskbarProbeStatus.Success, 0, 0, Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency,
                [new TaskbarPrimaryRange(Math.Min(EdgePadding, context.Length), Math.Max(Math.Min(EdgePadding, context.Length), context.Length - EdgePadding))], true);
        else if (_hostActions.IsEnvironmentRecovering || IsTaskbarPresentationSuspended || DateTime.UtcNow < _skipOccupiedAreaProbeUntilUtc)
            snapshot = new(TaskbarProbeStatus.Pending, Math.Max(0, state.Generation), 0, 0, []);
        else
            snapshot = _occupiedAreaService.GetSnapshot(_lastTaskbarHandle, rect, LayoutOrientation.Horizontal, dpi, EdgePadding);
        var desired = double.IsFinite(desiredDip) ? (int)Math.Ceiling(Math.Max(0, desiredDip) * dpi) : minimum;
        return TaskbarPlacementPolicy.Evaluate(state, snapshot, context.Alignment, context.Length, context.Padding, minimum, desired);
    }

    private RECT PositionHorizontalBar(RECT rect, double dpi)
    {
        var canvas = MediaControl.CurrentLayout?.Canvas;
        var desired = _lastDesiredSizeRequest?.PrimaryLength ?? canvas?.Width ?? 300;
        var decision = PreviewHorizontalPlacement(rect, dpi, desired, out var snapshot);
        _pendingPlacementContext = GetPlacementContext(rect, dpi);
        _pendingPlacementDecision = decision;
        _pendingPlacementSnapshot = snapshot;
        _hasSafePlacement = decision.State.IsVisible;
        if (!_hasSafePlacement)
        {
            if (_renderedHorizontalRect is not null) ClosePlacementPopups();
            StopLayoutTransition();
            _renderedHorizontalRect = null;
            _lengthConstraints.Remove(this);
            return default;
        }
        var thickness = Math.Min(Math.Max(1, rect.Bottom - rect.Top), (int)Math.Round((canvas?.Height ?? 44) * dpi));
        var cross = Math.Clamp((rect.Bottom - rect.Top - thickness) / 2 +
            (int)Math.Round(SettingsManager.Current.TaskbarBarCrossAxisOffsetDip * dpi), 0, Math.Max(0, rect.Bottom - rect.Top - thickness));
        var motion = MotionPolicy.ResolveCurrent();
        var now = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
        if (!_returnTransition.IsActive && _renderedHorizontalRect is { } rendered &&
            (rendered.Left != decision.Position || rendered.Right - rendered.Left != decision.Width ||
             rendered.Top != cross || rendered.Bottom - rendered.Top != thickness))
            ClosePlacementPopups();
        var canReturn = !_skipPlacementTransition && !_applySettingsSizeImmediately && !_isDragging &&
            !_isRestoringTaskbarPresentation && !MediaControl.IsRestConnectionTransitionActive &&
            _placementContext == _pendingPlacementContext && _placementState.IsVisible &&
            _placementState.Generation == decision.State.Generation && motion.UseTransitions &&
            _renderedHorizontalRect is { } current && current.Top == cross && current.Bottom == cross + thickness &&
            current.Left >= decision.State.Range.Start && current.Right <= decision.State.Range.End;
        if (canReturn && _renderedHorizontalRect is { } previous)
        {
            var wasActive = _returnTransition.IsActive;
            _returnTransition.Retarget(previous.Left, previous.Right - previous.Left,
                decision.Position, decision.Width, now, motion.PositionDuration.TotalSeconds);
            if (_returnTransition.IsActive)
            {
                if (!wasActive)
                {
                    MediaControl.ApplyPlacementMotionSuspension(true);
                }
                _returnDpi = dpi;
                _returnCross = cross;
                _returnThickness = thickness;
                _returnRange = decision.State.Range;
                _sizeAnimationTimer.Interval = TimeSpan.FromMilliseconds(motion.Mode == MotionMode.Reduced ? 33 : 16);
                _sizeAnimationTimer.Start();
                var frame = _returnTransition.Advance(now);
                _lengthConstraints.Update(this, decision.State.Minimum / dpi, decision.State.Budget / dpi);
                if (!_returnTransition.IsActive) StopLayoutTransition();
                return ApplyHorizontalFrame(frame.Position, frame.Width, dpi, cross, thickness);
            }
        }
        StopLayoutTransition();
        _lengthConstraints.Update(this, decision.State.Minimum / dpi, decision.State.Budget / dpi);
        return ApplyHorizontalFrame(decision.Position, decision.Width, dpi, cross, thickness);
    }

    private RECT ApplyHorizontalFrame(double position, double width, double dpi, int cross, int thickness)
    {
        var left = (int)Math.Round(position);
        var pixels = (int)Math.Round(width);
        var alignment = SettingsManager.Current.Position;
        var anchor = 2L * left + (alignment == TaskbarBarPosition.End ? 2L * pixels : alignment == TaskbarBarPosition.Center ? pixels : 0);
        MediaControl.ApplyPlacementAnchor(left, anchor, dpi);
        if (MediaControl.CurrentLayout is not { } layout || Math.Abs(layout.Canvas.Width * dpi - pixels) >= 0.5)
            MediaControl.ApplyPrimaryLength(pixels / dpi);
        Canvas.SetLeft(MediaControl, left / dpi);
        Canvas.SetTop(MediaControl, cross / dpi);
        MediaControl.Width = pixels / dpi;
        MediaControl.Height = thickness / dpi;
        var result = new RECT { Left = left, Top = cross, Right = left + pixels, Bottom = cross + thickness };
        _renderedHorizontalRect = result;
        return result;
    }

    private void AdvanceReturnTransition()
    {
        // 只检查轻量宿主几何；不探测占用区、不重跑设置，也不提交新的逻辑目标。
        ObserveTaskbarMotion(allowStableSample: false);
        if (_isClosing || _isEnvironmentSuspended || _isDragging || IsBackgroundPruned || IsTaskbarPresentationSuspended ||
            _placementContext is not { } context || context != GetPlacementContext(_placedTaskbarRect, _returnDpi))
        {
            StopLayoutTransition();
            return;
        }
        var frame = _returnTransition.Advance(Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency);
        var width = Math.Clamp((int)Math.Round(frame.Width), 1, _returnRange.Length);
        var position = Math.Clamp((int)Math.Round(frame.Position), _returnRange.Start, _returnRange.End - width);
        var rect = ApplyHorizontalFrame(position, width, _returnDpi, _returnCross, _returnThickness);
        if (!_hasInputRegion || !SameRect(_lastInputRegion, rect))
        {
            ApplyBarInputRegion(_windowHandle, rect);
            _lastInputRegion = rect;
            _hasInputRegion = true;
            _inputRegionWindowHandle = _windowHandle;
        }
        if (!_returnTransition.IsActive) StopLayoutTransition();
    }

    private void PrepareHorizontalDrag(int desiredPosition, int width, TaskbarPrimaryRange range, RECT rect, double dpi)
    {
        var position = Math.Clamp(desiredPosition, range.Start, Math.Max(range.Start, range.End - width));
        var alignment = SettingsManager.Current.Position;
        var anchor = 2L * position + (alignment == TaskbarBarPosition.End ? 2L * width
            : alignment == TaskbarBarPosition.Center ? width : 0);
        // A gesture explicitly defines the new home. Seed the next decision without committing
        // geometry during a query or reselecting a newly available endmost range behind the cursor.
        _dragPlacementContext = GetPlacementContext(rect, dpi);
        _dragPlacementSeed = TaskbarPlacementPolicy.ClearConfirmation(_placementState) with
        {
            IsDragSeed = true,
            HasHome = true,
            HomeAnchorTwice = anchor,
            AppliedAnchorTwice = anchor,
            Range = range,
            FallbackNotified = false,
            NeedsSpaceRecovery = false
        };
    }

    private void CommitHorizontalPlacement()
    {
        if (_pendingPlacementDecision is not { } decision || _isClosing)
            return;
        var previous = _placementState;
        if (!_isDragging && !_isDragPending && !MediaControl.IsRestLayerEmpty && (decision.NotifyFallback || previous.HasHome &&
            (previous.IsVisible && !decision.State.IsVisible ||
             previous.IsVisible && decision.State.IsVisible && previous.AppliedAnchorTwice != decision.State.AppliedAnchorTwice)))
        {
            var ranges = _pendingPlacementSnapshot is { } snapshot
                ? string.Join(",", snapshot.Ranges.Select(range => $"[{range.Start},{range.End}]")) : "unknown";
            AppLogService.Current?.Info("Taskbar", $"Placement monitor={_targetMonitorDeviceId}, visible={decision.State.IsVisible}, " +
                $"minimumPx={decision.State.Minimum}, rangePx=[{decision.State.Range.Start},{decision.State.Range.End}], " +
                $"availablePx={ranges}, probe={_pendingPlacementSnapshot?.Status}, budgetPx={decision.State.Budget}, " +
                $"desiredDip={_lastDesiredSizeRequest?.PrimaryLength:F1}, targetPx={decision.Width}");
        }
        _placementState = decision.State with { IsDragSeed = false };
        _placementContext = _pendingPlacementContext;
        _pendingPlacementDecision = null;
        _pendingPlacementSnapshot = null;
        _dragPlacementSeed = null;
        _dragPlacementContext = null;
        if (decision.NotifyFallback && !MediaControl.IsRestLayerEmpty)
            PlacementFallbackRequested?.Invoke(this, new TaskbarPlacementFallbackEventArgs(!decision.State.IsVisible));
    }
}
