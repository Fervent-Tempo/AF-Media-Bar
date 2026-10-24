// 按功能归组的设置契约；保持既有类型全名、枚举值及默认语义。
using AFMediaBar.Classes.Models;
using System.IO;

namespace AFMediaBar.Classes.Settings;


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

/// <summary>频谱采集的音频范围；程序范围不依赖输出设备。</summary>
public enum SpectrumCaptureSource
{
    /// <summary>采集后台选定的当前输出端点混音。</summary>
    CurrentDevice = 0,
    /// <summary>跨输出端点采集所有程序，排除 AF 及其子进程。</summary>
    AllApplications = 1,
    /// <summary>仅采集媒体栏当前显示的程序树；无法识别时不采集。</summary>
    CurrentMediaApplication = 2
}

/// <summary>
/// 频谱采集设备的来源策略；只在采集范围为「当前设备」时生效，与采集范围回答的是两个问题：
/// 范围决定采什么（整机混音还是某个进程树），策略决定从哪个端点采。
/// Which endpoint the spectrum is captured from. It only applies while the capture scope is the current device:
/// the scope answers what is captured (the whole endpoint mix or one process tree), the strategy answers where.
/// </summary>
public enum SpectrumCaptureDeviceMode
{
    /// <summary>跟随 AF 当前媒体来源使用的输出设备；无法识别时回退到系统默认设备。</summary>
    FollowMediaSource = 0,

    /// <summary>始终采集系统默认输出设备，不随媒体来源变化。</summary>
    SystemDefault = 1,

    /// <summary>固定采集 <see cref="SpectrumComponentSettings.CaptureDeviceId"/> 指定的设备。</summary>
    SpecificDevice = 2
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

    /// <summary>缺少字段的旧设置继续采集当前设备；程序模式无法解析时不扩大采集范围。</summary>
    public SpectrumCaptureSource CaptureSource { get; init; } = SpectrumCaptureSource.CurrentDevice;

    /// <summary>设备模式手动指定的输出端点；null 跟随 AF 当前媒体来源使用的设备。</summary>
    public string? CaptureDeviceId { get; init; }

    /// <summary>
    /// 采集设备策略；null 表示这份配置没有表达过该选择（升级前写下的设置文件正是如此）。
    /// 该成员以 init 属性存在：设置文件缺这个字段时反序列化落到 null，再由 <see cref="ResolveCaptureDeviceMode"/>
    /// 按 <see cref="CaptureDeviceId"/> 是否为空回退到升级前的判据，因此新增它不会改变任何既有配置选到的设备。
    /// Device strategy. Null means the configuration never expressed a choice, which is exactly what a settings
    /// file written before this field existed looks like. Missing the field deserializes to null, and
    /// <see cref="ResolveCaptureDeviceMode"/> then falls back to the pre-upgrade rule driven by whether
    /// <see cref="CaptureDeviceId"/> is set, so adding this member cannot change which device an existing
    /// configuration selects.
    /// </summary>
    public SpectrumCaptureDeviceMode? CaptureDeviceMode { get; init; }

    /// <summary>手动设备离线后仍能显示其最后已知名称。</summary>
    public string CaptureDeviceName { get; init; } = string.Empty;

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
        CaptureSource = Enum.IsDefined(CaptureSource) ? CaptureSource : SpectrumCaptureSource.CurrentDevice,
        CaptureDeviceMode = CaptureDeviceMode is { } mode && Enum.IsDefined(mode) ? mode : null,
        CaptureDeviceId = string.IsNullOrWhiteSpace(CaptureDeviceId) ? null : CaptureDeviceId.Trim(),
        CaptureDeviceName = CaptureDeviceName ?? string.Empty,
        ContentHeightDip = SnapContentHeightDip(ContentHeightDip)
    };

    /// <summary>
    /// 把持久化的策略解析成生效模式，四种情况在这里显式分流，供采集器与设置页共用同一判据：
    /// null 是旧配置缺字段，按 <see cref="CaptureDeviceId"/> 是否为空回退到升级前的行为
    ///（为空跟随媒体来源，否则使用已保存的设备）；其余三种按配置字面执行。
    /// 不能写成 mode ?? FollowMediaSource——那会把旧配置里已经保存的设备 ID 静默丢掉。
    /// Resolves the persisted strategy into the mode that takes effect, branching all four cases explicitly so
    /// the capture service and the settings page share one rule: null is a configuration that predates the field
    /// and falls back to the pre-upgrade behaviour driven by whether <see cref="CaptureDeviceId"/> is set (empty
    /// follows the media source, otherwise the saved device is used); the other three run as written.
    /// It must not be folded into mode ?? FollowMediaSource, which would silently drop a device id an existing
    /// configuration already stores.
    /// </summary>
    public SpectrumCaptureDeviceMode ResolveCaptureDeviceMode()
    {
        if (CaptureDeviceMode is { } mode) return mode;
        return string.IsNullOrWhiteSpace(CaptureDeviceId)
            ? SpectrumCaptureDeviceMode.FollowMediaSource
            : SpectrumCaptureDeviceMode.SpecificDevice;
    }

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
