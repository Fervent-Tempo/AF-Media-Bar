using AFMediaBar.Classes.Models.Layout;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Services.Localization;
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
public enum LyricsSecondaryLineMode
{
    /// <summary>显示下一句歌词。/ Show the next lyric line.</summary>
    NextLine = 0,

    /// <summary>显示当前句翻译。/ Show the translation of the active line.</summary>
    Translation = 1,

    /// <summary>显示当前句音译（如粤拼、罗马字）。成员值参与序列化，因此只能追加。
    /// Show the romanization of the active line (Cantonese jyutping, romanized Japanese, and so on). Member values take part in
    /// serialization, so this may only be appended.</summary>
    Romanization = 2
}

/// <summary>应用全部用户设置，并在属性直接修改时发布变更。 / All user settings; direct mutations publish changes.</summary>
public sealed class AppSettings : INotifyPropertyChanged
{
    private AppearanceSettings _appearance = AppearanceSettings.Default;
    private TrayWheelBehavior _trayWheelBehavior = TrayWheelBehavior.SwitchOutputDevice;
    private bool _lyricsEnabled = true;
    private bool _twoLineLyricsEnabled = true;
    /// <summary>
    /// 双行歌词第二行的**来源顺序**：列表顺序即优先级（默认 下一句 → 翻译 → 音译），未列出的来源不会被使用。
    /// Source order of the second lyric line: the list order is the priority (next line, translation, romanization by default) and a source
    /// missing from the list is never used.
    /// </summary>
    private LyricsSecondaryLineSettings _lyricsSecondaryLine = LyricsSecondaryLineSettings.Default;
    private bool _taskbarBarEnabled = true;
    private IReadOnlyList<string>? _taskbarTargetMonitorDeviceIds;
    private string? _taskbarTargetMonitorDeviceId;
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
    private LyricsTextAlignment _lyricsTextAlignment = LyricsTextAlignment.Left;
    private bool _lyricsSyllableHighlightEnabled = true;
    private int _lyricsUnsungOpacityPercent = LyricsUnsungOpacity.DefaultPercent;
    private int _lyricsCharacterSpacingPercent = LyricsCharacterSpacing.DefaultPercent;
    private int _lyricsLineGapPercent = LyricsLineGap.DefaultPercent;
    private bool _lyricsFixedWidthEnabled;
    private int _lyricsFixedWidthDip = LyricsFixedWidth.DefaultDip;
    private bool _lyricsInfoLineFilterEnabled = true;
    private LyricsMatchStrictness _lyricsMatchStrictness = LyricsMatchStrictness.Balanced;
    private LyricsSourceSettings _lyricsSource = LyricsSourceSettings.Default;
    private LyricsDefaultBindingSettings _lyricsDefaultBindings = LyricsDefaultBindingSettings.Default;
    private LyricsAdoptionMode _lyricsAdoptionMode = LyricsAdoptionMode.PreferDefaultSourceWithDeadline;
    private int _lyricsConcurrencyBatchSize = LyricsConcurrencyDefaults.BatchSizeDefault;
    private int _lyricsAdoptionDeadlineMilliseconds = LyricsConcurrencyDefaults.AdoptionDeadlineMillisecondsDefault;
    private TrackChangeNotificationSettings _trackChangeNotification = TrackChangeNotificationSettings.Default;
    private SmtcSourceFilterSettings _smtcSourceFilter = SmtcSourceFilterSettings.Default;
    private QuickLaunchSettings _quickLaunch = QuickLaunchSettings.Default;
    private SpectrumComponentSettings _spectrumComponent = SpectrumComponentSettings.Default;
    private PerformanceComponentSettings _performanceComponent = PerformanceComponentSettings.Default;
    private UpdateSettings _update = UpdateSettings.Default;
    private bool _launchAtStartup = true;
    private InterfaceLanguage _interfaceLanguage = InterfaceLanguage.System;

    public AppearanceSettings Appearance { get => _appearance; set => Set(ref _appearance, value.Normalize()); }
    public TrayWheelBehavior TrayWheelBehavior { get => _trayWheelBehavior; set => Set(ref _trayWheelBehavior, value); }
    public bool LyricsEnabled { get => _lyricsEnabled; set => Set(ref _lyricsEnabled, value); }
    public bool TwoLineLyricsEnabled { get => _twoLineLyricsEnabled; set => Set(ref _twoLineLyricsEnabled, value); }
    public LyricsSecondaryLineSettings LyricsSecondaryLine { get => _lyricsSecondaryLine; set => Set(ref _lyricsSecondaryLine, value.Normalize()); }
    public bool TaskbarBarEnabled { get => _taskbarBarEnabled; set => Set(ref _taskbarBarEnabled, value); }
    /// <summary>
    /// 任务栏媒体栏的显式显示器集合。空集合表示使用主显示器回退；列表保留暂时断开的目标，以便重连后自动恢复。
    /// Explicit monitor set for taskbar media bars. An empty set falls back to the primary monitor; temporarily disconnected targets remain so they
    /// can return automatically after reconnecting.
    /// </summary>
    public IReadOnlyList<string>? TaskbarTargetMonitorDeviceIds
    {
        get => _taskbarTargetMonitorDeviceIds;
        set => Set(ref _taskbarTargetMonitorDeviceIds, NormalizeMonitorDeviceIds(value));
    }

    /// <summary>
    /// 旧版单目标字段，仅用于读取已有设置；新写入统一使用 <see cref="TaskbarTargetMonitorDeviceIds"/>。
    /// Legacy single-target field retained only for loading existing settings; new writes use <see cref="TaskbarTargetMonitorDeviceIds"/>.
    /// </summary>
    public string? TaskbarTargetMonitorDeviceId
    {
        get => _taskbarTargetMonitorDeviceId;
        set => Set(ref _taskbarTargetMonitorDeviceId, string.IsNullOrWhiteSpace(value) ? null : value.Trim());
    }
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

    /// <summary>是否启用逐字擦亮（有音节时间轴时当前行随播放亮起）。/ Whether syllable highlighting is enabled.</summary>
    public bool LyricsSyllableHighlightEnabled { get => _lyricsSyllableHighlightEnabled; set => Set(ref _lyricsSyllableHighlightEnabled, value); }

    /// <summary>启用逐字擦亮时底色层（未唱部分）的不透明度百分比。/ Opacity percentage of the base (unsung) layer while highlighting is on.</summary>
    public int LyricsUnsungOpacityPercent { get => _lyricsUnsungOpacityPercent; set => Set(ref _lyricsUnsungOpacityPercent, value); }

    /// <summary>歌词字距（字号的百分比）。/ Lyric character spacing as a percentage of the font size.</summary>
    public int LyricsCharacterSpacingPercent { get => _lyricsCharacterSpacingPercent; set => Set(ref _lyricsCharacterSpacingPercent, value); }

    /// <summary>双行歌词额外增加的行距（字号的百分比）。/ Extra line gap for two-line lyrics as a percentage of the font size.</summary>
    public int LyricsLineGapPercent { get => _lyricsLineGapPercent; set => Set(ref _lyricsLineGapPercent, value); }

    /// <summary>歌词框是否固定长度（关闭时随歌词内容自动伸缩）。/ Whether the lyric box keeps a fixed length instead of following the content.</summary>
    public bool LyricsFixedWidthEnabled { get => _lyricsFixedWidthEnabled; set => Set(ref _lyricsFixedWidthEnabled, value); }

    /// <summary>歌词框固定长度（DIP）。/ Fixed lyric-box length in DIP.</summary>
    public int LyricsFixedWidthDip { get => _lyricsFixedWidthDip; set => Set(ref _lyricsFixedWidthDip, value); }

    /// <summary>是否丢弃作者、作曲、制作等信息行。/ Whether credit lines such as writer, composer, and producer are dropped.</summary>
    public bool LyricsInfoLineFilterEnabled { get => _lyricsInfoLineFilterEnabled; set => Set(ref _lyricsInfoLineFilterEnabled, value); }

    /// <summary>搜索型歌词来源的匹配严格度。/ Match strictness for search-based lyric sources.</summary>
    public LyricsMatchStrictness LyricsMatchStrictness { get => _lyricsMatchStrictness; set => Set(ref _lyricsMatchStrictness, value); }

    /// <summary>启用的歌词来源与它们的优先级顺序。/ The enabled lyric sources and their priority order.</summary>
    public LyricsSourceSettings LyricsSource { get => _lyricsSource; set => Set(ref _lyricsSource, value.Normalize()); }

    /// <summary>默认取词接口的用户绑定表。/ The user's default-interface binding table.</summary>
    public LyricsDefaultBindingSettings LyricsDefaultBindings { get => _lyricsDefaultBindings; set => Set(ref _lyricsDefaultBindings, value.Normalize()); }

    /// <summary>并发取词的结果采纳策略。/ The adoption mode for concurrent lyric retrieval.</summary>
    public LyricsAdoptionMode LyricsAdoptionMode { get => _lyricsAdoptionMode; set => Set(ref _lyricsAdoptionMode, value); }

    /// <summary>优先级来源每次并发发出的个数（默认接口不占批次名额）。/ How many priority sources are dispatched concurrently per batch (the default interface takes no batch slot).</summary>
    public int LyricsConcurrencyBatchSize { get => _lyricsConcurrencyBatchSize; set => Set(ref _lyricsConcurrencyBatchSize, LyricsConcurrencyDefaults.NormalizeBatchSize(value)); }

    /// <summary>候补结果出现后留给默认接口的倒计时（毫秒）。/ Countdown (milliseconds) left to the default interface once a candidate result exists.</summary>
    public int LyricsAdoptionDeadlineMilliseconds { get => _lyricsAdoptionDeadlineMilliseconds; set => Set(ref _lyricsAdoptionDeadlineMilliseconds, LyricsConcurrencyDefaults.NormalizeAdoptionDeadlineMilliseconds(value)); }
    public TrackChangeNotificationSettings TrackChangeNotification
    {
        get => _trackChangeNotification;
        set => Set(ref _trackChangeNotification, value.Normalize());
    }
    public SmtcSourceFilterSettings SmtcSourceFilter { get => _smtcSourceFilter; set => Set(ref _smtcSourceFilter, value.Normalize()); }
    public QuickLaunchSettings QuickLaunch { get => _quickLaunch; set => Set(ref _quickLaunch, value.Normalize()); }
    public SpectrumComponentSettings SpectrumComponent { get => _spectrumComponent; set => Set(ref _spectrumComponent, value.Normalize()); }
    public PerformanceComponentSettings PerformanceComponent { get => _performanceComponent; set => Set(ref _performanceComponent, value.Normalize()); }

    /// <summary>更新下载器设置：自动检查、自动下载安装、已跳过版本与上次检查结果。/ Update-downloader settings: automatic checking, automatic download and install, skipped version, and the last check result.</summary>
    public UpdateSettings Update { get => _update; set => Set(ref _update, value.Normalize()); }

    /// <summary>
    /// 是否随 Windows 登录自动启动。默认开启：设置里存的是用户意图，注册表里的 Run 项是它的执行结果，
    /// 因此启动时会按该值核对一次登记状态。
    /// Whether the application starts with the Windows session. On by default: the settings file stores the intent while the
    /// registry Run entry is its effect, so startup reconciles the registration against this value once.
    /// </summary>
    public bool LaunchAtStartup { get => _launchAtStartup; set => Set(ref _launchAtStartup, value); }

    /// <summary>
    /// 界面语言选项。默认「跟随系统」：全新安装时不猜用户想用哪一种中文，而是按系统 UI 语言解析。
    /// 这里存的是选项本身，实际生效的语言由 <see cref="LocalizationService"/> 解析并发布。
    /// The interface-language option. "Follow the system" by default: a fresh installation does not guess which kind of
    /// Chinese the user wants but resolves it against the system UI language. What is stored here is the option itself;
    /// <see cref="LocalizationService"/> resolves and publishes the language actually in effect.
    /// </summary>
    public InterfaceLanguage InterfaceLanguage { get => _interfaceLanguage; set => Set(ref _interfaceLanguage, value); }

    public event PropertyChangedEventHandler? PropertyChanged;

    public AppSettings Normalize()
    {
        var defaults = new AppSettings();
        var result = Clone();
        result.Appearance = result.Appearance.Normalize();
        if (!Enum.IsDefined(result.TrayWheelBehavior)) result.TrayWheelBehavior = defaults.TrayWheelBehavior;
        result.LyricsSecondaryLine = result.LyricsSecondaryLine.Normalize();
        if (!Enum.IsDefined(result.Position)) result.Position = defaults.Position;
        result.WindowMode = WindowMode.Taskbar;
        if (!Enum.IsDefined(result.LayoutOrientationMode)) result.LayoutOrientationMode = defaults.LayoutOrientationMode;
        if (!Enum.IsDefined(result.DynamicIslandBackgroundMode)) result.DynamicIslandBackgroundMode = defaults.DynamicIslandBackgroundMode;
        if (!Enum.IsDefined(result.DynamicIslandEdge)) result.DynamicIslandEdge = defaults.DynamicIslandEdge;
        if (!Enum.IsDefined(result.LyricsTextAlignment)) result.LyricsTextAlignment = defaults.LyricsTextAlignment;
        if (!Enum.IsDefined(result.LyricsMatchStrictness)) result.LyricsMatchStrictness = defaults.LyricsMatchStrictness;
        result.LyricsUnsungOpacityPercent = LyricsUnsungOpacity.Normalize(result.LyricsUnsungOpacityPercent);
        result.LyricsCharacterSpacingPercent = LyricsCharacterSpacing.Normalize(result.LyricsCharacterSpacingPercent);
        result.LyricsLineGapPercent = LyricsLineGap.Normalize(result.LyricsLineGapPercent);
        result.LyricsFixedWidthDip = LyricsFixedWidth.Normalize(result.LyricsFixedWidthDip);
        result.LyricsSource = result.LyricsSource.Normalize();
        result.LyricsDefaultBindings = result.LyricsDefaultBindings.Normalize();
        if (!Enum.IsDefined(result.LyricsAdoptionMode)) result.LyricsAdoptionMode = defaults.LyricsAdoptionMode;
        result.LyricsConcurrencyBatchSize = LyricsConcurrencyDefaults.NormalizeBatchSize(result.LyricsConcurrencyBatchSize);
        result.LyricsAdoptionDeadlineMilliseconds = LyricsConcurrencyDefaults.NormalizeAdoptionDeadlineMilliseconds(result.LyricsAdoptionDeadlineMilliseconds);
        result.TaskbarExperience = result.TaskbarExperience.Normalize();
        result.Interaction = result.Interaction.Normalize();
        result.TaskbarSurface = result.TaskbarSurface.Normalize();
        result.DynamicIslandSurface = result.DynamicIslandSurface.Normalize();
        result.TrackChangeNotification = result.TrackChangeNotification.Normalize();
        result.SmtcSourceFilter = result.SmtcSourceFilter.Normalize();
        result.QuickLaunch = result.QuickLaunch.Normalize();
        result.SpectrumComponent = result.SpectrumComponent.Normalize();
        result.PerformanceComponent = result.PerformanceComponent.Normalize();
        result.Update = result.Update.Normalize();
        if (!Enum.IsDefined(result.InterfaceLanguage)) result.InterfaceLanguage = defaults.InterfaceLanguage;
        result.TaskbarTargetMonitorDeviceId = string.IsNullOrWhiteSpace(result.TaskbarTargetMonitorDeviceId)
            ? null
            : result.TaskbarTargetMonitorDeviceId.Trim();
        result.TaskbarTargetMonitorDeviceIds = NormalizeMonitorDeviceIds(result.TaskbarTargetMonitorDeviceIds);
        if ((result.TaskbarTargetMonitorDeviceIds?.Count ?? 0) == 0 &&
            !string.IsNullOrWhiteSpace(result.TaskbarTargetMonitorDeviceId) &&
            !TaskbarTargetPolicy.IsLegacyAllTaskbars(result.TaskbarTargetMonitorDeviceId))
        {
            result.TaskbarTargetMonitorDeviceIds = [result.TaskbarTargetMonitorDeviceId];
            result.TaskbarTargetMonitorDeviceId = null;
        }
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
        LyricsSecondaryLine = LyricsSecondaryLine,
        TaskbarBarEnabled = TaskbarBarEnabled,
        TaskbarTargetMonitorDeviceIds = TaskbarTargetMonitorDeviceIds is null ? null : [.. TaskbarTargetMonitorDeviceIds],
        TaskbarTargetMonitorDeviceId = TaskbarTargetMonitorDeviceId,
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
        LyricsTextAlignment = LyricsTextAlignment,
        LyricsSyllableHighlightEnabled = LyricsSyllableHighlightEnabled,
        LyricsUnsungOpacityPercent = LyricsUnsungOpacityPercent,
        LyricsCharacterSpacingPercent = LyricsCharacterSpacingPercent,
        LyricsLineGapPercent = LyricsLineGapPercent,
        LyricsFixedWidthEnabled = LyricsFixedWidthEnabled,
        LyricsFixedWidthDip = LyricsFixedWidthDip,
        LyricsInfoLineFilterEnabled = LyricsInfoLineFilterEnabled,
        LyricsMatchStrictness = LyricsMatchStrictness,
        LyricsSource = LyricsSource,
        LyricsDefaultBindings = LyricsDefaultBindings,
        LyricsAdoptionMode = LyricsAdoptionMode,
        LyricsConcurrencyBatchSize = LyricsConcurrencyBatchSize,
        LyricsAdoptionDeadlineMilliseconds = LyricsAdoptionDeadlineMilliseconds,
        TrackChangeNotification = TrackChangeNotification,
        SmtcSourceFilter = SmtcSourceFilter,
        QuickLaunch = QuickLaunch,
        SpectrumComponent = SpectrumComponent,
        PerformanceComponent = PerformanceComponent,
        Update = Update,
        LaunchAtStartup = LaunchAtStartup,
        InterfaceLanguage = InterfaceLanguage
    };

    private static IReadOnlyList<string>? NormalizeMonitorDeviceIds(IEnumerable<string>? deviceIds)
    {
        if (deviceIds is null)
            return null;

        return deviceIds
            .Where(deviceId => !string.IsNullOrWhiteSpace(deviceId))
            .Select(deviceId => deviceId.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

/// <summary>设置重置范围。 / Settings reset scope.</summary>
public enum SettingsResetScope { General, Appearance, Layout, DisplayModes, ExtraFeatures, Interaction, Lyrics, All }

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
    private static AppSettings? _userDefaults;

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
    public static void SetLyricsMatchStrictness(LyricsMatchStrictness strictness) => Current.LyricsMatchStrictness = strictness;
    public static void SetLyricsSourceSettings(LyricsSourceSettings settings) => Current.LyricsSource = settings;
    public static void SetLyricsDefaultBindingSettings(LyricsDefaultBindingSettings settings) => Current.LyricsDefaultBindings = settings;
    public static void SetLyricsAdoptionMode(LyricsAdoptionMode mode) => Current.LyricsAdoptionMode = mode;
    public static void SetLyricsConcurrencyBatchSize(int batchSize) => Current.LyricsConcurrencyBatchSize = batchSize;
    public static void SetLyricsAdoptionDeadlineMilliseconds(int milliseconds) => Current.LyricsAdoptionDeadlineMilliseconds = milliseconds;
    public static void SetAppearanceSettings(AppearanceSettings appearance) => Current.Appearance = appearance;
    public static void SetTaskbarExperienceSettings(TaskbarExperienceSettings settings) => Current.TaskbarExperience = settings;
    public static void SetInteractionSettings(GlobalInteractionSettings settings) => Current.Interaction = settings;
    public static void SetTrackChangeNotificationSettings(TrackChangeNotificationSettings settings) => Current.TrackChangeNotification = settings;
    public static void SetSmtcSourceFilterSettings(SmtcSourceFilterSettings settings) => Current.SmtcSourceFilter = settings;
    public static void SetQuickLaunchSettings(QuickLaunchSettings settings) => Current.QuickLaunch = settings;
    public static void SetSpectrumComponentSettings(SpectrumComponentSettings settings) => Current.SpectrumComponent = settings;
    public static void SetPerformanceComponentSettings(PerformanceComponentSettings settings) => Current.PerformanceComponent = settings;
    public static void SetUpdateSettings(UpdateSettings settings) => Current.Update = settings;
    public static void RaiseLayoutSettingsChanged(WindowMode windowMode, LayoutOrientationMode orientationMode) => LayoutSettingsChanged?.Invoke(null, new LayoutSettingsChangedEventArgs(windowMode, orientationMode));

    public static void ResetGeneral()
    {
        // Current general-page controls are operating-system or navigation actions and do not
        // own interaction or lyric settings. Publish the reset boundary without changing them.
        Replace(Current.Clone(), SettingsResetScope.General);
    }
    public static void ResetAppearance()
    {
        var next = Current.Clone(); var defaults = Defaults;
        next.Appearance = defaults.Appearance;
        next.TaskbarSurface = defaults.TaskbarSurface;
        next.DynamicIslandSurface = defaults.DynamicIslandSurface;
        // 媒体文字大小的界面位于外观页的「媒体栏文字」分组，因此它也属于这一页的重置作用域。
        // 显示模式页的重置仍然重置同一份任务栏体验设置，两个入口重置同一组值不会互相矛盾——与灵动岛外观的处理相同。
        // The media text size is presented in the appearance page's media-bar-text group, so it belongs to this page's reset scope
        // too. The display-mode page's reset still resets the same taskbar experience settings, and both entries agreeing is what
        // keeps "restore this page" honest — the same arrangement the island appearance already uses.
        next.TaskbarExperience = next.TaskbarExperience with
        {
            MediaFontSizePercent = defaults.TaskbarExperience.MediaFontSizePercent
        };
        Replace(next, SettingsResetScope.Appearance);
    }
    public static void ResetDisplayModes()
    {
        var next = Current.Clone(); var defaults = Defaults;
        next.TaskbarExperience = defaults.TaskbarExperience;
        next.WindowMode = defaults.WindowMode; next.LayoutOrientationMode = defaults.LayoutOrientationMode;
        next.TaskbarBarEnabled = defaults.TaskbarBarEnabled;
        next.TaskbarTargetMonitorDeviceIds = defaults.TaskbarTargetMonitorDeviceIds;
        next.TaskbarTargetMonitorDeviceId = defaults.TaskbarTargetMonitorDeviceId;
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
        next.QuickLaunch = QuickLaunchSettings.Default;
        next.SpectrumComponent = defaults.SpectrumComponent;
        next.PerformanceComponent = defaults.PerformanceComponent;
        Replace(next, SettingsResetScope.ExtraFeatures);
    }
    public static void ResetInteraction()
    {
        var next = Current.Clone();
        next.Interaction = Defaults.Interaction;
        Replace(next, SettingsResetScope.Interaction);
    }
    public static void ResetLyrics()
    {
        var next = Current.Clone(); var defaults = Defaults;
        next.LyricsEnabled = defaults.LyricsEnabled; next.TwoLineLyricsEnabled = defaults.TwoLineLyricsEnabled;
        next.LyricsSecondaryLine = defaults.LyricsSecondaryLine; next.LyricsTextAlignment = defaults.LyricsTextAlignment;
        next.LyricsSyllableHighlightEnabled = defaults.LyricsSyllableHighlightEnabled;
        next.LyricsUnsungOpacityPercent = defaults.LyricsUnsungOpacityPercent;
        next.LyricsCharacterSpacingPercent = defaults.LyricsCharacterSpacingPercent;
        next.LyricsLineGapPercent = defaults.LyricsLineGapPercent;
        next.LyricsFixedWidthEnabled = defaults.LyricsFixedWidthEnabled;
        next.LyricsFixedWidthDip = defaults.LyricsFixedWidthDip;
        next.LyricsInfoLineFilterEnabled = defaults.LyricsInfoLineFilterEnabled;
        next.LyricsMatchStrictness = defaults.LyricsMatchStrictness;
        next.LyricsSource = defaults.LyricsSource;
        next.LyricsDefaultBindings = defaults.LyricsDefaultBindings;
        next.LyricsAdoptionMode = defaults.LyricsAdoptionMode;
        next.LyricsConcurrencyBatchSize = defaults.LyricsConcurrencyBatchSize;
        next.LyricsAdoptionDeadlineMilliseconds = defaults.LyricsAdoptionDeadlineMilliseconds;
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
    public static void ResetAll() => Replace(Defaults.Clone(), SettingsResetScope.All);

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
            case nameof(AppSettings.LyricsSecondaryLine):
            case nameof(AppSettings.LyricsSyllableHighlightEnabled):
            case nameof(AppSettings.LyricsUnsungOpacityPercent):
            case nameof(AppSettings.LyricsCharacterSpacingPercent):
            case nameof(AppSettings.LyricsLineGapPercent):
            case nameof(AppSettings.LyricsFixedWidthEnabled):
            case nameof(AppSettings.LyricsFixedWidthDip):
            case nameof(AppSettings.LyricsInfoLineFilterEnabled):
            case nameof(AppSettings.LyricsMatchStrictness):
            case nameof(AppSettings.LyricsSource):
            case nameof(AppSettings.LyricsDefaultBindings):
            case nameof(AppSettings.LyricsAdoptionMode):
            case nameof(AppSettings.LyricsConcurrencyBatchSize):
            case nameof(AppSettings.LyricsAdoptionDeadlineMilliseconds): LyricsSettingsChanged?.Invoke(null, EventArgs.Empty); break;
            case nameof(AppSettings.LyricsTextAlignment): LyricsSettingsChanged?.Invoke(null, EventArgs.Empty); break;
            case nameof(AppSettings.TaskbarExperience): TaskbarExperienceSettingsChanged?.Invoke(null, EventArgs.Empty); break;
            case nameof(AppSettings.Interaction): InteractionSettingsChanged?.Invoke(null, EventArgs.Empty); break;
            case nameof(AppSettings.TrackChangeNotification): TrackChangeNotificationSettingsChanged?.Invoke(null, EventArgs.Empty); break;
            case nameof(AppSettings.SmtcSourceFilter):
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

/// <summary>外观设置变更参数。 / Appearance settings change arguments.</summary>
public sealed class AppearanceSettingsChangedEventArgs(AppearanceSettings appearance) : EventArgs { public AppearanceSettings Appearance { get; } = appearance; }
/// <summary>布局设置变更参数。 / Layout settings change arguments.</summary>
public sealed class LayoutSettingsChangedEventArgs(WindowMode windowMode, LayoutOrientationMode orientationMode) : EventArgs
{
    public WindowMode WindowMode { get; } = windowMode;
    public LayoutOrientationMode OrientationMode { get; } = orientationMode;
}
