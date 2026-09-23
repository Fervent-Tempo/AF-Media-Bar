using AFMediaBar.Classes.Services.Players;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>
/// 网易云元数据文件解析的测试（正在播放列表与私人FM 队列）。
/// Tests for parsing the NetEase metadata files (the playing list and the private-FM queue).
///
/// 夹具取自真实客户端写出的文件（2026-09-19，字段裁剪但结构原样）。这些断言守的是一次已经真实发生过的故障：
/// 播放私人FM 时客户端不写 playingList，只有 fmPlay 的 currentIndex 知道当前曲目，于是媒体栏只剩 SMTC 给的标题与
/// 歌手、进度与时长恒为 0，歌词也完全没有兜底。
/// The fixtures come from files the real client wrote (2026-09-19, fields trimmed but the structure intact). They guard a failure that
/// really happened: the client does not write playingList while private FM plays and only fmPlay's currentIndex knows the current track,
/// so the media bar kept just the title and artist SMTC reported, position and duration stayed at zero, and lyrics had no fallback at all.
/// </summary>
[TestClass]
public sealed class NetEaseTrackMetadataTests
{
    /// <summary>fmPlay 的实测结构：currentIndex 指向队列里的当前曲目。<c>duration</c> 是毫秒，这里与解析无关。
    /// The real fmPlay shape: currentIndex points at the current track inside the queue. Its duration is in milliseconds and plays no part here.</summary>
    private const string FmQueueJson = """
        {"currentIndex":1,"queue":[
          {"id":"1313052981","alias":[],"duration":238400,"name":"Get High",
           "artists":[{"id":"1144018","name":"Oliverse","alias":[]}],
           "album":{"id":"73471103","name":"Dimension EP","cover":"http://p3.music.126.net/aaa.jpg","picId":"1"}},
          {"id":"1815533595","duration":295000,"name":"Look at the Sky",
           "artists":[{"id":"1","name":"Porter Robinson"}],
           "album":{"id":"2","name":"Nurture","picUrl":"http://p3.music.126.net/bbb.jpg"}}]}
        """;

    private const string PlayingListJson = """
        {"list":[
          {"id":"3316900774","displayOrder":0,"track":{"id":"3316900774","name":"Vermilion",
           "artists":[{"id":"12132340","name":"Kotori"}],
           "album":{"id":"9","name":"APEXA","cover":"http://p3.music.126.net/ccc.jpg"}}}]}
        """;

    /// <summary>
    /// 私人FM 播放时当前曲目只出现在 fmPlay 的 currentIndex 上：playingList 未命中必须回退到它，否则整首歌没有进度、时长与歌词。
    /// While private FM plays, the current track appears only at fmPlay's currentIndex: a playingList miss has to fall back to it, otherwise the
    /// whole track has no position, duration, or lyrics.
    /// </summary>
    [TestMethod]
    public void PlayingListMissFallsBackToThePrivateFmQueueByCurrentIndex()
    {
        Assert.IsFalse(NetEaseTrackMetadataReader.TryReadFromPlayingList(PlayingListJson, "1815533595", out _));

        Assert.IsTrue(NetEaseTrackMetadataReader.TryReadFromFmQueue(
            FmQueueJson,
            "1815533595",
            "Look at the Sky",
            out var metadata));
        Assert.AreEqual("Look at the Sky", metadata.Title);
        Assert.AreEqual("Porter Robinson", metadata.Artists);
        Assert.AreEqual("Nurture", metadata.Album);
        // 新写法的封面在 picUrl 上，解析必须两处都认。
        // Newer files carry the cover in picUrl, so both spellings have to be recognized.
        Assert.AreEqual("http://p3.music.126.net/bbb.jpg", metadata.Cover);
    }

    /// <summary>
    /// 队列里能按 id 找到时不再依赖 currentIndex：客户端可能还没来得及刷新它，此时按 id 命中仍然是对的。
    /// An id present in the queue no longer depends on currentIndex: the client may not have refreshed it yet, and the id hit is still right.
    /// </summary>
    [TestMethod]
    public void QueueEntryIsFoundByIdEvenWhenCurrentIndexIsStale()
    {
        Assert.IsTrue(NetEaseTrackMetadataReader.TryReadFromFmQueue(
            FmQueueJson,
            "1313052981",
            "Look at the Sky",
            out var metadata));
        Assert.AreEqual("Get High", metadata.Title);
        Assert.AreEqual("Oliverse", metadata.Artists);
        Assert.AreEqual("http://p3.music.126.net/aaa.jpg", metadata.Cover);
    }

    /// <summary>
    /// fmPlay 可能是上一次私人FM 会话留下的旧队列（当前曲目是播客、本地文件或云盘曲目，本来就不在里面）。
    /// 按 currentIndex 取出的那一条与 SMTC 报出的曲名对不上时 MUST 按未命中处理：宁可少一次补充，也不能把另一首歌的
    /// 标题、封面与歌词挂到当前曲目上。
    /// fmPlay may be the queue a previous private-FM session left behind (the current track being a podcast, a local file, or a cloud track was
    /// never in it). When the entry taken by currentIndex does not match the title SMTC reports it MUST count as a miss: one enrichment fewer
    /// is better than attaching another song's title, cover, and lyrics to the current track.
    /// </summary>
    [TestMethod]
    public void StaleQueueEntryIsRejectedWhenTheTitleDoesNotMatch()
    {
        Assert.IsFalse(NetEaseTrackMetadataReader.TryReadFromFmQueue(
            FmQueueJson,
            "9999999999",
            "某个播客节目",
            out _));

        // 没有 SMTC 曲名可校验时同样不采纳 currentIndex 那一条：没有证据就不要猜。
        // Without a title from SMTC to check against, the currentIndex entry is not used either: guessing without evidence is worse.
        Assert.IsFalse(NetEaseTrackMetadataReader.TryReadFromFmQueue(FmQueueJson, "9999999999", null, out _));

        // 越界的 currentIndex 只是坏文件，不是崩溃。
        // An out-of-range currentIndex is merely a broken file, not a crash.
        Assert.IsFalse(NetEaseTrackMetadataReader.TryReadFromFmQueue(
            """{"currentIndex":7,"queue":[{"id":"1","name":"A","artists":[],"album":null}]}""",
            "2",
            "A",
            out _));
    }

    /// <summary>曲名允许一端是另一端的子串：播放器会在曲名后补「(Live)」「(feat. …)」这类修饰，而这条判定只排除整首歌都不一样的情况。
    /// One title may be a substring of the other: players append decorations such as "(Live)" or "(feat. …)", and this test only rules out an
    /// entirely different song.</summary>
    [TestMethod]
    public void TitleComparisonToleratesPlayerDecorationsButNotDifferentSongs()
    {
        Assert.IsTrue(NetEaseTrackMetadataReader.TitlesDescribeTheSameTrack("Look at the Sky", "Look at the Sky (Live)"));
        Assert.IsTrue(NetEaseTrackMetadataReader.TitlesDescribeTheSameTrack("Get High", "get high"));
        Assert.IsFalse(NetEaseTrackMetadataReader.TitlesDescribeTheSameTrack("Look at the Sky", "Get High"));
        Assert.IsFalse(NetEaseTrackMetadataReader.TitlesDescribeTheSameTrack("", "Get High"));
        Assert.IsFalse(NetEaseTrackMetadataReader.TitlesDescribeTheSameTrack("Get High", null));
    }

    /// <summary>文件不存在（null）或被截断（客户端正在写）时按未命中处理，绝不能让解析异常冒泡到轮询循环。
    /// A missing file (null) or a truncated one (the client is writing it) counts as a miss; a parse failure must never bubble into the poll loop.</summary>
    [TestMethod]
    public void MissingOrTruncatedFilesAreTreatedAsMisses()
    {
        Assert.IsFalse(NetEaseTrackMetadataReader.TryReadFromPlayingList(null, "1", out _));
        Assert.IsFalse(NetEaseTrackMetadataReader.TryReadFromPlayingList("{\"list\":[", "1", out _));
        Assert.IsFalse(NetEaseTrackMetadataReader.TryReadFromFmQueue(null, "1", "A", out _));
        Assert.IsFalse(NetEaseTrackMetadataReader.TryReadFromFmQueue("{\"currentIndex\":0,\"queue\":", "1", "A", out _));
        Assert.IsFalse(NetEaseTrackMetadataReader.TryReadFromFmQueue("""{"currentIndex":0,"queue":[]}""", "1", "A", out _));
    }
}
