using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Settings;

namespace AFMediaBar.Classes.Services.Lyrics;

/// <summary>
/// 灵动岛卡片逐字擦亮的一帧决策：时间轴是否推进、擦亮层是否显示、裁剪宽度是多少。
/// One frame's decision for the capsule island card's syllable highlight: whether the timeline advances, whether the layer shows, and
/// how wide the clip is.
///
/// 决策本身是 <see cref="LyricHighlightPolicy"/> 的既有语义，这里只把"卡片上这一帧写什么"收成一处：岛的帧循环直接读它，
/// 于是推进条件既能被 MSTest 钉住，又不必在窗口里复制一份。时间轴与擦亮层分开决定——用户关掉开关或系统进入高对比度时
/// 只隐藏擦亮层，滚动轨迹不变。
/// The decision itself is <see cref="LyricHighlightPolicy"/>'s existing semantics; this only gathers "what this frame writes on the
/// card" into one place so the island's frame loop can read it. The advance conditions are therefore pinned by MSTest instead of being
/// copied into the window. Timeline and layer are decided separately: the user's switch and high contrast hide only the layer, leaving
/// the scrolling trajectory untouched.
/// </summary>
public static class CardLyricHighlightPolicy
{
    /// <summary>裁剪宽度小于该值时不显示擦亮层，避免行首出现一条几乎没有宽度的杂线。
    /// 与任务栏 <c>TaskBarMediaControl.Lyrics.cs</c> 的 <c>LyricHighlightMinimumWidth</c> 是同一个值；两处受各自文件边界限制
    /// 无法共享同一常量，因此**改动时必须同时改两处**。
    /// Below this clip width the reveal layer stays hidden rather than leaving a hair-width line at the head of the row.
    /// It is the same value as <c>LyricHighlightMinimumWidth</c> in <c>TaskBarMediaControl.Lyrics.cs</c>; the two cannot share one constant
    /// because of their file boundaries, so **both have to change together**.
    /// </summary>
    public const double MinimumClipWidth = 0.5;

    /// <summary>
    /// 一帧的擦亮状态。
    /// One frame's highlight state.
    /// </summary>
    /// <param name="AdvanceTimeline">时间轴是否继续推进；false 表示整条时间轴停止、调用方应还原并摘掉帧回调。/ Whether the timeline keeps advancing; false means it stops and the caller restores the appearance and detaches the frame callback.</param>
    /// <param name="ShowHighlight">是否显示擦亮层。/ Whether the reveal layer shows.</param>
    /// <param name="ClipWidth">高亮层的裁剪宽度（DIP）。/ Clip width of the reveal layer in DIP.</param>
    public readonly record struct FrameState(bool AdvanceTimeline, bool ShowHighlight, double ClipWidth);

    /// <summary>
    /// 解析卡片这一帧的擦亮状态。
    /// Resolves the card's highlight state for one frame.
    /// </summary>
    /// <param name="settings">应用设置（擦亮开关）。/ Application settings, for the highlight switch.</param>
    /// <param name="line">当前行；无当前行（落回作者）时为 null。/ The active line, null when there is none and the row fell back to the artist.</param>
    /// <param name="positionSeconds">当前播放位置（秒）。/ Current playback position in seconds.</param>
    /// <param name="connected">媒体会话是否已连接。/ Whether a media session is connected.</param>
    /// <param name="playing">是否正在播放。/ Whether playback is running.</param>
    /// <param name="lineVisible">卡片歌词行是否可见（卡片已展开）。/ Whether the card's lyric row is visible, meaning the card is expanded.</param>
    /// <param name="controlVisible">承载歌词的窗口/控件是否可见（<c>Window.IsVisible</c>）。/ Whether the window carrying the lyrics is visible (<c>Window.IsVisible</c>).</param>
    /// <param name="useContinuousMotion">系统是否允许连续动效。/ Whether the desktop allows continuous motion.</param>
    /// <param name="measuredTextWidth">当前行文本的实测宽度（DIP）；未测得时传 0。/ Measured width of the active line in DIP, or 0 while unmeasured.</param>
    /// <param name="highContrast">系统是否处于高对比度。/ Whether the system is in high contrast.</param>
    public static FrameState Resolve(
        AppSettings settings,
        LyricLine? line,
        double positionSeconds,
        bool connected,
        bool playing,
        bool lineVisible,
        bool controlVisible,
        bool useContinuousMotion,
        double measuredTextWidth,
        bool highContrast)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var presentation = LyricHighlightPolicy.ResolvePresentationState(
            highlightEnabled: settings.LyricsSyllableHighlightEnabled,
            hasCurrentLine: line is not null,
            connected: connected,
            playing: playing,
            lyricsVisible: lineVisible,
            // 裁定 10 要求传真实的控制可见性：用 <c>Window.IsVisible</c> 而不是常量，语义才与任务栏一致。
            // 注意它在当前实现下并**不是**多余的：卡片收拢时歌词行已由 lineVisible 挡住，但窗口真的被隐藏（IsVisible=false）时
            // 这一项也是唯一的拦截者，写死 true 会让时间轴在看不见的窗口上继续跑。
            // Ruling 10 asks for the real control visibility: <c>Window.IsVisible</c> rather than a constant keeps the semantics identical to
            // the taskbar's. It is deliberately **not** redundant here: a collapsed card is already covered by lineVisible, but a window that
            // is genuinely hidden (IsVisible false) is caught by this term alone, and hard-coding true would keep the timeline running on a
            // window nobody can see.
            controlVisible: controlVisible,
            // 岛的卡片没有"后台剪枝"这一态：卡片收拢时歌词行本身就不可见，已经由 lineVisible 覆盖。
            // The island's card has no background-pruned state: a collapsed card already makes the lyric row invisible, which
            // lineVisible covers.
            isAdvancePruned: false,
            highContrast: highContrast,
            useContinuousMotion: useContinuousMotion);

        if (!presentation.AdvanceTimeline || line is null)
        {
            return new FrameState(false, false, 0);
        }

        var progress = LyricHighlightPolicy.ResolveProgress(line, positionSeconds);
        if (progress is null)
        {
            return new FrameState(false, false, 0);
        }

        var clipWidth = LyricHighlightPolicy.ResolveClipWidth(progress.Value, measuredTextWidth);
        return new FrameState(
            true,
            presentation.ShowHighlight && clipWidth >= MinimumClipWidth,
            clipWidth);
    }
}
