using AFMediaBar.Classes.Models.Layout;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AFMediaBar.Classes.Settings;

/// <summary>任务栏位置。 / Taskbar placement.</summary>
public enum TaskbarBarPosition { Start = 0, Center = 1, End = 2 }
/// <summary>布局方向模式。 / Layout orientation mode.</summary>
public enum LayoutOrientationMode { Auto = 0, Horizontal = 1, Vertical = 2 }
/// <summary>灵动岛背景模式。 / Dynamic-island background mode.</summary>
public enum DynamicIslandBackgroundMode { SystemTheme = 0, Transparent = 1 }
/// <summary>托盘滚轮行为。 / Tray-wheel behavior.</summary>
public enum TrayWheelBehavior { AdjustVolume = 0, SwitchOutputDevice = 1, Disabled = 2 }
/// <summary>双行歌词第二行模式。 / Secondary lyric-line mode.</summary>
public enum LyricsSecondaryLineMode { NextLine = 0, Translation = 1 }

/// <summary>应用全部用户设置，并在属性直接修改时发布变更。 / All user settings; direct mutations publish changes.</summary>
public sealed class AppSettings : INotifyPropertyChanged
{
    private AppearanceSettings _appearance = AppearanceSettings.Default;
    private TrayWheelBehavior _trayWheelBehavior = TrayWheelBehavior.SwitchOutputDevice;
    private bool _lyricsEnabled = true;
    private bool _twoLineLyricsEnabled;
    private LyricsSecondaryLineMode _lyricsSecondaryLineMode = LyricsSecondaryLineMode.NextLine;
    private bool _taskbarBarEnabled = true;
    private int _taskbarBarSelectedMonitor;
    private TaskbarBarPosition _position = TaskbarBarPosition.Start;
    private bool _taskbarBarBackgroundBlur;
    private int _taskbarBarManualPadding;
    private WindowMode _windowMode = WindowMode.Taskbar;
    private LayoutOrientationMode _layoutOrientationMode = LayoutOrientationMode.Auto;
    private double _layoutLengthScalePercent = 100;
    private double _layoutThicknessScalePercent = 100;
    private DynamicIslandBackgroundMode _dynamicIslandBackgroundMode = DynamicIslandBackgroundMode.SystemTheme;
    private double _taskbarBarCrossAxisOffsetDip;
    private bool _taskbarBarAvoidIcons = true;
    private bool _taskbarBarPositionLocked;
    private double? _dynamicIslandLeft;
    private double? _dynamicIslandTop;
    private DynamicIslandEdge _dynamicIslandEdge = DynamicIslandEdge.Top;
    private bool _dynamicIslandEdgeDocked = true;
    private TaskbarExperienceSettings _taskbarExperience = TaskbarExperienceSettings.Default;
    private GlobalInteractionSettings _interaction = GlobalInteractionSettings.Default;
    private ModeSurfaceSettings _taskbarSurface = ModeSurfaceSettings.Default;
    private ModeSurfaceSettings _dynamicIslandSurface = ModeSurfaceSettings.Default;
    private LyricsTextAlignment _lyricsTextAlignment = LyricsTextAlignment.Center;

    public AppearanceSettings Appearance { get => _appearance; set => Set(ref _appearance, value.Normalize()); }
    public TrayWheelBehavior TrayWheelBehavior { get => _trayWheelBehavior; set => Set(ref _trayWheelBehavior, value); }
    public bool LyricsEnabled { get => _lyricsEnabled; set => Set(ref _lyricsEnabled, value); }
    public bool TwoLineLyricsEnabled { get => _twoLineLyricsEnabled; set => Set(ref _twoLineLyricsEnabled, value); }
    public LyricsSecondaryLineMode LyricsSecondaryLineMode { get => _lyricsSecondaryLineMode; set => Set(ref _lyricsSecondaryLineMode, value); }
    public bool TaskbarBarEnabled { get => _taskbarBarEnabled; set => Set(ref _taskbarBarEnabled, value); }
    public int TaskbarBarSelectedMonitor { get => _taskbarBarSelectedMonitor; set => Set(ref _taskbarBarSelectedMonitor, value); }
    public TaskbarBarPosition Position { get => _position; set => Set(ref _position, value); }
    public bool TaskbarBarBackgroundBlur { get => _taskbarBarBackgroundBlur; set => Set(ref _taskbarBarBackgroundBlur, value); }
    public int TaskbarBarManualPadding { get => _taskbarBarManualPadding; set => Set(ref _taskbarBarManualPadding, value); }
    public WindowMode WindowMode { get => _windowMode; set => Set(ref _windowMode, value); }
    public LayoutOrientationMode LayoutOrientationMode { get => _layoutOrientationMode; set => Set(ref _layoutOrientationMode, value); }
    public double LayoutLengthScalePercent { get => _layoutLengthScalePercent; set => Set(ref _layoutLengthScalePercent, value); }
    public double LayoutThicknessScalePercent { get => _layoutThicknessScalePercent; set => Set(ref _layoutThicknessScalePercent, value); }
    public DynamicIslandBackgroundMode DynamicIslandBackgroundMode { get => _dynamicIslandBackgroundMode; set => Set(ref _dynamicIslandBackgroundMode, value); }
    public double TaskbarBarCrossAxisOffsetDip { get => _taskbarBarCrossAxisOffsetDip; set => Set(ref _taskbarBarCrossAxisOffsetDip, value); }
    public bool TaskbarBarAvoidIcons { get => _taskbarBarAvoidIcons; set => Set(ref _taskbarBarAvoidIcons, value); }
    public bool TaskbarBarPositionLocked { get => _taskbarBarPositionLocked; set => Set(ref _taskbarBarPositionLocked, value); }
    public double? DynamicIslandLeft { get => _dynamicIslandLeft; set => Set(ref _dynamicIslandLeft, value); }
    public double? DynamicIslandTop { get => _dynamicIslandTop; set => Set(ref _dynamicIslandTop, value); }
    public DynamicIslandEdge DynamicIslandEdge { get => _dynamicIslandEdge; set => Set(ref _dynamicIslandEdge, value); }
    public bool DynamicIslandEdgeDocked { get => _dynamicIslandEdgeDocked; set => Set(ref _dynamicIslandEdgeDocked, value); }
    public TaskbarExperienceSettings TaskbarExperience { get => _taskbarExperience; set => Set(ref _taskbarExperience, value.Normalize()); }
    public GlobalInteractionSettings Interaction { get => _interaction; set => Set(ref _interaction, value.Normalize()); }
    public ModeSurfaceSettings TaskbarSurface { get => _taskbarSurface; set => Set(ref _taskbarSurface, value.Normalize()); }
    public ModeSurfaceSettings DynamicIslandSurface { get => _dynamicIslandSurface; set => Set(ref _dynamicIslandSurface, value.Normalize()); }
    public LyricsTextAlignment LyricsTextAlignment { get => _lyricsTextAlignment; set => Set(ref _lyricsTextAlignment, value); }

    public event PropertyChangedEventHandler? PropertyChanged;

    public AppSettings Normalize()
    {
        var defaults = new AppSettings();
        var result = Clone();
        result.Appearance = result.Appearance.Normalize();
        if (!Enum.IsDefined(result.TrayWheelBehavior)) result.TrayWheelBehavior = defaults.TrayWheelBehavior;
        if (!Enum.IsDefined(result.LyricsSecondaryLineMode)) result.LyricsSecondaryLineMode = defaults.LyricsSecondaryLineMode;
        if (!Enum.IsDefined(result.Position)) result.Position = defaults.Position;
        if (!Enum.IsDefined(result.WindowMode)) result.WindowMode = defaults.WindowMode;
        if (!Enum.IsDefined(result.LayoutOrientationMode)) result.LayoutOrientationMode = defaults.LayoutOrientationMode;
        if (!Enum.IsDefined(result.DynamicIslandBackgroundMode)) result.DynamicIslandBackgroundMode = defaults.DynamicIslandBackgroundMode;
        if (!Enum.IsDefined(result.DynamicIslandEdge)) result.DynamicIslandEdge = defaults.DynamicIslandEdge;
        if (!Enum.IsDefined(result.LyricsTextAlignment)) result.LyricsTextAlignment = defaults.LyricsTextAlignment;
        result.TaskbarExperience = result.TaskbarExperience.Normalize();
        result.Interaction = result.Interaction.Normalize();
        result.TaskbarSurface = result.TaskbarSurface.Normalize();
        result.DynamicIslandSurface = result.DynamicIslandSurface.Normalize();
        if (result.TaskbarBarSelectedMonitor < 0) result.TaskbarBarSelectedMonitor = defaults.TaskbarBarSelectedMonitor;
        if (!double.IsFinite(result.LayoutLengthScalePercent)) result.LayoutLengthScalePercent = defaults.LayoutLengthScalePercent;
        if (!double.IsFinite(result.LayoutThicknessScalePercent)) result.LayoutThicknessScalePercent = defaults.LayoutThicknessScalePercent;
        if (!double.IsFinite(result.TaskbarBarCrossAxisOffsetDip)) result.TaskbarBarCrossAxisOffsetDip = defaults.TaskbarBarCrossAxisOffsetDip;
        result.LayoutLengthScalePercent = Math.Clamp(result.LayoutLengthScalePercent, 70, 125);
        result.LayoutThicknessScalePercent = Math.Clamp(result.LayoutThicknessScalePercent, 70, 125);
        result.TaskbarBarCrossAxisOffsetDip = Math.Clamp(result.TaskbarBarCrossAxisOffsetDip, -20, 20);
        if (result.DynamicIslandLeft is not null && (!double.IsFinite(result.DynamicIslandLeft.Value) || result.DynamicIslandLeft < 0)) result.DynamicIslandLeft = null;
        if (result.DynamicIslandTop is not null && (!double.IsFinite(result.DynamicIslandTop.Value) || result.DynamicIslandTop < 0)) result.DynamicIslandTop = null;
        return result;
    }

    public AppSettings Clone() => new()
    {
        Appearance = Appearance,
        TrayWheelBehavior = TrayWheelBehavior,
        LyricsEnabled = LyricsEnabled,
        TwoLineLyricsEnabled = TwoLineLyricsEnabled,
        LyricsSecondaryLineMode = LyricsSecondaryLineMode,
        TaskbarBarEnabled = TaskbarBarEnabled,
        TaskbarBarSelectedMonitor = TaskbarBarSelectedMonitor,
        Position = Position,
        TaskbarBarBackgroundBlur = TaskbarBarBackgroundBlur,
        TaskbarBarManualPadding = TaskbarBarManualPadding,
        WindowMode = WindowMode,
        LayoutOrientationMode = LayoutOrientationMode,
        LayoutLengthScalePercent = LayoutLengthScalePercent,
        LayoutThicknessScalePercent = LayoutThicknessScalePercent,
        DynamicIslandBackgroundMode = DynamicIslandBackgroundMode,
        TaskbarBarCrossAxisOffsetDip = TaskbarBarCrossAxisOffsetDip,
        TaskbarBarAvoidIcons = TaskbarBarAvoidIcons,
        TaskbarBarPositionLocked = TaskbarBarPositionLocked,
        DynamicIslandLeft = DynamicIslandLeft,
        DynamicIslandTop = DynamicIslandTop,
        DynamicIslandEdge = DynamicIslandEdge,
        DynamicIslandEdgeDocked = DynamicIslandEdgeDocked,
        TaskbarExperience = TaskbarExperience,
        Interaction = Interaction,
        TaskbarSurface = TaskbarSurface,
        DynamicIslandSurface = DynamicIslandSurface,
        LyricsTextAlignment = LyricsTextAlignment
    };

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

/// <summary>设置重置范围。 / Settings reset scope.</summary>
public enum SettingsResetScope { General, Appearance, Layout, DisplayModes, Interaction, Lyrics, All }

/// <summary>设置变更通知参数。 / Settings change notification arguments.</summary>
public sealed class SettingsChangedEventArgs(SettingsResetScope? resetScope = null, string? propertyName = null) : EventArgs
{
    public SettingsResetScope? ResetScope { get; } = resetScope;
    public string? PropertyName { get; } = propertyName;
}

/// <summary>兼容现有调用方的全局设置门面。 / Global settings facade retained for compatibility.</summary>
public static class SettingsManager
{
    private static AppSettings _current = new();
    static SettingsManager() => Subscribe(_current);
    public static AppSettings Current { get => _current; set => Replace(value); }
    public static event EventHandler<SettingsChangedEventArgs>? SettingsChanged;
    public static event EventHandler<AppearanceSettingsChangedEventArgs>? AppearanceSettingsChanged;
    public static event EventHandler? TrayWheelBehaviorChanged;
    public static event EventHandler? LyricsSettingsChanged;
    public static event EventHandler<LayoutSettingsChangedEventArgs>? LayoutSettingsChanged;
    public static event EventHandler? TaskbarExperienceSettingsChanged;
    public static event EventHandler? InteractionSettingsChanged;

    public static void Replace(AppSettings settings, SettingsResetScope? scope = null)
    {
        var normalized = settings.Normalize();
        Unsubscribe(_current);
        _current = normalized;
        Subscribe(_current);
        RaiseAll(scope);
    }
    public static void SetTrayWheelBehavior(TrayWheelBehavior behavior) => Current.TrayWheelBehavior = behavior;
    public static void SetLyricsEnabled(bool enabled) => Current.LyricsEnabled = enabled;
    public static void SetTwoLineLyricsEnabled(bool enabled) => Current.TwoLineLyricsEnabled = enabled;
    public static void SetLyricsSecondaryLineMode(LyricsSecondaryLineMode mode) => Current.LyricsSecondaryLineMode = mode;
    public static void SetLyricsTextAlignment(LyricsTextAlignment alignment) => Current.LyricsTextAlignment = alignment;
    public static void SetAppearanceSettings(AppearanceSettings appearance) => Current.Appearance = appearance;
    public static void SetTaskbarExperienceSettings(TaskbarExperienceSettings settings) => Current.TaskbarExperience = settings;
    public static void SetInteractionSettings(GlobalInteractionSettings settings) => Current.Interaction = settings;
    public static void RaiseLayoutSettingsChanged(WindowMode windowMode, LayoutOrientationMode orientationMode) => LayoutSettingsChanged?.Invoke(null, new LayoutSettingsChangedEventArgs(windowMode, orientationMode));

    public static void ResetGeneral()
    {
        // Current general-page controls are operating-system or navigation actions and do not
        // own interaction or lyric settings. Publish the reset boundary without changing them.
        Replace(Current.Clone(), SettingsResetScope.General);
    }
    public static void ResetAppearance()
    {
        var next = Current.Clone();
        next.Appearance = AppearanceSettings.Default;
        next.TaskbarSurface = ModeSurfaceSettings.Default;
        next.DynamicIslandSurface = ModeSurfaceSettings.Default;
        Replace(next, SettingsResetScope.Appearance);
    }
    public static void ResetDisplayModes()
    {
        var next = Current.Clone(); var defaults = new AppSettings();
        next.TaskbarExperience = defaults.TaskbarExperience;
        next.WindowMode = defaults.WindowMode; next.LayoutOrientationMode = defaults.LayoutOrientationMode;
        next.TaskbarBarEnabled = defaults.TaskbarBarEnabled; next.TaskbarBarSelectedMonitor = defaults.TaskbarBarSelectedMonitor;
        next.Position = defaults.Position; next.TaskbarBarCrossAxisOffsetDip = defaults.TaskbarBarCrossAxisOffsetDip;
        next.TaskbarBarAvoidIcons = defaults.TaskbarBarAvoidIcons; next.TaskbarBarPositionLocked = defaults.TaskbarBarPositionLocked;
        Replace(next, SettingsResetScope.DisplayModes);
    }
    public static void ResetInteraction()
    {
        var next = Current.Clone(); next.Interaction = GlobalInteractionSettings.Default;
        Replace(next, SettingsResetScope.Interaction);
    }
    public static void ResetLyrics()
    {
        var next = Current.Clone(); var defaults = new AppSettings();
        next.LyricsEnabled = defaults.LyricsEnabled; next.TwoLineLyricsEnabled = defaults.TwoLineLyricsEnabled;
        next.LyricsSecondaryLineMode = defaults.LyricsSecondaryLineMode; next.LyricsTextAlignment = defaults.LyricsTextAlignment;
        Replace(next, SettingsResetScope.Lyrics);
    }
    public static void ResetLayout()
    {
        var next = Current.Clone(); var defaults = new AppSettings();
        next.TaskbarBarEnabled = defaults.TaskbarBarEnabled; next.TaskbarBarSelectedMonitor = defaults.TaskbarBarSelectedMonitor;
        next.Position = defaults.Position; next.TaskbarBarBackgroundBlur = defaults.TaskbarBarBackgroundBlur; next.TaskbarBarManualPadding = defaults.TaskbarBarManualPadding;
        next.WindowMode = defaults.WindowMode; next.LayoutOrientationMode = defaults.LayoutOrientationMode; next.LayoutLengthScalePercent = defaults.LayoutLengthScalePercent;
        next.LayoutThicknessScalePercent = defaults.LayoutThicknessScalePercent; next.DynamicIslandBackgroundMode = defaults.DynamicIslandBackgroundMode;
        next.TaskbarBarCrossAxisOffsetDip = defaults.TaskbarBarCrossAxisOffsetDip; next.TaskbarBarAvoidIcons = defaults.TaskbarBarAvoidIcons;
        next.TaskbarBarPositionLocked = defaults.TaskbarBarPositionLocked; next.DynamicIslandLeft = defaults.DynamicIslandLeft; next.DynamicIslandTop = defaults.DynamicIslandTop;
        next.DynamicIslandEdge = defaults.DynamicIslandEdge; next.DynamicIslandEdgeDocked = defaults.DynamicIslandEdgeDocked;
        Replace(next, SettingsResetScope.Layout);
    }
    public static void ResetAll() => Replace(new AppSettings(), SettingsResetScope.All);

    private static void Subscribe(AppSettings settings) => settings.PropertyChanged += OnPropertyChanged;
    private static void Unsubscribe(AppSettings settings) => settings.PropertyChanged -= OnPropertyChanged;
    private static void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        SettingsChanged?.Invoke(null, new SettingsChangedEventArgs(propertyName: e.PropertyName));
        switch (e.PropertyName)
        {
            case nameof(AppSettings.Appearance): AppearanceSettingsChanged?.Invoke(null, new AppearanceSettingsChangedEventArgs(Current.Appearance)); break;
            case nameof(AppSettings.TrayWheelBehavior): TrayWheelBehaviorChanged?.Invoke(null, EventArgs.Empty); break;
            case nameof(AppSettings.LyricsEnabled):
            case nameof(AppSettings.TwoLineLyricsEnabled):
            case nameof(AppSettings.LyricsSecondaryLineMode): LyricsSettingsChanged?.Invoke(null, EventArgs.Empty); break;
            case nameof(AppSettings.LyricsTextAlignment): LyricsSettingsChanged?.Invoke(null, EventArgs.Empty); break;
            case nameof(AppSettings.TaskbarExperience): TaskbarExperienceSettingsChanged?.Invoke(null, EventArgs.Empty); break;
            case nameof(AppSettings.Interaction): InteractionSettingsChanged?.Invoke(null, EventArgs.Empty); break;
            case nameof(AppSettings.TaskbarSurface):
            case nameof(AppSettings.DynamicIslandSurface): AppearanceSettingsChanged?.Invoke(null, new AppearanceSettingsChangedEventArgs(Current.Appearance)); break;
        }
    }
    private static void RaiseAll(SettingsResetScope? scope)
    {
        SettingsChanged?.Invoke(null, new SettingsChangedEventArgs(scope));
        AppearanceSettingsChanged?.Invoke(null, new AppearanceSettingsChangedEventArgs(Current.Appearance));
        TrayWheelBehaviorChanged?.Invoke(null, EventArgs.Empty); LyricsSettingsChanged?.Invoke(null, EventArgs.Empty);
        TaskbarExperienceSettingsChanged?.Invoke(null, EventArgs.Empty); InteractionSettingsChanged?.Invoke(null, EventArgs.Empty);
        RaiseLayoutSettingsChanged(Current.WindowMode, Current.LayoutOrientationMode);
    }
}

/// <summary>外观设置变更参数。 / Appearance settings change arguments.</summary>
public sealed class AppearanceSettingsChangedEventArgs(AppearanceSettings appearance) : EventArgs { public AppearanceSettings Appearance { get; } = appearance; }
/// <summary>布局设置变更参数。 / Layout settings change arguments.</summary>
public sealed class LayoutSettingsChangedEventArgs(WindowMode windowMode, LayoutOrientationMode orientationMode) : EventArgs
{
    public WindowMode WindowMode { get; } = windowMode;
    public LayoutOrientationMode OrientationMode { get; } = orientationMode;
}
