// 协调单个横向任务栏的已应用布局状态；窗口拥有状态，关闭后不发布通知。
// 区间与恢复算法归纯策略，探测与托盘资源仍由注入服务及隐藏宿主持有。
using System.Diagnostics;
using System.Windows.Controls;
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
    private TaskbarPlacementState? _dragPlacementSeed;
    private PlacementContext? _dragPlacementContext;

    /// <summary>仅在几何和命中区域已经应用后通知隐藏宿主。</summary>
    public event EventHandler<TaskbarPlacementFallbackEventArgs>? PlacementFallbackRequested;

    private readonly record struct PlacementContext(nint Handle, int Length, double Dpi,
        TaskbarBarPosition Alignment, int Padding, bool Avoid);

    private PlacementContext GetPlacementContext(RECT rect, double dpi) => new(
        _lastTaskbarHandle, rect.Right - rect.Left, dpi, SettingsManager.Current.Position,
        SettingsManager.Current.TaskbarBarManualPadding, SettingsManager.Current.TaskbarBarAvoidIcons);

    private TaskbarPlacementDecision PreviewHorizontalPlacement(RECT rect, double dpi, double desiredDip)
    {
        var context = GetPlacementContext(rect, dpi);
        var state = _dragPlacementContext == context && _dragPlacementSeed is { } seed ? seed
            : _placementContext == context ? _placementState : new TaskbarPlacementState();
        var minimum = MediaControl.IsRestLayerEmpty ? 0 : (int)Math.Ceiling(MediaControl.GetMinimumPrimaryLength(dpi) * dpi);
        TaskbarOccupancySnapshot snapshot;
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
        var desired = _sizeAnimationTimer.IsEnabled ? canvas?.Width ?? 300 : _lastDesiredSizeRequest?.PrimaryLength ?? canvas?.Width ?? 300;
        var decision = PreviewHorizontalPlacement(rect, dpi, desired);
        _pendingPlacementContext = GetPlacementContext(rect, dpi);
        _pendingPlacementDecision = decision;
        _hasSafePlacement = decision.State.IsVisible;
        if (!_hasSafePlacement)
        {
            _sizeAnimationTimer.Stop();
            _lengthConstraints.Remove(this);
            return default;
        }
        if (_sizeAnimationTimer.IsEnabled && ((canvas?.Width ?? 0) * dpi > decision.State.Budget ||
            _sizeAnimationTarget * dpi > decision.State.Budget))
        {
            // Clip this frame immediately, then finish toward the newly capped target instead
            // of stopping at an intermediate width until the next positioning poll.
            _sizeAnimationStart = decision.Width / dpi;
            _sizeAnimationTarget = Math.Clamp(_lastDesiredSizeRequest?.PrimaryLength ?? _sizeAnimationTarget,
                decision.State.Minimum / dpi, decision.State.Budget / dpi);
            _sizeAnimationProgress = 0;
            _sizeAnimationLastTimestamp = Stopwatch.GetTimestamp();
            if (Math.Abs(_sizeAnimationTarget - _sizeAnimationStart) < 0.1)
                _sizeAnimationTimer.Stop();
        }
        var thickness = Math.Min(Math.Max(1, rect.Bottom - rect.Top), (int)Math.Round((canvas?.Height ?? 44) * dpi));
        var cross = Math.Clamp((rect.Bottom - rect.Top - thickness) / 2 +
            (int)Math.Round(SettingsManager.Current.TaskbarBarCrossAxisOffsetDip * dpi), 0, Math.Max(0, rect.Bottom - rect.Top - thickness));
        MediaControl.ApplyPlacementAnchor(decision.Position, decision.State.AppliedAnchorTwice, dpi);
        MediaControl.ApplyPrimaryLength(decision.Width / dpi);
        Canvas.SetLeft(MediaControl, decision.Position / dpi);
        Canvas.SetTop(MediaControl, cross / dpi);
        MediaControl.Width = decision.Width / dpi;
        MediaControl.Height = thickness / dpi;
        _lengthConstraints.Update(this, decision.State.Minimum / dpi, decision.State.Budget / dpi);
        return new RECT { Left = decision.Position, Top = cross, Right = decision.Position + decision.Width, Bottom = cross + thickness };
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
        _placementState = decision.State;
        _placementContext = _pendingPlacementContext;
        _pendingPlacementDecision = null;
        _dragPlacementSeed = null;
        _dragPlacementContext = null;
        if (decision.NotifyFallback && !MediaControl.IsRestLayerEmpty)
            PlacementFallbackRequested?.Invoke(this, new TaskbarPlacementFallbackEventArgs(!decision.State.IsVisible));
    }
}
