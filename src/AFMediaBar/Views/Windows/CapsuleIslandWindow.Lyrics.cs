using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Services.Appearance;
using AFMediaBar.Classes.Services.Layout;
using AFMediaBar.Classes.Services.Lyrics;
using AFMediaBar.Classes.Settings;

namespace AFMediaBar.Views.Windows;

/// <summary>
/// 胶囊岛窗口的歌词部分：卡片态显示当前行，第二行按设置取译文/音译/下一句，逐字擦亮在这一行上叠一层高亮。
/// Lyric half of the capsule-island window: the card shows the active line, a second row takes the translation, romanization, or
/// next line per the settings, and syllable highlighting lays a reveal layer over that first row.
///
/// 取行一律交给 <see cref="LyricLinePresenter"/>（与任务栏同一份取词语义），两行的显隐交给
/// <see cref="CardLyricPresentationPolicy"/>，擦亮交给 <see cref="CardLyricHighlightPolicy"/>，本文件只负责把结果写进控件。
/// 推进**不外起帧源**：<c>CapsuleIslandWindow.Animation.cs</c> 的合成帧回调每帧调用
/// <see cref="AdvanceLyricHighlightFrame"/>，与形态动画、跑马灯共用同一条 <c>CompositionTarget.Rendering</c>。
/// Line selection always goes through <see cref="LyricLinePresenter"/> (the same retrieval semantics the taskbar uses), the two rows'
/// visibility through <see cref="CardLyricPresentationPolicy"/>, and the reveal through <see cref="CardLyricHighlightPolicy"/>; this
/// file only writes the results into the controls. Advancement **never starts a second frame source**: the compositor callback in
/// <c>CapsuleIslandWindow.Animation.cs</c> calls <see cref="AdvanceLyricHighlightFrame"/> once per frame, sharing the one
/// <c>CompositionTarget.Rendering</c> the shape animation and the marquee already use.
/// </summary>
public partial class CapsuleIslandWindow
{
    /// <summary>按播放位置选行、并产出当前行/下一句/译文/音译的取词器（与任务栏同一实现）。/ Line selector producing the active line, next line, translation, and romanization, the same implementation the taskbar uses.</summary>
    private readonly LyricLinePresenter _cardLyricPresenter = new();

    /// <summary>最近一次取词结果：逐字擦亮需要它带着的当前行对象（含音节时间轴）。/ The latest selection, which the reveal needs for its active line object (and its syllable timeline).</summary>
    private LyricLineUpdate _cardLyricUpdate = new(string.Empty, string.Empty, string.Empty, string.Empty, false, null);

    /// <summary>上一次写入的两行呈现；只有它变了才去碰控件，逐帧推进因此不产生多余的布局工作。/ The last written two-row presentation; the controls are only touched when it changes, so per-frame advancement costs no extra layout work.</summary>
    private CardLyricPresentationPolicy.PresentationState _cardLyricPresentation;

    /// <summary>高亮层当前是否由擦亮驱动（底色层的不透明度只在进入/离开擦亮时改写一次）。/ Whether the reveal currently drives the layers, so the base layer's opacity is written once on entering and leaving instead of per frame.</summary>
    private bool _isCardLyricHighlightActive;

    /// <summary>实测的当前行文本宽度（DIP）与它对应的文本/字号：裁剪宽度按字形宽度换算，只有在文本或字号真的变了时才重量一次。
    /// The measured width of the active line in DIP together with the text and font size it belongs to: the clip width converts from
    /// glyph width, so a re-measure is only needed when the text or the font size really changed.</summary>
    private double _cardLyricTextWidth;
    private string _measuredCardLyricText = string.Empty;
    private double _measuredCardLyricFontSize = double.NaN;

    /// <summary>
    /// 底色层在 XAML 里写的不透明度（<c>CapsuleIslandWindow.xaml</c> 的 <c>CardLyric.Opacity="0.85"</c>）。
    /// **这个常量必须与 XAML 的那个值一致**：擦亮结束后要把底色层还原到设计值，写死 1 会让底色层在播放过一次之后永久变亮。
    /// The opacity the base layer declares in XAML (<c>CardLyric.Opacity="0.85"</c> in <c>CapsuleIslandWindow.xaml</c>).
    /// **This constant has to match that XAML value**: the reveal restores the base layer to its design value on the way out, and hard-coding 1
    /// would leave the base layer permanently brighter once playback has run once.
    /// </summary>
    private const double CardLyricBaseOpacity = 0.85;

    /// <summary>胶囊歌词底色层在 XAML 里写的不透明度（<c>CapsuleLyricText.Opacity="0.85"</c>），语义与 <see cref="CardLyricBaseOpacity"/> 相同。
    /// The capsule lyric base layer's XAML opacity (<c>CapsuleLyricText.Opacity="0.85"</c>), with the same meaning as <see cref="CardLyricBaseOpacity"/>.</summary>
    private const double CapsuleLyricBaseOpacity = 0.85;

    /// <summary>
    /// 胶囊文字槽当前由谁管：歌名（旋转式跑马灯）还是当前歌词行（跟随式擦亮滚动）。
    ///
    /// 这是一个**显式状态**，绝不靠读 <c>CapsuleTitle.Text</c> 反推：两个槽都会往同一格写文本，靠文本猜"现在是谁在管"会让歌词窗口
    /// 被当成歌名去量宽、再被整体位移顶掉（接缝 S3/S4）。切换模式时必须把另一方的窗口/位移/裁剪全部复位，见 <see cref="ResetCapsuleSlot"/>。
    /// Which slot currently owns the capsule's text cell: the title (a rotating marquee) or the active lyric line (a follow reveal scroll).
    ///
    /// This is an **explicit state**, never inferred back out of <c>CapsuleTitle.Text</c>: both slots write text into the same cell, and guessing
    /// "who owns it now" from the string makes a lyric window get measured as if it were a title and then pushed out by the whole-track
    /// translate (seams S3/S4). Switching modes resets the other side's window, offset, and clip, see <see cref="ResetCapsuleSlot"/>.
    /// </summary>
    private CapsuleSlotMode _capsuleSlotMode = CapsuleSlotMode.Title;

    /// <summary>歌词槽当前那一行的**完整**文本（前缀宽度表与窗口都相对它算）。/ The lyric slot's current line in **full**, which the prefix-width table and the window are both relative to.</summary>
    private string _capsuleLyricBase = string.Empty;

    /// <summary>歌词槽的前缀宽度表（按需增长，换行时整表作废）。/ The lyric slot's prefix-width table, grown on demand and discarded whole when the line changes.</summary>
    private CapsuleLyricFollowPolicy.PrefixWidthTable _capsuleLyricWidths = CapsuleLyricFollowPolicy.EmptyTable;

    /// <summary>歌词槽上一帧呈现的已唱宽度（DIP）：跟随式只前进，这个值就是它的记忆。/ The revealed width the lyric slot presented last frame in DIP: the follow mode only moves forward, and this is its memory.</summary>
    private double _capsuleLyricRevealWidth;

    /// <summary>歌词槽的擦亮层当前是否由擦亮驱动（离开擦亮时只还原一次）。/ Whether the lyric slot's reveal layer is currently driven, so leaving the reveal restores once instead of per frame.</summary>
    private bool _isCapsuleLyricHighlightActive;

    /// <summary>量宽委托，缓存一次即可：逐帧新建闭包是白付的分配。/ The width delegate, cached once: building a closure per frame is a needless allocation.</summary>
    private Func<string, double>? _capsuleLyricWidthMeasure;

    /// <summary>量宽函数：与卡片同一个 <see cref="FormattedText"/> 口径，量的是**歌词底色层**的字形设置（两层同字号同字体，量哪一层都一样）。
    /// The width function: the same <see cref="FormattedText"/> basis the card uses, measured with the **lyric base layer**'s glyph settings (both
    /// layers share one font size and family, so either gives the same answer).</summary>
    private Func<string, double> CapsuleLyricWidthMeasure =>
        _capsuleLyricWidthMeasure ??= text => MeasureCardLyricTextWidth(text, CapsuleLyricText);

    /// <summary>
    /// 歌词前缀宽度表的**字形键**：字号、字重、字体族——任务栏的前缀宽度表纳入的正是这三项，岛这边也要纳入，
    /// 否则 <c>ApplyFontScales</c> 在 DPI/显示器变化时改了字号之后，同一张表里会混进两套度量（字形宽度、位移与裁剪边界全部错位），
    /// 直到换行才恢复。
    /// The lyric prefix-width table's **glyph key**: font size, weight, and family — the same three the taskbar's table keys on, and the island needs
    /// them too, or ApplyFontScales rewriting the font size on a DPI or monitor change would mix two sets of advances into one table (glyph widths,
    /// offsets, and clip edges all misaligned) until the next line change.
    /// </summary>
    private string CapsuleLyricMeasureKey =>
        $"{CapsuleLyricText.FontSize:0.##}|{CapsuleLyricText.FontWeight}|{CapsuleLyricText.FontFamily?.Source}";

    /// <summary>
    /// 刷新卡片歌词区：取词 → 两行呈现 → 逐字擦亮 → 胶囊文字槽。
    /// Refreshes the card's lyric area: select the line, resolve the two rows, then the reveal and the capsule text slot.
    /// </summary>
    /// <param name="positionSeconds">当前播放位置（秒）/ Current playback position in seconds.</param>
    /// <returns>卡片的歌词时间轴是否仍需逐帧推进（由 <see cref="RefreshCardLyricHighlight"/> 给出，调用方据此决定帧回调的去留）。
    /// Whether the card's lyric timeline still needs a frame per step, from <see cref="RefreshCardLyricHighlight"/>, which the caller uses to keep
    /// or detach the frame callback.</returns>
    private bool UpdateLyricLine(double positionSeconds)
    {
        if (_isClosing)
            return false;

        var lyrics = _snapshot.IsConnected ? _snapshot.Lyrics : null;
        _cardLyricUpdate = _cardLyricPresenter.Update(lyrics, positionSeconds);

        var state = CardLyricPresentationPolicy.Resolve(
            SettingsManager.Current,
            _cardLyricUpdate,
            _snapshot.Artist);

        // 只在呈现真的变化时写控件：这个方法是每帧调一次的，而擦亮每帧都要读同一份状态。
        // The controls are written only when the presentation really changes: this runs on every frame, while the reveal reads the same
        // state on every frame.
        if (!_cardLyricPresentation.Equals(state))
        {
            _cardLyricPresentation = state;
            // 高亮层的文本由 XAML 绑定跟随底色层（Text="{Binding Text, ElementName=CardLyric}"），因此这里只写一份文本：
            // 两层的文本永不可能漂移，也不需要在这里记着同步第二处。
            // The reveal layer's text follows the base layer through the XAML binding (Text="{Binding Text, ElementName=CardLyric}"), so the
            // text is written in exactly one place: the two layers can never drift apart and there is no second assignment to remember here.
            CardLyric.Text = state.FirstLine.Text;
            CardLyricSecondary.Text = state.SecondaryText;
            // 第二行占自己那一行（Grid.Row=2，Auto），隐藏时该行收成 0，seek 行与传输控制都不受影响。
            // The second row owns its own Auto row (Grid.Row 2), so hiding it collapses that row alone and neither the seek row nor the
            // transport controls move.
            CardLyricSecondary.Visibility = state.ShowSecondaryLine ? Visibility.Visible : Visibility.Collapsed;
        }

        // 胶囊文字槽跟着同一次取词走：模式（歌名/歌词）与选行也必须在暂停时成立，因此它挂在**取词**这一步上，
        // 而不是只挂在帧循环的擦亮那一支上。
        // The capsule text slot follows the same selection: its mode (title or lyric) and the chosen line have to hold while paused too, so it
        // hangs off the **selection** step rather than only off the reveal branch of the frame loop.
        RefreshCapsuleSlot();
        return RefreshCardLyricHighlight(positionSeconds);
    }

    /// <summary>
    /// 按当前状态刷新擦亮：时间轴不推进时还原外观，推进时确保帧回调存在并写入这一帧的裁剪。
    /// Refreshes the reveal for the current state: a stopped timeline restores the appearance, while an advancing one makes sure the frame
    /// callback exists and writes this frame's clip.
    ///
    /// **不变量：只要歌词时间轴需要推进，帧回调就必须挂着；它不能依赖形变或跑马灯来顺手挂上。**
    /// 判定"要推进"的条件与判定"要挂接"的条件因此是同一个（<see cref="CardLyricHighlightPolicy.FrameState.AdvanceTimeline"/>），
    /// 于是"卡片已展开时从暂停切回播放"这条既没有形变、标题也通常不溢出的路径同样能把帧循环挂上。
    /// **Invariant: while the lyric timeline needs advancing the frame callback must be attached, and it must never depend on a morph or the
    /// marquee attaching it as a side effect.** The condition that decides "advance" and the one that decides "attach" are therefore the same
    /// one, so the path that has neither a morph nor an overflowing title — resuming playback while the card is already expanded — attaches
    /// the loop just the same.
    /// </summary>
    /// <param name="positionSeconds">本帧的播放位置（由调用方取一次并复用，一帧只解算一次）。/ This frame's playback position, taken once by the caller and reused, so a frame resolves it once.</param>
    /// <returns>卡片的歌词时间轴是否仍需逐帧推进。/ Whether the card's lyric timeline still needs a frame per step.</returns>
    private bool RefreshCardLyricHighlight(double positionSeconds)
    {
        var frame = ResolveCardLyricHighlightFrame(positionSeconds);
        if (!frame.AdvanceTimeline)
        {
            RestoreCardLyricHighlight();
            return false;
        }

        // 与媒体状态变化那条入口（ApplySnapshot）同址：需要推进就一定挂上，不再依赖别处的副作用。
        // The same invariant ApplySnapshot enforces on every media-state change: needing to advance is enough to attach, with no reliance on
        // a side effect somewhere else.
        EnsureFrameLoop();

        // 时间轴在推进 ⇒ 底色层按设置压暗，擦亮层只裁到"已唱宽度"。
        // **不要**在这里按 ShowHighlight 分支去还原底色层：高亮层尚未开始时（已唱宽度 < 0.5 DIP）还原会让每次换行闪一帧亮，
        // 与任务栏 <c>TaskBarMediaControl.Lyrics.cs</c> 的语义一致——那时只隐藏高亮层，底色层保持压暗。
        // While the timeline advances the base layer is dimmed to the configured value and the reveal layer is only clipped to the sung width.
        // **Do not** branch on ShowHighlight here to restore the base layer: before the reveal has started (clip width under 0.5 DIP) that would
        // flash the base layer bright on every line change. The taskbar's own semantics (<c>TaskBarMediaControl.Lyrics.cs</c>) are the same:
        // with nothing revealed it hides only the reveal layer and leaves the base dimmed.
        CardLyric.Opacity = SettingsManager.Current.LyricsUnsungOpacityPercent / 100d;
        WriteCardLyricHighlightFrame(frame);
        return true;
    }

    /// <summary>
    /// 当前播放位置（秒）：与任务栏同款的本地插值（播放中按快照时间戳外推）；没有连接时按 0。
    /// The current playback position in seconds, using the same local interpolation the taskbar does (extrapolated from the snapshot's
    /// timestamp while playing), and zero while nothing is connected.
    /// </summary>
    private double CurrentPlaybackPosition => _snapshot.IsConnected
        ? TaskbarExperiencePolicy.GetPosition(_snapshot, DateTimeOffset.UtcNow)
        : 0;

    /// <summary>关闭擦亮并还原外观：底色层回到 XAML 的设计值、高亮层收起、裁剪清零。/ Turns the reveal off and restores the appearance: the base layer returns to its XAML design value, the reveal layer collapses, and the clip resets.</summary>
    private void RestoreCardLyricHighlight()
    {
        if (!_isCardLyricHighlightActive)
            return;

        _isCardLyricHighlightActive = false;
        CardLyric.Opacity = CardLyricBaseOpacity;
        CardLyricHighlight.Visibility = Visibility.Collapsed;
        CardLyricHighlightClip.Rect = new Rect(0, 0, 0, 0);
    }

    /// <summary>
    /// 隐藏高亮层并清掉裁剪，但**不动底色层的不透明度**：换行那一帧要立即收起上一行的亮区，而底色层此刻仍处在"已压暗"的状态，
    /// 还原它会闪一帧亮。
    /// Hides the reveal layer and clears the clip without touching the base layer's opacity: the frame a line changes has to drop the previous
    /// line's reveal at once, while the base layer is still dimmed at that moment and restoring it would flash bright for one frame.
    /// </summary>
    private void HideCardLyricHighlightLayer()
    {
        CardLyricHighlight.Visibility = Visibility.Collapsed;
        CardLyricHighlightClip.Rect = new Rect(0, 0, 0, 0);
    }

    /// <summary>
    /// 推进一帧：先按当前位置刷新**选行**，再按同一位置判断是否还要帧回调。
    /// Advances one frame: the line selection refreshes from the current position first, then that same position decides whether the
    /// callback is still needed.
    ///
    /// 选行必须由这里推进：改由帧循环驱动之后，若不在这一帧刷新选行，就没有任何东西会在播放期间更新它，卡片会永远停在窗口加载
    /// 那一刻那一句（原先由 <c>AdvanceProgress</c> 每 500ms 刷新，而逐字擦亮要求比 500ms 细得多，因此两个刷新源合并到帧循环一处）。
    /// <see cref="UpdateLyricLine"/> 内部有呈现去重，未变化时不碰任何控件。
    /// The selection has to advance from here: since the 500 ms progress timer no longer refreshes it, nothing would update the line
    /// while playing and the card would stay on whatever line it showed when the window loaded (that used to be
    /// <c>AdvanceProgress</c>'s job every 500 ms, while the reveal needs a much finer step, so both refresh sources are merged into this
    /// one frame loop). <see cref="UpdateLyricLine"/> deduplicates the presentation and touches no control while it is unchanged.
    ///
    /// 推进**不外起帧源**：本方法由合成帧回调每帧调用一次（与形态动画、跑马灯同一条 <c>CompositionTarget.Rendering</c>）；返回
    /// false 表示不再需要推进，调用方据此摘掉帧回调。帧间隔对擦亮是隐含的——进度直接由当前播放位置算出，播放位置本身就是时间的
    /// 函数，按帧累加反而会与显示刷新率绑定。
    /// Advancement **never starts a second frame source**: the compositor callback calls this once per frame (the one
    /// <c>CompositionTarget.Rendering</c> the shape animation and the marquee share). Answering false means nothing needs advancing any
    /// more and lets the caller detach the callback. The frame interval is implicit for the reveal: its progress comes straight from the
    /// current playback position, which is itself a function of time, so accumulating per frame would tie it to the refresh rate.
    ///
    /// **卡片与胶囊各自判定各自写**（接缝 S2）：两处用的是同一个策略，只是 <c>lineVisible</c> 一个传 <c>_isExpanded</c>、
    /// 一个传 <c>!_isExpanded</c>。早先这里写的是 <c>if (!_isExpanded) return false;</c>，于是胶囊态整条歌词时间轴都不推进，
    /// 编号 105 要求的"胶囊里也擦亮"根本走不到；而直接删掉那个早退又会让收拢的卡片继续跑时间轴，两边的可见性语义就都错了。
    /// **The card and the capsule each decide and write for themselves** (seam S2): both use one policy, with the only difference being
    /// <c>lineVisible</c> — <c>_isExpanded</c> for one and <c>!_isExpanded</c> for the other. This used to read
    /// <c>if (!_isExpanded) return false;</c>, which stopped the whole lyric timeline in the capsule state and made item 105's "the capsule reveals
    /// too" unreachable; deleting that early exit outright would instead let a collapsed card keep running its timeline, which gets both sides'
    /// visibility semantics wrong.
    /// </summary>
    private bool AdvanceLyricHighlightFrame()
    {
        if (_isClosing)
        {
            RestoreCardLyricHighlight();
            RestoreCapsuleLyricHighlight();
            return false;
        }

        // 同一帧只取一次位置，再用同一位置刷新选行与判断擦亮（选行与亮区因此不会因为两次取时间而错开）。
        // The position is taken once per frame and the same value drives both the selection refresh and the reveal decision, so the two cannot
        // drift apart by reading the clock twice.
        var position = CurrentPlaybackPosition;

        // 选行、卡片擦亮与胶囊槽模式都在这一步里完成，返回值是**卡片**时间轴是否仍需推进。
        // The selection, the card's reveal, and the capsule slot's mode all happen inside this call, and what it returns is whether the **card's**
        // timeline still needs a frame per step.
        var cardAdvancing = UpdateLyricLine(position);
        var capsuleAdvancing = AdvanceCapsuleLyricFrame(position);

        // 两边任一要继续就得留着帧回调：用户关掉擦亮或系统进入高对比度时两边都仍返回 true，因为时间轴还要跟随、选行也还要推进
        // （见 <see cref="IsLyricHighlightFrameNeeded"/>）。
        // One of the two still going keeps the callback attached: with the user's reveal switch off or the system in high contrast both still
        // answer true, because the timelines keep following and the selections keep advancing (see <see cref="IsLyricHighlightFrameNeeded"/>).
        return cardAdvancing || capsuleAdvancing;
    }

    #region 胶囊文字槽与跟随擦亮 / Capsule text slot and follow reveal

    /// <summary>
    /// 刷新胶囊文字槽：模式（歌名 / 当前歌词行）与该写的文本。
    ///
    /// "有没有歌词"整条复用卡片那条既有路径（见 <see cref="CapsuleLyricSlotPolicy"/>），本方法只负责把它落到控件上，并在模式真的
    /// 切换时把另一方的窗口/位移/裁剪复位。回落文本来自 <c>_capsuleFallbackTitle</c>——<c>ApplySnapshot</c> 不再直接写
    /// <c>CapsuleTitle.Text</c>，否则歌词态下那一次快照会把跟随窗口整条覆盖成歌名。
    /// Refreshes the capsule text slot: its mode (title or the active lyric line) and the text to write.
    ///
    /// "Are there lyrics" goes entirely through the card's existing path (see <see cref="CapsuleLyricSlotPolicy"/>); this method only lands the
    /// result on the controls and resets the other side's window, offset, and clip when the mode really changed. The fallback text comes from
    /// <c>_capsuleFallbackTitle</c> — ApplySnapshot no longer writes <c>CapsuleTitle.Text</c> directly, because in lyric mode that snapshot write
    /// would overwrite the whole follow window with the song title.
    /// </summary>
    private void RefreshCapsuleSlot()
    {
        if (_isClosing)
            return;

        var state = CapsuleLyricSlotPolicy.Resolve(
            SettingsManager.Current,
            _cardLyricUpdate,
            _capsuleFallbackTitle,
            // 胶囊可见性与胶囊侧时间轴的 lineVisible 是同一个输入：两者若各写一份，收拢后会出现"时间轴没跑、槽却还在歌词模式"
            // 或反过来的错配。
            // The capsule's visibility and the capsule timeline's lineVisible are one input: writing them separately would leave "no timeline
            // running but the slot stuck in lyric mode" after a collapse, or the other way round.
            capsuleVisible: !_isExpanded);

        if (state.Mode != _capsuleSlotMode)
        {
            _capsuleSlotMode = state.Mode;

            // 切换是**单向所有权**的交接点：另一方的窗口、位移与裁剪必须在这里全部归零，绝不能留着上一个模式的残值。
            // The switch is the handover point of **single ownership**: the other side's window, offset, and clip are all zeroed here, and no
            // leftover from the previous mode may survive.
            ResetCapsuleSlot();

            // 切回歌名槽时重新量一次：歌词态下 MeasureMarquee 整段不跑，容器宽与歌名文本都可能是过期的。
            // Re-measure on the way back into the title slot: MeasureMarquee does not run at all in lyric mode, so both the container width and the
            // title text can be stale.
            Dispatcher.BeginInvoke(MeasureMarquee, DispatcherPriority.Loaded);
        }

        if (_capsuleSlotMode == CapsuleSlotMode.Lyric)
        {
            CapsuleTitleTrack.Visibility = Visibility.Collapsed;
            CapsuleLyricHost.Visibility = Visibility.Visible;
            // 交给歌词槽的"这是不是同一行"由取词器给：`Changed` 只在**文档换了或行下标变了**时为真（见 LyricLinePresenter.Update），
            // 因此重复副歌那种"两行文本恰好相同"的换行同样会被认出来。
            // Whether this is still the same line comes from the presenter's own Changed flag, which is true only when the **document changed or the
            // line index moved** (see LyricLinePresenter.Update) — so a repeated chorus whose two lines happen to hold identical text is recognised too.
            SetCapsuleLyricContent(state.Text, _cardLyricUpdate.Changed);
            return;
        }

        CapsuleLyricHost.Visibility = Visibility.Collapsed;
        CapsuleTitleTrack.Visibility = Visibility.Visible;
        if (!string.Equals(CapsuleTitle.Text, state.Text, StringComparison.Ordinal))
            CapsuleTitle.Text = state.Text;
    }

    /// <summary>
    /// 交给歌词槽这一帧的内容：只有**真的换了一行**（文本变了，或取词器说行下标/文档变了）才作废前缀宽度表与亮区宽度。
    ///
    /// **槽不可见不等于这一行结束了。** 岛是"悬停即展开"，用户点封面暂停或指针扫过岛都会让文字槽临时切去歌名槽；如果把那次切换当成
    /// "歌词结束"，亮区宽度就会被清零，恢复播放时 <see cref="MarqueeFollowPolicy.ResolveRevealWidth"/> 只能从 0 按
    /// <c>MaximumRevealAdvanceEm × 字号</c> 一帧一帧追赶——用户看到的就是"暂停后恢复播放，擦亮又从这行的开头跑了一遍"。
    /// 因此这里只作废**真的换了行**那一种情况。
    /// Hands the lyric slot this frame's content: the prefix-width table and the reveal width are only discarded when the line **really changed**
    /// (the text differs, or the presenter says the index or the document moved).
    ///
    /// **An invisible slot does not mean the line ended.** The island expands on hover, so clicking the cover to pause or merely sweeping the pointer
    /// across the island temporarily switches the cell to the title slot; treating that switch as "the lyric is over" would zero the reveal width, and
    /// on resumption <see cref="MarqueeFollowPolicy.ResolveRevealWidth"/> could only catch up from zero at MaximumRevealAdvanceEm times the font size
    /// per frame — which is exactly what the user saw as "after resuming, the reveal runs through the line from the start again".
    /// </summary>
    /// <param name="text">当前歌词行的完整文本。/ The active lyric line in full.</param>
    /// <param name="lineChanged">取词器是否报告这一帧换了行（文档或行下标变了）。/ Whether the presenter reports a line change this frame (the document or the line index moved).</param>
    private void SetCapsuleLyricContent(string text, bool lineChanged)
    {
        text ??= string.Empty;
        if (!lineChanged && string.Equals(_capsuleLyricBase, text, StringComparison.Ordinal))
        {
            // 同一行：表与亮区宽度都是这一行的时间记忆，必须原样留着。控件文本也不动——它在切去歌名槽时并没有被清掉
            // （歌名态整格 Collapsed，本来就看不见），而播放中下一帧就会由 AdvanceCapsuleLyricFrame 覆写成窗口。
            // The same line: the table and the reveal width are this line's memory of time and are kept as they are. The control's text is left alone too
            // — it was never cleared when the cell switched to the title slot (the title slot is collapsed and therefore invisible anyway), and while
            // playing the next frame overwrites it with the window from AdvanceCapsuleLyricFrame.
            return;
        }

        _capsuleLyricBase = text;
        // 交回空表（它的 Content 是空串）而不是"长度相同就沿用"：长度相同不代表同一行。
        // Hand back the empty table (whose Content is the empty string) rather than reusing one of a matching length: a matching length does not
        // mean the same line.
        _capsuleLyricWidths = CapsuleLyricFollowPolicy.EmptyTable;
        _capsuleLyricRevealWidth = 0;

        // 底色层写一次；高亮层的文本由 XAML 绑定跟随（红线 6：成对元素不许两处赋值）。
        // 这一写同时承担"暂停时也要显示当前行"：暂停时帧循环不跑，窗口不会被重算，写的就应该是整行。
        // One write to the base layer; the reveal layer's text follows through the XAML binding (red line 6: paired elements are never assigned in two
        // places). This write also serves "the current line has to show while paused": the frame loop does not run then, no window is computed, and what
        // belongs on screen is the whole line.
        CapsuleLyricText.Text = text;
        CapsuleLyricTransform.X = 0;
        CapsuleLyricHighlightClip.Rect = new Rect(0, 0, 0, 0);
        CapsuleLyricHighlight.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// 模式切换时的复位：**只复位控件外观与歌名槽的状态**，歌词那一行的时间记忆（文本、前缀宽度表、亮区宽度）一律留着。
    ///
    /// 两个槽共用同一格文字，只要任何一方留着残值，下一个模式的画面就会带着上一个模式的位移或裁剪出现（例如切回歌名后整条歌名
    /// 被歌词的位移顶到左边）——因此位移、裁剪、显隐与不透明度必须归零。但"这一行唱到哪了"不是残值，它是**跨槽切换仍然成立的状态**：
    /// 槽只是暂时不可见，行没有结束。清掉它正是用户报障的那条缺陷。
    /// The reset performed on a mode switch: it **only resets the controls' appearance and the title slot's state**, while the lyric line's memory of
    /// time (its text, prefix-width table, and reveal width) is kept.
    ///
    /// The two slots share one text cell, and any leftover on either side makes the next mode's frame appear with the previous mode's offset or clip
    /// — a title pushed left by the lyric slot's translate, for instance — so the offset, the clip, the visibility, and the opacity all have to be
    /// zeroed. But "how far into this line the singing has got" is not a leftover: it stays true across the switch, because the slot is merely
    /// invisible for a moment and the line has not ended. Zeroing it is exactly the defect the user reported.
    /// </summary>
    private void ResetCapsuleSlot()
    {
        CapsuleLyricTransform.X = 0;
        CapsuleLyricHighlightClip.Rect = new Rect(0, 0, 0, 0);
        CapsuleLyricHighlight.Visibility = Visibility.Collapsed;
        CapsuleLyricText.Opacity = CapsuleLyricBaseOpacity;
        _isCapsuleLyricHighlightActive = false;

        // 歌名槽：轮转已取消，这里只把两份歌名都停在零位（第二份永久收起；它在"两份文本 + 反相位位移"的形状里布局矩形会移出容器、
        // 被 WPF 整个剔掉，歌名会因此整段消失），并让下一次 MeasureMarquee 继续走同一条复位路径。
        // The title slot: the rotation is gone, so this only parks both copies at zero (the second copy stays collapsed for good; in the old
        // "two copies plus an anti-phase shift" shape its layout rectangle left the container and WPF culled it whole, which made the title
        // vanish) and lets the next MeasureMarquee keep taking the same reset path.
        CapsuleTitleLoop.Visibility = Visibility.Collapsed;
        CapsuleTitleTransform.X = 0;
        CapsuleTitleLoopTransform.X = 0;
        _marqueeActive = false;
    }

    /// <summary>
    /// 推进胶囊歌词槽一帧：判定走 <see cref="CardLyricHighlightPolicy"/>（与卡片同一条路径，只把 <c>lineVisible</c> 换成"胶囊可见"），
    /// 几何走 <see cref="CapsuleLyricFollowPolicy"/>（跟随式：一份文本，窗口起点由亮区反推）。
    ///
    /// 裁定 6 与裁定 7 的分工就在这里：前者决定时间轴与亮层的去留，后者决定位移与裁剪宽度，两者**同出一张前缀宽度表**，
    /// 因此亮区边界、字形与窗口位置三者始终一致。
    /// Advances the capsule lyric slot by one frame: the decision goes through <see cref="CardLyricHighlightPolicy"/> (the same path the card uses,
    /// with only <c>lineVisible</c> swapped for "the capsule is visible") and the geometry through <see cref="CapsuleLyricFollowPolicy"/> (follow
    /// mode: one copy of the text with the window start solved backwards from the reveal).
    ///
    /// Rulings 6 and 7 divide the work exactly here: the former decides whether the timeline and the layer stay, the latter decides the offset and the
    /// clip width, and both read **one prefix-width table**, so the reveal edge, the glyphs, and the window position always agree.
    /// </summary>
    /// <param name="positionSeconds">本帧的播放位置（与卡片同一次取值）。/ This frame's playback position, the same value the card used.</param>
    /// <returns>胶囊的歌词时间轴是否仍需逐帧推进。/ Whether the capsule's lyric timeline still needs a frame per step.</returns>
    private bool AdvanceCapsuleLyricFrame(double positionSeconds)
    {
        if (_capsuleSlotMode != CapsuleSlotMode.Lyric)
        {
            RestoreCapsuleLyricHighlight();
            return false;
        }

        var frame = ResolveCapsuleLyricHighlightFrame(positionSeconds);
        if (!frame.AdvanceTimeline)
        {
            RestoreCapsuleLyricHighlight();
            return false;
        }

        var follow = CapsuleLyricFollowPolicy.Advance(
            _capsuleLyricBase,
            CapsuleLyricMeasureKey,
            _capsuleLyricWidths,
            _capsuleLyricRevealWidth,
            // 进度由当前行与播放位置算出（与卡片同一个入口），没有可用时间窗时按 0 处理，窗口因此停在行首而不是抛异常。
            // The progress comes from the line and the position through the same entry the card uses; a line without a usable window counts as zero,
            // which parks the window at the head instead of throwing.
            LyricHighlightPolicy.ResolveProgress(_cardLyricUpdate.CurrentLine!, positionSeconds) ?? 0,
            // 可用宽度取**歌名宿主**：它始终可见、与歌词槽同格等宽，而歌词槽在自己刚被切回可见的那一帧 `ActualWidth` 仍是 0
            // （该元素此前一直 Collapsed，切换与这一帧的跟进发生在同一个调用栈里，中间不可能发生布局）。用 0 会把窗口顶到行首、
            // 亮区宽度算错一帧，下一帧才跳回正确位置——看起来就是"回到歌词槽的一瞬间抖一下"。
            // The available width comes from the **title host**: it is always visible and shares the lyric slot's cell, whereas the lyric slot's
            // ActualWidth is still zero on the very frame it is switched back to visible (it had been collapsed, and the switch and this frame's
            // advance run in one call stack with no layout pass in between). A zero would park the window at the head and mis-size the reveal for one
            // frame, then snap back on the next — which reads as a jitter at the moment the lyric slot returns.
            CapsuleTitleHost.ActualWidth,
            CapsuleLyricText.FontSize,
            CapsuleLyricWidthMeasure);

        _capsuleLyricWidths = follow.Table;
        _capsuleLyricRevealWidth = follow.RevealWidth;

        if (!string.Equals(CapsuleLyricText.Text, follow.Window, StringComparison.Ordinal))
            CapsuleLyricText.Text = follow.Window;

        CapsuleLyricTransform.X = follow.OffsetDip;

        // 底色层按设置压暗，与卡片一致；高亮层只裁到"已唱宽度"（窗口坐标里就是 已唱宽度 − 窗口左缘宽度，裁定 7）。
        // The base layer dims to the configured value exactly as the card's does, and the reveal layer clips to the sung width — in the window's own
        // coordinates that is revealed minus the window's left edge (ruling 7).
        CapsuleLyricText.Opacity = SettingsManager.Current.LyricsUnsungOpacityPercent / 100d;
        if (frame.ShowHighlight && follow.ClipWidth >= CardLyricHighlightPolicy.MinimumClipWidth)
        {
            _isCapsuleLyricHighlightActive = true;
            CapsuleLyricHighlightClip.Rect = new Rect(0, 0, follow.ClipWidth, ResolveCapsuleLyricClipHeight());
            CapsuleLyricHighlight.Visibility = Visibility.Visible;
        }
        else
        {
            CapsuleLyricHighlight.Visibility = Visibility.Collapsed;
            CapsuleLyricHighlightClip.Rect = new Rect(0, 0, 0, 0);
        }

        return true;
    }

    /// <summary>关闭胶囊歌词的擦亮并还原外观：底色层回到 XAML 的设计值、高亮层收起、裁剪清零。/ Turns the capsule lyric reveal off and restores the appearance: the base layer returns to its XAML design value, the reveal layer collapses, and the clip resets.</summary>
    private void RestoreCapsuleLyricHighlight()
    {
        if (!_isCapsuleLyricHighlightActive)
            return;

        _isCapsuleLyricHighlightActive = false;
        CapsuleLyricText.Opacity = CapsuleLyricBaseOpacity;
        CapsuleLyricHighlight.Visibility = Visibility.Collapsed;
        CapsuleLyricHighlightClip.Rect = new Rect(0, 0, 0, 0);
    }

    /// <summary>裁剪高度取高亮层的实际高度；布局尚未跑完时用宿主高度，仍不可用则用 0（裁剪即不可见，不会画错）。/ Clip height from the reveal layer's actual height, falling back to the host and then to zero, which merely hides the layer rather than painting it wrong.</summary>
    private double ResolveCapsuleLyricClipHeight()
    {
        var height = CapsuleLyricHighlight.ActualHeight;
        if (!double.IsFinite(height) || height <= 0)
            height = CapsuleLyricHost.ActualHeight;

        return double.IsFinite(height) && height > 0 ? height : 0;
    }

    /// <summary>
    /// 求解胶囊这一帧的擦亮状态。
    /// Resolves the capsule's reveal state for one frame.
    ///
    /// 与卡片逐参数同源，只有两处按"胶囊"来取：<c>lineVisible</c> 传 <c>!_isExpanded</c>（胶囊可见），
    /// <c>measuredTextWidth</c> 传胶囊文字槽的**实测宽**（裁定 6：裁剪上限就是这一格）。这里读到的 <c>ClipWidth</c> 只用于判定阈值，
    /// 真正写进裁剪矩形的是 <see cref="CapsuleLyricFollowPolicy"/> 按前缀宽度表算出的宽度（裁定 7）。
    /// Parameter for parameter the same source as the card's, with only two taken "as the capsule": <c>lineVisible</c> is <c>!_isExpanded</c> (the
    /// capsule is visible) and <c>measuredTextWidth</c> is the capsule text slot's **measured** width (ruling 6: the clip's ceiling is that cell).
    /// The <c>ClipWidth</c> read here only feeds the threshold decision; what actually goes into the clip rectangle is the width
    /// <see cref="CapsuleLyricFollowPolicy"/> derives from the prefix-width table (ruling 7).
    /// </summary>
    /// <param name="positionSeconds">播放位置（秒）。/ Playback position in seconds.</param>
    private CardLyricHighlightPolicy.FrameState ResolveCapsuleLyricHighlightFrame(double positionSeconds)
    {
        if (_capsuleSlotMode != CapsuleSlotMode.Lyric)
            return new CardLyricHighlightPolicy.FrameState(false, false, 0);

        return CardLyricHighlightPolicy.Resolve(
            SettingsManager.Current,
            _cardLyricUpdate.CurrentLine,
            positionSeconds,
            connected: _snapshot.IsConnected,
            playing: _snapshot.IsPlaying,
            lineVisible: !_isExpanded,
            // 裁定 10 要求传真实的控制可见性：窗口被隐藏（IsVisible=false）时时间轴必须停止。
            // Ruling 10 asks for the real control visibility: a hidden window (IsVisible false) has to stop the timeline.
            controlVisible: IsVisible,
            useContinuousMotion: MotionPolicy.ResolveCurrent().UseContinuousMotion,
            // 与 <see cref="AdvanceCapsuleLyricFrame"/> 同一个宿主宽：歌词槽刚切回可见的那一帧它自己的 ActualWidth 还是 0。
            // The same host width AdvanceCapsuleLyricFrame uses: the lyric slot's own ActualWidth is still zero on the frame it is switched back to
            // visible.
            measuredTextWidth: CapsuleTitleHost.ActualWidth,
            highContrast: SystemParameters.HighContrast);
    }

    #endregion

    /// <summary>
    /// 把一帧的裁剪写进高亮层；已唱宽度未达阈值时只收起高亮层，底色层的压暗状态由调用方决定、这里不改。
    /// Writes one frame's clip into the reveal layer; a sung width below the threshold only hides that layer, while the base layer's dimmed
    /// state is the caller's decision and is left untouched here.
    /// </summary>
    /// <param name="frame">已解出的这一帧状态。/ The frame state already resolved.</param>
    private void WriteCardLyricHighlightFrame(CardLyricHighlightPolicy.FrameState frame)
    {
        if (!frame.ShowHighlight)
        {
            HideCardLyricHighlightLayer();
            return;
        }

        _isCardLyricHighlightActive = true;

        // 裁剪矩形只在文本自己的坐标系里按字形宽度裁剪：两层的字体族/字号/字重/边距相同，且高亮层的文本由 XAML 绑定跟随底色层，
        // 因此亮区与字形始终对齐。
        // The clip rectangle only trims by glyph width inside the text's own coordinate space: both layers share one font family, size, weight,
        // and margin, and the reveal layer's text follows the base layer through the XAML binding, so the reveal always stays aligned with the
        // glyphs.
        CardLyricHighlightClip.Rect = new Rect(0, 0, ResolveCardLyricClipWidth(frame.ClipWidth), ResolveCardLyricClipHeight());
        CardLyricHighlight.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// 亮区最远只到容器右边缘：整行唱完的宽度是**整行**字形的宽度，文本被省略时它宽于可见区，照抄会画到卡片之外。
    /// The reveal never goes past the container's right edge: the fully sung width is the width of the **whole** line's glyphs and exceeds
    /// the visible area whenever the text is trimmed, so copying it through would paint past the card.
    /// </summary>
    private double ResolveCardLyricClipWidth(double sungWidth)
    {
        var available = CardLyricContainer.ActualWidth;
        return double.IsFinite(available) && available > 0 ? Math.Min(sungWidth, available) : sungWidth;
    }

    /// <summary>裁剪高度取文本的实际高度；布局尚未跑完时用容器高度，仍不可用则用 0（裁剪即不可见，不会画错）。/ Clip height from the text's actual height, falling back to the container and then to zero, which merely hides the layer rather than painting it wrong.</summary>
    private double ResolveCardLyricClipHeight()
    {
        var height = CardLyricHighlight.ActualHeight;
        if (!double.IsFinite(height) || height <= 0)
            height = CardLyricContainer.ActualHeight;

        return double.IsFinite(height) && height > 0 ? height : 0;
    }

    /// <summary>
    /// 求解某一位置上的擦亮状态。
    /// Resolves the reveal state at one playback position.
    /// </summary>
    /// <param name="positionSeconds">播放位置（秒）。/ Playback position in seconds.</param>
    private CardLyricHighlightPolicy.FrameState ResolveCardLyricHighlightFrame(double positionSeconds)
    {
        EnsureCardLyricTextWidth();
        return CardLyricHighlightPolicy.Resolve(
            SettingsManager.Current,
            _cardLyricUpdate.CurrentLine,
            positionSeconds,
            connected: _snapshot.IsConnected,
            playing: _snapshot.IsPlaying,
            // 卡片的歌词行只有在展开态才可见；收拢途中（_isExpanded 已为 false）即停时间轴。
            // The card's lyric row is only visible while expanded, so a collapse in flight already stops the timeline.
            lineVisible: _isExpanded,
            // 裁定 10 要求传真实的控制可见性：窗口被隐藏（IsVisible=false）时时间轴必须停止。
            // Ruling 10 asks for the real control visibility: a hidden window (IsVisible false) has to stop the timeline.
            controlVisible: IsVisible,
            useContinuousMotion: MotionPolicy.ResolveCurrent().UseContinuousMotion,
            measuredTextWidth: _cardLyricTextWidth,
            highContrast: SystemParameters.HighContrast);
    }

    /// <summary>
    /// 按需重量当前行的字形宽度（DIP）：与任务栏用同一份 <see cref="FormattedText"/> 口径，只在文本或字号变化时重量，
    /// 于是逐帧推进不产生任何测量开销。
    /// Re-measures the active line's glyph width in DIP on demand, using the same <see cref="FormattedText"/> basis the taskbar does, and
    /// only when the text or the font size changed — which keeps per-frame advancement free of measuring.
    /// </summary>
    private void EnsureCardLyricTextWidth()
    {
        // 量的是底色层的文本：它由上面的去重分支写入，而高亮层的文本是同一次赋值的绑定结果（两层文本永远相同，量哪一层都一样）。
        // The base layer's text is what gets measured: the dedupe branch above writes it, while the reveal layer's text is the binding result
        // of that same assignment, so both are always identical and measuring either one gives the same width.
        var text = CardLyric.Text ?? string.Empty;
        var fontSize = CardLyric.FontSize;
        if (text == _measuredCardLyricText && Math.Abs(fontSize - _measuredCardLyricFontSize) < 0.01)
            return;

        _measuredCardLyricText = text;
        _measuredCardLyricFontSize = fontSize;
        _cardLyricTextWidth = text.Length == 0 ? 0 : MeasureCardLyricTextWidth(text, CardLyric);
    }

    /// <summary>
    /// 测量文本的自然宽度（DIP），字体族/字号/字重/字形取传入元素的值（实际传入的是底色层 <c>CardLyric</c>；两层经
    /// <c>ApplyFontScales</c> 同步缩放且都由 XAML 声明同一份字体设置，因此量出来的就是屏幕上这一行的字形宽度）。
    /// Measures a text's natural width in DIP from the family, size, weight, and stretch of the element passed in — in practice the base layer
    /// <c>CardLyric</c>; the two layers are scaled together by <c>ApplyFontScales</c> and declare the same font settings in XAML, so the result
    /// is exactly the glyph width of the line as drawn.
    /// </summary>
    private static double MeasureCardLyricTextWidth(string text, TextBlock source)
    {
        var pixelsPerDip = VisualTreeHelper.GetDpi(source).PixelsPerDip;
        var formatted = new FormattedText(
            text,
            System.Globalization.CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            new Typeface(source.FontFamily, source.FontStyle, source.FontWeight, source.FontStretch),
            source.FontSize,
            Brushes.Transparent,
            pixelsPerDip)
        {
            Trimming = TextTrimming.None
        };
        return formatted.WidthIncludingTrailingWhitespace;
    }
}
