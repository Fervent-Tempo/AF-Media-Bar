using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AFMediaBar.Classes.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>
/// 跑马灯的两种推进方式：轮转式（标题、歌手、第二行歌词、没有可用时间窗的歌词行）与跟随式（按歌词时间轴推进的主歌词行）。
/// The marquee's two advance modes: rotation for the title, artist, second lyric row, and lyric lines without a syllable timeline, and
/// the follow mode for the main lyric line advancing on its timeline.
/// </summary>
[TestClass]
public sealed class MarqueePolicyTests
{
    [TestMethod]
    public void RotationWindowWrapsTheContentAndKeepsTheSeparatorAtTheSeam()
    {
        const string content = "ABCDE";

        Assert.AreEqual("ABCDE   ", MarqueeRotationPolicy.BuildWindow(content, 0));
        Assert.AreEqual("BCDE   A", MarqueeRotationPolicy.BuildWindow(content, 1));
        Assert.AreEqual("DE   ABC", MarqueeRotationPolicy.BuildWindow(content, 3));
        // 偏移越界与负数都按窗口长度回绕，滚动因此不会走出字符串之外。
        // Out-of-range and negative offsets wrap by the window length, so the scroll can never leave the string.
        Assert.AreEqual("ABCDE   ", MarqueeRotationPolicy.BuildWindow(content, 8));
        // 逆向偏移等价于正向回绕：-1 与 7 是同一个窗口位置。
        // A negative offset is the same window position as its positive wrap: -1 and 7 agree.
        Assert.AreEqual(" ABCDE  ", MarqueeRotationPolicy.BuildWindow(content, -1));
        Assert.AreEqual(
            MarqueeRotationPolicy.BuildWindow(content, -1),
            MarqueeRotationPolicy.BuildWindow(content, 7));
        Assert.AreEqual(string.Empty, MarqueeRotationPolicy.BuildWindow(string.Empty, 3));
        Assert.AreEqual(8, MarqueeRotationPolicy.ResolveWindowLength(content.Length));
    }

    /// <summary>
    /// 轮转窗口必须与原文等长（全是同一段文字的轮换），否则每转一轮文字就会缩短或重排，
    /// 而这正是"看久了标题变了样"的来源。
    /// A rotation window has to be exactly as long as the content plus the separator, because otherwise the text would shrink or
    /// rearrange once per round — which is exactly how a title starts looking wrong after a while.
    /// </summary>
    [TestMethod]
    public void RotationWindowAlwaysKeepsEveryCharacter()
    {
        const string content = "夜航 星のうた";
        var windowLength = MarqueeRotationPolicy.ResolveWindowLength(content.Length);
        for (var offset = 0; offset < windowLength * 2; offset++)
        {
            var window = MarqueeRotationPolicy.BuildWindow(content, offset);
            Assert.AreEqual(windowLength, window.Length);
            CollectionAssert.AreEquivalent(
                (content + MarqueeRotationPolicy.Separator).ToCharArray(),
                window.ToCharArray());
        }
    }

    /// <summary>
    /// 跟随式窗口是原文的一个后缀：只丢弃已经唱过的字，永不回头，因此已唱段在窗口里始终是前缀。
    /// A follow window is a suffix of the content: it only discards characters that are already sung and never wraps, so the sung run
    /// stays a prefix of the window.
    /// </summary>
    [TestMethod]
    public void FollowWindowIsAlwaysASuffixOfTheContent()
    {
        const string content = "夜航星を歌うよ";

        Assert.AreEqual(content, MarqueeFollowPolicy.BuildWindow(content, 0));
        Assert.AreEqual("を歌うよ", MarqueeFollowPolicy.BuildWindow(content, 3));
        Assert.AreEqual("よ", MarqueeFollowPolicy.BuildWindow(content, content.Length - 1));
        Assert.AreEqual(string.Empty, MarqueeFollowPolicy.BuildWindow(content, content.Length + 5));
        Assert.AreEqual(string.Empty, MarqueeFollowPolicy.BuildWindow(null, 2));

        for (var start = 0; start <= content.Length; start++)
        {
            Assert.IsTrue(content.EndsWith(MarqueeFollowPolicy.BuildWindow(content, start), StringComparison.Ordinal));
        }
    }

    /// <summary>
    /// 擦亮还没到容器的 80% 时窗口停在原文开头；越过后窗口跟着亮区走，且走得是连续的（不是一次一个字）。
    /// While the reveal has not reached 80% of the container the window stays at the content's head; past that it follows the reveal, and
    /// it follows continuously rather than a character at a time.
    /// </summary>
    [TestMethod]
    public void FollowWindowOnlyMovesOnceTheRevealReachesItsRestPosition()
    {
        const double available = 100;

        Assert.AreEqual(0.8, MarqueeFollowPolicy.RevealEdgeRatio, 0.0001);
        // 亮区还没到 80 DIP：不丢弃任何宽度，窗口停在开头。
        // The reveal is still short of 80 DIP: nothing is dropped and the window stays at the head.
        Assert.AreEqual(0, MarqueeFollowPolicy.ResolveDroppedWidth(0, available), 0.0001);
        Assert.AreEqual(0, MarqueeFollowPolicy.ResolveDroppedWidth(80, available), 0.0001);
        // 越过后丢弃的宽度就是超出的部分，连续变化。
        Assert.AreEqual(0.5, MarqueeFollowPolicy.ResolveDroppedWidth(80.5, available), 0.0001);
        Assert.AreEqual(30, MarqueeFollowPolicy.ResolveDroppedWidth(110, available), 0.0001);
        Assert.AreEqual(0, MarqueeFollowPolicy.ResolveDroppedWidth(50, 0), 0.0001);
    }

    /// <summary>
    /// 位置必须**对亮区单调**：同一行内容上逐步推进亮区，窗口位置只允许前进或不动。
    /// 这是"长歌词抖动"的回归线——按"窗口里还能放下几个字"求解会让位置与窗口互相追赶，混排内容上来回跳。
    /// The position has to be **monotone in the reveal**: advancing the reveal step by step over one line may only move the window forward or
    /// hold it. This is the regression line for the long-lyric jitter: solving from "how many characters still fit" makes the position and
    /// the window chase each other and jump back and forth on mixed content.
    /// </summary>
    [TestMethod]
    public void FollowPositionIsMonotoneOnMixedWidthContent()
    {
        // 混排：窄字（6 DIP）与宽字（26 DIP）交替，正是"能放下的字数"剧烈变化的内容。
        // Mixed content: narrow (6 DIP) and wide (26 DIP) characters alternate, which is exactly what makes "how many characters fit" swing.
        const int contentLength = 60;
        var widths = BuildPrefixWidths(contentLength, index => index % 8 < 4 ? 6 : 26);
        const double available = 166;

        var previous = double.NaN;
        var backward = 0;
        var worst = 0d;
        for (var step = 0; step <= contentLength * 20; step++)
        {
            var sung = step / 20d;
            var sungWidth = MarqueeFollowPolicy.ResolveWidthAt(widths, contentLength, sung);
            var position = MarqueeFollowPolicy.ResolvePositionAtWidth(
                widths,
                contentLength,
                MarqueeFollowPolicy.ResolveDroppedWidth(sungWidth, available));

            Assert.IsTrue(position >= 0, "位置不允许为负 / the position never goes negative");
            Assert.IsTrue(position <= sung + 0.0001, "窗口不允许跑到亮区后面 / the window never passes the reveal");
            Assert.IsTrue(
                sungWidth - MarqueeFollowPolicy.ResolveWidthAt(widths, contentLength, position) <= available + 0.0001,
                $"已唱段越出容器：sung={sung} position={position} / the sung run left the container");

            if (!double.IsNaN(previous) && position < previous - 0.0001)
            {
                backward++;
                worst = Math.Max(worst, previous - position);
            }

            previous = position;
        }

        Assert.AreEqual(0, backward, $"窗口位置出现回退，最大 {worst:0.000} 字符 / the window position moved backwards");
    }

    /// <summary>
    /// 亮区停在容器的固定位置：唱到哪滚到哪，越往后窗口只前进。
    /// The reveal rests at a fixed spot in the container: the line scrolls to wherever the singing has got to and only ever moves forward.
    /// </summary>
    [TestMethod]
    public void FollowWindowKeepsTheRevealAtTheRestPosition()
    {
        const int contentLength = 40;
        const double available = 100;
        var widths = BuildPrefixWidths(contentLength, _ => 10);

        // 亮区已经越过 80 DIP 之后，已唱段的可见宽度恒为容器宽度的 80%。
        // Once the reveal has passed 80 DIP the sung run is always 80% of the container wide on screen.
        for (var sung = 8d; sung <= contentLength; sung += 0.25)
        {
            var sungWidth = MarqueeFollowPolicy.ResolveWidthAt(widths, contentLength, sung);
            var position = MarqueeFollowPolicy.ResolvePositionAtWidth(
                widths,
                contentLength,
                MarqueeFollowPolicy.ResolveDroppedWidth(sungWidth, available));
            Assert.AreEqual(80, sungWidth - MarqueeFollowPolicy.ResolveWidthAt(widths, contentLength, position), 0.0001);
        }

        // 整行唱完：窗口停在最后，尾巴（原文的剩余部分）仍在窗口里，位置不会越过原文长度。
        // A fully sung line rests at the end with the rest of the content still in the window, never past the content length.
        var finalPosition = MarqueeFollowPolicy.ResolvePositionAtWidth(
            widths,
            contentLength,
            MarqueeFollowPolicy.ResolveDroppedWidth(
                MarqueeFollowPolicy.ResolveWidthAt(widths, contentLength, contentLength),
                available));
        Assert.AreEqual(32, finalPosition, 0.0001);
        Assert.IsTrue(finalPosition <= contentLength);
    }

    /// <summary>
    /// 呈现层使用的已唱宽度只前进、每帧最多追赶一小段：播放器上报的播放位置会回退（很多播放器按整秒上报），
    /// 直接跟着它走会让整块歌词往回跳。这是"这一版比上一版还抖"的回归线。
    /// The presented sung width only moves forward and catches up by a bounded amount per frame: the player's reported position regresses,
    /// because many players report whole seconds, and following it directly dragged the whole line backwards. This is the regression line for
    /// the report that a later build jittered more than the previous one.
    /// </summary>
    [TestMethod]
    public void PresentedRevealOnlyMovesForwardAndCatchesUpInBoundedSteps()
    {
        var width = MarqueeFollowPolicy.ResolveRevealWidth(0, 30, 20);
        Assert.AreEqual(20, width, 0.001);
        width = MarqueeFollowPolicy.ResolveRevealWidth(width, 30, 20);
        Assert.AreEqual(30, width, 0.001);
        // 上报回退：忽略，等播放位置自己追上。
        // A backwards report is ignored, leaving the playback position to catch up.
        Assert.AreEqual(30, MarqueeFollowPolicy.ResolveRevealWidth(width, 12, 20), 0.001);
        Assert.AreEqual(30, MarqueeFollowPolicy.ResolveRevealWidth(width, 30, 20), 0.001);
        // 大幅前进也按步追赶，一次上报的台阶不会整块推走。
        // A large step forward is caught up in bounded steps, so one report never pushes the line in one go.
        Assert.AreEqual(50, MarqueeFollowPolicy.ResolveRevealWidth(width, 200, 20), 0.001);
        // 非法输入不会造出回退或 NaN。
        // Invalid input neither creates a regression nor a NaN.
        Assert.AreEqual(0, MarqueeFollowPolicy.ResolveRevealWidth(0, double.NaN, 20), 0.001);
        Assert.AreEqual(25, MarqueeFollowPolicy.ResolveRevealWidth(25, 5, 20), 0.001);
        Assert.AreEqual(40, MarqueeFollowPolicy.ResolveRevealWidth(25, 40, 0), 0.001);

        // 任意目标序列下都不回退。
        // No target sequence can make it move backwards.
        var previous = 0d;
        foreach (var target in new[] { 5d, 3, 40, 39, 39.5, 0, 80 })
        {
            var next = MarqueeFollowPolicy.ResolveRevealWidth(previous, target, 6);
            Assert.IsTrue(next >= previous, "呈现宽度不允许回退 / the presented width never moves backwards");
            previous = next;
        }
    }

    /// <summary>构造前缀宽度表：第 i 项是前 i 个字符的宽度，第 0 项为 0。/ Builds a prefix-width table: the i-th entry is the width of the first i characters and the zeroth is zero.</summary>
    private static double[] BuildPrefixWidths(int contentLength, Func<int, double> widthOf)
    {
        var widths = new double[contentLength + 1];
        for (var index = 0; index < contentLength; index++)
            widths[index + 1] = widths[index] + widthOf(index);

        return widths;
    }

    /// <summary>
    /// 位置按帧、按**像素**推进：速度是常量（DIP/秒），每帧的字符增量 = 像素增量 ÷ 当前字宽。
    /// 用像素定速是为了让屏幕上的速度恒定——按字定速时宽字走得快、窄字走得慢，每跨一个字符边界速度就变一次，看起来就是一个字一个字地蹦。
    /// The position advances per frame and in **pixels**: the speed is a constant in DIP per second and the per-frame character delta is the pixel
    /// delta divided by the current character's width. Pixels are what keeps the on-screen speed constant — a per-character speed makes wide
    /// characters move fast and narrow ones slow, changing the speed at every character boundary and reading as hopping one character at a time.
    /// </summary>
    [TestMethod]
    public void PositionAdvancesAtAConstantPixelSpeed()
    {
        Assert.AreEqual(16, MarqueeTiming.FrameInterval.TotalMilliseconds, 0.001);
        Assert.AreEqual(60, MarqueeTiming.ScrollSpeedDipPerSecond, 0.001);
        // 60 DIP/秒 × 16 毫秒 ≈ 每帧 0.96 DIP：一个 13 DIP 的汉字因此约 13.5 帧走完，屏幕上是连续移动。
        // Sixty DIP per second over sixteen milliseconds is about 0.96 DIP per frame, so a thirteen-DIP character takes about 13.5 frames and the
        // movement looks continuous.
        Assert.AreEqual(0.96, MarqueeTiming.DipPerFrame, 0.001);

        // 同样的像素增量在宽字上对应更小的字符增量，因此两者的屏幕速度一致。
        // The same pixel delta covers fewer characters on a wide glyph, which is what makes the on-screen speed identical.
        const double wide = 26;
        const double narrow = 6;
        Assert.AreEqual(MarqueeTiming.DipPerFrame / wide, MarqueeTiming.DipPerFrame / wide, 0.0001);
        Assert.IsTrue(MarqueeTiming.DipPerFrame / wide < MarqueeTiming.DipPerFrame / narrow);
        Assert.IsTrue(MarqueeTiming.LeadInDuration > TimeSpan.Zero);
    }

    /// <summary>
    /// 前缀宽度表的两向换算：位置换宽度按小数插值，宽度换位置是它的反函数（二分查找 + 插值），
    /// 因此亮区边界与窗口位置都连续变化，不会在字与字之间跳。
    /// The prefix-width table converts both ways: a position becomes a width by interpolation, and a width becomes a position through the
    /// inverse search, so both the reveal edge and the window position move continuously instead of jumping between characters.
    /// </summary>
    [TestMethod]
    public void PrefixWidthsConvertBothWaysWithInterpolation()
    {
        // 每个字符 10 DIP，共 8 个字符。
        // Ten DIP per character, eight characters in total.
        var prefixWidths = BuildPrefixWidths(8, _ => 10);

        Assert.AreEqual(0, MarqueeFollowPolicy.ResolveWidthAt(prefixWidths, 8, 0), 0.001);
        Assert.AreEqual(30, MarqueeFollowPolicy.ResolveWidthAt(prefixWidths, 8, 3), 0.001);
        // 小数位置在相邻两个前缀之间插值。
        // A fractional position interpolates between neighbouring prefixes.
        Assert.AreEqual(25, MarqueeFollowPolicy.ResolveWidthAt(prefixWidths, 8, 2.5), 0.001);
        Assert.AreEqual(32.5, MarqueeFollowPolicy.ResolveWidthAt(prefixWidths, 8, 3.25), 0.001);
        // 只测到一半时，越界位置夹到已测范围，不会读到未测的项。
        // While only half the table is measured an out-of-range position is clamped to the measured range.
        Assert.AreEqual(40, MarqueeFollowPolicy.ResolveWidthAt(prefixWidths, 4, 99), 0.001);
        Assert.AreEqual(0, MarqueeFollowPolicy.ResolveWidthAt(null, 8, 3), 0.001);

        Assert.AreEqual(0, MarqueeFollowPolicy.ResolvePositionAtWidth(prefixWidths, 8, 0), 0.001);
        Assert.AreEqual(3, MarqueeFollowPolicy.ResolvePositionAtWidth(prefixWidths, 8, 30), 0.001);
        Assert.AreEqual(2.5, MarqueeFollowPolicy.ResolvePositionAtWidth(prefixWidths, 8, 25), 0.001);
        // 反函数与正函数互为逆运算。
        // The two directions are inverses of each other.
        for (var position = 0d; position <= 8; position += 0.125)
        {
            var width = MarqueeFollowPolicy.ResolveWidthAt(prefixWidths, 8, position);
            Assert.AreEqual(position, MarqueeFollowPolicy.ResolvePositionAtWidth(prefixWidths, 8, width), 0.001);
        }

        // 宽度越界：夹到 [0, 已测字符数]。
        // Out-of-range widths clamp into [0, measured characters].
        Assert.AreEqual(8, MarqueeFollowPolicy.ResolvePositionAtWidth(prefixWidths, 8, 500), 0.001);
        Assert.AreEqual(4, MarqueeFollowPolicy.ResolvePositionAtWidth(prefixWidths, 4, 500), 0.001);
        Assert.AreEqual(0, MarqueeFollowPolicy.ResolvePositionAtWidth(null, 8, 30), 0.001);
    }

    [TestMethod]
    public void SungPositionFollowsTheRevealProgress()
    {
        Assert.AreEqual(0, MarqueeFollowPolicy.ResolveSungPosition(0, 20), 0.0001);
        Assert.AreEqual(5, MarqueeFollowPolicy.ResolveSungPosition(0.25, 20), 0.0001);
        // 已唱位置是连续的：按帧采样时不会只落在整字上。
        // The sung position is continuous: sampled per frame it does not have to land on whole characters.
        Assert.AreEqual(5.5, MarqueeFollowPolicy.ResolveSungPosition(0.275, 20), 0.0001);
        Assert.AreEqual(20, MarqueeFollowPolicy.ResolveSungPosition(1, 20), 0.0001);
        Assert.AreEqual(20, MarqueeFollowPolicy.ResolveSungPosition(2, 20), 0.0001);
        Assert.AreEqual(0, MarqueeFollowPolicy.ResolveSungPosition(double.NaN, 20), 0.0001);
        Assert.AreEqual(0, MarqueeFollowPolicy.ResolveSungPosition(0.5, 0), 0.0001);
        // 进度只前进不后退，因此已唱位置也单调不减。
        // Progress only moves forward, so the sung position never decreases either.
        var previous = 0d;
        for (var step = 0; step <= 100; step++)
        {
            var sung = MarqueeFollowPolicy.ResolveSungPosition(step / 100d, 20);
            Assert.IsTrue(sung >= previous);
            previous = sung;
        }
    }

    /// <summary>
    /// 切分点必须落在完整字符上：emoji（代理对）与带变体选择符的字被拆到窗口两端会各渲染成一个替代字形，
    /// 这是轮转相对"整段文字平移"新引入的风险，两条推进路径都要挡住。
    /// Split points always land on whole characters: an emoji (surrogate pair) or a character with a variation selector split across the
    /// window ends renders as replacement glyphs, which is a risk rotation newly introduced over translating the whole text, so both
    /// advance modes have to block it.
    /// </summary>
    [TestMethod]
    public void SplitPointsNeverBreakASurrogatePairOrACombiningMark()
    {
        const string content = "夜🌟航☺\uFE0F星";
        var contentElements = TextElements(content);

        // 轮转：每个偏移给出的窗口都是同一批完整文本元素的轮换，没有半个字符落在接缝两侧。
        // Rotation: every offset yields a rotation of the same whole text elements, with no half character at the seam.
        var sourceElements = TextElements(content + MarqueeRotationPolicy.Separator);
        var windowLength = MarqueeRotationPolicy.ResolveWindowLength(content.Length);
        for (var offset = 0; offset < windowLength * 2; offset++)
        {
            CollectionAssert.AreEquivalent(
                sourceElements,
                TextElements(MarqueeRotationPolicy.BuildWindow(content, offset)));
        }

        // 跟随：起点只向前对齐到元素边界，因此既不拆字也不后退，并且只在起点上限处停下。
        // Following: the start only snaps forward to an element boundary, so it neither splits nor moves backwards, and it stops only at
        // the start limit.
        var limit = content.Length - 1;
        var start = 0;
        for (var step = 0; step < content.Length + 5; step++)
        {
            var next = MarqueeFollowPolicy.SnapStart(content, start + 1, limit);
            Assert.IsTrue(next >= start, "窗口起点不允许后退 / the window start never moves backwards");
            Assert.IsTrue(next <= limit, "窗口起点不允许越过上限 / the window start never passes its limit");
            Assert.IsTrue(next > start || next == limit, "只允许在起点上限处停下 / only the start limit may stop the window");
            Assert.AreEqual(
                string.Concat(contentElements.Skip(CountTextElementsBefore(content, next))),
                MarqueeFollowPolicy.BuildWindow(content, next));
            start = next;
        }

        Assert.AreEqual(limit, start, "全部唱完后窗口滑到起点上限 / a fully sung line reaches the start limit");
    }

    /// <summary>按文本元素切分文字（emoji 与组合记号各算一个元素）。/ Splits text into text elements, counting an emoji or a combining mark as one.</summary>
    private static List<string> TextElements(string text)
    {
        var elements = new List<string>();
        for (var index = 0; index < text.Length;)
        {
            var element = StringInfo.GetNextTextElement(text, index);
            elements.Add(element);
            index += element.Length;
        }

        return elements;
    }

    /// <summary>索引之前有多少个完整文本元素。/ How many whole text elements precede the index.</summary>
    private static int CountTextElementsBefore(string text, int index)
    {
        var count = 0;
        for (var position = 0; position < index && position < text.Length; count++)
            position += StringInfo.GetNextTextElement(text, position).Length;

        return count;
    }
}
