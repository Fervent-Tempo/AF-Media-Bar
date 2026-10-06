using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services.Lyrics;
using AFMediaBar.Classes.Settings;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

[TestClass]
public sealed class LyricsPresentationProjectorTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 23, 8, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void LineSyncedLyricsNeverProduceWordScanProgress()
    {
        var snapshot = Snapshot([
            new LyricLine(0, 10, "first"),
            new LyricLine(10, 20, "second")
        ], position: 5);

        var frame = LyricsPresentationProjector.Project(snapshot, new AppSettings
        {
            LyricsEnabled = true,
            LyricsSyllableHighlightEnabled = true
        }, Now);

        Assert.IsTrue(frame.IsVisible);
        Assert.IsNull(frame.WordScanProgress);
        Assert.AreEqual(0.5, frame.LineProgress, 0.0001);
    }

    [TestMethod]
    public void SyllableLyricsProduceRealWordScanProgress()
    {
        var line = new LyricLine(0, 10, "AB")
        {
            Words = [new LyricWord(0, 2, "A"), new LyricWord(2, 4, "B")]
        };
        var snapshot = Snapshot([line], position: 1);

        var frame = LyricsPresentationProjector.Project(snapshot, new AppSettings
        {
            LyricsEnabled = true,
            LyricsSyllableHighlightEnabled = true
        }, Now);

        Assert.AreEqual(0.25, frame.WordScanProgress!.Value, 0.0001);
    }

    [TestMethod]
    public void TranslationSelectionUsesTranslationPairLayout()
    {
        var snapshot = Snapshot([
            new LyricLine(0, 10, "原文") { Translation = "translation" },
            new LyricLine(10, 20, "下一句") { Translation = "next translation" }
        ], position: 2);
        var settings = new AppSettings
        {
            LyricsEnabled = true,
            TwoLineLyricsEnabled = true,
            LyricsSecondaryLine = new LyricsSecondaryLineSettings([LyricsSecondaryLineMode.Translation])
        };

        var frame = LyricsPresentationProjector.Project(snapshot, settings, Now);

        Assert.IsTrue(frame.TranslationMode);
        Assert.AreEqual("translation", frame.CurrentTranslation);
        Assert.AreEqual("next translation", frame.NextTranslation);
        Assert.AreEqual(string.Empty, frame.Next);
    }

    [TestMethod]
    public void NextLineSelectionUsesSingleLayout()
    {
        var snapshot = Snapshot([
            new LyricLine(0, 10, "first"),
            new LyricLine(10, 20, "second")
        ], position: 2);
        var settings = new AppSettings
        {
            LyricsEnabled = true,
            TwoLineLyricsEnabled = true,
            LyricsSecondaryLine = new LyricsSecondaryLineSettings([LyricsSecondaryLineMode.NextLine])
        };

        var frame = LyricsPresentationProjector.Project(snapshot, settings, Now);

        Assert.IsFalse(frame.TranslationMode);
        Assert.AreEqual("second", frame.Next);
        Assert.AreEqual(string.Empty, frame.CurrentTranslation);
    }

    [TestMethod]
    public void MissingTranslationAndDisabledSecondRowProduceOnlyTheOriginal()
    {
        var snapshot = Snapshot([
            new LyricLine(0, 10, "中文原文") { Translation = " // " },
            new LyricLine(10, 20, "next") { Translation = "下一句译文" }
        ], position: 2);
        var settings = new AppSettings
        {
            LyricsEnabled = true,
            TwoLineLyricsEnabled = true,
            LyricsSecondaryLine = new LyricsSecondaryLineSettings([LyricsSecondaryLineMode.Translation])
        };

        var missingTranslation = LyricsPresentationProjector.Project(snapshot, settings, Now);
        Assert.AreEqual("中文原文", missingTranslation.Current);
        Assert.AreEqual(string.Empty, missingTranslation.Next);
        Assert.AreEqual(string.Empty, missingTranslation.CurrentTranslation);
        Assert.IsFalse(missingTranslation.TranslationMode);
        Assert.AreEqual("下一句译文", missingTranslation.NextTranslation);

        settings.TwoLineLyricsEnabled = false;
        var singleRow = LyricsPresentationProjector.Project(snapshot, settings, Now);
        Assert.AreEqual("中文原文", singleRow.Current);
        Assert.AreEqual(string.Empty, singleRow.Next);
        Assert.AreEqual(string.Empty, singleRow.CurrentTranslation);
        Assert.AreEqual(string.Empty, singleRow.NextTranslation);
        Assert.IsFalse(singleRow.TranslationMode);
    }

    [TestMethod]
    public void DisabledLyricsProduceHiddenFrame()
    {
        var frame = LyricsPresentationProjector.Project(
            Snapshot([new LyricLine(0, 10, "first")], position: 2),
            new AppSettings { LyricsEnabled = false },
            Now);

        Assert.IsFalse(frame.IsVisible);
    }

    [TestMethod]
    public void ChineseConversionRewritesEveryPresentedText()
    {
        // 这里同步加载转换器：生产路径由投影自身转到后台加载（见 LyricsChineseConverter.WarmUp），
        // 若测试也走那条路，断言会在词典就绪之前跑完，结果就是随机成败。
        // Loaded synchronously here: production lets projection itself kick off the background load (see
        // LyricsChineseConverter.WarmUp); if the test took that path too, the assertions would run before the dictionary
        // is ready and would pass or fail at random.
        LyricsChineseConverter.LoadNow();

        var snapshot = Snapshot([
            new LyricLine(0, 10, "我在这里等你") { Translation = "数据库连接池" },
            new LyricLine(10, 20, "岁月如歌") { Translation = "下一句译文" }
        ], position: 2);

        var nextLine = ProjectWith(snapshot, LyricsChineseConversionMode.SimplifiedToTraditional, LyricsSecondaryLineMode.NextLine);
        Assert.AreEqual("我在這裡等你", nextLine.Current);
        Assert.AreEqual("歲月如歌", nextLine.Next);

        // 译文走同一个设置，否则会出现"主行转了、第二行没转"的半截画面。
        // The translations follow the same setting, otherwise the frame would show a half-converted pair.
        var translation = ProjectWith(snapshot, LyricsChineseConversionMode.SimplifiedToTraditional, LyricsSecondaryLineMode.Translation);
        Assert.AreEqual("我在這裡等你", translation.Current);
        Assert.AreEqual("數據庫連接池", translation.CurrentTranslation);
        Assert.AreEqual("下一句譯文", translation.NextTranslation);
    }

    [TestMethod]
    public void ChineseConversionRewritesInTheTraditionalToSimplifiedDirection()
    {
        LyricsChineseConverter.LoadNow();

        var snapshot = Snapshot([
            new LyricLine(0, 10, "我在這裡等你") { Translation = "資料庫連接池" },
            new LyricLine(10, 20, "歲月如歌") { Translation = "下一句譯文" }
        ], position: 2);

        var nextLine = ProjectWith(snapshot, LyricsChineseConversionMode.TraditionalToSimplified, LyricsSecondaryLineMode.NextLine);
        Assert.AreEqual("我在这里等你", nextLine.Current);
        Assert.AreEqual("岁月如歌", nextLine.Next);

        var translation = ProjectWith(snapshot, LyricsChineseConversionMode.TraditionalToSimplified, LyricsSecondaryLineMode.Translation);
        // 注意这里是"资料库"而不是"数据库"：标准 t2s 只做逐字转换，不含地区用词对照
        // （正如 t2s 不会把"滑鼠"改成"鼠标"）。写死这条正是为了把这个行为钉住，改起来能立刻发现。
        // Note "资料库" rather than "数据库": the standard t2s config converts character by character and carries no regional
        // vocabulary mapping (the same reason t2s leaves 滑鼠 alone). Pinning this down means a behaviour change is noticed at once.
        Assert.AreEqual("资料库连接池", translation.CurrentTranslation);
        Assert.AreEqual("下一句译文", translation.NextTranslation);
    }

    [TestMethod]
    public void ChineseConversionDisabledKeepsOriginalTextAndLatinUntouched()
    {
        LyricsChineseConverter.LoadNow();

        var snapshot = Snapshot([
            new LyricLine(0, 10, "我在这里等你") { Translation = "数据库连接池" },
            new LyricLine(10, 20, "Never Gonna Give You Up") { Translation = "no conversion here" }
        ], position: 2);

        // 默认即为 None：升级前的行为必须逐字不变。
        // None is the default: the behaviour before this setting existed has to stay character for character.
        var frame = LyricsPresentationProjector.Project(snapshot, new AppSettings
        {
            LyricsEnabled = true,
            TwoLineLyricsEnabled = true,
            LyricsSecondaryLine = new LyricsSecondaryLineSettings([LyricsSecondaryLineMode.NextLine])
        }, Now);

        Assert.AreEqual("我在这里等你", frame.Current);
        Assert.AreEqual("Never Gonna Give You Up", frame.Next);

        // 即使开了转换，纯拉丁文本也应当保持原样。
        // Latin-only text stays untouched even with conversion on.
        var latinOnly = ProjectWith(
            Snapshot([new LyricLine(0, 10, "Never Gonna Give You Up")], position: 2),
            LyricsChineseConversionMode.SimplifiedToTraditional,
            LyricsSecondaryLineMode.NextLine);
        Assert.AreEqual("Never Gonna Give You Up", latinOnly.Current);
    }

    private static LyricsPresentationFrame ProjectWith(
        MediaSnapshot snapshot,
        LyricsChineseConversionMode conversion,
        LyricsSecondaryLineMode secondaryLine) =>
        LyricsPresentationProjector.Project(snapshot, new AppSettings
        {
            LyricsEnabled = true,
            TwoLineLyricsEnabled = true,
            LyricsSecondaryLine = new LyricsSecondaryLineSettings([secondaryLine]),
            LyricsChineseConversion = conversion
        }, Now);

    private static MediaSnapshot Snapshot(IReadOnlyList<LyricLine> lines, double position) => new(
        true,
        true,
        true,
        true,
        true,
        "title",
        "artist",
        "source",
        "Source",
        null,
        new LyricsResult("test", new LyricDocument(lines, LyricsSyncType.SyllableSynced, "test")),
        position,
        30,
        true,
        false,
        MediaRepeatMode.Off,
        1,
        Now);
}
