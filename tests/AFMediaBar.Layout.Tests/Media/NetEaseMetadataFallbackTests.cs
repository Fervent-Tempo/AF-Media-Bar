// 验证元数据未解析的内存读取（专区漫游不写 playingList/fmPlay）仍可发布时间轴、不放过不同曲目，且这种快照仍触发在线取词兜底；不访问进程或网络。
using System.Reflection;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Services.Lyrics;
using AFMediaBar.Classes.Services.Media.Sources.NetEase;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

[TestClass]
[DoNotParallelize]
public sealed class NetEaseMetadataFallbackTests
{
    [TestMethod]
    public void MetadataMissingSkipsTitleAndArtistChecks()
    {
        var info = Player(timelineOnly: true);

        Assert.IsTrue(NetEase.MatchesExpectedMetadata(info, "SMTC 标题", "SMTC 歌手", expectedDuration: null),
            "内存侧没有元数据时没有可比较的标题，时间轴应保留。");
        Assert.IsTrue(NetEase.MatchesExpectedMetadata(info, null, null, expectedDuration: null),
            "SMTC 也没有元数据时沿用旧行为。");
    }

    [TestMethod]
    public void MetadataPresentStillRejectsAnotherTrack()
    {
        var info = Player(timelineOnly: false);

        Assert.IsFalse(NetEase.MatchesExpectedMetadata(info, "另一首", "SMTC 歌手", expectedDuration: null),
            "两端都有标题且不一致时必须失败关闭。");
        Assert.IsFalse(NetEase.MatchesExpectedMetadata(info, "测试曲目", "另一人", expectedDuration: null),
            "歌手不相交时必须失败关闭。");
        Assert.IsTrue(NetEase.MatchesExpectedMetadata(info, "测试曲目", "测试歌手", expectedDuration: null));
    }

    [TestMethod]
    public void DurationMismatchFailsClosed()
    {
        var info = Player(timelineOnly: true);

        Assert.IsFalse(NetEase.MatchesExpectedMetadata(info, "SMTC 标题", null, expectedDuration: 300),
            "时长相差超过 2 秒说明不是同一曲目。");
        Assert.IsTrue(NetEase.MatchesExpectedMetadata(info, "SMTC 标题", null, expectedDuration: 201));
    }

    [TestMethod]
    public void TimelineOnlyReadPublishesWhenSmtcSuppliesIdentity()
    {
        var smtc = SmtcSnapshot("SMTC 标题", "SMTC 歌手");

        Assert.IsTrue(NetEaseMediaProvider.CanPublishTimelineOnly(Player(timelineOnly: true), smtc),
            "SMTC 提供曲名时可以只发布时间轴。");
        Assert.IsTrue(NetEaseMediaProvider.CanPublishTimelineOnly(Player(timelineOnly: false), MediaSnapshot.Disconnected),
            "内存自带曲名时不受 SMTC 影响。");
        Assert.IsFalse(NetEaseMediaProvider.CanPublishTimelineOnly(Player(timelineOnly: true), MediaSnapshot.Disconnected),
            "双方都没有曲名时仍失败关闭。");
        Assert.IsFalse(NetEaseMediaProvider.CanPublishTimelineOnly(Player(timelineOnly: true), SmtcSnapshot(string.Empty, "只有歌手")),
            "只有歌手时不得放出空标题的进度条。");
    }

    [TestMethod]
    public void CandidateSelectionPrefersMetadataAndRejectsAmbiguity()
    {
        Assert.AreEqual(0, NetEaseMemoryReader.SelectCandidate(["曲目"]));
        Assert.AreEqual(1, NetEaseMemoryReader.SelectCandidate([string.Empty, "曲目"]),
            "同时存在时间轴候选时仍应选中带元数据的那个。");
        Assert.AreEqual(-1, NetEaseMemoryReader.SelectCandidate(["曲目 A", "曲目 B"]),
            "两个候选都能给出曲目身份时判为歧义。");
        Assert.AreEqual(-1, NetEaseMemoryReader.SelectCandidate([string.Empty, string.Empty]),
            "多条只有时间轴的候选无法唯一确定。");
        Assert.AreEqual(0, NetEaseMemoryReader.SelectCandidate([string.Empty]),
            "唯一的候选即使只有时间轴也接受。");
    }

    [TestMethod]
    public void TimelineOnlyProviderSnapshotStillAllowsOnlineLyricsFallback()
    {
        var timelineOnly = MediaSnapshot.Disconnected with
        {
            IsConnected = true,
            IsPlaying = true,
            Title = string.Empty,
            Artist = string.Empty,
            SourceId = "cloudmusic",
            Position = 42,
            Duration = 200
        };

        Assert.IsTrue(MediaEnrichmentFallbackPolicy.ProviderCannotProvideLyrics(null),
            "提供器发布过空快照时本来就该走兜底。");
        Assert.IsTrue(MediaEnrichmentFallbackPolicy.ProviderCannotProvideLyrics(timelineOnly),
            "只有时间轴、没有曲名的快照也取不到词，必须继续走在线兜底。");
        Assert.IsFalse(MediaEnrichmentFallbackPolicy.ProviderCannotProvideLyrics(SmtcSnapshot("标题", "歌手")),
            "带曲名的快照能自己供词，不触发兜底。");
    }

    [TestMethod]
    public void TimelineOnlySourceSnapshotTriggersFallbackEvaluation()
    {
        var timelineOnly = MediaSnapshot.Disconnected with
        {
            IsConnected = true,
            IsPlaying = true,
            Title = string.Empty,
            SourceId = "cloudmusic",
            Position = 42,
            Duration = 200
        };

        Assert.IsTrue(MediaSessionService.ShouldRequestLyricsFallbackOnSourceSnapshot(null),
            "空快照仍要评估兜底。");
        Assert.IsTrue(MediaSessionService.ShouldRequestLyricsFallbackOnSourceSnapshot(timelineOnly),
            "非空但没有曲名的时间轴快照也必须重新评估，否则后续切歌不会取词。");
        Assert.IsFalse(MediaSessionService.ShouldRequestLyricsFallbackOnSourceSnapshot(SmtcSnapshot("标题", "歌手")),
            "带曲名的快照不触发兜底评估。");
    }

    [TestMethod]
    public void TimelineOnlySnapshotKeepsPositionAndDuration()
    {
        StaTest.Run(dispatcher =>
        {
            using var provider = new NetEaseMediaProvider(new LyricsService(), dispatcher,
                () => throw new InvalidOperationException("本测试不得读取真实进程。"));
            var observed = new List<MediaSnapshot>();
            provider.SnapshotChanged += (_, snapshot) =>
            {
                if (snapshot is not null)
                    observed.Add(snapshot);
            };
            provider.UpdateSessionSnapshot(SmtcSnapshot("SMTC 标题", "SMTC 歌手"));

            var readAt = DateTimeOffset.UtcNow;
            Publish(provider, Player(timelineOnly: true), readAt);

            Assert.AreEqual(1, observed.Count, "元数据缺失不应阻止时间轴发布。");
            Assert.AreEqual(42d, observed[0].Position);
            Assert.AreEqual(200d, observed[0].Duration);
            Assert.AreEqual(string.Empty, observed[0].Title, "标题由合并层的 SMTC 通道提供。");
            Assert.AreEqual(readAt, observed[0].TimelineUpdatedAt);
            return Task.CompletedTask;
        });
    }

    private static void Publish(NetEaseMediaProvider provider, PlayerInfo info, DateTimeOffset readAt) =>
        typeof(NetEaseMediaProvider).GetMethod("PublishPlayerInfo", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(provider, [info, readAt, CancellationToken.None]);

    private static PlayerInfo Player(bool timelineOnly) => new()
    {
        Identity = "1234567",
        Title = timelineOnly ? string.Empty : "测试曲目",
        Artists = timelineOnly ? string.Empty : "测试歌手",
        Album = timelineOnly ? string.Empty : "测试专辑",
        Cover = string.Empty,
        Url = "https://music.163.com/#/song?id=1234567",
        Schedule = 42,
        Duration = 200,
        Pause = false
    };

    private static MediaSnapshot SmtcSnapshot(string title, string artist) => MediaSnapshot.Disconnected with
    {
        IsConnected = true,
        IsPlaying = true,
        Title = title,
        Artist = artist,
        SourceId = "cloudmusic.exe",
        SourceName = "网易云音乐"
    };
}
