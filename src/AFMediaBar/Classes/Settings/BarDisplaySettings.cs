// 保存宿主专属几何；呈现与交互始终读取全局配置。
using AFMediaBar.Classes.Models.Layout;

namespace AFMediaBar.Classes.Settings;

/// <summary>任务栏与桌面悬浮模式分别保存的几何偏好，不复制字体、外观或内容。</summary>
public sealed record BarDisplaySettings
{
    private static readonly AppSettings _defaults = new();
    public TaskbarLengthMode LengthMode { get; init; } = _defaults.TaskbarExperience.LengthMode;
    public double FixedLengthDip { get; init; } = _defaults.TaskbarExperience.FixedLengthDip;
    public TaskbarContentArrangement? Arrangement { get; init; }
    public bool LyricsFixedWidthEnabled { get; init; } = _defaults.LyricsFixedWidthEnabled;
    public int LyricsFixedWidthDip { get; init; } = _defaults.LyricsFixedWidthDip;
    public TaskbarBarPosition Position { get; init; } = _defaults.Position;
    public int TaskbarBarManualPadding { get; init; } = _defaults.TaskbarBarManualPadding;
    public double TaskbarBarCrossAxisOffsetDip { get; init; } = _defaults.TaskbarBarCrossAxisOffsetDip;
    public bool TaskbarBarPositionLocked { get; init; } = _defaults.TaskbarBarPositionLocked;
    public double LayoutLengthScalePercent { get; init; } = _defaults.LayoutLengthScalePercent;
    public double LayoutThicknessScalePercent { get; init; } = _defaults.LayoutThicknessScalePercent;

    public static BarDisplaySettings Capture(AppSettings settings) => new()
    {
        LengthMode = settings.TaskbarExperience.LengthMode,
        FixedLengthDip = settings.TaskbarExperience.FixedLengthDip,
        Arrangement = settings.TaskbarExperience.Arrangement,
        LyricsFixedWidthEnabled = settings.LyricsFixedWidthEnabled,
        LyricsFixedWidthDip = settings.LyricsFixedWidthDip,
        Position = settings.Position,
        TaskbarBarManualPadding = settings.TaskbarBarManualPadding,
        TaskbarBarCrossAxisOffsetDip = settings.TaskbarBarCrossAxisOffsetDip,
        TaskbarBarPositionLocked = settings.TaskbarBarPositionLocked,
        LayoutLengthScalePercent = settings.LayoutLengthScalePercent,
        LayoutThicknessScalePercent = settings.LayoutThicknessScalePercent,
    };

    public BarDisplaySettings Normalize() => Capture(ApplyTo(new AppSettings()).Normalize());

    public AppSettings ApplyTo(AppSettings global)
    {
        var result = global.Clone();
        result.TaskbarExperience = global.TaskbarExperience with { LengthMode = LengthMode, FixedLengthDip = FixedLengthDip, Arrangement = Arrangement };
        result.LyricsFixedWidthEnabled = LyricsFixedWidthEnabled;
        result.LyricsFixedWidthDip = LyricsFixedWidthDip;
        result.Position = Position;
        result.TaskbarBarManualPadding = TaskbarBarManualPadding;
        result.TaskbarBarCrossAxisOffsetDip = TaskbarBarCrossAxisOffsetDip;
        result.TaskbarBarPositionLocked = TaskbarBarPositionLocked;
        result.LayoutLengthScalePercent = LayoutLengthScalePercent;
        result.LayoutThicknessScalePercent = LayoutThicknessScalePercent;
        return result;
    }
}
