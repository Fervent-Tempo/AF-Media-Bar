using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services.Lyrics;
using AFMediaBar.Classes.Settings;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>
/// 灵动岛卡片歌词区的两行呈现判定：第一行取当前句（无词/占位时回落作者），第二行按用户排的来源顺序取词、无内容时整行隐藏。
/// The capsule island card's two-row lyric presentation: the first row takes the active line (falling back to the artist without usable
/// lyrics), and the second picks its source along the user's order and hides entirely when nothing has content.
/// </summary>
[TestClass]
public sealed class CardLyricPresentationPolicyTests
{
    /// <summary>
    /// 第一行写当前句，第二行默认顺序取到译文；两个开关都打开时两行同时成立。
    /// The first row carries the active line and the second takes the translation through the default order; with both switches on the two
    /// rows hold at once.
    /// </summary>
    [TestMethod]
    public void BothSwitchesOnShowTheActiveLineAndTheSecondarySource()
    {
        var settings = new AppSettings { LyricsEnabled = true, TwoLineLyricsEnabled = true };
        var update = new LyricLineUpdate("当前句", "下一句", "译文", "yinyi", true, new LyricLine(0, 3, "当前句"));

        var state = CardLyricPresentationPolicy.Resolve(settings, update, "作者");

        Assert.AreEqual("当前句", state.FirstLine.Text);
        Assert.IsTrue(state.FirstLine.ShowActiveLine);
        // 默认顺序是 翻译 → 音译 → 下一句，因此译文优先。
        // The default order is translation, romanization, next line, so the translation wins.
        Assert.AreEqual("译文", state.SecondaryText);
        Assert.IsTrue(state.ShowSecondaryLine);
    }

    /// <summary>
    /// 占位歌词（来源用"暂无歌词"这类整行表示没有歌词）不算歌词：第一行留空让调用方回落作者，第二行与擦亮层都不激活。
    /// A placeholder lyric — a source answering "no lyrics" with one whole line — is not a lyric: the first row stays empty so the caller falls
    /// back to the artist, and neither the second row nor the reveal layer is activated.
    /// </summary>
    [TestMethod]
    public void PlaceholderLineFallsBackAndHidesTheSecondRow()
    {
        var settings = new AppSettings { LyricsEnabled = true, TwoLineLyricsEnabled = true };
        // 网易云无词曲目的实测返回：整行带着时间戳通过解析，因此必须在呈现这一层被认出来。
        // The measured answer from a lyric-less NetEase track: the whole line passes parsing with its timestamp, so it has to be recognised
        // here, at the presentation layer.
        var placeholder = new LyricLine(0, 3, "暂无歌词");
        var update = new LyricLineUpdate("暂无歌词", "下一句", "译文", "yinyi", true, placeholder);

        var state = CardLyricPresentationPolicy.Resolve(settings, update, "作者");

        Assert.AreEqual("作者", state.FirstLine.Text);
        Assert.IsFalse(state.FirstLine.ShowActiveLine);
        // CurrentLine 非 null（占位行也是个 LyricLine），若不加占位判定，这里会给出 true 并让第二行与擦亮一起上线。
        // CurrentLine is not null — a placeholder is a LyricLine too — so without the placeholder check this would answer true and bring both
        // the second row and the reveal online.
        Assert.AreEqual(string.Empty, state.SecondaryText);
        Assert.IsFalse(state.ShowSecondaryLine);
    }

    /// <summary>
    /// 只有空白的行同样不算歌词（<see cref="LyricPlaceholderPolicy"/> 把空白一律按占位处理）：第一行回落作者，第二行隐藏。
    /// A whitespace-only line is not a lyric either (<see cref="LyricPlaceholderPolicy"/> treats whitespace as a placeholder): the first row
    /// falls back to the artist and the second row hides.
    /// </summary>
    [TestMethod]
    public void WhitespaceLineFallsBackAndHidesTheSecondRow()
    {
        var settings = new AppSettings { LyricsEnabled = true, TwoLineLyricsEnabled = true };
        var update = new LyricLineUpdate("   ", "下一句", "译文", "yinyi", true, new LyricLine(0, 3, "   "));

        var state = CardLyricPresentationPolicy.Resolve(settings, update, "作者");

        Assert.AreEqual("作者", state.FirstLine.Text);
        Assert.IsFalse(state.FirstLine.ShowActiveLine);
        Assert.IsFalse(state.ShowSecondaryLine);
    }

    /// <summary>
    /// 歌词总开关关掉时两行都不显示，第一行落回回落文本（卡片是作者），与"这首歌没有歌词"走同一条路。
    /// With the lyrics master switch off neither row shows and the first row falls back to the caller's text (the artist on the card), which is
    /// the very path "this track has no lyrics" takes.
    /// </summary>
    [TestMethod]
    public void MasterSwitchOffFallsBackToTheArtistAndHidesBothRows()
    {
        var settings = new AppSettings { LyricsEnabled = false, TwoLineLyricsEnabled = true };
        var update = new LyricLineUpdate("当前句", "下一句", "译文", "yinyi", true, new LyricLine(0, 3, "当前句"));

        var state = CardLyricPresentationPolicy.Resolve(settings, update, "作者");

        Assert.AreEqual("作者", state.FirstLine.Text);
        Assert.IsFalse(state.FirstLine.ShowActiveLine);
        Assert.AreEqual(string.Empty, state.SecondaryText);
        Assert.IsFalse(state.ShowSecondaryLine);
    }

    /// <summary>
    /// 没有命中任何歌词行（位置早于第一行、文档缺失）时第一行同样落回回落文本，擦亮层保持关闭。
    /// When no lyric line matches — a position before the first line, or no document at all — the first row falls back the same way and the
    /// highlight layer stays off.
    /// </summary>
    [TestMethod]
    public void MissingActiveLineFallsBackAndKeepsTheHighlightOff()
    {
        var settings = new AppSettings { LyricsEnabled = true, TwoLineLyricsEnabled = true };
        var update = new LyricLineUpdate(string.Empty, "下一句", string.Empty, string.Empty, false, null);

        var state = CardLyricPresentationPolicy.Resolve(settings, update, "作者");

        Assert.AreEqual("作者", state.FirstLine.Text);
        Assert.IsFalse(state.FirstLine.ShowActiveLine);
        // 回落行不是歌词，第二行没有可以对照的当前句，因此不显示——否则作者下面会挂着"下一句"。
        // A fallback row is not a lyric, so the second row has no active line to sit under and stays hidden; otherwise the next line would hang
        // underneath the artist.
        Assert.IsFalse(state.ShowSecondaryLine);
    }

    /// <summary>
    /// 第二行严格跟随双行开关与用户在歌词页排的来源顺序，未列出的来源不会被使用。
    /// The second row follows the two-line switch and the source order the user arranged on the lyrics page; a source that is not listed is
    /// never used.
    /// </summary>
    [TestMethod]
    public void SecondaryLineFollowsTheSwitchAndTheConfiguredOrder()
    {
        var update = new LyricLineUpdate("当前句", "下一句", "译文", "yinyi", true, new LyricLine(0, 3, "当前句"));

        var twoLineOff = new AppSettings { LyricsEnabled = true, TwoLineLyricsEnabled = false };
        Assert.IsFalse(CardLyricPresentationPolicy.Resolve(twoLineOff, update, "作者").ShowSecondaryLine);

        // 用户把"下一句"排到最前：它优先于译文与音译。
        // The user put the next line first: it wins over the translation and the romanization.
        var nextFirst = new AppSettings
        {
            LyricsEnabled = true,
            TwoLineLyricsEnabled = true,
            LyricsSecondaryLine = new LyricsSecondaryLineSettings([LyricsSecondaryLineMode.NextLine])
        };
        var state = CardLyricPresentationPolicy.Resolve(nextFirst, update, "作者");
        Assert.AreEqual("下一句", state.SecondaryText);
        Assert.IsTrue(state.ShowSecondaryLine);

        // 只列音译：即使有译文也只显示音译。
        // Only the romanization is listed: a translation is ignored.
        var romanizationOnly = new AppSettings
        {
            LyricsEnabled = true,
            TwoLineLyricsEnabled = true,
            LyricsSecondaryLine = new LyricsSecondaryLineSettings([LyricsSecondaryLineMode.Romanization])
        };
        Assert.AreEqual("yinyi", CardLyricPresentationPolicy.Resolve(romanizationOnly, update, "作者").SecondaryText);

        // 显式的空数组：一个来源都不用，第二行隐藏。
        // An explicit empty list uses no source at all, so the second row hides.
        var none = new AppSettings
        {
            LyricsEnabled = true,
            TwoLineLyricsEnabled = true,
            LyricsSecondaryLine = new LyricsSecondaryLineSettings([])
        };
        Assert.IsFalse(CardLyricPresentationPolicy.Resolve(none, update, "作者").ShowSecondaryLine);
    }

    /// <summary>
    /// 所有来源都没有内容（或只有空白）时第二行整行隐藏，不留空白占位。
    /// When every source is empty, or whitespace only, the second row hides entirely instead of leaving blank space.
    /// </summary>
    [TestMethod]
    public void EmptySecondarySourcesHideTheRow()
    {
        var settings = new AppSettings { LyricsEnabled = true, TwoLineLyricsEnabled = true };
        var update = new LyricLineUpdate("当前句", null!, null!, "   ", true, new LyricLine(0, 3, "当前句"));

        var state = CardLyricPresentationPolicy.Resolve(settings, update, "作者");

        Assert.AreEqual(string.Empty, state.SecondaryText);
        Assert.IsFalse(state.ShowSecondaryLine);
    }
}

/// <summary>
/// 灵动岛卡片逐字擦亮的一帧决策：时间轴是否推进、擦亮层是否显示、裁剪宽度是多少。
/// One frame's decision for the capsule island card's syllable highlight: whether the timeline advances, whether the highlight layer shows,
/// and how wide the clip is.
/// </summary>
[TestClass]
public sealed class CardLyricHighlightPolicyTests
{
    /// <summary>四音节的一行：10–14 秒，每 0.25 秒一个字，便于按位置核对进度。/ A four-syllable line from 10 to 14 seconds, one syllable per 0.25 s, so progress can be checked by position.</summary>
    private static LyricLine Line() => new(10, 14, "你好世界")
    {
        Words =
        [
            new LyricWord(10, 11, "你"),
            new LyricWord(11, 12, "好"),
            new LyricWord(12, 13, "世"),
            new LyricWord(13, 14, "界")
        ]
    };

    /// <summary>擦亮开关打开的设置：其余开关按用例单独给。/ Settings with the highlight switch on; every other switch is given per test.</summary>
    private static AppSettings Settings(bool highlightEnabled = true) =>
        new() { LyricsSyllableHighlightEnabled = highlightEnabled };

    /// <summary>一帧的解算入口，参数按名给出，避免位置参数顺序读错。/ The one-frame entry point with named arguments, so positional mistakes cannot slip in.</summary>
    private static CardLyricHighlightPolicy.FrameState Resolve(
        LyricLine? line,
        double positionSeconds = 12,
        bool connected = true,
        bool playing = true,
        bool lineVisible = true,
        bool controlVisible = true,
        bool useContinuousMotion = true,
        double measuredTextWidth = 100,
        bool highContrast = false,
        bool highlightEnabled = true) =>
        CardLyricHighlightPolicy.Resolve(
            Settings(highlightEnabled),
            line,
            positionSeconds,
            connected,
            playing,
            lineVisible,
            controlVisible,
            useContinuousMotion,
            measuredTextWidth,
            highContrast);

    /// <summary>
    /// 播放中、有当前行、擦亮开关打开、窗口可见：时间轴推进、擦亮层显示，裁剪宽度就是已唱比例乘实测行宽。
    /// While playing with an active line, the highlight switch on, and a visible window, the timeline advances, the layer shows, and the clip
    /// width is the sung ratio times the measured line width.
    /// </summary>
    [TestMethod]
    public void PlayingWithTheSwitchOnAdvancesAndShowsTheReveal()
    {
        var state = Resolve(Line());

        Assert.IsTrue(state.AdvanceTimeline);
        Assert.IsTrue(state.ShowHighlight);
        // 12 秒时刚好唱完前两个字，进度 0.5。
        // At twelve seconds exactly two of the four syllables are sung, so the progress is 0.5.
        Assert.AreEqual(50, state.ClipWidth, 0.001);
    }

    /// <summary>
    /// 窗口本身被隐藏（<c>IsVisible == false</c>）时时间轴停止：这一项是裁定 10 要求的真实控制可见性，不是常量 true。
    /// A hidden window (<c>IsVisible == false</c>) stops the timeline: this term is the real control visibility ruling 10 asks for, not a
    /// constant true.
    /// </summary>
    [TestMethod]
    public void HiddenWindowStopsTheTimeline()
    {
        var state = Resolve(Line(), controlVisible: false);

        Assert.IsFalse(state.AdvanceTimeline);
        Assert.IsFalse(state.ShowHighlight);
        Assert.AreEqual(0, state.ClipWidth, 0.001);
    }

    /// <summary>
    /// 用户关掉擦亮开关：只隐藏擦亮层，时间轴照常推进——滚动轨迹在开关前后必须完全一致。
    /// With the user's highlight switch off only the layer hides while the timeline keeps advancing, so the scrolling trajectory is identical on
    /// both sides of the switch.
    /// </summary>
    [TestMethod]
    public void SwitchOffHidesOnlyTheLayerAndKeepsTheTimeline()
    {
        var state = Resolve(Line(), highlightEnabled: false);

        Assert.IsTrue(state.AdvanceTimeline);
        Assert.IsFalse(state.ShowHighlight);
    }

    /// <summary>
    /// 高对比度只隐藏擦亮层，与用户关掉开关是同一条路。
    /// High contrast hides only the layer, the same path the switch takes.
    /// </summary>
    [TestMethod]
    public void HighContrastHidesOnlyTheLayer()
    {
        var state = Resolve(Line(), highContrast: true);

        Assert.IsTrue(state.AdvanceTimeline);
        Assert.IsFalse(state.ShowHighlight);
    }

    /// <summary>
    /// 暂停、断连、卡片收拢或减少动效时整条时间轴停止，擦亮层一并收起。
    /// Pausing, disconnecting, collapsing the card, or reduced motion stops the whole timeline and takes the layer down with it.
    /// </summary>
    [TestMethod]
    public void StoppedTimelineNeverShowsTheReveal()
    {
        foreach (var stopped in new[]
                 {
                     Resolve(Line(), playing: false),
                     Resolve(Line(), connected: false),
                     Resolve(Line(), lineVisible: false),
                     Resolve(Line(), useContinuousMotion: false),
                     Resolve(null)
                 })
        {
            Assert.IsFalse(stopped.AdvanceTimeline);
            Assert.IsFalse(stopped.ShowHighlight);
            Assert.AreEqual(0, stopped.ClipWidth, 0.001);
        }
    }

    /// <summary>
    /// 裁剪宽度小于半个 DIP 或行宽不可用时不算"显示"：调用方据此隐藏高亮层，行首不会出现一条几乎没有宽度的杂线；
    /// 但时间轴仍然推进（此时只隐藏高亮层，底色层的压暗状态保持不变）。
    /// A clip width under half a DIP, or an unusable line width, does not count as showing: the caller hides the layer instead of leaving a
    /// hair-width line at the head of the row, while the timeline keeps advancing (only the layer hides; the base layer stays dimmed).
    /// </summary>
    [TestMethod]
    public void HairWidthAndUnusableLineWidthHideTheLayerButKeepTheTimeline()
    {
        // 进度 0.002 × 100 DIP = 0.2 DIP，低于半个 DIP 的阈值。
        // A progress of 0.002 over 100 DIP is 0.2 DIP, below the half-DIP threshold.
        var hair = Resolve(new LyricLine(10, 14, "line"), positionSeconds: 10.008);
        Assert.IsTrue(hair.AdvanceTimeline);
        Assert.AreEqual(0.2, hair.ClipWidth, 0.001);
        Assert.IsFalse(hair.ShowHighlight);

        var unmeasured = Resolve(Line(), measuredTextWidth: 0);
        Assert.IsTrue(unmeasured.AdvanceTimeline);
        Assert.IsFalse(unmeasured.ShowHighlight);
    }
}
