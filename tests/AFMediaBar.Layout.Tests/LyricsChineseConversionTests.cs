// 验证歌词繁简转换只改写"送进呈现层"的那份文本，并且当前行、下一句、译文共用同一个设置；不访问网络。
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services.Lyrics;
using AFMediaBar.Classes.Settings;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Reflection;

namespace AFMediaBar.Layout.Tests;

[TestClass]
[DoNotParallelize]
public sealed class LyricsChineseConversionTests
{
    private static readonly DateTimeOffset _now = new(2026, 9, 23, 8, 0, 0, TimeSpan.Zero);

    // 等待真实后台转换，投影断言只检查已准备好的结果；非阻塞行为由独立缓存回归覆盖。
    [TestInitialize]
    public async Task Initialize()
    {
        string[] texts = ["我在这里等你", "数据库连接池", "岁月如歌", "下一句译文",
            "我在這裡等你", "資料庫連接池", "歲月如歌", "下一句譯文"];
        foreach (var mode in new[] { LyricsChineseConversionMode.SimplifiedToTraditional, LyricsChineseConversionMode.TraditionalToSimplified })
            foreach (var text in texts)
                _ = LyricsChineseConverter.Convert(text, mode);
        await LyricsChineseConverter.WaitForIdleAsync().WaitAsync(TimeSpan.FromSeconds(10));
    }

    [TestMethod]
    public async Task ConversionDoesNotWaitForConverterPublication()
    {
        // 控制初始化的发布锁，验证呈现调用不会等待后台加载或发布。
        var sync = typeof(LyricsChineseConverter).GetField("_sync", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        using var acquired = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var publisher = Task.Run(() =>
        {
            lock (sync)
            {
                acquired.Set();
                Assert.IsTrue(release.Wait(TimeSpan.FromSeconds(10)), "发布锁未及时释放。");
            }
        });
        Task<string>? conversion = null;
        try
        {
            Assert.IsTrue(acquired.Wait(TimeSpan.FromSeconds(5)), "后台未取得发布锁。");
            conversion = Task.Run(() => LyricsChineseConverter.Convert(
                "我在这里等你", LyricsChineseConversionMode.SimplifiedToTraditional));
            Assert.AreEqual("我在這裡等你", await conversion.WaitAsync(TimeSpan.FromSeconds(2)));
        }
        finally
        {
            release.Set();
            await publisher.WaitAsync(TimeSpan.FromSeconds(5));
            if (conversion is not null)
                await conversion.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [TestMethod]
    public void ConversionRewritesEveryPresentedText()
    {
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
    public void ConversionRewritesInTheTraditionalToSimplifiedDirection()
    {
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
    public void ConversionDisabledKeepsOriginalTextAndLatinUntouched()
    {
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
        }, _now);

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
        }, _now);

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
        _now);
}
