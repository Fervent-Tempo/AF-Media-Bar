// 验证 SMTC 优先合并、换歌缓冲和 stale/无 SMTC 回退；不读取进程或 SMTC。
using System.Windows.Media;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services.Media.Sources.Shared;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

[TestClass]
public sealed class SmtcFirstMergeStateTests
{
    [TestMethod]
    public void SmtcMetadataAndArtworkWinWhileMemorySuppliesTimeline()
    {
        var artwork = Artwork();
        var smtc = Snapshot("SMTC title", "SMTC artist", artwork, position: 0, duration: 0,
            lyrics: LyricsResult.NoLyrics("smtc"));
        var memory = Snapshot("memory title", "memory artist", null, position: 45, duration: 200,
            lyrics: LyricsResult.NoLyrics("memory"));
        var state = CreateState();

        var merged = state.Merge(memory, smtc, "kugou");

        Assert.AreEqual("SMTC title", merged.Title);
        Assert.AreEqual("SMTC artist", merged.Artist);
        Assert.AreSame(artwork, merged.Artwork);
        Assert.AreEqual(0d, merged.Position, "换歌缓冲期内不叠加内存进度。");
        Assert.AreSame(smtc.Lyrics, merged.Lyrics, "缓冲期内保留 SMTC 歌词。");
    }

    [TestMethod]
    public void HoldLetsSmtcCatchUpThenMemoryTimelineIsApplied()
    {
        var clock = new ManualTimeProvider();
        var state = CreateState(clock);
        var first = Snapshot("A", "artist", Artwork(), position: 10, duration: 200);
        var same = Snapshot("A", "artist", Artwork(), position: 0, duration: 0);

        var merged = state.Merge(first, same, "kugou");
        Assert.AreEqual(10d, merged.Position, "完全一致时立即可叠加内存进度。");

        var next = Snapshot("B", "artist", null, position: 1, duration: 300);
        merged = state.Merge(next, same, "kugou");
        Assert.AreEqual(0d, merged.Position, "换到 B 后先保持 SMTC 旧歌。");
        Assert.AreEqual("A", merged.Title);

        clock.Advance(TimeSpan.FromMilliseconds(1400));
        merged = state.Merge(next, same, "kugou");
        Assert.AreEqual(0d, merged.Position, "1.5 秒缓冲未结束。");

        clock.Advance(TimeSpan.FromMilliseconds(200));
        merged = state.Merge(next, same, "kugou");
        Assert.AreEqual(1d, merged.Position, "缓冲结束仍不一致时改用内存时间轴。");
        Assert.AreEqual("A", merged.Title, "标题仍以 SMTC 为准。");
    }

    [TestMethod]
    public void EmptySmtcMetadataFallsBackToMemory()
    {
        var smtc = Snapshot("", "", Artwork(), position: 0, duration: 0);
        var memory = Snapshot("memory title", "memory artist", null, position: 12, duration: 120);
        var merged = CreateState().Merge(memory, smtc, "kugou");

        Assert.AreEqual("memory title", merged.Title);
        Assert.AreEqual("memory artist", merged.Artist);
        Assert.AreEqual(12d, merged.Position);
    }

    [TestMethod]
    public void StaleMemoryFallsBackToSmtc()
    {
        var artwork = Artwork();
        var smtc = Snapshot("track", "artist", artwork, position: 0, duration: 0);
        var memory = Snapshot("track", "artist", null, position: 50, duration: 200) with { IsStale = true };
        var merged = CreateState().Merge(memory, smtc, "kugou");

        Assert.AreSame(artwork, merged.Artwork);
        Assert.AreEqual(0d, merged.Position, "stale 内存不得覆盖 SMTC 时间轴。");
        Assert.IsFalse(merged.IsStale);
    }

    [TestMethod]
    public void NoSmtcUsesMemorySnapshot()
    {
        var memory = Snapshot("memory", "artist", null, position: 20, duration: 180);
        var merged = CreateState().Merge(memory, MediaSnapshot.Disconnected, "kugou");

        Assert.AreEqual("memory", merged.Title);
        Assert.AreEqual(20d, merged.Position);
        Assert.AreEqual(180d, merged.Duration);
        Assert.IsNull(merged.Artwork);
    }

    [TestMethod]
    public void ForeignSmtcSessionIsNotUsedAsBaseline()
    {
        var memory = Snapshot("memory", "artist", null, position: 20, duration: 180);
        var foreign = Snapshot("other", "other", Artwork(), position: 60, duration: 200) with { SourceId = "chrome" };
        var state = new SmtcFirstMergeState(snapshot => snapshot.SourceId == "kugou", SameTrack);

        var merged = state.Merge(memory, foreign, "kugou");

        Assert.AreEqual("memory", merged.Title);
        Assert.AreEqual(20d, merged.Position);
    }

    [TestMethod]
    public void ControlsAndPlayStateComeFromSmtc()
    {
        var smtc = Snapshot("track", "artist", Artwork(), position: 5, duration: 200) with
        {
            IsPlaying = false,
            CanPlayPause = true,
            CanSkipNext = true,
            CanSeek = true,
            RepeatMode = MediaRepeatMode.All
        };
        var memory = Snapshot("track", "artist", null, position: 60, duration: 200) with
        {
            IsPlaying = true,
            CanPlayPause = false,
            CanSeek = false
        };
        var merged = CreateState().Merge(memory, smtc, "kugou");

        Assert.IsFalse(merged.IsPlaying);
        Assert.IsTrue(merged.CanPlayPause);
        Assert.IsTrue(merged.CanSkipNext);
        Assert.IsTrue(merged.CanSeek);
        Assert.AreEqual(MediaRepeatMode.All, merged.RepeatMode);
        Assert.AreEqual(60d, merged.Position);
    }

    private static SmtcFirstMergeState CreateState(ManualTimeProvider? clock = null) =>
        new(_ => true, SameTrack, clock ?? new ManualTimeProvider());

    private static bool SameTrack(MediaSnapshot memory, MediaSnapshot smtc) =>
        string.Equals(memory.Title.Trim(), smtc.Title.Trim(), StringComparison.OrdinalIgnoreCase) &&
        string.Equals(memory.Artist.Trim(), smtc.Artist.Trim(), StringComparison.OrdinalIgnoreCase);

    private static MediaSnapshot Snapshot(string title, string artist, ImageSource? artwork,
        double position, double duration, LyricsResult? lyrics = null) =>
        MediaSnapshot.Disconnected with
        {
            IsConnected = true,
            IsPlaying = true,
            CanPlayPause = true,
            Title = title,
            Artist = artist,
            SourceId = "kugou",
            SourceName = "酷狗音乐",
            Artwork = artwork,
            Lyrics = lyrics,
            Position = position,
            Duration = duration,
            CanSeek = true,
            TimelineUpdatedAt = DateTimeOffset.UtcNow
        };

    private static ImageSource Artwork()
    {
        var image = new DrawingImage();
        image.Freeze();
        return image;
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 11, 13, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        internal void Advance(TimeSpan delta) => _now += delta;
    }
}
