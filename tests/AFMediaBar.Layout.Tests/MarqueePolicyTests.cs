using System.Globalization;
using AFMediaBar.Classes.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>标题与歌手轮转跑马灯的纯策略测试。/ Pure policy tests for the title/artist rotation marquee.</summary>
[TestClass]
public sealed class MarqueePolicyTests
{
    [TestMethod]
    public void RotationWindowWrapsContentAndKeepsTheSeam()
    {
        const string content = "ABCDE";

        Assert.AreEqual("ABCDE   ", MarqueeRotationPolicy.BuildWindow(content, 0));
        Assert.AreEqual("BCDE   A", MarqueeRotationPolicy.BuildWindow(content, 1));
        Assert.AreEqual("DE   ABC", MarqueeRotationPolicy.BuildWindow(content, 3));
        Assert.AreEqual("ABCDE   ", MarqueeRotationPolicy.BuildWindow(content, 8));
        Assert.AreEqual(MarqueeRotationPolicy.BuildWindow(content, -1), MarqueeRotationPolicy.BuildWindow(content, 7));
        Assert.AreEqual(string.Empty, MarqueeRotationPolicy.BuildWindow(string.Empty, 3));
        Assert.AreEqual(8, MarqueeRotationPolicy.ResolveWindowLength(content.Length));
    }

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

    [TestMethod]
    public void TimingAdvancesAtConstantPixelSpeed()
    {
        Assert.AreEqual(16, MarqueeTiming.FrameInterval.TotalMilliseconds, 0.001);
        Assert.AreEqual(60, MarqueeTiming.ScrollSpeedDipPerSecond, 0.001);
        Assert.AreEqual(0.96, MarqueeTiming.DipPerFrame, 0.001);
        Assert.IsTrue(MarqueeTiming.DipPerFrame / 26 < MarqueeTiming.DipPerFrame / 6);
        Assert.IsTrue(MarqueeTiming.LeadInDuration > TimeSpan.Zero);
    }

    [TestMethod]
    public void PrefixWidthsInterpolateFractionalPositions()
    {
        var widths = new[] { 0d, 10, 20, 30, 40, 50, 60, 70, 80 };

        Assert.AreEqual(0, MarqueeRotationPolicy.ResolveWidthAt(widths, 8, 0), 0.001);
        Assert.AreEqual(30, MarqueeRotationPolicy.ResolveWidthAt(widths, 8, 3), 0.001);
        Assert.AreEqual(25, MarqueeRotationPolicy.ResolveWidthAt(widths, 8, 2.5), 0.001);
        Assert.AreEqual(40, MarqueeRotationPolicy.ResolveWidthAt(widths, 4, 99), 0.001);
        Assert.AreEqual(0, MarqueeRotationPolicy.ResolveWidthAt(null, 8, 3), 0.001);
    }

    [TestMethod]
    public void RotationNeverSplitsSurrogatesOrCombiningMarks()
    {
        const string content = "夜🌟航☺\uFE0F星";
        var sourceElements = TextElements(content + MarqueeRotationPolicy.Separator);
        var windowLength = MarqueeRotationPolicy.ResolveWindowLength(content.Length);

        for (var offset = 0; offset < windowLength * 2; offset++)
        {
            CollectionAssert.AreEquivalent(
                sourceElements,
                TextElements(MarqueeRotationPolicy.BuildWindow(content, offset)));
        }
    }

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
}
