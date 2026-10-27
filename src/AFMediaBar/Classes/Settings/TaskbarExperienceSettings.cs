// 按功能归组的设置契约；保持既有类型全名、枚举值及默认语义。
using AFMediaBar.Classes.Models;
using System.IO;

namespace AFMediaBar.Classes.Settings;

/// <summary>外侧完整面板入口的打开方式；数值参与设置序列化。</summary>
public enum FullPanelHandleTrigger { Click = 0, Hover = 1 }


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

/// <summary>横向任务栏的内容排列方向；属于开发中新增设置。</summary>
public enum TaskbarContentArrangement
{
    Left = 0,
    Right = 1
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
    Volume = 5,
    InputDevice = 6,
    /// <summary>所选蓝牙设备的电量组件。</summary>
    BluetoothBattery = 7
}

/// <summary>播放器表面的基础背景方案。 / Basic background style for a player surface.</summary>
public enum PlayerSurfaceStyle
{
    Automatic = 0,
    Solid = 1,
    ThemeTint = 2
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
    // 保留已发布字段的读取，悬停层进度已移除，不再参与呈现或宽度计算。
    bool ProgressVisible)
{
    /// <summary>手势优先的默认悬停控制组合。 / Default gesture-first hover-control combination.</summary>
    public bool InputDeviceVisible { get; init; }

    public static TaskbarHoverControlsSettings Default { get; } = new(false, false, true, true, true);
}

/// <summary>
/// 悬停封面时的放大方式：<see cref="Off"/> 保持栏内紧凑 1.1 倍放大；<see cref="Zoom"/> 把封面组件本身原地放大
/// （底部锚定、只向上生长，超出任务栏的部分经透明 Popup 呈现）；<see cref="Preview"/> 用独立的大图预览卡片
/// 突出在任务栏上方。
/// How the artwork behaves on hover: <see cref="Off"/> keeps the compact 1.1× zoom inside the bar; <see cref="Zoom"/>
/// enlarges the artwork component itself in place (anchored at its bottom, growing only upward, with the part beyond the
/// taskbar rendered through a transparent Popup); <see cref="Preview"/> pops a separate large-preview card above the taskbar.
/// </summary>
public enum ArtworkHoverMode
{
    /// <summary>栏内紧凑放大 1.1 倍（默认）。/ Compact 1.1× zoom inside the bar (default).</summary>
    Off = 0,

    /// <summary>原地放大：封面组件底部锚定、向上生长 <c>ArtworkHoverZoomScaleFactor</c>（2×），且内容侧同步推开让位（方向随展开排布解析）。/ In-place zoom: the artwork grows to <c>ArtworkHoverZoomScaleFactor</c> (2×) upward from its bottom, with the content side pushed aside to make room (the direction follows the resolved expansion arrangement).</summary>
    Zoom = 1,

    /// <summary>大图预览：独立卡片突出任务栏上方。/ Large preview: a separate card popping above the taskbar.</summary>
    Preview = 2,

    /// <summary>悬停不放大，封面保持原样。/ No zoom on hover; the artwork stays as it is.</summary>
    None = 3
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

    /// <summary>悬停层按钮之间的间距（DIP）。/ Gap between hover-layer buttons in DIP.</summary>
    public double HoverButtonSpacingDip { get; init; } = 2;

    /// <summary>标题和歌手文字的对齐方式。 / Alignment of title and artist text.</summary>
    public TaskbarMediaTextAlignment MediaTextAlignment { get; init; } = TaskbarMediaTextAlignment.Center;

    /// <summary>用户手动选择的内容排布；null 表示跟随任务栏对齐。</summary>
    public TaskbarContentArrangement? Arrangement { get; init; }

    /// <summary>静置层是否显示播放态频谱。 / Whether the rest layer shows the playing spectrum.</summary>
    public bool SpectrumVisible { get; init; } = true;

    /// <summary>有媒体时静置层是否显示封面；不影响无媒体时的小音符。/ Whether the rest layer shows artwork while media is connected; the idle note has its own setting.</summary>
    public bool ArtworkVisible { get => _artworkVisible ?? true; init => _artworkVisible = value; }

    private readonly bool? _artworkVisible;

    /// <summary>
    /// 静置层封面缩到原槽位的八成并居中，默认关闭；只在显示封面时生效。
    /// Shrinks the rest-layer artwork to 80% of its slot and centers it, off by default; applies only while the artwork is shown.
    /// </summary>
    public bool SmallArtwork { get => _smallArtwork ?? false; init => _smallArtwork = value; }

    private readonly bool? _smallArtwork;

    /// <summary>静置层是否显示性能组件。 / Whether the rest layer shows the performance component.</summary>
    public bool PerformanceVisible { get; init; } = true;

    /// <summary>
    /// 静置层是否显示底部的播放进度条，默认关闭。关闭后只在媒体报告了时长时消失的那条进度不再绘制，
    /// 封面边缘与完整层的进度不受影响。
    /// Whether the rest layer shows its bottom playback-progress bar. Off by default. Turning it off only removes that bar, which otherwise
    /// appears whenever the session reports a duration; the artwork edge and full panel keep their own progress.
    /// </summary>
    public bool RestProgressVisible { get; init; } = false;

    /// <summary>在封面边缘显示播放进度；与文字下方的静置进度独立。</summary>
    public bool ArtworkProgressVisible { get => _artworkProgressVisible ?? true; init => _artworkProgressVisible = value; }

    private readonly bool? _artworkProgressVisible;

    /// <summary>是否显示外侧横杆；不限制封面、文字等交互打开完整面板。</summary>
    public bool FullPanelEntryVisible { get; init; } = true;

    /// <summary>外侧横杆的打开方式，默认点击。</summary>
    public FullPanelHandleTrigger FullPanelEntryTrigger { get; init; } = FullPanelHandleTrigger.Click;

    /// <summary>外侧入口持续悬停后打开的等待时间，单位为毫秒。</summary>
    public int FullPanelEntryHoverDelayMilliseconds { get; init; } = DefaultFullPanelEntryHoverDelayMilliseconds;

    public const int DefaultFullPanelEntryHoverDelayMilliseconds = 500;
    public const int MinimumFullPanelEntryHoverDelayMilliseconds = 100;
    public const int MaximumFullPanelEntryHoverDelayMilliseconds = 1000;
    public const int FullPanelEntryHoverDelayStepMilliseconds = 100;

    /// <summary>设置编辑器使用秒显示，范围和步长从毫秒契约派生。</summary>
    public static double MinimumFullPanelEntryHoverDelaySeconds => MinimumFullPanelEntryHoverDelayMilliseconds / 1000d;
    public static double MaximumFullPanelEntryHoverDelaySeconds => MaximumFullPanelEntryHoverDelayMilliseconds / 1000d;
    public static double FullPanelEntryHoverDelayStepSeconds => FullPanelEntryHoverDelayStepMilliseconds / 1000d;

    /// <summary>悬停层中各项控制的显隐设置。 / Visibility settings for individual hover-layer controls.</summary>
    public TaskbarHoverControlsSettings HoverControls { get; init; } = TaskbarHoverControlsSettings.Default;

    /// <summary>
    /// 静置层是否显示输出设备按钮。默认关闭：它与悬停层里的同名按钮是同一件事的两种入口，
    /// 常驻一个按钮会占掉媒体文字的位置，因此由用户显式打开。
    /// Whether the rest layer shows the output-device button. Off by default: it is one of two entries for the same thing as the
    /// hover-layer button, and a permanently visible button takes room from the media text, so the user turns it on explicitly.
    /// </summary>
    public bool OutputDeviceVisible { get; init; }
    public bool InputDeviceVisible { get; init; }

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

    /// <summary>悬停层按钮间距的安全上限。/ Safe upper bound for the hover-button gap.</summary>
    public const double MaximumHoverButtonSpacingDip = 16;

    /// <summary>静置层媒体文字字号缩放的持久化安全下限。/ Persistence-safe lower bound for the rest-layer media font-size scale.</summary>
    public const int MinimumMediaFontSizePercent = 80;

    /// <summary>
    /// 静置层媒体文字字号缩放的持久化安全上限。任务栏高度固定，标题与歌手两行必须容纳在该高度内，
    /// 因此上限保持在两行仍能完整显示的范围内。
    /// Persistence-safe upper bound for the rest-layer media font-size scale. The taskbar height is fixed and the title and
    /// artist must both fit inside it, so the upper bound keeps two lines fully visible.
    /// </summary>
    public const int MaximumMediaFontSizePercent = 125;

    /// <summary>Existing media-font size precision in percent, shared by slider and direct input.</summary>
    public const int MediaFontSizeStepPercent = 5;

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
            // 已发布的旧总开关只折合为入口可见性，完整面板本身始终可通过交互打开。
            FullPanelEntryVisible = FullLayerEnabled && FullPanelEntryVisible,
            FullLayerEnabled = true,
            FullPanelEntryTrigger = Enum.IsDefined(FullPanelEntryTrigger) ? FullPanelEntryTrigger : FullPanelHandleTrigger.Click,
            FullPanelEntryHoverDelayMilliseconds = NormalizeFullPanelEntryHoverDelay(FullPanelEntryHoverDelayMilliseconds),
            Density = Enum.IsDefined(Density) ? Density : defaults.Density,
            ContentLayout = Enum.IsDefined(ContentLayout) ? ContentLayout : defaults.ContentLayout,
            MediaTextAlignment = Enum.IsDefined(MediaTextAlignment) ? MediaTextAlignment : defaults.MediaTextAlignment,
            Arrangement = Arrangement is { } arrangement && Enum.IsDefined(arrangement) ? arrangement : null,
            FullPanel = FullPanel.Normalize(),
            LengthMode = Enum.IsDefined(LengthMode) ? LengthMode : defaults.LengthMode,
            FixedLengthDip = double.IsFinite(FixedLengthDip) && FixedLengthDip >= MinimumStoredFixedLengthDip
                ? Math.Clamp(Math.Round(FixedLengthDip, MidpointRounding.AwayFromZero), MinimumStoredFixedLengthDip, MaximumStoredFixedLengthDip)
                : defaults.FixedLengthDip,
            ComponentSpacingDip = double.IsFinite(ComponentSpacingDip)
                ? Math.Clamp(ComponentSpacingDip, MinimumComponentSpacingDip, MaximumComponentSpacingDip)
                : defaults.ComponentSpacingDip,
            HoverButtonSpacingDip = double.IsFinite(HoverButtonSpacingDip)
                ? Math.Clamp(HoverButtonSpacingDip, 0, MaximumHoverButtonSpacingDip)
                : defaults.HoverButtonSpacingDip,
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

    private static int NormalizeFullPanelEntryHoverDelay(int milliseconds)
    {
        // 旧 JSON 的结构体反序列化可能跳过属性初始值，以 0 表示缺字段。
        if (milliseconds == 0) return DefaultFullPanelEntryHoverDelayMilliseconds;
        return (int)Math.Round(Math.Clamp(milliseconds, MinimumFullPanelEntryHoverDelayMilliseconds, MaximumFullPanelEntryHoverDelayMilliseconds) /
            (double)FullPanelEntryHoverDelayStepMilliseconds, MidpointRounding.AwayFromZero) * FullPanelEntryHoverDelayStepMilliseconds;
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
