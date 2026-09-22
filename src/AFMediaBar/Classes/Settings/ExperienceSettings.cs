using AFMediaBar.Classes.Models;
using System.IO;

namespace AFMediaBar.Classes.Settings;

/// <summary>播放器表面滚轮可绑定的媒体或音频动作。 / Media or audio action bindable to player-surface wheel input.</summary>
public enum WheelAction
{
    PreviousNext = 0,
    CurrentApplicationVolume = 1,
    OutputDevice = 2,
    SwitchMediaSource = 3,

    /// <summary>不绑定：滚轮在媒体栏上不做任何事（提示仍会说明"已禁用"）。成员值参与序列化，因此只能追加。 / Not bound: the wheel does nothing over the media bar, while the tooltip still states that it is disabled. Member values take part in serialization, so this may only be appended.</summary>
    Disabled = 4
}

/// <summary>单击通知区域图标时执行的动作。 / Action performed when the notification-area icon is clicked.</summary>
public enum TrayClickAction
{
    None = 0,
    OpenSettings = 1,
    OpenAudioControl = 2,
    OpenContextMenu = 3,

    /// <summary>打开输出设备菜单。它与音频控制浮窗是两条不同路径：前者只换设备，后者带音量与设备列表。 / Opens the output-device menu. It is a different path from the audio flyout: this one only switches devices, the flyout also carries volume.</summary>
    OpenOutputDeviceMenu = 4,

    /// <summary>打开当前应用音量菜单。 / Opens the current application's volume menu.</summary>
    OpenCurrentAppVolumeMenu = 5
}

/// <summary>任务栏静置内容的点击结果。 / Result of clicking taskbar rest-layer content.</summary>
public enum PlayerClickAction
{
    TogglePlayPause = 0,
    ActivateSource = 1,

    /// <summary>打开完整层。 / Opens the full layer.</summary>
    OpenFullPanel = 2,

    /// <summary>不绑定：点击该区域不做任何事。成员值参与序列化，因此只能追加。 / Not bound: clicking that region does nothing. Member values take part in serialization, so this may only be appended.</summary>
    Disabled = 3
}

/// <summary>普通滚轮映射切换到组合映射时使用的共享修饰键。 / Shared modifier that switches plain wheel input to its chord mapping.</summary>
public enum InteractionModifier
{
    Shift = 0,
    LeftMouseButton = 1,
    RightMouseButton = 2
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

/// <summary>任务栏标题和歌手文字的对齐方式。 / Alignment of taskbar title and artist text.</summary>
public enum TaskbarMediaTextAlignment
{
    Left = 0,
    Center = 1,
    Right = 2
}

/// <summary>任务栏媒体条主轴长度的决定方式。 / How the taskbar media bar resolves its primary-axis length.</summary>
public enum TaskbarLengthMode
{
    FollowContent = 0,
    Fixed = 1
}

/// <summary>
/// 横向任务栏静置层里的一个组件。成员值参与序列化，因此只能追加。
/// One component of the horizontal taskbar rest layer. Member values take part in serialization, so this may only be appended.
/// </summary>
public enum TaskbarRestComponent
{
    /// <summary>封面；没有媒体时是音符（无媒体时点它或在其上滚轮可挑播放器）。 / Artwork; a music note without media (clicking it or wheeling over it picks a player).</summary>
    Artwork = 0,

    /// <summary>媒体文字：标题与歌手，或歌词两行。 / Media text: the title and artist, or the two lyric rows.</summary>
    MediaText = 1,

    /// <summary>播放态频谱。 / The playing spectrum.</summary>
    Spectrum = 2,

    /// <summary>性能指标。 / The performance metrics.</summary>
    Performance = 3,

    /// <summary>输出设备按钮：点击打开设备菜单，滚轮切换设备。 / Output-device button: clicking opens the device menu, the wheel switches devices.</summary>
    OutputDevice = 4,

    /// <summary>音量按钮：点击打开音量菜单，滚轮调节当前媒体来源的音量。 / Volume button: clicking opens the volume menu, the wheel adjusts the current source's volume.</summary>
    Volume = 5
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

/// <summary>SMTC 应用来源允许列表。 / Allow-list for SMTC application sources.</summary>
public readonly record struct SmtcSourceFilterSettings(bool Enabled, IReadOnlyList<string>? AllowedSourceIds)
{
    public static SmtcSourceFilterSettings Default { get; } = new(false, []);

    public SmtcSourceFilterSettings Normalize() => this with
    {
        AllowedSourceIds = (AllowedSourceIds ?? [])
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray()
    };
}

/// <summary>快速启动列表设置。 / Quick-launch list settings.</summary>
public readonly record struct QuickLaunchSettings(IReadOnlyList<QuickLaunchEntry>? Entries)
{
    public static QuickLaunchSettings Default { get; } = new([]);

    public QuickLaunchSettings Normalize()
    {
        var entries = new List<QuickLaunchEntry>();
        var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in Entries ?? [])
        {
            if (string.IsNullOrWhiteSpace(entry.Target) || !Enum.IsDefined(entry.Kind))
                continue;
            var target = entry.Target.Trim();
            if (!targets.Add($"{entry.Kind}:{target}"))
                continue;
            var id = string.IsNullOrWhiteSpace(entry.Id) ? Guid.NewGuid().ToString("N") : entry.Id.Trim();
            var displayName = string.IsNullOrWhiteSpace(entry.DisplayName)
                ? Path.GetFileNameWithoutExtension(target)
                : entry.DisplayName.Trim();
            entries.Add(entry with
            {
                Id = id,
                DisplayName = displayName,
                Target = target,
                SourceId = string.IsNullOrWhiteSpace(entry.SourceId) ? null : entry.SourceId.Trim()
            });
        }
        return new QuickLaunchSettings(entries);
    }
}

/// <summary>任务栏静置层频谱的呈现样式。 / Presentation style of the taskbar rest-layer spectrum.</summary>
public enum SpectrumStyle
{
    /// <summary>贴底的柱状图，柱高随音量增长。 / Bottom-anchored bars whose height follows the level.</summary>
    Bars = 0,

    /// <summary>围绕垂直中线上下延伸的连续波形。 / Continuous waveform extending above and below the vertical centre.</summary>
    Waveform = 1,

    /// <summary>由离散方块组成的像素柱状图。 / Pixel column chart built from discrete blocks.</summary>
    PixelBars = 2,

    /// <summary>从垂直中线向上下同时延伸的对称柱状图。 / Symmetric bars growing both up and down from the vertical centre.</summary>
    MirroredBars = 3
}

/// <summary>任务栏频谱组件设置。 / Taskbar spectrum component settings.</summary>
public readonly record struct SpectrumComponentSettings(int BandCount, int RefreshRateHz, int SensitivityPercent)
{
    /// <summary>
    /// 频谱呈现样式。该成员以 init 属性而不是位置参数存在：设置文件缺这个字段时反序列化落到声明处的默认值（柱状图），
    /// 既不需要改动既有的构造签名，也不会让"没写过这项"被读成别的样式。
    /// The presentation style. It is an init property rather than a positional parameter, so a settings file that lacks the field
    /// deserializes to the default declared here (bars): the existing constructor signature stays intact, and "never written" is
    /// never read as some other style.
    /// </summary>
    public SpectrumStyle Style { get; init; } = SpectrumStyle.Bars;

    /// <summary>
    /// 柱数下限。频谱组件宽度随柱数增长，柱宽保持固定，因此柱数不能再降到 9 以下：
    /// 更少的柱子会把频谱压缩成一小撮，与「柱数决定宽度」的尺寸关系自相矛盾。
    /// Lower bound of the bar count. The component width grows with the bar count while each bar keeps a fixed width, so
    /// the count cannot drop below nine: fewer bars would collapse the spectrum into a stub and contradict the very size
    /// relationship the count is supposed to express.
    /// </summary>
    public const int MinimumBandCount = 9;

    /// <summary>柱数上限；受默认 FFT 分辨率与任务栏可用长度限制。 / Upper bound, limited by FFT resolution and available taskbar length.</summary>
    public const int MaximumBandCount = 24;

    /// <summary>柱数默认值。 / Default bar count.</summary>
    public const int DefaultBandCount = 9;

    /// <summary>刷新率下限（Hz）。 / Lower refresh-rate bound in hertz.</summary>
    public const int MinimumRefreshRateHz = 5;

    /// <summary>刷新率上限（Hz）。 / Upper refresh-rate bound in hertz.</summary>
    public const int MaximumRefreshRateHz = 30;

    /// <summary>灵敏度下限（百分比）。静态增益低于 10% 时频谱几乎不动，因此下限取 10。 / Lower sensitivity bound in percent. A static gain below 10% leaves the spectrum almost still, so the floor is ten.</summary>
    public const int MinimumSensitivityPercent = 10;

    /// <summary>灵敏度上限（百分比）。 / Upper sensitivity bound in percent.</summary>
    public const int MaximumSensitivityPercent = 400;

    /// <summary>
    /// 频谱内容区的横轴尺寸（横向任务栏就是高度，DIP）。频谱只出现在横向任务栏，因此这个值就是柱子的最大高度；
    /// 它同时决定悬停表面要留出多少空间（表面在内容四周各留 1 DIP）。
    /// Cross-axis extent of the spectrum content area, which is the height on a horizontal taskbar, in DIP. The spectrum only appears on
    /// horizontal taskbars, so this is the tallest a bar can be, and it also decides how much room the hover surface has to leave (the
    /// surface keeps one DIP of padding around the content).
    ///
    /// 该成员以 init 属性存在，旧设置文件缺少该字段时反序列化到声明处的默认值，不需要改动既有构造签名。
    /// The member is an init property, so an older settings file that lacks the field deserializes to the declared default and the existing
    /// constructor signature stays intact.
    /// </summary>
    public double ContentHeightDip { get; init; } = DefaultContentHeightDip;

    /// <summary>频谱内容区横轴尺寸的下限（DIP）；再矮就只剩几个像素方块，读不出高低。 / Lower bound of the spectrum's cross-axis size in DIP; anything shorter leaves a couple of pixel blocks with no readable level.</summary>
    public const double MinimumContentHeightDip = 14;

    /// <summary>频谱内容区横轴尺寸的上限（DIP）；再高就会顶到悬停表面与媒体栏的内边界。 / Upper bound in DIP; anything taller would run into the hover surface and the bar's inner edge.</summary>
    public const double MaximumContentHeightDip = 34;

    /// <summary>频谱内容区横轴尺寸的滑杆步进（DIP）。 / Slider step of the spectrum's cross-axis size in DIP.</summary>
    public const double ContentHeightStepDip = 2;

    /// <summary>频谱内容区横轴尺寸的默认值（DIP）：比升级前的固定 21 略高，柱子更容易读出高低。 / Default cross-axis size in DIP: a little taller than the fixed 21 used before, which makes the levels easier to read.</summary>
    public const double DefaultContentHeightDip = 26;

    /// <summary>
    /// 把任意输入吸附到步长网格并夹进区间。
    /// Snaps any input onto the step grid and clamps it into range.
    /// </summary>
    /// <param name="dip">原始尺寸（DIP）。/ Raw size in DIP.</param>
    public static double SnapContentHeightDip(double dip)
    {
        if (!double.IsFinite(dip))
        {
            return DefaultContentHeightDip;
        }

        var snapped = Math.Round(dip / ContentHeightStepDip, MidpointRounding.AwayFromZero) * ContentHeightStepDip;
        return Math.Clamp(snapped, MinimumContentHeightDip, MaximumContentHeightDip);
    }

    /// <summary>
    /// 灵敏度滑杆的步进（百分比）。1–400 之间用 1 步进会给出四百个位置，而听感上的差别远达不到这个分辨率。
    /// Step of the sensitivity slider in percent. Stepping by one across 1–400 would give four hundred positions while the
    /// audible difference is nowhere near that resolution.
    /// </summary>
    public const int SensitivityStepPercent = 10;

    public static SpectrumComponentSettings Default { get; } = new(DefaultBandCount, 20, 100);

    public SpectrumComponentSettings Normalize() => new(
        Math.Clamp(BandCount, MinimumBandCount, MaximumBandCount),
        Math.Clamp(RefreshRateHz, MinimumRefreshRateHz, MaximumRefreshRateHz),
        SnapSensitivityPercent(SensitivityPercent))
    {
        Style = Enum.IsDefined(Style) ? Style : SpectrumStyle.Bars,
        ContentHeightDip = SnapContentHeightDip(ContentHeightDip)
    };

    /// <summary>
    /// 把灵敏度吸附到步长网格并夹取。旧设置文件里的 1–9 会被抬到下限，非整十的取值落到最近的整十值上，
    /// 否则界面会显示一个滑杆位置无法表达的读数。
    /// Snaps sensitivity onto the step grid and clamps it. Values of 1–9 in an older file are lifted to the floor and off-grid
    /// values land on the nearest ten, so the interface never shows a reading its own slider position cannot express.
    /// </summary>
    /// <param name="sensitivityPercent">待换算的灵敏度（百分比）。/ Sensitivity to normalize, in percent.</param>
    public static int SnapSensitivityPercent(int sensitivityPercent)
    {
        var snapped = (int)Math.Round(sensitivityPercent / (double)SensitivityStepPercent, MidpointRounding.AwayFromZero)
                      * SensitivityStepPercent;
        return Math.Clamp(snapped, MinimumSensitivityPercent, MaximumSensitivityPercent);
    }
}

/// <summary>任务栏性能组件设置。 / Taskbar performance component settings.</summary>
public readonly record struct PerformanceComponentSettings(
    IReadOnlyList<MetricKind>? Metrics,
    int RefreshIntervalMilliseconds,
    bool OpenTaskManagerOnClick)
{
    /// <summary>
    /// 采样间隔下限（毫秒）。旧范围（250–60000 毫秒）里真正可用的部分只占一小段，滑杆其余行程全是没人会选的取值，
    /// 因此收敛到 0.5–5 秒。
    /// Lower sampling-interval bound in milliseconds. In the previous 250–60000 ms range only a small slice was usable and
    /// the rest of the slider travel held values nobody would pick, so the range is narrowed to 0.5–5 seconds.
    /// </summary>
    public const int MinimumRefreshIntervalMilliseconds = 500;

    /// <summary>采样间隔上限（5 秒）；再慢就只剩一个偶尔跳动的数字。 / Upper sampling-interval bound (5 seconds); anything slower is a number that rarely moves.</summary>
    public const int MaximumRefreshIntervalMilliseconds = 5000;

    /// <summary>采样间隔的默认值（2.5 秒）。 / Default sampling interval (2.5 seconds).</summary>
    public const int DefaultRefreshIntervalMilliseconds = 2500;

    /// <summary>
    /// 采样间隔的步长（0.5 秒）。界面的滑杆按该步长吸附，因此写入设置的值也必须落到步长网格上，
    /// 否则界面会显示一个滑杆位置无法表达的读数。
    /// Step of the sampling interval (0.5 seconds). The slider snaps to this step, so the stored value must land on the same
    /// grid; otherwise the interface shows a reading its own slider position cannot express.
    /// </summary>
    public const int RefreshIntervalStepMilliseconds = 500;

    /// <summary>
    /// 性能组件的默认设置。点击打开任务管理器默认为开启：该开关此前一直存在，但点击被任务栏拖动逻辑吞掉，
    /// 因此没有任何用户能在它关闭的状态下做出有效选择。 / Defaults for the performance component. Opening Task Manager
    /// on click is on by default: the switch existed before but the click was swallowed by the taskbar drag logic, so no
    /// user could have made a meaningful choice while it was off.
    /// </summary>
    public static PerformanceComponentSettings Default { get; } = new([MetricKind.SystemMemory], DefaultRefreshIntervalMilliseconds, true);

    public PerformanceComponentSettings Normalize()
    {
        var metrics = (Metrics ?? [])
            .Where(Enum.IsDefined)
            .Distinct()
            .OrderBy(metric => metric)
            .ToArray();
        if (metrics.Length == 0)
            metrics = [MetricKind.SystemMemory];
        return new PerformanceComponentSettings(
            metrics,
            SnapRefreshIntervalMilliseconds(RefreshIntervalMilliseconds),
            OpenTaskManagerOnClick);
    }

    /// <summary>把采样间隔吸附到界面步长网格并夹取到安全区间。 / Snaps the sampling interval onto the interface step grid and clamps it to the safe range.</summary>
    /// <param name="milliseconds">待换算的采样间隔（毫秒）。/ Sampling interval to normalize, in milliseconds.</param>
    public static int SnapRefreshIntervalMilliseconds(int milliseconds)
    {
        var snapped = (int)Math.Round(milliseconds / (double)RefreshIntervalStepMilliseconds, MidpointRounding.AwayFromZero)
                      * RefreshIntervalStepMilliseconds;
        return Math.Clamp(snapped, MinimumRefreshIntervalMilliseconds, MaximumRefreshIntervalMilliseconds);
    }
}

/// <summary>曲目切换通知在目标工作区中的位置。 / Position of the track-change notification in the target work area.</summary>
public enum TrackChangeNotificationPosition
{
    BottomLeft = 0,
    TopLeft = 1,
    TopCenter = 2,
    TopRight = 3,
    BottomCenter = 4,
    BottomRight = 5
}

/// <summary>曲目切换通知选择显示器的方式。 / Method used to select the display for track-change notifications.</summary>
public enum NotificationTargetMode
{
    Fixed = 0,
    ForegroundWindow = 1
}

/// <summary>曲目切换通知的用户设置。 / User settings for track-change notifications.</summary>
public readonly record struct TrackChangeNotificationSettings(
    bool Enabled,
    bool ShowWhenFullscreen,
    int DurationMilliseconds,
    TrackChangeNotificationPosition Position,
    NotificationTargetMode TargetMode,
    string? FixedMonitorDeviceId)
{
    /// <summary>通知的默认设置。 / Default notification settings.</summary>
    public static TrackChangeNotificationSettings Default { get; } = new(
        false,
        false,
        1000,
        TrackChangeNotificationPosition.BottomLeft,
        NotificationTargetMode.Fixed,
        null);

    /// <summary>归一化枚举、时长和设备标识。 / Normalizes enums, duration, and the device identifier.</summary>
    public TrackChangeNotificationSettings Normalize()
    {
        var defaults = Default;
        return this with
        {
            DurationMilliseconds = Math.Clamp(DurationMilliseconds, 1000, 10000),
            Position = Enum.IsDefined(Position) ? Position : defaults.Position,
            TargetMode = Enum.IsDefined(TargetMode) ? TargetMode : defaults.TargetMode,
            FixedMonitorDeviceId = string.IsNullOrWhiteSpace(FixedMonitorDeviceId)
                ? null
                : FixedMonitorDeviceId.Trim()
        };
    }
}

/// <summary>任务栏完整层的功能组显隐设置。 / Visibility settings for taskbar full-panel feature groups.</summary>
public readonly record struct TaskbarFullPanelSettings(
    bool MediaInfoVisible,
    bool MediaControlsVisible,
    bool AudioControlsVisible,
    bool PerformanceVisible)
{
    /// <summary>仅显示媒体信息和媒体控制。 / Shows only media information and media controls.</summary>
    public static TaskbarFullPanelSettings Compact { get; } = new(true, true, false, false);

    /// <summary>显示所有功能组。 / Shows every feature group.</summary>
    public static TaskbarFullPanelSettings Full { get; } = new(true, true, true, true);

    /// <summary>完整层的默认设置。 / Default settings for the full panel.</summary>
    public static TaskbarFullPanelSettings Default => Full;

    /// <summary>确保至少保留一个功能组。 / Ensures at least one feature group remains visible.</summary>
    public TaskbarFullPanelSettings Normalize() =>
        MediaInfoVisible || MediaControlsVisible || AudioControlsVisible || PerformanceVisible
            ? this
            : Compact;
}

/// <summary>任务栏悬停层中各项控制的显隐设置。 / Visibility settings for controls in the taskbar hover layer.</summary>
public readonly record struct TaskbarHoverControlsSettings(
    bool PlayPauseVisible,
    bool PreviousNextVisible,
    bool OutputDeviceVisible,
    bool AudioControlVisible,
    bool ProgressVisible)
{
    /// <summary>手势优先的默认悬停控制组合。 / Default gesture-first hover-control combination.</summary>
    public static TaskbarHoverControlsSettings Default { get; } = new(false, false, true, true, true);
}

/// <summary>任务栏三层体验设置。 / Settings for the three-layer taskbar experience.</summary>
public readonly record struct TaskbarExperienceSettings(
    bool HoverLayerEnabled,
    bool FullLayerEnabled,
    TaskbarInformationDensity Density,
    TaskbarContentLayout ContentLayout,
    TaskbarFullPanelSettings FullPanel,
    TaskbarLengthMode LengthMode,
    double FixedLengthDip)
{
    /// <summary>任务栏组件之间的实际间距（DIP）。/ Actual gap between taskbar components in DIP.</summary>
    public double ComponentSpacingDip { get; init; } = 12;

    /// <summary>标题和歌手文字的对齐方式。 / Alignment of title and artist text.</summary>
    public TaskbarMediaTextAlignment MediaTextAlignment { get; init; } = TaskbarMediaTextAlignment.Left;

    /// <summary>静置层是否显示播放态频谱。 / Whether the rest layer shows the playing spectrum.</summary>
    public bool SpectrumVisible { get; init; } = true;

    /// <summary>静置层是否显示性能组件。 / Whether the rest layer shows the performance component.</summary>
    public bool PerformanceVisible { get; init; } = true;

    /// <summary>
    /// 静置层是否显示底部的播放进度条。关闭后只在媒体报告了时长时消失的那条进度不再绘制，
    /// 悬停层与完整层的进度不受影响。
    /// Whether the rest layer shows its bottom playback-progress bar. Turning it off only removes that bar, which otherwise
    /// appears whenever the session reports a duration; the hover and full layers keep their own progress.
    /// </summary>
    public bool RestProgressVisible { get; init; } = true;

    /// <summary>
    /// 静置层与悬停层是否提供进入完整层的入口。关闭后静置层文字区顶部那条细杠不再绘制、也不再可点，
    /// 悬停层的完整层按钮同时隐藏；完整层本身与其它入口（托盘、菜单）不受影响。
    /// Whether the rest and hover layers offer an entry into the full layer. Turning it off stops drawing and hit-testing the thin
    /// bar above the rest-layer text and hides the hover layer's full-layer button; the full layer itself and other entries
    /// (tray, menus) stay as they are.
    /// </summary>
    public bool FullPanelEntryVisible { get; init; } = true;

    /// <summary>悬停层中各项控制的显隐设置。 / Visibility settings for individual hover-layer controls.</summary>
    public TaskbarHoverControlsSettings HoverControls { get; init; } = TaskbarHoverControlsSettings.Default;

    /// <summary>
    /// 静置层是否显示输出设备按钮。默认关闭：它与悬停层里的同名按钮是同一件事的两种入口，
    /// 常驻一个按钮会占掉媒体文字的位置，因此由用户显式打开。
    /// Whether the rest layer shows the output-device button. Off by default: it is one of two entries for the same thing as the
    /// hover-layer button, and a permanently visible button takes room from the media text, so the user turns it on explicitly.
    /// </summary>
    public bool OutputDeviceVisible { get; init; }

    /// <summary>静置层是否显示音量按钮。默认关闭，理由同 <see cref="OutputDeviceVisible"/>。/ Whether the rest layer shows the volume button. Off by default, for the same reason as <see cref="OutputDeviceVisible"/>.</summary>
    public bool VolumeVisible { get; init; }

    /// <summary>
    /// 静置层里可排序的那一段组件的顺序；<see langword="null"/> 表示从未配置（用默认顺序）。
    /// Order of the reorderable part of the rest layer; <see langword="null"/> means never configured (the default order applies).
    ///
    /// 封面与媒体文字不在这个列表里：它们的位置固定在整条媒体栏的最前面（<c>TaskbarRestLayoutPolicy.FixedOrder</c>），
    /// 用户排不了它们，设置里因此也不存它们的顺序。
    /// The artwork and the media text are not part of this list: they are pinned to the front of the bar
    /// (<c>TaskbarRestLayoutPolicy.FixedOrder</c>), the user cannot move them, and their order is therefore not stored either.
    ///
    /// 列表里没有出现的已知组件由 <c>TaskbarRestLayoutPolicy.ResolveOrder</c> 按默认顺序补在后面，因此旧设置文件不会
    /// 因为新版本多了一个组件而把它丢掉；用户给定的相对顺序 MUST NOT 被重排。
    /// A known component missing from the list is appended in the default order by <c>TaskbarRestLayoutPolicy.ResolveOrder</c>, so an older
    /// settings file never loses a component a newer version added; the relative order the user gave MUST NOT be rearranged.
    /// </summary>
    public IReadOnlyList<TaskbarRestComponent>? RestComponentOrder { get; init; }

    /// <summary>
    /// 没有媒体（没有 SMTC 来源）时仍然留在任务栏上的静置层组件。这个行为没有开关：没有媒体时媒体栏本来就没有内容可显示，
    /// 保留哪几个组件是这一份列表唯一的答案。
    /// The rest-layer components that stay on the taskbar while there is no media (no SMTC source). There is no switch for this behaviour:
    /// without media the bar has nothing to show anyway, and which components remain is the only question this list answers.
    ///
    /// 三种取值 MUST 可区分：<see langword="null"/> = 从未配置（用 <c>TaskbarRestLayoutPolicy.DefaultIdleComponents</c>，
    /// 即快速启动小音符）；空列表 = 用户关掉了全部（整条媒体栏隐藏）；非空列表 = 只保留列出的组件。
    /// The three values MUST stay distinguishable: null means never configured (the default is
    /// <c>TaskbarRestLayoutPolicy.DefaultIdleComponents</c>, the quick-launch note), an empty list means the user turned everything off (which
    /// hides the whole bar), and a non-empty list keeps exactly those components.
    /// </summary>
    public IReadOnlyList<TaskbarRestComponent>? IdleComponents { get; init; }

    /// <summary>
    /// 静置层媒体文字（标题、歌手、歌词）的字号缩放百分比。
    /// Font-size scale percentage for rest-layer media text: title, artist, and lyrics.
    /// </summary>
    public int MediaFontSizePercent { get; init; } = 100;

    /// <summary>组件间距的持久化安全下限。/ Persistence-safe lower bound for component spacing.</summary>
    public const double MinimumComponentSpacingDip = 4;

    /// <summary>组件间距的持久化安全上限。/ Persistence-safe upper bound for component spacing.</summary>
    public const double MaximumComponentSpacingDip = 32;

    /// <summary>静置层媒体文字字号缩放的持久化安全下限。/ Persistence-safe lower bound for the rest-layer media font-size scale.</summary>
    public const int MinimumMediaFontSizePercent = 80;

    /// <summary>
    /// 静置层媒体文字字号缩放的持久化安全上限。任务栏高度固定，标题与歌手两行必须容纳在该高度内，
    /// 因此上限保持在两行仍能完整显示的范围内。
    /// Persistence-safe upper bound for the rest-layer media font-size scale. The taskbar height is fixed and the title and
    /// artist must both fit inside it, so the upper bound keeps two lines fully visible.
    /// </summary>
    public const int MaximumMediaFontSizePercent = 125;

    /// <summary>固定长度设置的持久化安全下限。 / Persistence-safe lower bound for the fixed-length setting.</summary>
    public const double MinimumStoredFixedLengthDip = 120;

    /// <summary>固定长度设置的持久化安全上限；运行时仍按任务栏可用区间夹取。 / Persistence-safe upper bound; runtime still clamps to the taskbar's available range.</summary>
    public const double MaximumStoredFixedLengthDip = 4096;

    public static TaskbarExperienceSettings Default { get; } = new(
        true,
        true,
        TaskbarInformationDensity.Balanced,
        TaskbarContentLayout.AdaptiveStack,
        TaskbarFullPanelSettings.Default,
        TaskbarLengthMode.FollowContent,
        360);

    public TaskbarExperienceSettings Normalize()
    {
        var defaults = Default;
        return this with
        {
            Density = Enum.IsDefined(Density) ? Density : defaults.Density,
            ContentLayout = Enum.IsDefined(ContentLayout) ? ContentLayout : defaults.ContentLayout,
            MediaTextAlignment = Enum.IsDefined(MediaTextAlignment) ? MediaTextAlignment : defaults.MediaTextAlignment,
            FullPanel = FullPanel.Normalize(),
            LengthMode = Enum.IsDefined(LengthMode) ? LengthMode : defaults.LengthMode,
            FixedLengthDip = double.IsFinite(FixedLengthDip) && FixedLengthDip >= MinimumStoredFixedLengthDip
                ? Math.Clamp(FixedLengthDip, MinimumStoredFixedLengthDip, MaximumStoredFixedLengthDip)
                : defaults.FixedLengthDip,
            ComponentSpacingDip = double.IsFinite(ComponentSpacingDip)
                ? Math.Clamp(ComponentSpacingDip, MinimumComponentSpacingDip, MaximumComponentSpacingDip)
                : defaults.ComponentSpacingDip,
            // schema 7 及更早的设置文件没有该字段，反序列化得到 0；0 与任何合法值都不同，因此回退到默认值。
            // Settings files up to schema 7 lack this field and deserialize it as 0; 0 is outside every legal value, so it
            // falls back to the default instead of being clamped to the minimum.
            MediaFontSizePercent = MediaFontSizePercent <= 0
                ? defaults.MediaFontSizePercent
                : Math.Clamp(MediaFontSizePercent, MinimumMediaFontSizePercent, MaximumMediaFontSizePercent),
            RestComponentOrder = NormalizeComponentList(RestComponentOrder),
            IdleComponents = NormalizeComponentList(IdleComponents)
        };
    }

    /// <summary>
    /// 归一化一个组件列表：丢掉未定义的成员与重复项，其余顺序原样保留；<see langword="null"/> 保持为 <see langword="null"/>
    /// （"从未配置"因此不会被写成空列表）。
    /// Normalizes a component list: undefined members and duplicates are dropped while the remaining order is kept exactly as given;
    /// <see langword="null"/> stays <see langword="null"/>, so "never configured" is never rewritten as an empty list.
    /// </summary>
    private static IReadOnlyList<TaskbarRestComponent>? NormalizeComponentList(IReadOnlyList<TaskbarRestComponent>? components)
    {
        if (components is null)
        {
            return null;
        }

        if (components.Count == 0)
        {
            return [];
        }

        var result = new List<TaskbarRestComponent>(components.Count);
        foreach (var component in components)
        {
            if (Enum.IsDefined(component) && !result.Contains(component))
            {
                result.Add(component);
            }
        }

        return result;
    }
}

/// <summary>任务栏播放器和托盘图标共用的点击与滚轮绑定。 / Click and wheel bindings shared by the taskbar player and tray icon.</summary>
public readonly record struct GlobalInteractionSettings(
    PlayerClickAction ArtworkClickAction,
    PlayerClickAction TextClickAction,
    WheelAction PrimaryWheelAction,
    InteractionModifier Modifier,
    WheelAction ChordWheelAction,
    TrayClickAction TrayClickAction,
    TrayWheelBehavior TrayPrimaryWheelAction,
    TrayWheelBehavior TrayChordWheelAction)
{
    public static GlobalInteractionSettings Default { get; } = new(
        PlayerClickAction.TogglePlayPause,
        PlayerClickAction.ActivateSource,
        WheelAction.PreviousNext,
        InteractionModifier.Shift,
        WheelAction.SwitchMediaSource,
        TrayClickAction.OpenAudioControl,
        TrayWheelBehavior.SwitchOutputDevice,
        TrayWheelBehavior.AdjustVolume);

    public GlobalInteractionSettings Normalize()
    {
        var defaults = Default;
        return this with
        {
            ArtworkClickAction = Enum.IsDefined(ArtworkClickAction) ? ArtworkClickAction : defaults.ArtworkClickAction,
            TextClickAction = Enum.IsDefined(TextClickAction) ? TextClickAction : defaults.TextClickAction,
            PrimaryWheelAction = Enum.IsDefined(PrimaryWheelAction) ? PrimaryWheelAction : defaults.PrimaryWheelAction,
            Modifier = Enum.IsDefined(Modifier) ? Modifier : defaults.Modifier,
            ChordWheelAction = Enum.IsDefined(ChordWheelAction) ? ChordWheelAction : defaults.ChordWheelAction,
            TrayClickAction = TrayClickAction is TrayClickAction.OpenAudioControl
                or TrayClickAction.OpenSettings
                or TrayClickAction.OpenContextMenu
                or TrayClickAction.OpenOutputDeviceMenu
                or TrayClickAction.OpenCurrentAppVolumeMenu
                ? TrayClickAction
                : defaults.TrayClickAction,
            TrayPrimaryWheelAction = NormalizeTrayWheelAction(TrayPrimaryWheelAction, defaults.TrayPrimaryWheelAction),
            TrayChordWheelAction = NormalizeTrayWheelAction(TrayChordWheelAction, defaults.TrayChordWheelAction)
        };
    }

    private static TrayWheelBehavior NormalizeTrayWheelAction(TrayWheelBehavior action, TrayWheelBehavior fallback) =>
        action is TrayWheelBehavior.AdjustVolume or TrayWheelBehavior.SwitchOutputDevice ? action : fallback;
}

/// <summary>一个显示模式的基础表面外观。 / Basic surface appearance for one display mode.</summary>
public readonly record struct ModeSurfaceSettings(
    PlayerSurfaceStyle Style,
    int BackgroundOpacityPercent,
    double CornerRadiusDip)
{
    /// <summary>
    /// 默认圆角 24 DIP：与 iOS 卡片的圆角基准（<c>CapsuleIslandMetrics.CardCornerRadius</c>）一致，
    /// 老配置里写死的 6 会把卡片压成一个小圆角，读起来不像灵动岛。
    /// Default corner radius of 24 DIP, matching the iOS card baseline (<c>CapsuleIslandMetrics.CardCornerRadius</c>);
    /// a hard-coded 6 in an older configuration squashes the card into a small radius that no longer reads as an island.
    /// </summary>
    public static ModeSurfaceSettings Default { get; } = new(PlayerSurfaceStyle.Automatic, 100, 24);

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
