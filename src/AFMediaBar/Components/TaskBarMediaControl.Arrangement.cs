// Applies horizontal component direction and hover anchors; the control owns all referenced visuals.
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Settings;

namespace AFMediaBar.Components;

/// <summary>媒体栏横向排布呈现。/ Horizontal arrangement of the media bar.</summary>
public partial class TaskBarMediaControl
{
    private long? _taskbarAnchorTwicePx;
    private int _taskbarPrimaryPositionPx;
    private double _taskbarPlacementDpiScale = 1;

    /// <summary>宿主提供同一物理坐标基准，随后应用长度时同步放置按钮。</summary>
    public void ApplyPlacementAnchor(int primaryPositionPx, long anchorTwicePx, double dpiScale)
    {
        _taskbarPrimaryPositionPx = primaryPositionPx;
        _taskbarAnchorTwicePx = anchorTwicePx;
        _taskbarPlacementDpiScale = dpiScale;
    }

    /// <summary>按实际控件与对齐计算可行最小长度，返回 DIP；不取决于标题宽度。</summary>
    public double GetMinimumPrimaryLength(double dpiScale)
    {
        var scale = double.IsFinite(dpiScale) && dpiScale > 0 ? dpiScale : 1;
        var experience = SettingsManager.Current.TaskbarExperience.Normalize();
        if (_isVertical || !_isConnected || !experience.HoverLayerEnabled)
            return Math.Ceiling(_minimumPrimaryLength * scale) / scale;
        var baseLength = Math.Max(_minimumPrimaryLength, ResolveRestLayout(experience, _minimumPrimaryLength).ContentWidth);
        var layout = ResolveRestLayout(experience, baseLength);
        TaskbarHoverActions.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var actions = Math.Ceiling(TaskbarHoverActions.DesiredSize.Width * scale);
        var left = Math.Ceiling(layout.TextLeft * scale) + Math.Ceiling(5 * scale);
        var right = Math.Ceiling((baseLength - layout.TextLeft - layout.TextWidth) * scale) + Math.Ceiling(6 * scale);
        var minimum = TaskbarArrangementPolicy.MinimumLength(left, right, actions, SettingsManager.Current.Position);
        return Math.Max(Math.Ceiling(_minimumPrimaryLength * scale), minimum) / scale;
    }

    private TaskbarContentArrangement ResolvedTaskbarArrangement => TaskbarArrangementPolicy.ResolveContent(
        SettingsManager.Current.TaskbarExperience.Normalize().Arrangement, SettingsManager.Current.Position);

    private TaskbarBarPosition ResolvedTaskbarHoverAlignment => SettingsManager.Current.Position;

    private void ApplyTaskbarActionsGeometry(double barWidth, double textLeft, double textWidth)
    {
        TaskbarHoverActions.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        // Position must not contribute to the next measure. Margin plus layout rounding fed a
        // one-pixel error back into DesiredSize at fractional widths and alternated every refresh.
        var dpiScale = _taskbarAnchorTwicePx.HasValue ? _taskbarPlacementDpiScale : VisualTreeHelper.GetDpi(TaskbarHoverActions).DpiScaleX;
        var actionsPx = (int)Math.Ceiling(TaskbarHoverActions.DesiredSize.Width * dpiScale);
        var textLeftPx = (int)Math.Round(textLeft * dpiScale);
        var origin = _taskbarAnchorTwicePx.HasValue ? _taskbarPrimaryPositionPx : 0;
        var alignment = ResolvedTaskbarHoverAlignment;
        double absolute;
        if (alignment == TaskbarBarPosition.Center)
        {
            var anchor = _taskbarAnchorTwicePx ?? (long)Math.Round(barWidth * dpiScale);
            absolute = Math.Floor((anchor - actionsPx) / 2.0);
        }
        else
        {
            // Use a canonical fixed-width layout, rather than subtracting changing widths.
            // This also avoids a one-pixel ties-to-even oscillation at the right anchor.
            var experience = SettingsManager.Current.TaskbarExperience.Normalize();
            var baseWidth = Math.Max(_minimumPrimaryLength, ResolveRestLayout(experience, _minimumPrimaryLength).ContentWidth);
            var fixedLayout = ResolveRestLayout(experience, baseWidth);
            var anchor = _taskbarAnchorTwicePx.HasValue ? _taskbarAnchorTwicePx.Value / 2.0
                : alignment == TaskbarBarPosition.End ? Math.Round(barWidth * dpiScale) : 0;
            absolute = alignment == TaskbarBarPosition.End
                ? anchor - Math.Ceiling(Math.Round((baseWidth - fixedLayout.TextLeft - fixedLayout.TextWidth + 6) * dpiScale, 6)) - actionsPx
                : anchor + Math.Ceiling(Math.Round((fixedLayout.TextLeft + 5) * dpiScale, 6));
        }
        var local = absolute - origin - textLeftPx;
        TaskbarHoverActionsOffset.X = (Math.Round(local) - Math.Round(5 * dpiScale)) / dpiScale;
    }

    private Rect TaskbarRevealRect(double revealedWidth) => new(
        TaskbarArrangementPolicy.RevealLeft(TaskbarHoverLayer.Width, revealedWidth, ResolvedTaskbarHoverAlignment),
        0, revealedWidth, HoverRevealHost.Height);
}
