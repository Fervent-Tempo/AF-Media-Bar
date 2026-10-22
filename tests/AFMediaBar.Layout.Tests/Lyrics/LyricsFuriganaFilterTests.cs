// 验证显示过滤不破坏来源文档、跨音节时间轴及用户保存的开关；不连接歌词来源。
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Services.Localization;
using AFMediaBar.Classes.Services.Lyrics;
using AFMediaBar.Classes.Services.Settings;
using AFMediaBar.Classes.Settings;
using AFMediaBar.ViewModels.Pages;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>日语振假名显示过滤、逐字进度与设置兼容回归。</summary>
[TestClass, DoNotParallelize]
public sealed class LyricsFuriganaFilterTests
{
    [TestMethod]
    public void ProjectionFiltersOnlyKanaAnnotationsAndPreservesSourceAndSecondaryText()
    {
        const string Original = "目覚(める)と日々（ひび） (oh yeah)（中文）(かなmix) (かな！)";
        var line = new LyricLine(0, 10, Original) { Translation = "訳(やく)", Romanization = "yaku（ヤク）" };
        var next = new LyricLine(10, 20, "声（コエ）と歌(ｳﾀ)");
        var snapshot = Snapshot([line, next], 2);
        var settings = new AppSettings { LyricsFuriganaFilterEnabled = true, LyricsSecondaryLine = new([LyricsSecondaryLineMode.NextLine]) };
        var frame = LyricsPresentationProjector.Project(snapshot, settings, DateTimeOffset.UtcNow);
        Assert.AreEqual("目覚と日々 (oh yeah)（中文）(かなmix) (かな！)", frame.Current);
        Assert.AreEqual("声と歌", frame.Next);
        Assert.IsNull(frame.WordScanProgress, "行级歌词不能因过滤而产生伪造的逐字进度。");
        Assert.AreEqual(Original, line.Text);
        Assert.AreEqual("声（コエ）と歌(ｳﾀ)", next.Text);
        settings.LyricsSecondaryLine = new([LyricsSecondaryLineMode.Translation]);
        Assert.AreEqual(line.Translation, LyricsPresentationProjector.Project(snapshot, settings, DateTimeOffset.UtcNow).CurrentTranslation);
        settings.LyricsSecondaryLine = new([LyricsSecondaryLineMode.Romanization]);
        Assert.AreEqual(line.Romanization, LyricsPresentationProjector.Project(snapshot, settings, DateTimeOffset.UtcNow).CurrentTranslation);
        settings.LyricsFuriganaFilterEnabled = false;
        var original = LyricsPresentationProjector.Project(snapshot, settings, DateTimeOffset.UtcNow);
        Assert.AreEqual(Original, original.Current);
        Assert.AreEqual(frame.LineProgress, original.LineProgress);
        foreach (var retained in new[] { "(かな)", "目(かな）", "目(かな", "目()", "目(か な)", "目(・)" })
            Assert.AreEqual(retained, LyricsFuriganaFilter.Apply(new LyricLine(0, 10, retained)).Text);
    }

    [TestMethod]
    public void SplitAnnotationsUseVisibleWordWeightsWithoutChangingTimestamps()
    {
        LyricWord[] words = [new(0, 1, "日"), new(1, 2, "々(ひ"), new(2, 3, "び)"), new(3, 4, "の"), new(4, 5, "歌")];
        var line = new LyricLine(0, 5, "日々(ひび)の歌") { Words = words };
        var filtered = LyricsFuriganaFilter.Apply(line);
        Assert.AreEqual("日々の歌", filtered.Text);
        for (var index = 0; index < words.Length; index++)
        {
            Assert.AreEqual(words[index].Start, filtered.Words[index].Start);
            Assert.AreEqual(words[index].End, filtered.Words[index].End);
        }
        var settings = new AppSettings { LyricsFuriganaFilterEnabled = true };
        var singing = LyricsPresentationProjector.Project(Snapshot([line], 1.5), settings, DateTimeOffset.UtcNow);
        Assert.AreEqual(0.375, singing.WordScanProgress!.Value, 0.000001);
        var annotation = LyricsPresentationProjector.Project(Snapshot([line], 2.5), settings, DateTimeOffset.UtcNow);
        Assert.AreEqual(0.5, annotation.WordScanProgress!.Value, 0.000001);
        settings.LyricsFuriganaFilterEnabled = false;
        var original = LyricsPresentationProjector.Project(Snapshot([line], 2.5), settings, DateTimeOffset.UtcNow);
        Assert.AreEqual(0.625, original.WordScanProgress!.Value, 0.000001);
        CollectionAssert.AreEqual(words, line.Words.ToArray());
    }

    [TestMethod]
    public void KanaBracketStaysWhenTheTranslationCarriesTheSameInterjection()
    {
        // 译文行同样带括号内容时，主行的假名括号是唱出来的人声而不是标注音：删掉会和译文对不上。
        var line = new LyricLine(0, 5, "ミニスカートで襲撃だ！（キュン）") { Translation = "穿着迷你裙袭来！（心跳~）" };
        Assert.AreEqual("ミニスカートで襲撃だ！（キュン）", LyricsFuriganaFilter.Apply(line).Text);

        // 未翻译、原样保留在译文里的人声同样保留。
        var kept = new LyricLine(0, 5, "ミニスカートで襲撃だ！（キュン）") { Translation = "袭击！（キュン）" };
        Assert.AreEqual("ミニスカートで襲撃だ！（キュン）", LyricsFuriganaFilter.Apply(kept).Text);

        // 译文没有任何括号，或括号里是纯假名注音（如「訳(やく)」）时，照旧按标注音隐藏。
        var plain = new LyricLine(0, 5, "日々(ひび)の歌") { Translation = "日日的歌" };
        Assert.AreEqual("日々の歌", LyricsFuriganaFilter.Apply(plain).Text);
        var noted = new LyricLine(0, 5, "目覚(める)と日々（ひび）") { Translation = "訳(やく)" };
        Assert.AreEqual("目覚と日々", LyricsFuriganaFilter.Apply(noted).Text);

        // 呈现层：译文作次行时两边的括号都在。
        var snapshot = Snapshot([line], 2);
        var settings = new AppSettings { LyricsFuriganaFilterEnabled = true, LyricsSecondaryLine = new([LyricsSecondaryLineMode.Translation]) };
        var frame = LyricsPresentationProjector.Project(snapshot, settings, DateTimeOffset.UtcNow);
        Assert.AreEqual("ミニスカートで襲撃だ！（キュン）", frame.Current);
        Assert.AreEqual("穿着迷你裙袭来！（心跳~）", frame.CurrentTranslation);
    }

    [TestMethod]
    public void TranslationBracketKeepsTheWholeLineEvenWhenItAlsoHidesAnAnnotation()
    {
        // 取舍：判定按行进行，不逐括号配对。译文带括号人声时，同行里的真标注音也会一起保留——
        // 留着标注音只是碍眼，删掉人声却会和译文对不上，所以选轻的一侧。
        var mixed = new LyricLine(0, 5, "歌(ｳﾀ)！（キュン）") { Translation = "唱歌！（心动）" };
        Assert.AreEqual("歌(ｳﾀ)！（キュン）", LyricsFuriganaFilter.Apply(mixed).Text);

        // 译文保留英文人声（括号内非假名）时同样触发保留。
        var english = new LyricLine(0, 5, "日々(ひび)の歌") { Translation = "日日 (oh yeah)" };
        Assert.AreEqual("日々(ひび)の歌", LyricsFuriganaFilter.Apply(english).Text);
    }

    [TestMethod]
    public void PersistenceAndLyricsResetPreserveTheExplicitChoiceAndInactiveEditorsCannotWrite()
    {
        StaTest.Run(_ =>
        {
            var previous = SettingsManager.Current;
            var previousDefaults = SettingsManager.UserDefaults;
            var directory = Path.Combine(Path.GetTempPath(), "AFMediaBar-furigana-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                using var store = new SettingsPersistenceService(directory);
                File.WriteAllText(Path.Combine(directory, "user-defaults.json"), "{\"schemaVersion\":2,\"settings\":{\"lyricsEnabled\":false}}");
                var legacy = store.LoadUserDefaults();
                Assert.IsNotNull(legacy);
                Assert.IsFalse(legacy.LyricsFuriganaFilterEnabled);
                Assert.IsFalse(legacy.LyricsEnabled);
                SettingsManager.Current = legacy;
                using var context = new SettingsPageContext();
                using var localization = new LocalizationService();
                using var editor = new LyricsViewModel(localization, new LegacySettingsConfiguration(context));
                editor.FuriganaFilterEnabled = true;
                Assert.IsNull(store.SaveCurrentAsUserDefaults());
                Assert.IsTrue(store.LoadUserDefaults()!.LyricsFuriganaFilterEnabled);
                editor.FuriganaFilterEnabled = false;
                SettingsManager.ResetLyrics();
                Assert.IsTrue(editor.FuriganaFilterEnabled, "歌词重置应采用已保存的用户默认值。");
                SettingsManager.SetUserDefaults(null);
                SettingsManager.ResetLyrics();
                Assert.IsFalse(editor.FuriganaFilterEnabled);
                context.Deactivate();
                editor.FuriganaFilterEnabled = true;
                Assert.IsFalse(SettingsManager.Current.LyricsFuriganaFilterEnabled);
                Assert.IsFalse(LyricsCacheInvalidationPolicy.ShouldClearCache(nameof(AppSettings.LyricsFuriganaFilterEnabled), null));
            }
            finally
            {
                SettingsManager.SetUserDefaults(previousDefaults);
                SettingsManager.Current = previous;
                Directory.Delete(directory, recursive: true);
            }
            return Task.CompletedTask;
        });
    }

    private static MediaSnapshot Snapshot(IReadOnlyList<LyricLine> lines, double position) => MediaSnapshot.Disconnected with
    {
        IsConnected = true,
        Lyrics = new LyricsResult("test", new LyricDocument(lines, LyricsSyncType.MixedSynced, "test")),
        Position = position,
        Duration = 30
    };
}
