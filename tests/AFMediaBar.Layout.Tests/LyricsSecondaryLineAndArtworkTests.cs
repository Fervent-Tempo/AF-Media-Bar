using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Services.Layout;
using AFMediaBar.Classes.Services.Lyrics;
using AFMediaBar.Classes.Settings;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>
/// 第二行歌词的取词顺序（首选项 + 固定回退链）与封面框的尺寸策略。
/// The second lyric line's source order (a preference plus a fixed fallback chain) and the artwork box sizing policy.
/// </summary>
[TestClass]
public sealed class LyricsSecondaryLineAndArtworkTests
{
    /// <summary>
    /// 顺序即优先级：排在最前面的来源只要有内容就用它，无论其他来源有没有内容。
    /// The order is the priority: the source listed first is used whenever it has content, regardless of the others.
    /// </summary>
    [TestMethod]
    public void TheFirstSourceWithContentWins()
    {
        var next = new LyricsSecondaryLineSettings([LyricsSecondaryLineMode.NextLine]);
        var translation = new LyricsSecondaryLineSettings([LyricsSecondaryLineMode.Translation]);
        var romanization = new LyricsSecondaryLineSettings([LyricsSecondaryLineMode.Romanization]);

        Assert.AreEqual("下一句", LyricsSecondaryLinePolicy.Resolve(next, "下一句", "译文", "yinyi"));
        Assert.AreEqual("译文", LyricsSecondaryLinePolicy.Resolve(translation, "下一句", "译文", "yinyi"));
        Assert.AreEqual("yinyi", LyricsSecondaryLinePolicy.Resolve(romanization, "下一句", "译文", "yinyi"));
    }

    /// <summary>
    /// 某个来源没有内容时按列表顺序继续往下取：默认顺序是 下一句 → 翻译 → 音译，用户排的顺序优先。
    /// A source without content falls through to the next one in the list: the default order is next line, translation, romanization, and the order
    /// the user arranged wins.
    /// </summary>
    [TestMethod]
    public void MissingSourcesFallThroughInOrder()
    {
        var order = new LyricsSecondaryLineSettings(
        [
            LyricsSecondaryLineMode.Translation,
            LyricsSecondaryLineMode.Romanization,
            LyricsSecondaryLineMode.NextLine
        ]);

        // 首选翻译但这一句没有译文：先退到音译。
        // The list starts with the translation while this line has none: the romanization comes next.
        Assert.AreEqual("yinyi", LyricsSecondaryLinePolicy.Resolve(order, "下一句", null, "yinyi"));
        Assert.AreEqual("下一句", LyricsSecondaryLinePolicy.Resolve(order, "下一句", null, null));

        // 用户把"下一句"排到最前：它优先，即使有译文。
        // The user put the next line first: it wins even though a translation exists.
        var nextFirst = new LyricsSecondaryLineSettings(
        [
            LyricsSecondaryLineMode.NextLine,
            LyricsSecondaryLineMode.Translation,
            LyricsSecondaryLineMode.Romanization
        ]);
        Assert.AreEqual("下一句", LyricsSecondaryLinePolicy.Resolve(nextFirst, "下一句", "译文", "yinyi"));

        // 未列出的来源不会被使用：只留音译时，即使有译文也只显示音译。
        // A source missing from the list is never used: with only the romanization listed, a translation is ignored.
        var onlyRomanization = new LyricsSecondaryLineSettings([LyricsSecondaryLineMode.Romanization]);
        Assert.AreEqual("yinyi", LyricsSecondaryLinePolicy.Resolve(onlyRomanization, "下一句", "译文", "yinyi"));
        Assert.AreEqual(string.Empty, LyricsSecondaryLinePolicy.Resolve(onlyRomanization, "下一句", "译文", null));

        // 未配置（null）时使用默认顺序。
        // Nothing configured (null) means the default order.
        CollectionAssert.AreEqual(
            new[]
            {
                LyricsSecondaryLineMode.NextLine,
                LyricsSecondaryLineMode.Translation,
                LyricsSecondaryLineMode.Romanization
            },
            LyricsSecondaryLinePolicy.ResolveOrder(LyricsSecondaryLineSettings.Default).ToArray());
        Assert.AreEqual(
            "下一句",
            LyricsSecondaryLinePolicy.Resolve(LyricsSecondaryLineSettings.Default, "下一句", "译文", null));

        // 显式的空数组表示一个来源都不用：第二行不显示，不会悄悄退回默认顺序。
        // An explicit empty array means no source at all: the second line stays hidden instead of silently reverting to the default order.
        var none = new LyricsSecondaryLineSettings([]);
        Assert.AreEqual(0, LyricsSecondaryLinePolicy.ResolveOrder(none).Count);
        Assert.AreEqual(string.Empty, LyricsSecondaryLinePolicy.Resolve(none, "下一句", "译文", "yinyi"));
    }

    /// <summary>
    /// 所有来源都为空（或只有空白）时返回空串：调用方据此隐藏第二行，而不是留一个空白占位。
    /// All sources being empty, or whitespace only, returns an empty string so the caller hides the row instead of leaving a blank placeholder.
    /// </summary>
    [TestMethod]
    public void EmptySourcesHideTheSecondLine()
    {
        var order = LyricsSecondaryLineSettings.Default;
        Assert.AreEqual(string.Empty, LyricsSecondaryLinePolicy.Resolve(order, null, null, null));
        Assert.AreEqual(string.Empty, LyricsSecondaryLinePolicy.Resolve(order, "  ", "", " "));
        // 空白不算内容，因此会继续回退到真的有内容的来源。
        // Whitespace is not content, so the fallback continues to a source that really has some.
        Assert.AreEqual("下一句", LyricsSecondaryLinePolicy.Resolve(order, "下一句", "   ", null));
    }

    /// <summary>
    /// 封面框按封面比例算宽度：正方形不变、宽封面变宽、竖版变窄，且都不需要留白；超出范围才留白。
    /// The artwork box follows the cover's aspect: square stays square, a wide cover widens, a portrait narrows, none of them letterboxed;
    /// only an out-of-range aspect letterboxes.
    /// </summary>
    [TestMethod]
    public void ArtworkBoxFollowsTheCoverAspect()
    {
        var square = ArtworkBoxPolicy.Resolve(36, 600, 600);
        Assert.AreEqual(36, square.Width, 0.001);
        Assert.IsFalse(square.Letterbox);

        // 16:9 视频封面（1.78）仍在范围内：宽度按比例，不留白也不裁切。
        // A 16:9 video cover at 1.78 is still inside the range: the width follows the ratio with neither letterboxing nor cropping.
        var wide = ArtworkBoxPolicy.Resolve(36, 1920, 1080);
        Assert.AreEqual(64, wide.Width, 0.01);
        Assert.IsFalse(wide.Letterbox);

        // 21:9 超宽封面：夹到上限 1.8，因为超出的部分只能留白。
        // A 21:9 ultra-wide cover clamps to 1.8, since the rest would have to letterbox.
        var ultraWide = ArtworkBoxPolicy.Resolve(36, 2560, 1080);
        Assert.AreEqual(36 * 1.8, ultraWide.Width, 0.001);
        Assert.IsTrue(ultraWide.Letterbox);

        // 4:3 与 3:2 都在范围内：宽度按比例、不留白、也不裁切。
        // 4:3 and 3:2 are inside the range: the width follows the ratio with neither letterboxing nor cropping.
        var fourThree = ArtworkBoxPolicy.Resolve(36, 4, 3);
        Assert.AreEqual(48, fourThree.Width, 0.001);
        Assert.IsFalse(fourThree.Letterbox);

        var threeTwo = ArtworkBoxPolicy.Resolve(36, 3, 2);
        Assert.AreEqual(54, threeTwo.Width, 0.001);
        Assert.IsFalse(threeTwo.Letterbox);

        // 竖版海报 2:3：变窄但不留白。
        // A 2:3 portrait poster narrows without letterboxing.
        var portrait = ArtworkBoxPolicy.Resolve(36, 2, 3);
        Assert.AreEqual(24, portrait.Width, 0.001);
        Assert.IsFalse(portrait.Letterbox);

        // 极端细高：夹到下限 0.6 并留白。
        // An extremely tall cover clamps to 0.6 and letterboxes.
        var tall = ArtworkBoxPolicy.Resolve(36, 200, 1000);
        Assert.AreEqual(36 * 0.6, tall.Width, 0.001);
        Assert.IsTrue(tall.Letterbox);

        // 没有封面（尺寸无效）时保持正方形，占位音符居中。
        // Without artwork, an unusable size, the box stays square so the placeholder note stays centred.
        Assert.AreEqual(36, ArtworkBoxPolicy.Resolve(36, 0, 0).Width, 0.001);
        Assert.AreEqual(36, ArtworkBoxPolicy.Resolve(36, -5, 10).Width, 0.001);
        Assert.AreEqual(0, ArtworkBoxPolicy.Resolve(double.NaN, 100, 100).Width, 0.001);
    }
}
