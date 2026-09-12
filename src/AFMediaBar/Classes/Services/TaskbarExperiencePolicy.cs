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

/// <summary>任务栏内容宽度和播放进度的纯策略。 / Pure taskbar content-width and playback-progress policies.</summary>
public static class TaskbarExperiencePolicy
{
    public static double CalculateWidth(
        double measuredTextWidth,
        double artworkRight,
        double spectrumWidth,
        double trailingMargin,
        bool transportVisible,
        bool hoverLayerEnabled,
        bool progressVisible,
        TaskbarInformationDensity density,
        double maximumWidth)
    {
        var metrics = TaskbarDensityMetrics.From(density);
        var hoverWidth = hoverLayerEnabled
            ? CalculateHoverLayerWidth(transportVisible, progressVisible, density)
            : 0;
        var middleWidth = Math.Max(Math.Max(0, measuredTextWidth), hoverWidth);
        var desired = Math.Max(0, artworkRight) +
                      metrics.SectionGap +
                      middleWidth +
                      metrics.SectionGap +
                      Math.Max(0, spectrumWidth) +
                      Math.Max(0, trailingMargin);
        return double.IsFinite(maximumWidth) ? Math.Min(desired, Math.Max(0, maximumWidth)) : desired;
    }

    /// <summary>计算中间文字区域容纳悬停控件所需的最小宽度。 / Calculates the middle text region's minimum width for hover controls.</summary>
    public static double CalculateHoverLayerWidth(
        bool transportVisible,
        bool progressVisible,
        TaskbarInformationDensity density)
    {
        var metrics = TaskbarDensityMetrics.From(density);
        var buttonCount = transportVisible ? 5 : 2;
        var buttons = buttonCount * (metrics.ButtonSize + 2);
        var deviceGap = 4;
        var progress = progressVisible ? 8 + metrics.ProgressWidth : 0;
        return 11 + buttons + deviceGap + progress;
    }

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
