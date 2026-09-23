using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Settings;

namespace AFMediaBar.Classes.Services;

/// <summary>信息密度对应的实际组件尺寸。 / Actual component sizes for an information-density preset.</summary>
public readonly record struct TaskbarDensityMetrics(
    double ButtonSize,
    double ProgressWidth,
    double HoverLayerHeight,
    double SectionGap)
{
    public static TaskbarDensityMetrics From(TaskbarInformationDensity density) => density switch
    {
        TaskbarInformationDensity.Minimal => new(22, 76, 36, 6),
        TaskbarInformationDensity.Information => new(28, 118, 42, 10),
        _ => new(24, 96, 40, 8)
    };
}

/// <summary>任务栏媒体呈现、内容宽度和播放进度的纯策略。 / Pure policies for taskbar media presentation, content width, and playback progress.</summary>
public static class TaskbarExperiencePolicy
{
    /// <summary>
    /// 计算横向任务栏媒体条宽度；连接期间固定预留频谱位置，断开时移除该位置。
    /// Calculates horizontal taskbar media-bar width, reserving spectrum space for every connected state.
    /// </summary>
    public static double CalculateWidth(
        double measuredTextWidth,
        double artworkRight,
        double spectrumWidth,
        double trailingMargin,
        bool mediaConnected,
        bool spectrumVisible,
        bool transportVisible,
        bool hoverLayerEnabled,
        bool progressVisible,
        TaskbarInformationDensity density,
        double maximumWidth,
        double? componentSpacingDip = null,
        bool performanceVisible = false,
        double performanceWidth = 0,
        TaskbarHoverControlsSettings? hoverControls = null)
    {
        var sectionGap = ResolveSectionGap(TaskbarDensityMetrics.From(density), componentSpacingDip);
        var hoverMinimum = hoverLayerEnabled
            ? CalculateHoverLayerWidth(
                hoverControls ?? new TaskbarHoverControlsSettings(
                    transportVisible,
                    transportVisible,
                    true,
                    true,
                    progressVisible),
                progressVisible,
                density,
                componentSpacingDip)
            : 0;

        // 媒体文字自己就是列表里的一个组件，宽度取"量出的文字"与"悬停层下限"的较大者；断开时文字组件整块不在列表里，
        // 因此这里不再出现"断开也预留文字宽度"的可能。
        // The media text is itself one component of the list, with a width that is the larger of the measured text and the hover-layer
        // minimum; while disconnected the text component is absent from the list altogether, so there is no way for a disconnected bar
        // to reserve text width.
        var components = new List<TaskbarRestComponent>(4)
        {
            TaskbarRestComponent.Artwork
        };
        if (mediaConnected)
        {
            components.Add(TaskbarRestComponent.MediaText);
        }

        if (mediaConnected && spectrumVisible)
        {
            components.Add(TaskbarRestComponent.Spectrum);
        }

        if (performanceVisible)
        {
            components.Add(TaskbarRestComponent.Performance);
        }

        return CalculateRestWidth(
            components,
            leadingInset: 0,
            textWidth: Math.Max(Math.Max(0, measuredTextWidth), hoverMinimum),
            trailingMargin: trailingMargin,
            sectionGap: sectionGap,
            widthOf: component => component switch
            {
                TaskbarRestComponent.Artwork => Math.Max(0, artworkRight),
                TaskbarRestComponent.Spectrum => Math.Max(0, spectrumWidth),
                TaskbarRestComponent.Performance => Math.Max(0, performanceWidth),
                _ => 0
            },
            maximumWidth: maximumWidth);
    }

    /// <summary>
    /// 按"这次可见的组件集合"计算横向任务栏媒体条需要的长度（单位 DIP）。
    ///
    /// 它与组件顺序无关，这一点是刻意的：媒体栏长度只是一个和，而每个组件的位置由
    /// <see cref="TaskbarRestLayoutPolicy.Arrange"/> 按用户顺序排出来。两者用同一套间距规则
    /// （可见组件数 − 1 个 <paramref name="sectionGap"/>），因此长度与位置永远一致，改顺序不会改变需要的长度。
    /// Calculates the length a horizontal taskbar media bar needs for the set of components visible this time, in DIP.
    ///
    /// It is deliberately independent of the component order: the bar length is only a sum, while every component's position is arranged
    /// in the user's order by <see cref="TaskbarRestLayoutPolicy.Arrange"/>. Both use the same spacing rule — one
    /// <paramref name="sectionGap"/> per adjacent pair — so the length and the positions always agree, and reordering never changes the
    /// length that is needed.
    /// </summary>
    /// <param name="visibleComponents">这次可见的组件（顺序无关）。/ The components visible this time; the order does not matter.</param>
    /// <param name="leadingInset">第一个组件之前的留白（DIP）。/ Padding before the first component, in DIP.</param>
    /// <param name="textWidth">媒体文字组件占用的宽度；文字不可见时不会被用到。/ Width the media text component takes; unused when the text is not visible.</param>
    /// <param name="trailingMargin">尾部留白（DIP）。/ Trailing margin, in DIP.</param>
    /// <param name="sectionGap">组件间距（DIP）。/ Gap between components, in DIP.</param>
    /// <param name="widthOf">取某个组件固定宽度的函数；媒体文字不会被问到。/ Supplies one component's fixed width; the media text is never asked.</param>
    /// <param name="maximumWidth">任务栏给出的安全上限。/ The safe maximum the taskbar allows.</param>
    public static double CalculateRestWidth(
        IReadOnlyList<TaskbarRestComponent> visibleComponents,
        double leadingInset,
        double textWidth,
        double trailingMargin,
        double sectionGap,
        Func<TaskbarRestComponent, double> widthOf,
        double maximumWidth)
    {
        if (visibleComponents.Count == 0)
        {
            return 0;
        }

        var total = Math.Max(0, leadingInset);
        for (var index = 0; index < visibleComponents.Count; index++)
        {
            var component = visibleComponents[index];
            total += component == TaskbarRestComponent.MediaText
                ? Math.Max(0, textWidth)
                : Math.Max(0, widthOf(component));
            if (index < visibleComponents.Count - 1)
            {
                total += Math.Max(0, sectionGap);
            }
        }

        return ClampWidth(total + Math.Max(0, trailingMargin), maximumWidth);
    }

    private static double ClampWidth(double desired, double maximumWidth) =>
        double.IsFinite(maximumWidth) ? Math.Min(desired, Math.Max(0, maximumWidth)) : desired;

    /// <summary>
    /// 按内容跟随或固定模式解析最终长度，并把固定值限制在当前悬停层下限与可用区间上限之间。
    /// Resolves the final length in content-following or fixed mode and clamps a fixed value
    /// between the current hover-layer minimum and the available-range maximum.
    /// </summary>
    public static double ResolvePrimaryLength(
        double contentLength,
        double minimumLength,
        double maximumLength,
        TaskbarLengthMode mode,
        double fixedLength)
    {
        var minimum = double.IsFinite(minimumLength) ? Math.Max(0, minimumLength) : 0;
        var maximum = double.IsFinite(maximumLength)
            ? Math.Max(minimum, maximumLength)
            : double.PositiveInfinity;
        var content = double.IsFinite(contentLength) ? Math.Max(0, contentLength) : minimum;
        var requested = mode == TaskbarLengthMode.Fixed && double.IsFinite(fixedLength)
            ? fixedLength
            : content;
        return Math.Clamp(requested, minimum, maximum);
    }

    /// <summary>
    /// 返回文字超出容器的滚动距离。判据只有"量出来的文字比可用宽度长"，与长度模式无关：
    /// 跟随内容模式下媒体栏被任务栏安全上限夹住时文字同样会超出，那时也必须能滚动看全。
    /// Returns the distance the text overflows its container. The only condition is that the measured text is wider than the
    /// available width, independent of the length mode: in follow-content mode the bar is clamped by the taskbar's safe maximum
    /// and the text overflows there too, and it has to stay scrollable.
    /// </summary>
    /// <param name="measuredTextLength">量出的文字宽度。/ Measured text width.</param>
    /// <param name="availableTextLength">容器可用的文字宽度。/ Text width available inside the container.</param>
    public static double CalculateMarqueeOverflow(double measuredTextLength, double availableTextLength)
    {
        if (!double.IsFinite(measuredTextLength) || !double.IsFinite(availableTextLength))
            return 0;

        return Math.Max(0, measuredTextLength - Math.Max(0, availableTextLength));
    }

    /// <summary>计算中间文字区域容纳悬停控件所需的最小宽度。 / Calculates the middle text region's minimum width for hover controls.</summary>
    public static double CalculateHoverLayerWidth(
        bool transportVisible,
        bool progressVisible,
        TaskbarInformationDensity density,
        double? componentSpacingDip = null)
    {
        var metrics = TaskbarDensityMetrics.From(density);
        var sectionGap = ResolveSectionGap(metrics, componentSpacingDip);
        var buttonCount = transportVisible ? 5 : 2;
        var buttons = buttonCount * metrics.ButtonSize + Math.Max(0, buttonCount - 1) * sectionGap;
        var progress = progressVisible ? sectionGap + metrics.ProgressWidth : 0;
        return 11 + buttons + progress;
    }

    /// <summary>按独立控制显隐计算悬停层最小宽度。 / Calculates hover width from independently visible controls.</summary>
    public static double CalculateHoverLayerWidth(
        TaskbarHoverControlsSettings controls,
        bool progressAvailable,
        TaskbarInformationDensity density,
        double? componentSpacingDip = null)
    {
        var metrics = TaskbarDensityMetrics.From(density);
        var sectionGap = ResolveSectionGap(metrics, componentSpacingDip);
        var buttonCount = (controls.PlayPauseVisible ? 1 : 0) +
                          (controls.PreviousNextVisible ? 2 : 0) +
                          (controls.OutputDeviceVisible ? 1 : 0) +
                          (controls.AudioControlVisible ? 1 : 0);
        var buttons = buttonCount == 0 ? 0 : buttonCount * metrics.ButtonSize + (buttonCount - 1) * sectionGap;
        var progress = controls.ProgressVisible && progressAvailable ? (buttonCount > 0 ? sectionGap : 0) + metrics.ProgressWidth : 0;
        return 11 + buttons + progress;
    }

    /// <summary>
    /// 解析实际使用的组件间距：设置里的值优先，非法时退回信息密度的预设间距。
    /// Resolves the component spacing actually used: the settings value wins, and an unusable one falls back to the density preset.
    /// </summary>
    /// <param name="density">信息密度。/ Information density.</param>
    /// <param name="componentSpacingDip">设置里的间距；为空或非法时用密度预设。/ Spacing from the settings, or the density preset when it is absent or unusable.</param>
    public static double ResolveSectionGap(TaskbarInformationDensity density, double? componentSpacingDip) =>
        ResolveSectionGap(TaskbarDensityMetrics.From(density), componentSpacingDip);

    private static double ResolveSectionGap(TaskbarDensityMetrics metrics, double? componentSpacingDip) =>
        componentSpacingDip is { } value && double.IsFinite(value)
            ? Math.Clamp(value, TaskbarExperienceSettings.MinimumComponentSpacingDip, TaskbarExperienceSettings.MaximumComponentSpacingDip)
            : metrics.SectionGap;

    public static double GetPosition(MediaSnapshot snapshot, DateTimeOffset now)
    {
        if (snapshot.Duration <= 0)
            return 0;

        var position = snapshot.Position;
        if (snapshot.IsPlaying && snapshot.TimelineUpdatedAt != DateTimeOffset.MinValue)
        {
            var elapsed = Math.Max(0, (now - snapshot.TimelineUpdatedAt).TotalSeconds);
            position += elapsed * (snapshot.PlaybackRate <= 0 ? 1 : snapshot.PlaybackRate);
        }

        return Math.Clamp(position, 0, snapshot.Duration);
    }
}
