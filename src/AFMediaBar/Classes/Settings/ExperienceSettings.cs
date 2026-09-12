namespace AFMediaBar.Classes.Settings;

/// <summary>播放器表面的全局操作方式。 / Global interaction mode for player surfaces.</summary>
public enum MediaInteractionMode
{
    Buttons = 0,
    Hybrid = 1,
    Gestures = 2
}

/// <summary>鼠标滚轮执行的媒体动作。 / Media action performed by the mouse wheel.</summary>
public enum WheelAction
{
    PreviousNext = 0,
    CurrentApplicationVolume = 1,
    OutputDevice = 2
}

/// <summary>组合滚轮使用的鼠标按键。 / Mouse button used by a chorded wheel gesture.</summary>
public enum MouseChordButton
{
    Left = 0,
    Right = 1
}

/// <summary>单击通知区域图标时执行的动作。 / Action performed when the notification-area icon is clicked.</summary>
public enum TrayClickAction
{
    None = 0,
    OpenSettings = 1,
    OpenAudioControl = 2
}

/// <summary>任务栏固定布局的信息密度。 / Information density for the fixed taskbar layout.</summary>
public enum TaskbarInformationDensity
{
    Minimal = 0,
    Balanced = 1,
    Information = 2
}

/// <summary>任务栏媒体文字的固定布局。 / Fixed layout used by taskbar media text.</summary>
public enum TaskbarContentLayout
{
    CompactInline = 0,
    AdaptiveStack = 1,
    CenteredStack = 2
}

/// <summary>播放器表面的基础背景方案。 / Basic background style for a player surface.</summary>
public enum PlayerSurfaceStyle
{
    Automatic = 0,
    Solid = 1,
    ThemeTint = 2
}

/// <summary>歌词文字对齐方式。 / Lyric text alignment.</summary>
public enum LyricsTextAlignment
{
    Left = 0,
    Center = 1,
    Right = 2
}

/// <summary>任务栏三层体验设置。 / Settings for the three-layer taskbar experience.</summary>
public readonly record struct TaskbarExperienceSettings(
    bool HoverLayerEnabled,
    bool FullLayerEnabled,
    TaskbarInformationDensity Density,
    TaskbarContentLayout ContentLayout)
{
    public static TaskbarExperienceSettings Default { get; } = new(
        true,
        true,
        TaskbarInformationDensity.Balanced,
        TaskbarContentLayout.AdaptiveStack);

    public TaskbarExperienceSettings Normalize()
    {
        var defaults = Default;
        return this with
        {
            Density = Enum.IsDefined(Density) ? Density : defaults.Density,
            ContentLayout = Enum.IsDefined(ContentLayout) ? ContentLayout : defaults.ContentLayout
        };
    }
}

/// <summary>四种显示模式共用的交互设置。 / Interaction settings shared by all display modes.</summary>
public readonly record struct GlobalInteractionSettings(
    MediaInteractionMode Mode,
    WheelAction PrimaryWheelAction,
    bool ChordWheelEnabled,
    MouseChordButton ChordButton,
    WheelAction ChordWheelAction,
    TrayClickAction TrayClickAction,
    bool TrayUsesGlobalWheel)
{
    public static GlobalInteractionSettings Default { get; } = new(
        MediaInteractionMode.Hybrid,
        WheelAction.PreviousNext,
        false,
        MouseChordButton.Left,
        WheelAction.CurrentApplicationVolume,
        TrayClickAction.OpenAudioControl,
        true);

    public GlobalInteractionSettings Normalize()
    {
        var defaults = Default;
        return this with
        {
            Mode = Enum.IsDefined(Mode) ? Mode : defaults.Mode,
            PrimaryWheelAction = Enum.IsDefined(PrimaryWheelAction) ? PrimaryWheelAction : defaults.PrimaryWheelAction,
            ChordButton = Enum.IsDefined(ChordButton) ? ChordButton : defaults.ChordButton,
            ChordWheelAction = Enum.IsDefined(ChordWheelAction) ? ChordWheelAction : defaults.ChordWheelAction,
            TrayClickAction = Enum.IsDefined(TrayClickAction) ? TrayClickAction : defaults.TrayClickAction
        };
    }
}

/// <summary>一个显示模式的基础表面外观。 / Basic surface appearance for one display mode.</summary>
public readonly record struct ModeSurfaceSettings(
    PlayerSurfaceStyle Style,
    int BackgroundOpacityPercent,
    double CornerRadiusDip)
{
    public static ModeSurfaceSettings Default { get; } = new(PlayerSurfaceStyle.Automatic, 100, 6);

    public ModeSurfaceSettings Normalize()
    {
        var defaults = Default;
        return this with
        {
            Style = Enum.IsDefined(Style) ? Style : defaults.Style,
            BackgroundOpacityPercent = Math.Clamp(BackgroundOpacityPercent, 0, 100),
            CornerRadiusDip = double.IsFinite(CornerRadiusDip)
                ? Math.Clamp(CornerRadiusDip, 0, 24)
                : defaults.CornerRadiusDip
        };
    }
}
