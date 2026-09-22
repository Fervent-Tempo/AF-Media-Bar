using System;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Services.Layout;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>
/// 胶囊歌词槽的**跟随式**滚动：一份文本、窗口起点由亮区反推，亮区永远停在可视区右缘附近。
/// The capsule lyric slot's **follow** scroll: one copy of the text, the window start solved backwards from the reveal, and the reveal always
/// resting near the container's right edge.
///
/// 与胶囊歌名那份**旋转式**（两份文本 + 循环位移）是两套语义，本测试断言的正是"紧跟擦亮、不循环"这一条：
/// 窗口永远是原文的后缀，起点只前进。
/// It is a different mode from the capsule title's **rotation** (two copies plus a cyclic shift), and what these tests pin down is exactly
/// "follows the reveal, never loops": the window is always a suffix of the content and its start only ever moves forward.
/// </summary>
[TestClass]
public sealed class CapsuleLyricFollowPolicyTests
{
    /// <summary>空表：还没有量过任何一个前缀。/ The empty table: no prefix measured yet.</summary>
    private static CapsuleLyricFollowPolicy.PrefixWidthTable Empty() => CapsuleLyricFollowPolicy.EmptyTable;

    /// <summary>测试用的测量键：真实调用方传「字号|字重|字体族」。/ The measure key these tests use; real callers pass "size|weight|family".</summary>
    private const string MeasureKey = "13|Normal|test";

    /// <summary>
    /// 前缀宽度表的有效性键必须包含**字号/字重/字体族**，不能只看文本：<c>ApplyFontScales</c> 会在 DPI 或显示器变化时改字号，
    /// 那时表里会混进两套度量（字形宽度、位移与裁剪边界全部错位），直到换行才恢复。
    /// The prefix-width table's validity key has to include the **font size, weight, and family**, not just the text: ApplyFontScales rewrites the
    /// font size on a DPI or monitor change, and without the key the table would then hold two sets of advances (glyph widths, offsets, and clip
    /// edges all misaligned) until the next line change.
    /// </summary>
    [TestMethod]
    public void MeasureKeyInvalidatesTheTableEvenWhenTheTextIsUnchanged()
    {
        const string content = "字号变了要重量";
        var calls = 0;
        double Measure(string text)
        {
            calls++;
            return text.Length * 10;
        }

        var table = CapsuleLyricFollowPolicy.EnsurePrefixWidths(Empty(), content, MeasureKey, 3, Measure);
        Assert.AreEqual(3, calls);
        Assert.AreEqual(MeasureKey, table.MeasureKey);

        // 同一文本、同一测量键：一个字符都不重量。
        // The same text under the same key measures nothing at all.
        table = CapsuleLyricFollowPolicy.EnsurePrefixWidths(table, content, MeasureKey, 3, Measure);
        Assert.AreEqual(3, calls);
        Assert.AreEqual(MeasureKey, table.MeasureKey);

        // 文本**没变**、字号键变了：整表重测。这正是 DPI/显示器变化那一帧，旧写法会一直用到换行为止。
        // The text is **unchanged** while the key moved: the whole table is re-measured. That is exactly the DPI/monitor frame, where the old code
        // kept using the stale table until the next line change.
        table = CapsuleLyricFollowPolicy.EnsurePrefixWidths(table, content, "17|Normal|test", 3, Measure);
        Assert.AreEqual("17|Normal|test", table.MeasureKey);
        Assert.AreEqual(6, calls, "测量键变了必须整表重测 / a changed measure key has to re-measure the whole table");

        // 走一帧也要认这个键：换键后 Advance 会把表重建，不会读到旧度量。
        // 整行 7 个字、进度 1 ⇒ 需要 8 个前缀（夹到 7），因此这一步量 7 次，累计 6 + 7 = 13。
        // 注意这里的量宽函数必须是**计数用的 Measure**，换成 TenPerCharacter 就只是在测"走了一帧"而已。
        // One frame has to honour the key as well: after a key change Advance rebuilds the table instead of reading stale advances. The line holds seven
        // characters and the progress is one, so eight prefixes are wanted (clamped to seven), which measures seven here for a running total of 6 + 7.
        // Note that the width function here has to be the **counting** Measure; passing TenPerCharacter would only test "a frame ran".
        var frame = CapsuleLyricFollowPolicy.Advance(
            content, "20|Bold|test", table, previousRevealWidth: 0, progress: 1, availableWidth: 100, fontSize: 20, Measure);
        Assert.AreEqual("20|Bold|test", frame.Table.MeasureKey);
        Assert.AreEqual(13, calls, "Advance 也必须按新键整表重测 / Advance has to re-measure for the new key too");
    }

    /// <summary>固定字宽的假量宽函数（每个字符 10 DIP），测试里不碰 WPF 的 <c>FormattedText</c>。/ A fake width function with a fixed advance (10 DIP per character), so no test touches WPF's FormattedText.</summary>
    private static double TenPerCharacter(string text) => text.Length * 10;

    /// <summary>
    /// 前缀宽度表**按需增长**：只量到本次真的需要的那个字符，已经量过的不重复量、也不会因为需求变少而丢弃。
    /// The prefix-width table **grows on demand**: only the characters really required this time are measured, nothing already measured is
    /// measured twice, and a smaller requirement never throws measurements away.
    /// </summary>
    [TestMethod]
    public void PrefixWidthsGrowOnDemandWithoutReMeasuring()
    {
        const string content = "你好世界啊";
        var calls = 0;
        double Measure(string text)
        {
            calls++;
            return TenPerCharacter(text);
        }

        var table = CapsuleLyricFollowPolicy.EnsurePrefixWidths(Empty(), content, MeasureKey, 2, Measure);
        Assert.AreEqual(2, table.MeasuredCharacters);
        Assert.AreEqual(20, table.Widths[2], 0.001);
        Assert.AreEqual(2, calls);
        // 第 0 项永远是 0（长度为 0 的前缀）。
        // The zeroth entry is always zero, the width of the empty prefix.
        Assert.AreEqual(0, table.Widths[0], 0.001);

        // 需求收缩：一个字符都不用再量。
        // A smaller requirement measures nothing at all.
        table = CapsuleLyricFollowPolicy.EnsurePrefixWidths(table, content, MeasureKey, 1, Measure);
        Assert.AreEqual(2, table.MeasuredCharacters);
        Assert.AreEqual(2, calls);

        // 需求增长：只补量差额（第 3、4 个前缀），前面两个不重复量。
        // A larger requirement measures only the difference, the third and fourth prefixes.
        table = CapsuleLyricFollowPolicy.EnsurePrefixWidths(table, content, MeasureKey, 4, Measure);
        Assert.AreEqual(4, table.MeasuredCharacters);
        Assert.AreEqual(40, table.Widths[4], 0.001);
        Assert.AreEqual(4, calls);

        // 越界需求夹到原文长度。
        // An out-of-range requirement clamps to the content length.
        table = CapsuleLyricFollowPolicy.EnsurePrefixWidths(table, content, MeasureKey, 99, Measure);
        Assert.AreEqual(content.Length, table.MeasuredCharacters);
        Assert.AreEqual(content.Length, calls);
    }

    /// <summary>
    /// 换了一行（哪怕长度恰好相同）必须整表重测：长度相等不能当成"同一份文本"，否则新行会读到上一行的字宽。
    /// A different line has to invalidate the whole table even when the length happens to match: equal length must not be read as "same text",
    /// or the new line would be measured with the previous line's advances.
    /// </summary>
    [TestMethod]
    public void PrefixWidthsAreRebuiltWhenTheContentChangesAtTheSameLength()
    {
        var table = CapsuleLyricFollowPolicy.EnsurePrefixWidths(Empty(), "abc", MeasureKey, 3, TenPerCharacter);
        Assert.AreEqual(30, table.Widths[3], 0.001);
        Assert.AreEqual("abc", table.Content);

        // 同样三个字符，但每个字符 100 DIP：整表必须重测。
        // The same three characters, but a hundred DIP each: the whole table has to be re-measured.
        table = CapsuleLyricFollowPolicy.EnsurePrefixWidths(table, "XYZ", MeasureKey, 3, text => text.Length * 100);
        Assert.AreEqual("XYZ", table.Content);
        Assert.AreEqual(3, table.MeasuredCharacters);
        Assert.AreEqual(300, table.Widths[3], 0.001);
    }

    /// <summary>
    /// 歌词行比文字槽短时窗口一动不动：不丢弃任何宽度、位移为 0、整行都在窗口里，擦亮只是从行首向右走。
    /// While the line is shorter than the slot nothing moves: no width is dropped, the offset stays zero, the whole line stays in the window,
    /// and the reveal merely walks right from the head.
    /// </summary>
    [TestMethod]
    public void ShortLineNeverScrolls()
    {
        const string content = "短短五个字";   // 5 字符 × 10 = 50 DIP
        var table = Empty();
        var reveal = 0d;

        for (var step = 0; step <= 10; step++)
        {
            var frame = CapsuleLyricFollowPolicy.Advance(
                content, MeasureKey, table, reveal, step / 10d, availableWidth: 100, fontSize: 13, TenPerCharacter);
            table = frame.Table;
            reveal = frame.RevealWidth;

            Assert.AreEqual(0, frame.WindowStart);
            Assert.AreEqual(content, frame.Window);
            Assert.AreEqual(0, frame.OffsetDip, 0.001);
            // 已唱宽度就是亮区本身（窗口左缘不丢弃任何宽度）。
            // The sung width is the reveal itself, because the window's left edge drops nothing.
            Assert.AreEqual(reveal, frame.ClipWidth, 0.001);
        }

        Assert.AreEqual(50, reveal, 0.001);
    }

    /// <summary>
    /// **裁定 7 的回归线**：亮区的裁剪宽度必须与前缀宽度表同口径（<c>revealed − windowLeft</c>），**不是**"进度 × 整行宽度"。
    /// 混排字宽下两者差得很远，只有前者能让亮区停在可视区右缘。
    /// **The regression line for ruling 7**: the reveal's clip width has to come from the same prefix-width table as the offset
    /// (<c>revealed − windowLeft</c>), **not** from "progress times the whole line's width". On mixed advances the two differ widely, and only the
    /// former puts the reveal edge at the container's right edge.
    /// </summary>
    [TestMethod]
    public void ClipWidthComesFromThePrefixTableNotFromProgressTimesTheWholeLine()
    {
        // 10 个字符：前 5 个窄（4 DIP），后 5 个宽（20 DIP），总宽 120 DIP。
        // Ten characters: the first five narrow (4 DIP) and the last five wide (20 DIP), a hundred and twenty DIP in total.
        const string content = "lllllWWWWW";
        double Measure(string text)
        {
            var sum = 0d;
            foreach (var character in text)
                sum += character == 'l' ? 4 : 20;
            return sum;
        }

        var frame = CapsuleLyricFollowPolicy.Advance(
            content, MeasureKey, Empty(), previousRevealWidth: 0, progress: 1, availableWidth: 50, fontSize: 10, Measure);

        // 每帧最多追赶 MaximumRevealAdvanceEm × 字号 = 1.6 × 10 = 16 DIP，因此第一帧的亮区宽度就是 16，而不是 120。
        // One frame catches up by at most MaximumRevealAdvanceEm times the font size, sixteen DIP, so the first frame's reveal is sixteen wide
        // rather than a hundred and twenty.
        Assert.AreEqual(16, frame.RevealWidth, 0.001);
        Assert.AreEqual(0, frame.WindowStart);
        Assert.AreEqual(0, frame.OffsetDip, 0.001);
        Assert.AreEqual(16, frame.ClipWidth, 0.001);
        // "进度 × 整行宽度"会给出 120：这一条断言就是在钉住"不许那样算"。
        // "Progress times the whole line" would answer a hundred and twenty: this assertion is what pins "never compute it that way".
        Assert.AreNotEqual(120, frame.ClipWidth, 5);
    }

    /// <summary>
    /// **裁定 7 的核心不变量**：窗口开始跟随后，亮区在屏幕上的右缘恒等于文字槽的 80%（<c>RevealEdgeRatio</c>）。
    /// 屏幕位置 = 裁剪宽度 + 位移，因此这一条同时钉住了"位移与裁剪必须同口径"。
    /// **The core invariant of ruling 7**: once the window follows, the reveal's right edge on screen always equals eighty percent of the text
    /// slot (<c>RevealEdgeRatio</c>). The screen position is the clip width plus the offset, so this single assertion also pins "the offset and
    /// the clip have to come from one table".
    /// </summary>
    [TestMethod]
    public void RevealEdgeRestsAtEightyPercentOfTheSlotOnceTheWindowFollows()
    {
        const int contentLength = 40;
        const double available = 100;
        const double fontSize = 10;
        var content = new string('字', contentLength);   // 每个字 10 DIP，总宽 400
        var table = Empty();
        var reveal = 0d;
        var followed = false;

        // 固定的进度 1：亮区的目标宽度是整行宽度，跟随者每帧最多追赶 16 DIP，于是可以逐帧观察它推进到右缘后窗口如何跟着左移。
        // A fixed progress of one: the reveal's target is the whole line and the follower catches up by at most sixteen DIP per frame, so the
        // window can be watched moving left once the reveal reaches the right edge.
        for (var frameIndex = 0; frameIndex < 40; frameIndex++)
        {
            var frame = CapsuleLyricFollowPolicy.Advance(
                content, MeasureKey, table, reveal, progress: 1, availableWidth: available, fontSize, TenPerCharacter);
            table = frame.Table;
            reveal = frame.RevealWidth;

            Assert.IsTrue(frame.OffsetDip <= 0.001, "位移只能是向左侧的 / the offset can only be leftwards");
            Assert.IsTrue(frame.ClipWidth >= 0, "裁剪宽度不允许为负 / the clip width never goes negative");
            Assert.IsTrue(
                frame.ClipWidth <= available + 0.001,
                $"亮区不允许越出文字槽：clip={frame.ClipWidth} / the reveal never leaves the slot");
            Assert.IsTrue(content.EndsWith(frame.Window, StringComparison.Ordinal), "窗口永远是原文的后缀 / the window is always a suffix");

            if (frame.WindowStart > 0)
                followed = true;

            // 窗口跟上之后，亮区右缘停在文字槽的 80% 处。
            // Once the window follows, the reveal's right edge rests at eighty percent of the slot.
            if (followed)
                Assert.AreEqual(0.8 * available, frame.ClipWidth + frame.OffsetDip, 0.001);
        }

        Assert.IsTrue(followed, "长行必须最终进入跟随状态 / a long line has to reach the follow state");
        Assert.IsTrue(reveal > 0.8 * available, "亮区必须真的推进过右缘 / the reveal has to move past the edge");
    }

    /// <summary>
    /// 窗口起点只前进、从不回头，而且左缘真的会一路更新（不是"亮区推出去之后文字就不动了"）。
    /// The window start only moves forward and never back, and the left edge really keeps updating, rather than "the text freezes once the reveal
    /// has been pushed out".
    /// </summary>
    [TestMethod]
    public void WindowStartOnlyMovesForwardAndTheLeftEdgeKeepsUpdating()
    {
        const int contentLength = 60;
        var content = new string('字', contentLength);
        var table = Empty();
        var reveal = 0d;
        var previousStart = 0;
        var distinctWindows = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);

        for (var step = 0; step <= contentLength; step++)
        {
            var frame = CapsuleLyricFollowPolicy.Advance(
                content, MeasureKey, table, reveal, progress: step / (double)contentLength, availableWidth: 100, fontSize: 10, TenPerCharacter);
            table = frame.Table;
            reveal = frame.RevealWidth;

            Assert.IsTrue(
                frame.WindowStart >= previousStart,
                $"窗口起点出现回退：{previousStart} → {frame.WindowStart} / the window start moved backwards");
            previousStart = frame.WindowStart;
            distinctWindows.Add(frame.Window);
        }

        // 左侧持续更新：一路走过来至少出现过几十种不同的窗口内容。
        // The left side keeps updating: dozens of distinct windows appear along the way.
        Assert.IsTrue(distinctWindows.Count > 20, $"窗口内容只有 {distinctWindows.Count} 种，说明窗口没有真的跟着走");
        Assert.IsTrue(previousStart > 0, "唱完之后窗口必须已经左移 / the window has to have moved left by the end");
    }

    /// <summary>
    /// 播放器的上报会回退（很多播放器按整秒上报），呈现层的亮区只前进：任何目标序列都不会让亮区或窗口往回跳。
    /// The player's reported position regresses, because many players report whole seconds; the presented reveal only moves forward, so no target
    /// sequence can make the reveal or the window jump back.
    /// </summary>
    [TestMethod]
    public void RevealAndWindowNeverMoveBackwardsUnderARegressingPosition()
    {
        var content = new string('字', 30);
        var table = Empty();
        var reveal = 0d;
        var previousStart = 0;

        foreach (var progress in new[] { 0.1, 0.6, 0.3, 0.9, 0.2, 1.0, 0.4, 0.0, 1.0 })
        {
            var frame = CapsuleLyricFollowPolicy.Advance(
                content, MeasureKey, table, reveal, progress, availableWidth: 100, fontSize: 10, TenPerCharacter);
            table = frame.Table;

            Assert.IsTrue(frame.RevealWidth >= reveal - 0.001, "亮区不允许回退 / the reveal never moves backwards");
            Assert.IsTrue(frame.WindowStart >= previousStart, "窗口起点不允许回退 / the window start never moves backwards");
            reveal = frame.RevealWidth;
            previousStart = frame.WindowStart;
        }
    }

    /// <summary>
    /// 边界输入不产生 NaN、不抛异常：空文本、零宽文字槽、非法进度都要能算出有限值。
    /// Boundary inputs produce no NaN and throw nothing: empty text, a zero-width slot, and an invalid progress all have to resolve to finite
    /// values.
    /// </summary>
    [TestMethod]
    public void BoundaryInputsStayFinite()
    {
        foreach (var (content, progress, available) in new[]
                 {
                     (string.Empty, 1d, 100d),
                     ("字", 1d, 0d),
                     ("字", double.NaN, 100d),
                     ("字", 0.5, double.NaN),
                     ("字", -5d, 50d),
                     ("字", 5d, 50d)
                 })
        {
            var frame = CapsuleLyricFollowPolicy.Advance(content, MeasureKey, Empty(), 0, progress, available, 13, TenPerCharacter);
            Assert.IsTrue(double.IsFinite(frame.OffsetDip), $"OffsetDip 不是有限值：{frame.OffsetDip}");
            Assert.IsTrue(double.IsFinite(frame.ClipWidth), $"ClipWidth 不是有限值：{frame.ClipWidth}");
            Assert.IsTrue(double.IsFinite(frame.RevealWidth), $"RevealWidth 不是有限值：{frame.RevealWidth}");
            Assert.IsTrue(frame.ClipWidth >= 0, "裁剪宽度不允许为负 / the clip width never goes negative");
            Assert.IsNotNull(frame.Window);
        }
    }

    /// <summary>
    /// 亮区的每帧追赶有上限（<c>MaximumRevealAdvanceEm</c> × 字号）：跟随式靠它吸收播放器上报的台阶，一次上报不会把整行推走。
    /// The reveal's per-frame catch-up is capped at <c>MaximumRevealAdvanceEm</c> times the font size: that is what absorbs the steps in the
    /// player's reporting, so one report never pushes the whole line in a single frame.
    /// </summary>
    [TestMethod]
    public void RevealCatchesUpByAtMostTheConfiguredEmPerFrame()
    {
        var content = new string('字', 100);   // 总宽 1000 DIP
        var frame = CapsuleLyricFollowPolicy.Advance(
            content, MeasureKey, Empty(), 0, progress: 1, availableWidth: 100, fontSize: 13, TenPerCharacter);

        Assert.AreEqual(MarqueeFollowPolicy.MaximumRevealAdvanceEm * 13, frame.RevealWidth, 0.001);
    }
}
