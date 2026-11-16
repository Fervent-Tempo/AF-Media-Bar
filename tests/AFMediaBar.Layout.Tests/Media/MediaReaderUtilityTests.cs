// 验证新增读取工具的纯逻辑：PE 指纹、酷狗构建表、continuity 与网易云元数据时长。
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services.Media.Readers;
using AFMediaBar.Classes.Services.Media.Sources.KuGou;
using AFMediaBar.Classes.Services.Media.Sources.NetEase;
using AFMediaBar.Classes.Services.Media.Sources.Shared;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

[TestClass]
public sealed class MediaReaderUtilityTests
{
    [TestMethod]
    public void ReadsCurrentAssemblyPeFingerprint()
    {
        var path = typeof(PeBuildFingerprint).Assembly.Location;
        Assert.IsTrue(PeBuildFingerprint.TryReadFile(path, out var fingerprint));
        Assert.AreNotEqual(0u, fingerprint.SizeOfImage, "PE 可选头必须给出非零映像大小；确定性构建的时间戳可以为 0。");
    }

    [TestMethod]
    public void KuGouBuildTableMatchesLatestFingerprintAndLegacyVersion()
    {
        var latest = KuGouBuildProfiles.Match(
            new PeBuildFingerprint(0x6AC9EC86, 4587520),
            new PeBuildFingerprint(0x6AE2B74C, 39858176),
            "1.2.0.523", "20.1.61.28014");
        Assert.IsNotNull(latest);
        Assert.AreEqual(0x3949F8, latest.Layout.PositionOffset);
        Assert.AreEqual(1000, latest.Layout.DurationUnitsPerSecond);

        var legacy = KuGouBuildProfiles.Match(default, default, "1.2.0.519", "20.1.51.27972");
        Assert.IsNotNull(legacy);
        Assert.AreEqual(0x3929D8, legacy.Layout.PositionOffset);

        Assert.IsNull(KuGouBuildProfiles.Match(default, default, "9.9.9.9", "9.9.9.9"));
        Assert.IsNull(KuGouBuildProfiles.Match(
            new PeBuildFingerprint(0x11111111, 1),
            new PeBuildFingerprint(0x22222222, 2),
            "1.2.0.523", "20.1.61.28014"),
            "同文件版本但构建指纹不同，不能回退到旧偏移。");
    }

    [TestMethod]
    public void ContinuityHoldsThenExpires()
    {
        var now = new DateTimeOffset(2026, 11, 13, 0, 0, 0, TimeSpan.Zero);
        var buffer = new SourceContinuityBuffer(TimeSpan.FromSeconds(1.5));
        var snapshot = MediaSnapshot.Disconnected with
        {
            IsConnected = true,
            IsPlaying = true,
            Title = "Track",
            Artist = "Artist",
            Position = 12,
            Duration = 180
        };

        Assert.AreSame(snapshot, buffer.Update(snapshot, processPresent: true, now));
        var stale = buffer.Update(null, processPresent: true, now);
        Assert.IsNotNull(stale);
        Assert.IsTrue(stale.IsStale);
        Assert.IsFalse(stale.IsPlaying);
        Assert.AreEqual(12d, stale.Position);

        Assert.IsNull(buffer.Update(null, processPresent: true, now.AddSeconds(2)));
        Assert.IsNull(buffer.Update(snapshot, processPresent: false, now));
    }

    [TestMethod]
    public void NetEasePlayingListCarriesDurationFallback()
    {
        const string json = """
            {"list":[{"id":"123","track":{"name":"Track","artists":[{"name":"Artist"}],
            "album":{"name":"Album","cover":"https://example.invalid/cover.jpg"},"duration":208546}}]}
            """;
        Assert.IsTrue(NetEaseTrackMetadataReader.TryReadFromPlayingList(json, "123", out var metadata));
        Assert.AreEqual(208.546, metadata.DurationSeconds, 0.001);
        Assert.AreEqual("Track", metadata.Title);
        Assert.AreEqual("Artist", metadata.Artists);
    }

    [TestMethod]
    public void NetEaseFmQueueStillRequiresExpectedTitle()
    {
        const string json = """
            {"currentIndex":0,"queue":[{"id":"999","name":"Track","artists":[{"name":"Artist"}],
            "album":{"name":"Album","picUrl":"https://example.invalid/cover.jpg"},"duration":208546}]}
            """;
        Assert.IsFalse(NetEaseTrackMetadataReader.TryReadFromFmQueue(json, "123", "Another", out _));
        Assert.IsTrue(NetEaseTrackMetadataReader.TryReadFromFmQueue(json, "123", "Track", out var metadata));
        Assert.AreEqual(208.546, metadata.DurationSeconds, 0.001);
    }
}
