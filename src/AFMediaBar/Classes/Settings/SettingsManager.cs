// 维护当前设置、变更通知与重置门面；持久化由设置服务负责。
using AFMediaBar.Classes.Models.Layout;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Services.Localization;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AFMediaBar.Classes.Settings;


/// <summary>兼容现有调用方的全局设置门面。 / Global settings facade retained for compatibility.</summary>
public static class SettingsManager
{
    private static AppSettings _current = new();
    private static AppSettings? _userDefaults;
    private static bool _publishingTaskbarAppearanceOnly;

    /// <summary>
    /// 用户自己保存的默认设置。存在时所有"恢复默认设置"入口都以它为准，而不是程序内置默认值；
    /// 组合根在启动阶段读入快照后调用 <see cref="SetUserDefaults"/>，因此本类本身不做任何文件 I/O。
    /// The defaults the user saved themselves. When present, every "restore defaults" entry uses it instead of the built-in
    /// defaults; the composition root loads the snapshot at startup and calls <see cref="SetUserDefaults"/>, so this class itself
    /// performs no file I/O.
    /// </summary>
    public static AppSettings? UserDefaults => _userDefaults;

    /// <summary>当前生效的默认设置：用户快照优先，其次是内置默认。/ Effective defaults: the user snapshot first, the built-in defaults otherwise.</summary>
    private static AppSettings Defaults => _userDefaults ?? new AppSettings();

    /// <summary>设置或清除用户默认设置快照。/ Sets or clears the user-defaults snapshot.</summary>
    /// <param name="defaults">用户保存的默认设置；传 null 表示回到内置默认。/ Saved defaults, or null to fall back to the built-in ones.</param>
    public static void SetUserDefaults(AppSettings? defaults) => _userDefaults = defaults?.Normalize();

    static SettingsManager() => Subscribe(_current);
    public static AppSettings Current { get => _current; set => Replace(value); }
    public static event EventHandler<SettingsChangedEventArgs>? SettingsChanged;
    public static event EventHandler<AppearanceSettingsChangedEventArgs>? AppearanceSettingsChanged;
    public static event EventHandler<AppearanceSettingsChangedEventArgs>? TaskbarAppearanceSettingsChanged;
    public static event EventHandler? TrayWheelBehaviorChanged;
    public static event EventHandler? LyricsSettingsChanged;
    public static event EventHandler<LayoutSettingsChangedEventArgs>? LayoutSettingsChanged;
    public static event EventHandler? TaskbarExperienceSettingsChanged;
    public static event EventHandler? InteractionSettingsChanged;
    public static event EventHandler? TrackChangeNotificationSettingsChanged;
    public static event EventHandler? ExtraFeaturesSettingsChanged;
    public static event EventHandler? TaskbarTargetMonitorChanged;

    /// <summary>更新设置变化（开关、跳过版本或上次检查结果）。/ Update settings changed: toggles, skipped version, or the last check result.</summary>
    public static event EventHandler? UpdateSettingsChanged;

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
    public static void SetAllowBrowserAndVideoLyrics(bool enabled) => Current.AllowBrowserAndVideoLyrics = enabled;
    public static void SetTwoLineLyricsEnabled(bool enabled) => Current.TwoLineLyricsEnabled = enabled;
    public static void SetLyricsSecondaryLineSettings(LyricsSecondaryLineSettings settings) => Current.LyricsSecondaryLine = settings;
    public static void SetLyricsTextAlignment(LyricsTextAlignment alignment) => Current.LyricsTextAlignment = alignment;
    public static void SetLyricsSyllableHighlightEnabled(bool enabled) => Current.LyricsSyllableHighlightEnabled = enabled;
    public static void SetLyricsUnsungOpacityPercent(int percent) => Current.LyricsUnsungOpacityPercent = LyricsUnsungOpacity.Normalize(percent);
    public static void SetLyricsCharacterSpacingPercent(int percent) => Current.LyricsCharacterSpacingPercent = LyricsCharacterSpacing.Normalize(percent);
    public static void SetLyricsLineGapPercent(int percent) => Current.LyricsLineGapPercent = LyricsLineGap.Normalize(percent);
    public static void SetLyricsFixedWidthEnabled(bool enabled) => Current.LyricsFixedWidthEnabled = enabled;
    public static void SetLyricsFixedWidthDip(int dip) => Current.LyricsFixedWidthDip = LyricsFixedWidth.Normalize(dip);
    public static void SetLyricsInfoLineFilterEnabled(bool enabled) => Current.LyricsInfoLineFilterEnabled = enabled;
    public static void SetLyricsChineseConversion(LyricsChineseConversionMode mode) => Current.LyricsChineseConversion = mode;
    public static void SetLyricsSourceSettings(LyricsSourceSettings settings) => Current.LyricsSource = settings;
    /// <summary>保存自定义艺术家分隔符，触发歌词重新匹配。</summary>
    public static void SetLyricsArtistSeparators(string separators) => Current.LyricsArtistSeparators = separators;
    public static void SetAppearanceSettings(AppearanceSettings appearance)
    {
        var current = Current.Appearance.Normalize();
        var normalized = appearance.Normalize();
        var differsOnlyInTaskbarMaterial = current with
        {
            TaskbarBackgroundMaterial = normalized.TaskbarBackgroundMaterial,
            TaskbarBackgroundOpacityPercent = normalized.TaskbarBackgroundOpacityPercent,
            ShowTaskbarBackgroundWhenIdle = normalized.ShowTaskbarBackgroundWhenIdle,
            ShowTaskbarBackgroundTint = normalized.ShowTaskbarBackgroundTint,
            ShowTaskbarBackgroundBorder = normalized.ShowTaskbarBackgroundBorder,
            ShowTaskbarBackgroundTexture = normalized.ShowTaskbarBackgroundTexture,
            TaskbarFrostedStyle = normalized.TaskbarFrostedStyle
        } == normalized;

        _publishingTaskbarAppearanceOnly = differsOnlyInTaskbarMaterial;
        try
        {
            Current.Appearance = normalized;
        }
        finally
        {
            _publishingTaskbarAppearanceOnly = false;
        }
    }
    public static void SetTaskbarExperienceSettings(TaskbarExperienceSettings settings) => Current.TaskbarExperience = settings;
    public static void SetInteractionSettings(GlobalInteractionSettings settings) => Current.Interaction = settings;
    public static void SetTrackChangeNotificationSettings(TrackChangeNotificationSettings settings) => Current.TrackChangeNotification = settings;
    public static void SetSmtcSourceFilterSettings(SmtcSourceFilterSettings settings) => Current.SmtcSourceFilter = settings;
    public static void SetMediaSelectionSettings(MediaSelectionSettings settings) => Current.MediaSelection = settings;
    public static void SetQuickLaunchSettings(QuickLaunchSettings settings) => Current.QuickLaunch = settings;
    public static void SetSpectrumComponentSettings(SpectrumComponentSettings settings) => Current.SpectrumComponent = settings;
    public static void SetPerformanceComponentSettings(PerformanceComponentSettings settings) => Current.PerformanceComponent = settings;
    public static void SetUpdateSettings(UpdateSettings settings) => Current.Update = settings;
    public static void RaiseLayoutSettingsChanged(WindowMode windowMode, LayoutOrientationMode orientationMode) => LayoutSettingsChanged?.Invoke(null, new LayoutSettingsChangedEventArgs(windowMode, orientationMode));

    public static void ResetGeneral()
    {
        // Current general-page controls are operating-system or navigation actions and do not
        // own interaction or lyric settings. Publish the reset boundary without changing them.
        var next = Current.Clone();
        next.SystemMemoryOptimization = Defaults.SystemMemoryOptimization;
        Replace(next, SettingsResetScope.General);
    }
    public static void ResetAppearance()
    {
        var next = Current.Clone(); var defaults = Defaults;
        next.Appearance = defaults.Appearance;
        next.TaskbarSurface = defaults.TaskbarSurface;
        next.DynamicIslandSurface = defaults.DynamicIslandSurface;
        // 外观页仍写入原有 TaskbarExperience 字段；本页重置只恢复外观字段，不动各层功能开关。
        next.TaskbarExperience = next.TaskbarExperience with
        {
            MediaFontSizePercent = defaults.TaskbarExperience.MediaFontSizePercent,
            Density = defaults.TaskbarExperience.Density,
            ContentLayout = defaults.TaskbarExperience.ContentLayout,
            MediaTextAlignment = defaults.TaskbarExperience.MediaTextAlignment,
            ComponentSpacingDip = defaults.TaskbarExperience.ComponentSpacingDip,
            HoverButtonSpacingDip = defaults.TaskbarExperience.HoverButtonSpacingDip,
            LengthMode = defaults.TaskbarExperience.LengthMode,
            FixedLengthDip = defaults.TaskbarExperience.FixedLengthDip,
            RestComponentOrder = defaults.TaskbarExperience.RestComponentOrder
        };
        Replace(next, SettingsResetScope.Appearance);
    }
    /// <summary>Restores application typography and window theme without replacing media color or taskbar layout.</summary>
    public static void ResetApplicationAppearance()
    {
        var next = Current.Clone();
        next.Appearance = Defaults.Appearance with
        {
            PlayerForegroundMode = next.Appearance.PlayerForegroundMode,
            TaskbarBackgroundMaterial = next.Appearance.TaskbarBackgroundMaterial,
            TaskbarBackgroundOpacityPercent = next.Appearance.TaskbarBackgroundOpacityPercent,
            ShowTaskbarBackgroundWhenIdle = next.Appearance.ShowTaskbarBackgroundWhenIdle,
            ShowTaskbarBackgroundTint = next.Appearance.ShowTaskbarBackgroundTint,
            ShowTaskbarBackgroundBorder = next.Appearance.ShowTaskbarBackgroundBorder,
            ShowTaskbarBackgroundTexture = next.Appearance.ShowTaskbarBackgroundTexture,
            TaskbarFrostedStyle = next.Appearance.TaskbarFrostedStyle,
            IdleNoteColor = next.Appearance.IdleNoteColor
        };
        Replace(next, SettingsResetScope.Appearance);
    }

    /// <summary>Restores taskbar appearance while preserving application fonts and theme.</summary>
    public static void ResetTaskbarAppearance()
    {
        var originalAppearance = Current.Appearance;
        // 布局已移到内容与布局页，本入口只恢复媒体栏视觉字段。
        var next = Current.Clone();
        var defaults = Defaults;
        next.Appearance = originalAppearance with
        {
            PlayerForegroundMode = defaults.Appearance.PlayerForegroundMode,
            TaskbarBackgroundMaterial = defaults.Appearance.TaskbarBackgroundMaterial,
            TaskbarBackgroundOpacityPercent = defaults.Appearance.TaskbarBackgroundOpacityPercent,
            ShowTaskbarBackgroundWhenIdle = defaults.Appearance.ShowTaskbarBackgroundWhenIdle,
            ShowTaskbarBackgroundTint = defaults.Appearance.ShowTaskbarBackgroundTint,
            ShowTaskbarBackgroundBorder = defaults.Appearance.ShowTaskbarBackgroundBorder,
            ShowTaskbarBackgroundTexture = defaults.Appearance.ShowTaskbarBackgroundTexture,
            TaskbarFrostedStyle = defaults.Appearance.TaskbarFrostedStyle,
            IdleNoteColor = defaults.Appearance.IdleNoteColor
        };
        next.TaskbarExperience = next.TaskbarExperience with
        {
            MediaFontSizePercent = defaults.TaskbarExperience.MediaFontSizePercent
        };
        Replace(next, SettingsResetScope.Appearance);
    }

    /// <summary>恢复内容与布局，不改屏幕位置、外观或歌词取词及文本处理偏好。</summary>
    public static void ResetContentLayout()
    {
        var next = Current.Clone();
        var defaults = Defaults;
        next.TaskbarExperience = AFMediaBar.Classes.Services.Settings.SettingsResetPolicy.Content(next.TaskbarExperience,
            defaults.TaskbarExperience);
        next.LyricsTextAlignment = defaults.LyricsTextAlignment;
        next.LyricsFixedWidthEnabled = defaults.LyricsFixedWidthEnabled;
        next.LyricsFixedWidthDip = defaults.LyricsFixedWidthDip;
        next.LyricsLongLineScrollEnabled = defaults.LyricsLongLineScrollEnabled;
        Replace(next, SettingsResetScope.ContentLayout);
    }

    /// <summary>恢复显示目标与定位，不恢复各层内容及外观。</summary>
    public static void ResetScreenAndPlacement()
    {
        var next = Current.Clone();
        var defaults = Defaults;
        next.TaskbarTargetMonitorDeviceIds = defaults.TaskbarTargetMonitorDeviceIds;
        next.TaskbarTargetMonitorDeviceId = defaults.TaskbarTargetMonitorDeviceId;
        next.Position = defaults.Position;
        next.TaskbarBarManualPadding = defaults.TaskbarBarManualPadding;
        next.TaskbarBarCrossAxisOffsetDip = defaults.TaskbarBarCrossAxisOffsetDip;
        next.TaskbarBarAvoidIcons = defaults.TaskbarBarAvoidIcons;
        next.TaskbarBarPositionLocked = defaults.TaskbarBarPositionLocked;
        Replace(next, SettingsResetScope.Layout);
    }

    public static void ResetDisplayModes()
    {
        var next = Current.Clone(); var defaults = Defaults;
        // 显示模式页只恢复各层功能；尺寸、间距、排列与文字样式由外观页重置。
        next.TaskbarExperience = next.TaskbarExperience with
        {
            HoverLayerEnabled = defaults.TaskbarExperience.HoverLayerEnabled,
            FullLayerEnabled = defaults.TaskbarExperience.FullLayerEnabled,
            FullPanel = defaults.TaskbarExperience.FullPanel,
            SpectrumVisible = defaults.TaskbarExperience.SpectrumVisible,
            PerformanceVisible = defaults.TaskbarExperience.PerformanceVisible,
            RestProgressVisible = defaults.TaskbarExperience.RestProgressVisible,
            ArtworkProgressVisible = defaults.TaskbarExperience.ArtworkProgressVisible,
            ArtworkVisible = defaults.TaskbarExperience.ArtworkVisible,
            FullPanelEntryVisible = defaults.TaskbarExperience.FullPanelEntryVisible,
            FullPanelEntryTrigger = defaults.TaskbarExperience.FullPanelEntryTrigger,
            FullPanelEntryHoverDelayMilliseconds = defaults.TaskbarExperience.FullPanelEntryHoverDelayMilliseconds,
            HoverControls = defaults.TaskbarExperience.HoverControls,
            OutputDeviceVisible = defaults.TaskbarExperience.OutputDeviceVisible,
            InputDeviceVisible = defaults.TaskbarExperience.InputDeviceVisible,
            VolumeVisible = defaults.TaskbarExperience.VolumeVisible,
            Arrangement = defaults.TaskbarExperience.Arrangement,
            IdleComponents = defaults.TaskbarExperience.IdleComponents
        };
        next.WindowMode = defaults.WindowMode; next.LayoutOrientationMode = defaults.LayoutOrientationMode;
        next.TaskbarBarEnabled = defaults.TaskbarBarEnabled;
        next.TaskbarTargetMonitorDeviceIds = defaults.TaskbarTargetMonitorDeviceIds;
        next.TaskbarTargetMonitorDeviceId = defaults.TaskbarTargetMonitorDeviceId;
        next.TaskbarBarManualPadding = defaults.TaskbarBarManualPadding;
        next.Position = defaults.Position; next.TaskbarBarCrossAxisOffsetDip = defaults.TaskbarBarCrossAxisOffsetDip;
        next.TaskbarBarAvoidIcons = defaults.TaskbarBarAvoidIcons; next.TaskbarBarPositionLocked = defaults.TaskbarBarPositionLocked;
        // 灵动岛外观的 UI 现在位于显示模式页的灵动岛分区，因此它的默认值也归这一页的重置作用域；
        // ResetAppearance 仍然重置同一份设置，两个入口重置同一组值不会互相矛盾。
        // The island appearance UI now lives in the display-mode page's island section, so its defaults belong to
        // this page's reset scope too; ResetAppearance still resets the same values, and both entries agreeing is
        // what keeps "restore this page" honest.
        next.DynamicIslandSurface = defaults.DynamicIslandSurface;
        Replace(next, SettingsResetScope.DisplayModes);
    }
    public static void ResetExtraFeatures()
    {
        var next = Current.Clone(); var defaults = Defaults;
        next.TrackChangeNotification = defaults.TrackChangeNotification;
        next.SmtcSourceFilter = defaults.SmtcSourceFilter;
        next.MediaSelection = defaults.MediaSelection;
        Replace(next, SettingsResetScope.ExtraFeatures);
    }
    public static void ResetComponents()
    {
        var next = Current.Clone(); var defaults = Defaults;
        next.QuickLaunch = defaults.QuickLaunch;
        next.SpectrumComponent = defaults.SpectrumComponent;
        next.PerformanceComponent = defaults.PerformanceComponent;
        next.BluetoothBatteryWidget = defaults.BluetoothBatteryWidget;
        next.TaskbarExperience = next.TaskbarExperience with
        { SpectrumVisible = defaults.TaskbarExperience.SpectrumVisible, PerformanceVisible = defaults.TaskbarExperience.PerformanceVisible };
        Replace(next, SettingsResetScope.Components);
    }
    public static void ResetDeviceManagement()
    {
        var next = Current.Clone();
        next.AudioDevices = Defaults.AudioDevices with
        { OutputCycleHotkey = next.AudioDevices.OutputCycleHotkey, InputCycleHotkey = next.AudioDevices.InputCycleHotkey };
        Replace(next, SettingsResetScope.DeviceManagement);
    }

    public static void ResetInteraction()
    {
        var next = Current.Clone();
        next.Interaction = Defaults.Interaction;
        next.AudioDevices = next.AudioDevices with
        { OutputCycleHotkey = Defaults.AudioDevices.OutputCycleHotkey, InputCycleHotkey = Defaults.AudioDevices.InputCycleHotkey };
        Replace(next, SettingsResetScope.Interaction);
    }
    public static void ResetLyrics()
    {
        var next = Current.Clone(); var defaults = Defaults;
        next.LyricsEnabled = defaults.LyricsEnabled; next.TwoLineLyricsEnabled = defaults.TwoLineLyricsEnabled;
        next.AllowBrowserAndVideoLyrics = defaults.AllowBrowserAndVideoLyrics;
        next.LyricsSecondaryLine = defaults.LyricsSecondaryLine;
        next.LyricsSyllableHighlightEnabled = defaults.LyricsSyllableHighlightEnabled;
        next.LyricsUnsungOpacityPercent = defaults.LyricsUnsungOpacityPercent;
        next.LyricsCharacterSpacingPercent = defaults.LyricsCharacterSpacingPercent;
        next.LyricsLineGapPercent = defaults.LyricsLineGapPercent;
        next.LyricsInfoLineFilterEnabled = defaults.LyricsInfoLineFilterEnabled;
        next.LyricsFuriganaFilterEnabled = defaults.LyricsFuriganaFilterEnabled;
        next.LyricsBracketSymbolsHidden = defaults.LyricsBracketSymbolsHidden;
        next.LyricsChineseConversion = defaults.LyricsChineseConversion;
        next.LyricsSource = defaults.LyricsSource;
        next.LyricsArtistSeparators = defaults.LyricsArtistSeparators;
        Replace(next, SettingsResetScope.Lyrics);
    }
    public static void ResetLayout()
    {
        var next = Current.Clone(); var defaults = Defaults;
        next.TaskbarBarEnabled = defaults.TaskbarBarEnabled;
        next.TaskbarTargetMonitorDeviceIds = defaults.TaskbarTargetMonitorDeviceIds;
        next.TaskbarTargetMonitorDeviceId = defaults.TaskbarTargetMonitorDeviceId;
        next.Position = defaults.Position; next.TaskbarBarBackgroundBlur = defaults.TaskbarBarBackgroundBlur; next.TaskbarBarManualPadding = defaults.TaskbarBarManualPadding;
        next.WindowMode = defaults.WindowMode; next.LayoutOrientationMode = defaults.LayoutOrientationMode; next.LayoutLengthScalePercent = defaults.LayoutLengthScalePercent;
        next.LayoutThicknessScalePercent = defaults.LayoutThicknessScalePercent; next.DynamicIslandBackgroundMode = defaults.DynamicIslandBackgroundMode;
        next.TaskbarBarCrossAxisOffsetDip = defaults.TaskbarBarCrossAxisOffsetDip; next.TaskbarBarAvoidIcons = defaults.TaskbarBarAvoidIcons;
        next.TaskbarBarPositionLocked = defaults.TaskbarBarPositionLocked; next.DynamicIslandLeft = defaults.DynamicIslandLeft; next.DynamicIslandTop = defaults.DynamicIslandTop;
        next.DynamicIslandEdge = defaults.DynamicIslandEdge; next.DynamicIslandEdgeDocked = defaults.DynamicIslandEdgeDocked;
        Replace(next, SettingsResetScope.Layout);
    }
    /// <summary>
    /// 全部恢复默认：以用户保存的默认设置为准，没有快照时回到程序内置默认。
    /// Restores everything: the user's saved defaults when they exist, the built-in defaults otherwise.
    /// </summary>
    public static void ResetAll()
    {
        var next = Defaults.Clone();
        // 兼容提示属于一次性启动状态，不是用户外观偏好；恢复默认不能让它再次弹出或再次强制开启材质。
        // The compatibility notice is one-time startup state rather than an appearance preference; resetting settings must not
        // make it appear again or automatically force the material on again.
        next.TranslucentTbCompatibilityPromptShown = Current.TranslucentTbCompatibilityPromptShown;
        Replace(next, SettingsResetScope.All);
    }

    private static void Subscribe(AppSettings settings) => settings.PropertyChanged += OnPropertyChanged;
    private static void Unsubscribe(AppSettings settings) => settings.PropertyChanged -= OnPropertyChanged;
    private static void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        SettingsChanged?.Invoke(null, new SettingsChangedEventArgs(propertyName: e.PropertyName));
        switch (e.PropertyName)
        {
            case nameof(AppSettings.Appearance):
                if (_publishingTaskbarAppearanceOnly)
                    TaskbarAppearanceSettingsChanged?.Invoke(null, new AppearanceSettingsChangedEventArgs(Current.Appearance));
                else
                    AppearanceSettingsChanged?.Invoke(null, new AppearanceSettingsChangedEventArgs(Current.Appearance));
                break;
            case nameof(AppSettings.TrayWheelBehavior): TrayWheelBehaviorChanged?.Invoke(null, EventArgs.Empty); break;
            case nameof(AppSettings.LyricsEnabled):
            case nameof(AppSettings.AllowBrowserAndVideoLyrics):
            case nameof(AppSettings.TwoLineLyricsEnabled):
            case nameof(AppSettings.LyricsSecondaryLine):
            case nameof(AppSettings.LyricsSyllableHighlightEnabled):
            case nameof(AppSettings.LyricsUnsungOpacityPercent):
            case nameof(AppSettings.LyricsCharacterSpacingPercent):
            case nameof(AppSettings.LyricsLineGapPercent):
            case nameof(AppSettings.LyricsFixedWidthEnabled):
            case nameof(AppSettings.LyricsFixedWidthDip):
            case nameof(AppSettings.LyricsLongLineScrollEnabled):
            case nameof(AppSettings.LyricsChineseConversion):
            case nameof(AppSettings.LyricsInfoLineFilterEnabled):
            case nameof(AppSettings.LyricsFuriganaFilterEnabled):
            case nameof(AppSettings.LyricsBracketSymbolsHidden):
            case nameof(AppSettings.LyricsArtistSeparators):
            case nameof(AppSettings.LyricsSource): LyricsSettingsChanged?.Invoke(null, EventArgs.Empty); break;
            case nameof(AppSettings.LyricsTextAlignment): LyricsSettingsChanged?.Invoke(null, EventArgs.Empty); break;
            case nameof(AppSettings.TaskbarExperience): TaskbarExperienceSettingsChanged?.Invoke(null, EventArgs.Empty); break;
            case nameof(AppSettings.Interaction): InteractionSettingsChanged?.Invoke(null, EventArgs.Empty); break;
            case nameof(AppSettings.Experiments):
                InteractionSettingsChanged?.Invoke(null, EventArgs.Empty);
                TaskbarExperienceSettingsChanged?.Invoke(null, EventArgs.Empty);
                break;
            case nameof(AppSettings.TrackChangeNotification): TrackChangeNotificationSettingsChanged?.Invoke(null, EventArgs.Empty); break;
            case nameof(AppSettings.SmtcSourceFilter):
            case nameof(AppSettings.MediaSelection):
            case nameof(AppSettings.QuickLaunch):
            case nameof(AppSettings.SpectrumComponent):
            case nameof(AppSettings.PerformanceComponent): ExtraFeaturesSettingsChanged?.Invoke(null, EventArgs.Empty); break;
            case nameof(AppSettings.Update): UpdateSettingsChanged?.Invoke(null, EventArgs.Empty); break;
            case nameof(AppSettings.TaskbarTargetMonitorDeviceIds):
            case nameof(AppSettings.TaskbarTargetMonitorDeviceId): TaskbarTargetMonitorChanged?.Invoke(null, EventArgs.Empty); break;
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
        TrackChangeNotificationSettingsChanged?.Invoke(null, EventArgs.Empty); TaskbarTargetMonitorChanged?.Invoke(null, EventArgs.Empty);
        ExtraFeaturesSettingsChanged?.Invoke(null, EventArgs.Empty);
        UpdateSettingsChanged?.Invoke(null, EventArgs.Empty);
        RaiseLayoutSettingsChanged(Current.WindowMode, Current.LayoutOrientationMode);
    }
}
