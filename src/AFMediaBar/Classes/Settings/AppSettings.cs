// 定义应用设置模型和属性通知，不负责持久化。
using AFMediaBar.Classes.Models.Layout;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Services.Localization;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AFMediaBar.Classes.Settings;


/// <summary>应用全部用户设置，并在属性直接修改时发布变更。 / All user settings; direct mutations publish changes.</summary>
public sealed class AppSettings : INotifyPropertyChanged
{
    /// <summary>Minimum cross-axis taskbar offset in DIP.</summary>
    public const double MinimumTaskbarCrossAxisOffsetDip = -20d;
    /// <summary>Maximum cross-axis taskbar offset in DIP.</summary>
    public const double MaximumTaskbarCrossAxisOffsetDip = 20d;
    private AppearanceSettings _appearance = AppearanceSettings.Default;
    private ExperimentalSettings _experiments = new();
    private FloatingBarSettings _floatingBar = new();
    private TrayWheelBehavior _trayWheelBehavior = TrayWheelBehavior.SwitchOutputDevice;
    private bool _lyricsEnabled = true;
    private bool _allowBrowserAndVideoLyrics;
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
    private AudioDeviceSettings _audioDevices = AudioDeviceSettings.Default;
    private GlobalInteractionSettings _interaction = GlobalInteractionSettings.Default;
    private ModeSurfaceSettings _taskbarSurface = ModeSurfaceSettings.Default;
    private ModeSurfaceSettings _dynamicIslandSurface = ModeSurfaceSettings.Default;
    private LyricsTextAlignment _lyricsTextAlignment = LyricsTextAlignment.Center;
    private bool _lyricsSyllableHighlightEnabled = true;
    private int _lyricsUnsungOpacityPercent = LyricsUnsungOpacity.DefaultPercent;
    private int _lyricsCharacterSpacingPercent = LyricsCharacterSpacing.DefaultPercent;
    private int _lyricsLineGapPercent = LyricsLineGap.DefaultPercent;
    private bool _lyricsFixedWidthEnabled;
    private int _lyricsFixedWidthDip = LyricsFixedWidth.DefaultDip;
    private bool _lyricsLongLineScrollEnabled = true;
    private bool _lyricsInfoLineFilterEnabled = true;
    private bool _lyricsFuriganaFilterEnabled;
    private bool _lyricsBracketSymbolsHidden = true;
    private LyricsChineseConversionMode _lyricsChineseConversion = LyricsChineseConversionMode.None;
    private LyricsSourceSettings _lyricsSource = LyricsSourceSettings.Default;
    private string _lyricsArtistSeparators = "/";
    private TrackChangeNotificationSettings _trackChangeNotification = TrackChangeNotificationSettings.Default;
    private SmtcSourceFilterSettings _smtcSourceFilter = SmtcSourceFilterSettings.Default;
    private MediaSelectionSettings _mediaSelection = MediaSelectionSettings.Default;
    private QuickLaunchSettings _quickLaunch = QuickLaunchSettings.Default;
    private SpectrumComponentSettings _spectrumComponent = SpectrumComponentSettings.Default;
    private BluetoothBatteryWidgetSettings _bluetoothBatteryWidget = new();
    private SystemMemoryOptimizationSettings _systemMemoryOptimization = new();
    private PerformanceComponentSettings _performanceComponent = PerformanceComponentSettings.Default;
    private UpdateSettings _update = UpdateSettings.Default;
    private bool _launchAtStartup = true;
    private bool _developerModeEnabled;
    private InterfaceLanguage _interfaceLanguage = InterfaceLanguage.System;
    private bool _translucentTbCompatibilityPromptShown;

    public AppearanceSettings Appearance { get => _appearance; set => Set(ref _appearance, value.Normalize()); }
    public ExperimentalSettings Experiments { get => _experiments; set => Set(ref _experiments, value ?? new()); }
    public FloatingBarSettings FloatingBar { get => _floatingBar; set => Set(ref _floatingBar, (value ?? new()).Normalize()); }
    public TrayWheelBehavior TrayWheelBehavior { get => _trayWheelBehavior; set => Set(ref _trayWheelBehavior, value); }
    public bool LyricsEnabled { get => _lyricsEnabled; set => Set(ref _lyricsEnabled, value); }
    public bool AllowBrowserAndVideoLyrics { get => _allowBrowserAndVideoLyrics; set => Set(ref _allowBrowserAndVideoLyrics, value); }
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
    public AudioDeviceSettings AudioDevices { get => _audioDevices; set => Set(ref _audioDevices, (value ?? AudioDeviceSettings.Default).Normalize()); }
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

    /// <summary>
    /// 长歌词动态滚动：跟随内容模式下，逐字歌词超过封顶宽度时随演唱进度横向滚动，不再撑开媒体栏。
    /// Long-lyric auto-scroll: in follow-content mode a syllable-timed line wider than the cap scrolls with the
    /// playback progress instead of stretching the media bar.
    /// </summary>
    public bool LyricsLongLineScrollEnabled { get => _lyricsLongLineScrollEnabled; set => Set(ref _lyricsLongLineScrollEnabled, value); }

    /// <summary>是否丢弃作者、作曲、制作等信息行。/ Whether credit lines such as writer, composer, and producer are dropped.</summary>
    public bool LyricsInfoLineFilterEnabled { get => _lyricsInfoLineFilterEnabled; set => Set(ref _lyricsInfoLineFilterEnabled, value); }

    /// <summary>仅在显示时隐藏括号内的日语假名，不修改来源歌词。</summary>
    public bool LyricsFuriganaFilterEnabled { get => _lyricsFuriganaFilterEnabled; set => Set(ref _lyricsFuriganaFilterEnabled, value); }

    /// <summary>仅在显示时清理来源附加的格式标记（行首斜杠、整行【】或〖〗符号），不修改来源歌词。</summary>
    public bool LyricsBracketSymbolsHidden { get => _lyricsBracketSymbolsHidden; set => Set(ref _lyricsBracketSymbolsHidden, value); }

    /// <summary>
    /// 歌词文本的简繁转换方向：<c>None</c> 表示不做转换。它只影响呈现，因此改变它不必清空取词缓存。
    /// Conversion direction of lyric text: <c>None</c> means no conversion. It only affects presentation, so changing it does not
    /// require clearing the retrieval cache.
    /// </summary>
    public LyricsChineseConversionMode LyricsChineseConversion
    {
        get => _lyricsChineseConversion;
        set => Set(ref _lyricsChineseConversion, value);
    }


    /// <summary>启用的歌词来源；取词顺序由固定策略决定。</summary>
    public LyricsSourceSettings LyricsSource { get => _lyricsSource; set => Set(ref _lyricsSource, value.Normalize()); }

    /// <summary>歌词匹配的全局艺术家分隔符，每行一项，保留首尾空格；默认包含斜杠，空文本关闭分割。</summary>
    public string LyricsArtistSeparators { get => _lyricsArtistSeparators; set => Set(ref _lyricsArtistSeparators, value ?? string.Empty); }





    public TrackChangeNotificationSettings TrackChangeNotification
    {
        get => _trackChangeNotification;
        set => Set(ref _trackChangeNotification, value.Normalize());
    }
    public SmtcSourceFilterSettings SmtcSourceFilter { get => _smtcSourceFilter; set => Set(ref _smtcSourceFilter, value.Normalize()); }
    public MediaSelectionSettings MediaSelection { get => _mediaSelection; set => Set(ref _mediaSelection, (value ?? MediaSelectionSettings.Default).Normalize()); }
    public QuickLaunchSettings QuickLaunch { get => _quickLaunch; set => Set(ref _quickLaunch, value.Normalize()); }
    public SpectrumComponentSettings SpectrumComponent { get => _spectrumComponent; set => Set(ref _spectrumComponent, value.Normalize()); }
    public BluetoothBatteryWidgetSettings BluetoothBatteryWidget { get => _bluetoothBatteryWidget; set => Set(ref _bluetoothBatteryWidget, (value ?? new()).Normalize()); }
    public SystemMemoryOptimizationSettings SystemMemoryOptimization { get => _systemMemoryOptimization; set => Set(ref _systemMemoryOptimization, (value ?? new()).Normalize()); }
    public PerformanceComponentSettings PerformanceComponent { get => _performanceComponent; set => Set(ref _performanceComponent, value.Normalize()); }

    /// <summary>更新下载器设置：自动检查、自动下载安装、已跳过版本与上次检查结果。/ Update-downloader settings: automatic checking, automatic download and install, skipped version, and the last check result.</summary>
    public UpdateSettings Update { get => _update; set => Set(ref _update, value.Normalize()); }

    /// <summary>
    /// 是否随 Windows 登录自动启动。默认开启：设置里存的是用户意图，注册表里的 Run 项是它的执行结果，
    /// 因此启动时会按该值核对一次登记状态。
    /// Whether the application starts with the Windows session. On by default: the settings file stores the intent while the
    /// registry Run entry is its effect, so startup reconciles the registration against this value once.
    /// </summary>
    /// <summary>手动开启开发者工具，默认关闭；参与用户默认快照。</summary>
    public bool DeveloperModeEnabled { get => _developerModeEnabled; set => Set(ref _developerModeEnabled, value); }

    public bool LaunchAtStartup { get => _launchAtStartup; set => Set(ref _launchAtStartup, value); }

    /// <summary>
    /// 界面语言选项。默认「跟随系统」：全新安装时不猜用户想用哪一种中文，而是按系统 UI 语言解析。
    /// 这里存的是选项本身，实际生效的语言由 <see cref="LocalizationService"/> 解析并发布。
    /// The interface-language option. "Follow the system" by default: a fresh installation does not guess which kind of
    /// Chinese the user wants but resolves it against the system UI language. What is stored here is the option itself;
    /// <see cref="LocalizationService"/> resolves and publishes the language actually in effect.
    /// </summary>
    public InterfaceLanguage InterfaceLanguage { get => _interfaceLanguage; set => Set(ref _interfaceLanguage, value); }

    /// <summary>
    /// 是否已经向用户说明过 TranslucentTB 兼容处理；一旦为 true，后续启动不再自动改动磨砂设置。
    /// Whether the TranslucentTB compatibility action has already been explained; once true, later starts never change the
    /// frosted-background setting automatically.
    /// </summary>
    public bool TranslucentTbCompatibilityPromptShown
    {
        get => _translucentTbCompatibilityPromptShown;
        set => Set(ref _translucentTbCompatibilityPromptShown, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public AppSettings Normalize()
    {
        var defaults = new AppSettings();
        var result = Clone();
        result.Appearance = result.Appearance.Normalize();
        result.Experiments = result.Experiments.Normalize();
        if (!Enum.IsDefined(result.TrayWheelBehavior)) result.TrayWheelBehavior = defaults.TrayWheelBehavior;
        result.LyricsSecondaryLine = result.LyricsSecondaryLine.Normalize();
        if (!Enum.IsDefined(result.Position)) result.Position = defaults.Position;
        // 旧灵动岛等未实现标识继续回退；悬浮模式不再受旧实验字段限制。
        if (result.WindowMode != WindowMode.FloatingBar)
            result.WindowMode = WindowMode.Taskbar;
        if (!Enum.IsDefined(result.LayoutOrientationMode)) result.LayoutOrientationMode = defaults.LayoutOrientationMode;
        if (!Enum.IsDefined(result.DynamicIslandBackgroundMode)) result.DynamicIslandBackgroundMode = defaults.DynamicIslandBackgroundMode;
        if (!Enum.IsDefined(result.DynamicIslandEdge)) result.DynamicIslandEdge = defaults.DynamicIslandEdge;
        if (!Enum.IsDefined(result.LyricsTextAlignment)) result.LyricsTextAlignment = defaults.LyricsTextAlignment;
        if (!Enum.IsDefined(result.LyricsChineseConversion)) result.LyricsChineseConversion = defaults.LyricsChineseConversion;
        result.LyricsUnsungOpacityPercent = LyricsUnsungOpacity.Normalize(result.LyricsUnsungOpacityPercent);
        result.LyricsCharacterSpacingPercent = LyricsCharacterSpacing.Normalize(result.LyricsCharacterSpacingPercent);
        result.LyricsLineGapPercent = LyricsLineGap.Normalize(result.LyricsLineGapPercent);
        result.LyricsFixedWidthDip = LyricsFixedWidth.Normalize(result.LyricsFixedWidthDip);
        result.LyricsSource = result.LyricsSource.Normalize();
        result.TaskbarExperience = result.TaskbarExperience.Normalize();
        result.Interaction = result.Interaction.Normalize();
        result.AudioDevices = result.AudioDevices.Normalize();
        result.TaskbarSurface = result.TaskbarSurface.Normalize();
        result.DynamicIslandSurface = result.DynamicIslandSurface.Normalize();
        result.TrackChangeNotification = result.TrackChangeNotification.Normalize();
        result.SmtcSourceFilter = result.SmtcSourceFilter.Normalize();
        result.MediaSelection = result.MediaSelection.Normalize();
        result.QuickLaunch = result.QuickLaunch.Normalize();
        result.SpectrumComponent = result.SpectrumComponent.Normalize();
        result.BluetoothBatteryWidget = result.BluetoothBatteryWidget.Normalize();
        result.SystemMemoryOptimization = result.SystemMemoryOptimization.Normalize();
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
        result.TaskbarBarCrossAxisOffsetDip = Math.Clamp(result.TaskbarBarCrossAxisOffsetDip, MinimumTaskbarCrossAxisOffsetDip, MaximumTaskbarCrossAxisOffsetDip);
        if (result.DynamicIslandLeft is not null && (!double.IsFinite(result.DynamicIslandLeft.Value) || result.DynamicIslandLeft < 0)) result.DynamicIslandLeft = null;
        if (result.DynamicIslandTop is not null && (!double.IsFinite(result.DynamicIslandTop.Value) || result.DynamicIslandTop < 0)) result.DynamicIslandTop = null;
        if (result.WindowMode == WindowMode.FloatingBar && result.FloatingBar.Profile is null)
            result.FloatingBar = result.FloatingBar with { Profile = BarDisplaySettings.Capture(result) };
        return result;
    }

    public AppSettings Clone() => new()
    {
        Appearance = Appearance,
        Experiments = Experiments with { },
        FloatingBar = FloatingBar.Normalize(),
        TrayWheelBehavior = TrayWheelBehavior,
        LyricsEnabled = LyricsEnabled,
        AllowBrowserAndVideoLyrics = AllowBrowserAndVideoLyrics,
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
        AudioDevices = AudioDevices.Normalize(),
        TaskbarSurface = TaskbarSurface,
        DynamicIslandSurface = DynamicIslandSurface,
        LyricsTextAlignment = LyricsTextAlignment,
        LyricsSyllableHighlightEnabled = LyricsSyllableHighlightEnabled,
        LyricsUnsungOpacityPercent = LyricsUnsungOpacityPercent,
        LyricsCharacterSpacingPercent = LyricsCharacterSpacingPercent,
        LyricsLineGapPercent = LyricsLineGapPercent,
        LyricsFixedWidthEnabled = LyricsFixedWidthEnabled,
        LyricsFixedWidthDip = LyricsFixedWidthDip,
        LyricsLongLineScrollEnabled = LyricsLongLineScrollEnabled,
        LyricsInfoLineFilterEnabled = LyricsInfoLineFilterEnabled,
        LyricsFuriganaFilterEnabled = LyricsFuriganaFilterEnabled,
        LyricsBracketSymbolsHidden = LyricsBracketSymbolsHidden,
        LyricsChineseConversion = LyricsChineseConversion,
        LyricsSource = LyricsSource,
        LyricsArtistSeparators = LyricsArtistSeparators,
        TrackChangeNotification = TrackChangeNotification,
        SmtcSourceFilter = SmtcSourceFilter,
        MediaSelection = MediaSelection,
        QuickLaunch = QuickLaunch,
        SpectrumComponent = SpectrumComponent,
        BluetoothBatteryWidget = BluetoothBatteryWidget,
        SystemMemoryOptimization = SystemMemoryOptimization,
        PerformanceComponent = PerformanceComponent,
        Update = Update,
        DeveloperModeEnabled = DeveloperModeEnabled,
        LaunchAtStartup = LaunchAtStartup,
        InterfaceLanguage = InterfaceLanguage,
        TranslucentTbCompatibilityPromptShown = TranslucentTbCompatibilityPromptShown
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
