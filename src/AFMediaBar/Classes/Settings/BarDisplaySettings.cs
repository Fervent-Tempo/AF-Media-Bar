// 隔离媒体栏呈现偏好；投影保留全局字体、来源、取词和音频策略。
using AFMediaBar.Classes.Models.Layout;

namespace AFMediaBar.Classes.Settings;

/// <summary>任务栏和悬浮宿主共用的配置形状，不持有全局资源。</summary>
public sealed record BarDisplaySettings
{
    private static readonly AppSettings _defaults = new();
    public TaskbarExperienceSettings TaskbarExperience { get; init; } = _defaults.TaskbarExperience;
    public AppearanceSettings Appearance { get; init; } = _defaults.Appearance;
    public GlobalInteractionSettings Interaction { get; init; } = _defaults.Interaction;
    public ModeSurfaceSettings TaskbarSurface { get; init; } = _defaults.TaskbarSurface;
    public SpectrumComponentSettings SpectrumComponent { get; init; } = _defaults.SpectrumComponent;
    public PerformanceComponentSettings PerformanceComponent { get; init; } = _defaults.PerformanceComponent;
    public QuickLaunchSettings QuickLaunch { get; init; } = _defaults.QuickLaunch;
    public bool LyricsEnabled { get; init; } = _defaults.LyricsEnabled;
    public bool TwoLineLyricsEnabled { get; init; } = _defaults.TwoLineLyricsEnabled;
    public LyricsSecondaryLineSettings LyricsSecondaryLine { get; init; } = _defaults.LyricsSecondaryLine;
    public LyricsTextAlignment LyricsTextAlignment { get; init; } = _defaults.LyricsTextAlignment;
    public bool LyricsSyllableHighlightEnabled { get; init; } = _defaults.LyricsSyllableHighlightEnabled;
    public int LyricsUnsungOpacityPercent { get; init; } = _defaults.LyricsUnsungOpacityPercent;
    public int LyricsCharacterSpacingPercent { get; init; } = _defaults.LyricsCharacterSpacingPercent;
    public int LyricsLineGapPercent { get; init; } = _defaults.LyricsLineGapPercent;
    public bool LyricsFixedWidthEnabled { get; init; } = _defaults.LyricsFixedWidthEnabled;
    public int LyricsFixedWidthDip { get; init; } = _defaults.LyricsFixedWidthDip;
    public LyricsChineseConversionMode LyricsChineseConversion { get; init; } = _defaults.LyricsChineseConversion;
    public TaskbarBarPosition Position { get; init; } = _defaults.Position;
    public int TaskbarBarManualPadding { get; init; } = _defaults.TaskbarBarManualPadding;
    public double TaskbarBarCrossAxisOffsetDip { get; init; } = _defaults.TaskbarBarCrossAxisOffsetDip;
    public bool TaskbarBarPositionLocked { get; init; } = _defaults.TaskbarBarPositionLocked;
    public double LayoutLengthScalePercent { get; init; } = _defaults.LayoutLengthScalePercent;
    public double LayoutThicknessScalePercent { get; init; } = _defaults.LayoutThicknessScalePercent;

    public static BarDisplaySettings Capture(AppSettings settings) => new()
    {
        TaskbarExperience = settings.TaskbarExperience,
        Appearance = settings.Appearance,
        Interaction = settings.Interaction,
        TaskbarSurface = settings.TaskbarSurface,
        SpectrumComponent = settings.SpectrumComponent,
        PerformanceComponent = settings.PerformanceComponent,
        QuickLaunch = settings.QuickLaunch,
        LyricsEnabled = settings.LyricsEnabled,
        TwoLineLyricsEnabled = settings.TwoLineLyricsEnabled,
        LyricsSecondaryLine = settings.LyricsSecondaryLine,
        LyricsTextAlignment = settings.LyricsTextAlignment,
        LyricsSyllableHighlightEnabled = settings.LyricsSyllableHighlightEnabled,
        LyricsUnsungOpacityPercent = settings.LyricsUnsungOpacityPercent,
        LyricsCharacterSpacingPercent = settings.LyricsCharacterSpacingPercent,
        LyricsLineGapPercent = settings.LyricsLineGapPercent,
        LyricsFixedWidthEnabled = settings.LyricsFixedWidthEnabled,
        LyricsFixedWidthDip = settings.LyricsFixedWidthDip,
        LyricsChineseConversion = settings.LyricsChineseConversion,
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
        result.TaskbarExperience = TaskbarExperience;
        result.TaskbarSurface = TaskbarSurface;
        result.SpectrumComponent = SpectrumComponent;
        result.PerformanceComponent = PerformanceComponent;
        result.QuickLaunch = QuickLaunch;
        result.LyricsEnabled = LyricsEnabled;
        result.TwoLineLyricsEnabled = TwoLineLyricsEnabled;
        result.LyricsSecondaryLine = LyricsSecondaryLine;
        result.LyricsTextAlignment = LyricsTextAlignment;
        result.LyricsSyllableHighlightEnabled = LyricsSyllableHighlightEnabled;
        result.LyricsUnsungOpacityPercent = LyricsUnsungOpacityPercent;
        result.LyricsCharacterSpacingPercent = LyricsCharacterSpacingPercent;
        result.LyricsLineGapPercent = LyricsLineGapPercent;
        result.LyricsFixedWidthEnabled = LyricsFixedWidthEnabled;
        result.LyricsFixedWidthDip = LyricsFixedWidthDip;
        result.LyricsChineseConversion = LyricsChineseConversion;
        result.Position = Position;
        result.TaskbarBarManualPadding = TaskbarBarManualPadding;
        result.TaskbarBarCrossAxisOffsetDip = TaskbarBarCrossAxisOffsetDip;
        result.TaskbarBarPositionLocked = TaskbarBarPositionLocked;
        result.LayoutLengthScalePercent = LayoutLengthScalePercent;
        result.LayoutThicknessScalePercent = LayoutThicknessScalePercent;
        result.Appearance = MergeAppearance(global.Appearance, Appearance);
        result.Interaction = MergeInteraction(global.Interaction, Interaction);
        return result;
    }

    internal static AppearanceSettings MergeAppearance(AppearanceSettings global, AppearanceSettings bar) => global with
    {
        PlayerForegroundMode = bar.PlayerForegroundMode,
        TaskbarBackgroundMaterial = bar.TaskbarBackgroundMaterial,
        TaskbarBackgroundOpacityPercent = bar.TaskbarBackgroundOpacityPercent,
        ShowTaskbarBackgroundWhenIdle = bar.ShowTaskbarBackgroundWhenIdle,
        ShowTaskbarBackgroundTint = bar.ShowTaskbarBackgroundTint,
        ShowTaskbarBackgroundBorder = bar.ShowTaskbarBackgroundBorder,
        ShowTaskbarBackgroundTexture = bar.ShowTaskbarBackgroundTexture,
        TaskbarFrostedStyle = bar.TaskbarFrostedStyle,
        IdleNoteColor = bar.IdleNoteColor,
    };
    internal static GlobalInteractionSettings MergeInteraction(GlobalInteractionSettings global, GlobalInteractionSettings bar) => global with
    {
        ArtworkClickAction = bar.ArtworkClickAction,
        TextClickAction = bar.TextClickAction,
        ArtworkHoverMode = bar.ArtworkHoverMode,
        PrimaryWheelAction = bar.PrimaryWheelAction,
        ChordWheelAction = bar.ChordWheelAction,
        Modifier = bar.Modifier,
        ShowWheelTooltips = bar.ShowWheelTooltips,
    };
}

