// 统一横竖向按钮与完整面板入口的主轴锚点；布局快照和引用的视觉对象均由本控件拥有。
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Settings;
using AFMediaBar.Classes.Services.Layout;

namespace AFMediaBar.Components;

/// <summary>媒体栏排布方向、悬停按钮与完整面板入口的锚点呈现。</summary>
public partial class TaskBarMediaControl
{
    private long? _taskbarAnchorTwicePx;
    private int _taskbarPrimaryPositionPx;
    private double _taskbarPlacementDpiScale = 1;
    private double? _fullPanelEntryPrimaryDip;
    private HoverActionsGeometry? _hoverActionsGeometry;
    private readonly record struct HoverActionsGeometry(double Length, double TextLeft, double TextLength,
        double Leading, double Trailing, double ActionsLength, double ReferenceLeading, double ReferenceTrailing);

    /// <summary>宿主提供同一物理坐标基准，随后应用长度时同步放置按钮。</summary>
    public void ApplyPlacementAnchor(int primaryPositionPx, long anchorTwicePx, double dpiScale)
    {
        if (_taskbarPrimaryPositionPx == primaryPositionPx && _taskbarAnchorTwicePx == anchorTwicePx && _taskbarPlacementDpiScale == dpiScale) return;
        _taskbarPrimaryPositionPx = primaryPositionPx;
        _taskbarAnchorTwicePx = anchorTwicePx;
        _taskbarPlacementDpiScale = dpiScale;
        if (_hoverActionsGeometry is { } geometry) PositionHoverActions(geometry);
    }

    /// <summary>按实际控件与对齐计算可行最小长度，返回 DIP；不取决于标题宽度。</summary>
    public double GetMinimumPrimaryLength(double dpiScale)
    {
        var scale = double.IsFinite(dpiScale) && dpiScale > 0 ? dpiScale : 1;
        var experience = PresentationSettings.TaskbarExperience.Normalize();
        if (!_isConnected || !experience.HoverLayerEnabled)
            return Math.Ceiling(_minimumPrimaryLength * scale) / scale;
        var baseLength = GetHoverReferenceLength();
        var layout = _isVertical ? ResolveVerticalLayout(baseLength) : ResolveRestLayout(experience, baseLength);
        var actions = Math.Ceiling(MeasureHoverActionsPrimaryLength() * scale);
        var left = Math.Ceiling(layout.TextLeft * scale) + Math.Ceiling((_isVertical ? 2 : TaskbarHoverContent.Margin.Left) * scale);
        var right = Math.Ceiling((baseLength - layout.TextLeft - layout.TextWidth) * scale) + Math.Ceiling((_isVertical ? 2 : TaskbarHoverContent.Margin.Right) * scale);
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

    private double MeasureHoverActionsPrimaryLength()
    {
        TaskbarHoverActions.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return _isVertical ? TaskbarHoverActions.DesiredSize.Height : TaskbarHoverActions.DesiredSize.Width;
    }

    private double GetHoverReferenceLength()
    {
        var layout = _isVertical ? ResolveVerticalLayout(_minimumPrimaryLength)
            : ResolveRestLayout(PresentationSettings.TaskbarExperience.Normalize(), _minimumPrimaryLength);
        return Math.Max(_minimumPrimaryLength, layout.ContentWidth);
    }

    internal Point GetFullPanelEntryCenter()
    {
        var primary = _fullPanelEntryPrimaryDip ?? _minimumPrimaryLength / 2;
        return _isVertical ? new Point(MainBorder.Width / 2, primary) : new Point(primary, MainBorder.Height / 2);
    }

    internal Point GetFullPanelEntryScreenCenter() => PointToScreen(GetFullPanelEntryCenter());

    private void ApplyTaskbarActionsGeometry(double barWidth, double textLeft, double textWidth) =>
        ApplyHoverActionsGeometry(barWidth, textLeft, textWidth, TaskbarHoverContent.Margin.Left, TaskbarHoverContent.Margin.Right);

    private void ApplyHoverActionsGeometry(double primaryLength, double textLeft, double textWidth, double leading, double trailing)
    {
        var referenceLength = GetHoverReferenceLength();
        var reference = _isVertical ? ResolveVerticalLayout(referenceLength)
            : ResolveRestLayout(PresentationSettings.TaskbarExperience.Normalize(), referenceLength);
        var geometry = new HoverActionsGeometry(primaryLength, textLeft, textWidth, leading, trailing,
            MeasureHoverActionsPrimaryLength(), reference.TextLeft, referenceLength - reference.TextLeft - reference.TextWidth);
        _hoverActionsGeometry = geometry;
        PositionHoverActions(geometry);
    }

    private void PositionHoverActions(HoverActionsGeometry geometry)
    {
        var (primaryLength, textLeft, textWidth, leading, trailing, measuredLength, referenceLeading, referenceTrailing) = geometry;
        // Position must not contribute to the next measure. Margin plus layout rounding fed a
        // one-pixel error back into DesiredSize at fractional widths and alternated every refresh.
        var dpiScale = _taskbarAnchorTwicePx.HasValue ? _taskbarPlacementDpiScale : VisualTreeHelper.GetDpi(TaskbarHoverActions).DpiScaleX;
        var hasActions = PresentationSettings.TaskbarExperience.HoverLayerEnabled && measuredLength > 0;
        var actionsPx = (int)Math.Ceiling((hasActions ? measuredLength : FullPanelPlacementPolicy.HitLengthDip) * dpiScale);
        var leadingPx = Math.Ceiling(leading * dpiScale);
        var trailingPx = Math.Ceiling(trailing * dpiScale);
        var textLeftPx = (int)Math.Round(textLeft * dpiScale);
        var origin = _taskbarAnchorTwicePx.HasValue ? _taskbarPrimaryPositionPx : 0;
        var alignment = ResolvedTaskbarHoverAlignment;
        double absolute;
        if (alignment == TaskbarBarPosition.Center)
        {
            var anchor = _taskbarAnchorTwicePx ?? (long)Math.Round(primaryLength * dpiScale);
            absolute = Math.Floor((anchor - actionsPx) / 2.0);
        }
        else
        {
            // Use a canonical fixed-width layout, rather than subtracting changing widths.
            // This also avoids a one-pixel ties-to-even oscillation at the right anchor.
            var anchor = _taskbarAnchorTwicePx.HasValue ? _taskbarAnchorTwicePx.Value / 2.0
                : alignment == TaskbarBarPosition.End ? Math.Round(primaryLength * dpiScale) : 0;
            absolute = alignment == TaskbarBarPosition.End
                ? anchor - Math.Ceiling(Math.Round(referenceTrailing * dpiScale, 6)) - trailingPx - actionsPx
                : anchor + Math.Ceiling(Math.Round(referenceLeading * dpiScale, 6)) + leadingPx;
        }
        if (!hasActions)
        {
            _fullPanelEntryPrimaryDip = Math.Clamp(absolute - origin + actionsPx / 2.0,
                Math.Min(actionsPx / 2.0, primaryLength * dpiScale / 2),
                Math.Max(primaryLength * dpiScale / 2, primaryLength * dpiScale - actionsPx / 2.0)) / dpiScale;
            return;
        }
        var local = absolute - origin - textLeftPx;
        // 设置刷新或尺寸过渡中锚点可能仍属于上一帧，先保证按钮落在当前文字区。
        // 留一个物理像素，容纳边界和内边距各自的布局舍入误差。
        var maximum = Math.Max(1, Math.Floor(textWidth * dpiScale) - actionsPx - 1);
        local = Math.Clamp(Math.Round(local), 1, maximum);
        var offset = (local - Math.Round(leading * dpiScale)) / dpiScale;
        if (_isVertical) TaskbarHoverActionsOffset.Y = offset;
        else TaskbarHoverActionsOffset.X = offset;
        _fullPanelEntryPrimaryDip = (textLeftPx + local + actionsPx / 2.0) / dpiScale;
    }

    private Rect TaskbarRevealRect(double revealedWidth) => new(
        _isVertical ? (_verticalTaskbarOnLeft ? Math.Max(0, TaskbarHoverLayer.Width - revealedWidth) : 0) :
            TaskbarArrangementPolicy.RevealLeft(TaskbarHoverLayer.Width, revealedWidth, ResolvedTaskbarHoverAlignment),
        0, revealedWidth, HoverRevealHost.Height);
}
