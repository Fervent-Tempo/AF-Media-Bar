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
        var experience = PresentationSettings.TaskbarExperience.Normalize();
        if (_isVertical || !_isConnected || !experience.HoverLayerEnabled)
            return Math.Ceiling(_minimumPrimaryLength * scale) / scale;
        var baseLength = Math.Max(_minimumPrimaryLength, ResolveRestLayout(experience, _minimumPrimaryLength).ContentWidth);
        var layout = ResolveRestLayout(experience, baseLength);
        var actions = Math.Ceiling(MeasureTaskbarHoverActionsWidth() * scale);
        var left = Math.Ceiling(layout.TextLeft * scale) + Math.Ceiling(TaskbarHoverContent.Margin.Left * scale);
        var right = Math.Ceiling((baseLength - layout.TextLeft - layout.TextWidth) * scale) + Math.Ceiling(TaskbarHoverContent.Margin.Right * scale);
        var minimum = TaskbarArrangementPolicy.MinimumLength(left, right, actions, PresentationSettings.Position);
        return Math.Max(Math.Ceiling(_minimumPrimaryLength * scale), minimum) / scale;
    }

    /// <summary>
    /// 解析后生效的展开方向（用户手动选择优先，否则跟随任务栏对齐）。原地放大链路以它为单一事实源：
    /// 推挤符号与宿主输入区的扩展侧都按它取，见 <see cref="ArtworkHoverController.AnimateArtworkZoomPush"/>
    /// 与 <see cref="TaskbarArrangementPolicy.ExtendZoomOverflow"/>。
    /// The resolved expansion direction (the user's manual choice first, the taskbar alignment otherwise). The in-place zoom
    /// chain treats it as the single source of truth: both the push sign and the host's input-region extension side follow
    /// it — see <see cref="ArtworkHoverController.AnimateArtworkZoomPush"/> and
    /// <see cref="TaskbarArrangementPolicy.ExtendZoomOverflow"/>.
    /// </summary>
    internal TaskbarContentArrangement ResolvedTaskbarArrangement => TaskbarArrangementPolicy.ResolveContent(
        PresentationSettings.TaskbarExperience.Normalize().Arrangement, PresentationSettings.Position);

    private TaskbarBarPosition ResolvedTaskbarHoverAlignment => PresentationSettings.Position;

    private double MeasureTaskbarHoverActionsWidth()
    {
        TaskbarHoverActions.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return TaskbarHoverActions.DesiredSize.Width;
    }

    private void ApplyTaskbarActionsGeometry(double barWidth, double textLeft, double textWidth)
    {
        // Position must not contribute to the next measure. Margin plus layout rounding fed a
        // one-pixel error back into DesiredSize at fractional widths and alternated every refresh.
        var dpiScale = _taskbarAnchorTwicePx.HasValue ? _taskbarPlacementDpiScale : VisualTreeHelper.GetDpi(TaskbarHoverActions).DpiScaleX;
        var actionsPx = (int)Math.Ceiling(MeasureTaskbarHoverActionsWidth() * dpiScale);
        var leadingPx = Math.Ceiling(TaskbarHoverContent.Margin.Left * dpiScale);
        var trailingPx = Math.Ceiling(TaskbarHoverContent.Margin.Right * dpiScale);
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
            var experience = PresentationSettings.TaskbarExperience.Normalize();
            var baseWidth = Math.Max(_minimumPrimaryLength, ResolveRestLayout(experience, _minimumPrimaryLength).ContentWidth);
            var fixedLayout = ResolveRestLayout(experience, baseWidth);
            var anchor = _taskbarAnchorTwicePx.HasValue ? _taskbarAnchorTwicePx.Value / 2.0
                : alignment == TaskbarBarPosition.End ? Math.Round(barWidth * dpiScale) : 0;
            absolute = alignment == TaskbarBarPosition.End
                ? anchor - Math.Ceiling(Math.Round((baseWidth - fixedLayout.TextLeft - fixedLayout.TextWidth) * dpiScale, 6)) - trailingPx - actionsPx
                : anchor + Math.Ceiling(Math.Round(fixedLayout.TextLeft * dpiScale, 6)) + leadingPx;
        }
        var local = absolute - origin - textLeftPx;
        // 设置刷新或尺寸过渡中锚点可能仍属于上一帧，先保证按钮落在当前文字区。
        // 留一个物理像素，容纳边界和内边距各自的布局舍入误差。
        var maximum = Math.Max(1, Math.Floor(textWidth * dpiScale) - actionsPx - 1);
        local = Math.Clamp(Math.Round(local), 1, maximum);
        TaskbarHoverActionsOffset.X = (local - Math.Round(TaskbarHoverContent.Margin.Left * dpiScale)) / dpiScale;
    }

    private Rect TaskbarRevealRect(double revealedWidth) => new(
        _isVertical ? (_verticalTaskbarOnLeft ? Math.Max(0, TaskbarHoverLayer.Width - revealedWidth) : 0) :
            TaskbarArrangementPolicy.RevealLeft(TaskbarHoverLayer.Width, revealedWidth, ResolvedTaskbarHoverAlignment),
        0, revealedWidth, HoverRevealHost.Height);
}
